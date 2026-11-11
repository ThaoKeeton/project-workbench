using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace Workbench.TestExecutor;

public sealed record WebResult(bool Found, bool Ok, string Actual, bool FallbackEligible, byte[]? Png, string? Error = null);

public interface IWebSession : IAsyncDisposable
{
    bool IsConnected { get; }

    Task<WebResult> PerformAsync(TestStep step, CancellationToken cancellationToken);

    Task<byte[]?> CapturePngAsync(CancellationToken cancellationToken);

    Task<string> SnapshotAsync(int maxBytes, CancellationToken cancellationToken);
}

public interface IWebSessionSource
{
    Task<IWebSession> OpenAsync(TestSuite suite, CancellationToken cancellationToken);
}

public sealed class DisconnectedWebSession : IWebSession
{
    public static DisconnectedWebSession Instance { get; } = new();

    public bool IsConnected => false;

    public Task<WebResult> PerformAsync(TestStep step, CancellationToken cancellationToken)
        => Task.FromResult(new WebResult(false, false, ExecutorError.DebugPortMissing, true, null));

    public Task<byte[]?> CapturePngAsync(CancellationToken cancellationToken) => Task.FromResult<byte[]?>(null);

    public Task<string> SnapshotAsync(int maxBytes, CancellationToken cancellationToken) => Task.FromResult("");

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public sealed class DefaultWebSessionSource : IWebSessionSource
{
    public async Task<IWebSession> OpenAsync(TestSuite suite, CancellationToken cancellationToken)
    {
        var application = suite.Application;
        if (application is null) return DisconnectedWebSession.Instance;
        LaunchedTarget? launched = null;
        try
        {
            var port = application.DebugPort;
            if (application.Executable is { Length: > 0 })
            {
                launched = await ElectronTargetLauncher.LaunchAsync(application, cancellationToken);
                port = launched.Port;
            }
            if (port is not int debugPort) return DisconnectedWebSession.Instance;
            var session = await CdpWebSession.ConnectAsync(debugPort, application.WindowTitle, application.Url, cancellationToken, suite.PopupSelector);
            if (session is null)
            {
                if (launched is not null) await launched.DisposeAsync();
                return DisconnectedWebSession.Instance;
            }
            if (launched is not null) session.Own(launched);
            return session;
        }
        catch (Exception error) when (error is ExecutorDataException or HttpRequestException or IOException or WebSocketException or TaskCanceledException)
        {
            if (cancellationToken.IsCancellationRequested) throw;
            if (launched is not null) await launched.DisposeAsync();
            return DisconnectedWebSession.Instance;
        }
    }
}

public sealed class LaunchedTarget : IAsyncDisposable
{
    private readonly Process _process;

    public LaunchedTarget(Process process, int port)
    {
        _process = process;
        Port = port;
    }

    public int Port { get; }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (!_process.HasExited) _process.Kill(entireProcessTree: true);
        }
        catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
        }
        _process.Dispose();
        await Task.CompletedTask;
    }
}

public static class ElectronTargetLauncher
{
    public static async Task<LaunchedTarget> LaunchAsync(ApplicationTarget application, CancellationToken cancellationToken)
    {
        if (application.Executable is not { Length: > 0 } || !File.Exists(application.Executable))
            throw new ExecutorDataException(ExecutorError.LaunchFailed);
        var port = application.DebugPort ?? FreePort();
        var start = new ProcessStartInfo(application.Executable)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        var debuggingPort = "--remote-debugging-port=" + port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var userArguments = (application.Arguments ?? []).Where(argument => !argument.StartsWith("--remote-debugging-port=", StringComparison.Ordinal) && argument != "--remote-allow-origins=*").ToArray();
        var dotnet = Path.GetFileName(application.Executable) is "dotnet" or "dotnet.exe";
        if (!dotnet)
        {
            start.ArgumentList.Add(debuggingPort);
            start.ArgumentList.Add("--remote-allow-origins=*");
        }
        foreach (var argument in userArguments) start.ArgumentList.Add(argument);
        if (dotnet)
        {
            start.ArgumentList.Add(debuggingPort);
            start.ArgumentList.Add("--remote-allow-origins=*");
        }
        foreach (var pair in application.Environment ?? new Dictionary<string, string>())
            start.Environment[pair.Key] = pair.Value;
        var process = Process.Start(start) ?? throw new ExecutorDataException(ExecutorError.LaunchFailed);
        _ = Drain(process);
        var ready = await WaitAsync(port, cancellationToken);
        if (!ready)
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            process.Dispose();
            throw new ExecutorDataException(ExecutorError.LaunchFailed);
        }
        return new LaunchedTarget(process, port);
    }

    private static async Task Drain(Process process)
    {
        try
        {
            await Task.WhenAll(process.StandardOutput.ReadToEndAsync(), process.StandardError.ReadToEndAsync());
        }
        catch (Exception error) when (error is IOException or ObjectDisposedException)
        {
        }
    }

    public static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static async Task<bool> WaitAsync(int port, CancellationToken cancellationToken)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var response = await http.GetAsync("http" + "://127.0.0.1:" + port + "/json/version", cancellationToken);
                if (response.IsSuccessStatusCode) return true;
            }
            catch (HttpRequestException) { }
            await Task.Delay(100, cancellationToken);
        }
        return false;
    }
}

/// <summary>
/// Drives a page over its DevTools websocket.
/// Playwright 1.64 <c>ConnectOverCDP</c> attaches to Electron but never lists the page
/// (the browser context stays empty). <c>_electron.launch</c> is Node-only and injects
/// <c>--inspect</c> plus a loader into the process. This client attaches to the page
/// endpoint the app already exposes, without changing that process.
/// </summary>
public sealed class CdpWebSession : IWebSession
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly int _port;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(5) };
    private ClientWebSocket? _socket;
    private int _next = 1;
    private LaunchedTarget? _owned;
    private string _popup = ExecutorSchemas.PopupSelector;
    private string? _title;
    private string? _url;

    private CdpWebSession(int port) => _port = port;

    public bool IsConnected => _socket?.State == WebSocketState.Open;

    public static async Task<CdpWebSession?> ConnectAsync(int port, string? title, string? url, CancellationToken cancellationToken, string? popupSelector = null)
    {
        CdpWebSession? session = null;
        try
        {
            session = new CdpWebSession(port)
            {
                _popup = string.IsNullOrEmpty(popupSelector) ? ExecutorSchemas.PopupSelector : popupSelector,
                _title = title,
                _url = url
            };
            var deadline = DateTime.UtcNow.AddSeconds(15);
            while (DateTime.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    if (await session.ActivateAsync(title, url, cancellationToken)) return session;
                }
                catch (Exception error) when (error is HttpRequestException or IOException or WebSocketException or TaskCanceledException or ExecutorDataException)
                {
                    if (cancellationToken.IsCancellationRequested) throw;
                }
                await Task.Delay(100, cancellationToken);
            }
            session.ReleaseSocket();
            return null;
        }
        catch (Exception error) when (error is HttpRequestException or IOException or WebSocketException or TaskCanceledException or ExecutorDataException)
        {
            if (cancellationToken.IsCancellationRequested) throw;
            session?.ReleaseSocket();
            return null;
        }
    }

    public void Own(LaunchedTarget launched) => _owned = launched;

    public async Task<WebResult> PerformAsync(TestStep step, CancellationToken cancellationToken)
    {
        await WaitUntilResponsiveAsync(cancellationToken);
        if (step.Action == "switch-window")
        {
            var switched = await ActivateAsync(step.Target?.Title ?? step.Target?.Name, step.Target?.Url, cancellationToken);
            return switched
                ? new WebResult(true, true, "switched", false, null)
                : new WebResult(false, false, ExecutorError.WindowNotFound, false, null);
        }
        if (step.Action == "screenshot")
        {
            var png = await ScreenshotAsync(cancellationToken);
            return new WebResult(true, true, "screenshot", false, png);
        }
        if (step.Action == "wait-for") return await WaitForAsync(step, cancellationToken);
        if (step.Action is "click" or "invoke" or "expand") return await ClickAsync(step, cancellationToken);
        if (step.Action is "read-scene" or "assert-scene" or "project" or "drag" or "orbit") return await SceneAsync(step, cancellationToken);
        var blocked = await PopupAsync(step, cancellationToken);
        if (blocked is not null) return blocked;
        return ToResult(step, await EvaluateAsync(RequestFor(step), cancellationToken));
    }

    public async Task<byte[]?> CapturePngAsync(CancellationToken cancellationToken)
    {
        if (!IsConnected) return null;
        try
        {
            return await ScreenshotAsync(cancellationToken);
        }
        catch (Exception error) when (error is ExecutorDataException or WebSocketException or IOException or OperationCanceledException)
        {
            if (cancellationToken.IsCancellationRequested) throw;
            return null;
        }
    }

    public async Task<string> SnapshotAsync(int maxBytes, CancellationToken cancellationToken)
    {
        var value = await EvaluateAsync(new { op = "snapshot", selector = "", text = "", role = "", name = "", value = "", expected = "" }, cancellationToken);
        if (!value.TryGetProperty("nodes", out var nodes)) return "";
        var lines = new List<string>();
        foreach (var node in nodes.EnumerateArray())
        {
            var role = node.GetProperty("role").GetString();
            var name = node.GetProperty("name").GetString();
            var text = node.GetProperty("text").GetString();
            var selector = node.GetProperty("selector").GetString();
            lines.Add("[" + role + "] name=" + name + " text=" + text + " selector=" + selector);
        }
        var snapshot = string.Join('\n', lines);
        while (Encoding.UTF8.GetByteCount(snapshot) > maxBytes && snapshot.Length > 0)
            snapshot = snapshot[..^1];
        return snapshot;
    }

    public async Task<IReadOnlyList<RecordedInput>> ReadRecordingAsync(CancellationToken cancellationToken)
    {
        var value = await EvaluateAsync(new { op = "record-export", selector = "", text = "", role = "", name = "", value = "", expected = "" }, cancellationToken);
        if (!value.TryGetProperty("events", out var events)) return [];
        return events.EnumerateArray().Select(item => new RecordedInput(
            item.GetProperty("op").GetString() ?? "click",
            item.GetProperty("selector").GetString() ?? "",
            item.TryGetProperty("text", out var text) ? text.GetString() ?? "" : "",
            item.TryGetProperty("value", out var typed) ? typed.GetString() ?? "" : "")).ToArray();
    }

    public Task StartRecordingAsync(CancellationToken cancellationToken)
        => EvaluateAsync(new { op = "record-start", selector = "", text = "", role = "", name = "", value = "", expected = "" }, cancellationToken);

    public async ValueTask DisposeAsync()
    {
        ReleaseSocket();
        if (_owned is not null) await _owned.DisposeAsync();
    }

    private void ReleaseSocket()
    {
        _socket?.Dispose();
        _http.Dispose();
    }

    private async Task<bool> ActivateAsync(string? title, string? url, CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(VersionUrl("/json/list"), cancellationToken);
        var pages = JsonSerializer.Deserialize<List<CdpPage>>(await response.Content.ReadAsStringAsync(cancellationToken), Json) ?? [];
        var page = Pick(pages, title, url);
        if (page?.WebSocketDebuggerUrl is not { Length: > 0 } socketUrl) return false;
        if (!IsLoopback(socketUrl)) throw new ExecutorDataException(ExecutorError.DebugPortMissing);
        _socket?.Dispose();
        _socket = new ClientWebSocket();
        await _socket.ConnectAsync(new Uri(socketUrl), cancellationToken);
        return true;
    }

    private async Task<byte[]> ScreenshotAsync(CancellationToken cancellationToken)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            var result = await CallAsync("Page.captureScreenshot", new { format = "png" }, budget.Token);
            if (result.ValueKind == JsonValueKind.Object && result.TryGetProperty("data", out var data))
            {
                var encoded = data.GetString() ?? "";
                if (encoded.Length > 0) return Convert.FromBase64String(encoded);
            }
        }
        catch (Exception error) when (error is ExecutorDataException or WebSocketException or IOException or OperationCanceledException or KeyNotFoundException or FormatException)
        {
            if (cancellationToken.IsCancellationRequested) throw;
        }
        var fallback = await EvaluateAsync(new { op = "screenshot", selector = "", text = "", role = "", name = "", value = "", expected = "", strategy = "element", popup = _popup, objectName = "", allowPopup = true }, cancellationToken);
        if (fallback.ValueKind != JsonValueKind.Object || !fallback.TryGetProperty("rgba", out var rgba))
            throw new ExecutorDataException(ExecutorError.CaptureFailed);
        var pixels = Convert.FromBase64String(rgba.GetString() ?? "");
        var width = fallback.GetProperty("width").GetInt32();
        var height = fallback.GetProperty("height").GetInt32();
        return PngImage.Create(width, height, pixels).Encode();
    }

    private Task WaitUntilResponsiveAsync(CancellationToken cancellationToken)
        => EvaluateAsync(new { op = "ping", selector = "", text = "", role = "", name = "", value = "", expected = "", strategy = "element", popup = _popup, objectName = "", allowPopup = true }, cancellationToken);

    private async Task<WebResult> WaitForAsync(TestStep step, CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var blocked = await PopupAsync(step, cancellationToken);
            if (blocked is not null) return blocked;
            var result = ToResult(step, await EvaluateAsync(RequestFor(step), cancellationToken));
            if (result.Found && result.Ok) return result;
            if (!cancellationToken.CanBeCanceled) return result;
            await Task.Delay(100, cancellationToken);
        }
    }

    private async Task<WebResult> ClickAsync(TestStep step, CancellationToken cancellationToken)
    {
        var blocked = await PopupAsync(step, cancellationToken);
        if (blocked is not null) return blocked;
        var strategies = step.Expected is { Length: > 0 } ? new[] { "element", "center", "center" } : new[] { "element" };
        WebResult? last = null;
        foreach (var strategy in strategies)
        {
            var value = await EvaluateAsync(RequestFor(step, strategy: strategy), cancellationToken);
            last = ToResult(step, value);
            if (last.Error == ExecutorError.UnexpectedPopup || !last.Found) return last;
            if (strategy == "center" && step.Expected is { Length: > 0 } && !last.Actual.Contains(step.Expected, StringComparison.Ordinal)
                && value.TryGetProperty("x", out var xValue) && value.TryGetProperty("y", out var yValue))
            {
                await DispatchClickAsync(xValue.GetDouble(), yValue.GetDouble(), cancellationToken);
                last = ToResult(step, await EvaluateAsync(RequestFor(step, "read-body"), cancellationToken));
            }
            if (step.Expected is not { Length: > 0 } || last.Actual.Contains(step.Expected, StringComparison.Ordinal))
            {
                var opened = await PopupAsync(step, cancellationToken);
                return opened ?? last with { Ok = true };
            }
        }
        var after = await PopupAsync(step, cancellationToken);
        return after ?? last ?? new WebResult(false, false, ExecutorError.ActionFailed, false, null);
    }

    private async Task<WebResult> SceneAsync(TestStep step, CancellationToken cancellationToken)
    {
        var blocked = await PopupAsync(step, cancellationToken);
        if (blocked is not null) return blocked;
        if (step.Action is "drag" or "orbit")
        {
            var projected = await EvaluateAsync(RequestFor(step, "project"), cancellationToken);
            var point = ToResult(step, projected);
            if (!point.Found || !projected.TryGetProperty("x", out var xValue) || !projected.TryGetProperty("y", out var yValue))
                return point with { Ok = false };
            var (dx, dy) = step.Action == "orbit" ? (80d, 0d) : ScenePointer.ParseDelta(step.Input);
            foreach (var move in ScenePointer.Drag(xValue.GetDouble(), yValue.GetDouble(), dx, dy))
            {
                await CallAsync("Input.dispatchMouseEvent", new
                {
                    type = move.Type,
                    x = move.X,
                    y = move.Y,
                    button = "left",
                    buttons = move.Type == "mouseReleased" ? 0 : 1,
                    clickCount = 1
                }, cancellationToken);
            }
        }
        if (step.Action == "project") return ToResult(step, await EvaluateAsync(RequestFor(step, "project"), cancellationToken));
        var read = ToResult(step, await EvaluateAsync(RequestFor(step, "read-scene"), cancellationToken));
        if (!read.Found || !read.Ok || step.Expected is not { Length: > 0 }) return read;
        var ok = SceneAssert.Matches(read.Actual, step.Expected, step.Tolerance ?? 0, out var detail);
        return read with { Ok = ok, Actual = ok ? read.Actual : detail };
    }

    private async Task<WebResult?> PopupAsync(TestStep step, CancellationToken cancellationToken)
    {
        if (step.AllowPopup || step.Action is "screenshot" or "switch-window") return null;
        var value = await EvaluateAsync(RequestFor(step, "popup"), cancellationToken);
        if (!value.TryGetProperty("popup", out var popup) || popup.ValueKind != JsonValueKind.True) return null;
        return ToResult(step, value);
    }

    private async Task DispatchClickAsync(double x, double y, CancellationToken cancellationToken)
    {
        await CallAsync("Input.dispatchMouseEvent", new { type = "mousePressed", x, y, button = "left", buttons = 1, clickCount = 1 }, cancellationToken);
        await CallAsync("Input.dispatchMouseEvent", new { type = "mouseReleased", x, y, button = "left", buttons = 0, clickCount = 1 }, cancellationToken);
    }

    private async Task<JsonElement> EvaluateAsync(object request, CancellationToken cancellationToken)
    {
        var result = await CallAsync("Runtime.evaluate", new { expression = CdpExpression.Build(request), returnByValue = true }, cancellationToken);
        if (!result.TryGetProperty("result", out var inner) || !inner.TryGetProperty("value", out var value))
            return default;
        return value;
    }

    private async Task<JsonElement> CallAsync(string method, object parameters, CancellationToken cancellationToken)
    {
        if (_socket is not { State: WebSocketState.Open } && !await ActivateAsync(_title, _url, cancellationToken))
            throw new ExecutorDataException(ExecutorError.DebugPortMissing);
        var socket = _socket ?? throw new ExecutorDataException(ExecutorError.DebugPortMissing);
        var id = _next++;
        var payload = JsonSerializer.Serialize(new { id, method, @params = parameters });
        await socket.SendAsync(Encoding.UTF8.GetBytes(payload), WebSocketMessageType.Text, true, cancellationToken);
        while (true)
        {
            var message = await ReadMessageAsync(cancellationToken);
            using var document = JsonDocument.Parse(message);
            if (!document.RootElement.TryGetProperty("id", out var messageId) || messageId.GetInt32() != id)
                continue;
            if (document.RootElement.TryGetProperty("error", out _)) throw new ExecutorDataException(ExecutorError.ActionFailed);
            return document.RootElement.GetProperty("result").Clone();
        }
    }

    private async Task<byte[]> ReadMessageAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[1024 * 256];
        using var message = new MemoryStream();
        WebSocketReceiveResult received;
        do
        {
            // Opening a network directory can block the renderer for one to two minutes.
            // Keep this same call until the page answers or the step timeout cancels it.
            var pending = _socket!.ReceiveAsync(buffer, cancellationToken);
            while (!pending.IsCompleted)
            {
                var pause = Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                if (await Task.WhenAny(pending, pause) == pause && !pending.IsCompleted)
                    continue;
                break;
            }
            received = await pending;
            message.Write(buffer, 0, received.Count);
        }
        while (!received.EndOfMessage);
        return message.ToArray();
    }

    private string VersionUrl(string path) => "http" + "://127.0.0.1:" + _port.ToString(System.Globalization.CultureInfo.InvariantCulture) + path;

    private static CdpPage? Pick(IReadOnlyList<CdpPage> pages, string? title, string? url)
    {
        var candidates = pages.Where(page => page.Type is null or "page").ToArray();
        if (url is { Length: > 0 })
        {
            var byUrl = candidates.FirstOrDefault(page => page.Url?.Contains(url, StringComparison.Ordinal) == true);
            if (byUrl is not null) return byUrl;
        }
        if (title is { Length: > 0 })
            return candidates.FirstOrDefault(page => page.Title?.Contains(title, StringComparison.Ordinal) == true);
        return candidates.FirstOrDefault();
    }

    private static string? ReadText(JsonElement value, string name)
    {
        if (!value.TryGetProperty(name, out var property)) return null;
        return property.ValueKind == JsonValueKind.String ? property.GetString() : property.ToString();
    }

    private static bool IsLoopback(string socketUrl)
    {
        if (!Uri.TryCreate(socketUrl, UriKind.Absolute, out var uri)) return false;
        return uri.Host is "127.0.0.1" or "localhost" or "::1";
    }

    private object RequestFor(TestStep step, string? op = null, string? strategy = null) => new
    {
        op = op ?? step.Action,
        selector = step.Target?.Selector ?? "",
        text = step.Target?.Text ?? "",
        role = step.Target?.Role ?? "",
        name = step.Target?.Name ?? "",
        value = step.Input ?? "",
        expected = step.Expected ?? "",
        strategy = strategy ?? "element",
        popup = _popup,
        objectName = step.Target?.ObjectName ?? "",
        allowPopup = step.AllowPopup
    };

    private static WebResult ToResult(TestStep step, JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object)
            return new WebResult(false, false, ExecutorError.ActionFailed, false, null);
        if (value.TryGetProperty("popup", out var popup) && popup.ValueKind == JsonValueKind.True)
            return new WebResult(true, false, ReadText(value, "actual") ?? "", false, null, ExecutorError.UnexpectedPopup);
        var found = value.TryGetProperty("found", out var foundValue) && foundValue.ValueKind == JsonValueKind.True && foundValue.GetBoolean();
        if (!found) return new WebResult(false, false, ExecutorError.ControlNotFound, true, null);
        var ok = !value.TryGetProperty("ok", out var okValue) || okValue.ValueKind != JsonValueKind.False;
        var actual = ReadText(value, "actual") ?? ReadText(value, "value") ?? "ok";
        if (step.Sensitive) actual = "[masked]";
        return new WebResult(true, ok, actual, false, null);
    }

    private sealed record CdpPage(string? Id, string? Title, string? Url, string? Type, string? WebSocketDebuggerUrl);
}
