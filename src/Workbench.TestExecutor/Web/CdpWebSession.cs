using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace Workbench.TestExecutor;

public sealed record WebResult(bool Found, bool Ok, string Actual, bool FallbackEligible, byte[]? Png);

public interface IWebSession : IAsyncDisposable
{
    bool IsConnected { get; }

    Task<WebResult> PerformAsync(TestStep step, CancellationToken cancellationToken);

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
            var session = await CdpWebSession.ConnectAsync(debugPort, application.WindowTitle, application.Url, cancellationToken);
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
        foreach (var argument in application.Arguments ?? [])
        {
            if (!argument.StartsWith("--remote-debugging-port=", StringComparison.Ordinal))
                start.ArgumentList.Add(argument);
        }
        start.ArgumentList.Add("--remote-debugging-port=" + port.ToString(System.Globalization.CultureInfo.InvariantCulture));
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
        var deadline = DateTime.UtcNow.AddSeconds(ExecutorSchemas.DefaultFlaUiTimeoutSeconds);
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var response = await http.GetAsync("http://127.0.0.1:" + port + "/json/version", cancellationToken);
                if (response.IsSuccessStatusCode) return true;
            }
            catch (HttpRequestException) { }
            await Task.Delay(100, cancellationToken);
        }
        return false;
    }
}

public sealed class CdpWebSession : IWebSession
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly int _port;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(5) };
    private ClientWebSocket? _socket;
    private int _next = 1;
    private LaunchedTarget? _owned;

    private CdpWebSession(int port) => _port = port;

    public bool IsConnected => _socket?.State == WebSocketState.Open;

    public static async Task<CdpWebSession?> ConnectAsync(int port, string? title, string? url, CancellationToken cancellationToken)
    {
        CdpWebSession? session = null;
        try
        {
            session = new CdpWebSession(port);
            if (!await session.ActivateAsync(title, url, cancellationToken))
            {
                session.Dispose();
                return null;
            }
            return session;
        }
        catch (Exception error) when (error is HttpRequestException or IOException or WebSocketException or TaskCanceledException or ExecutorDataException)
        {
            if (cancellationToken.IsCancellationRequested) throw;
            session?.Dispose();
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
        var value = await EvaluateAsync(RequestFor(step), cancellationToken);
        var found = value.TryGetProperty("found", out var foundValue) && foundValue.GetBoolean();
        if (!found) return new WebResult(false, false, ExecutorError.ControlNotFound, true, null);
        var ok = true;
        if (value.TryGetProperty("ok", out var okValue) && okValue.ValueKind == JsonValueKind.False) ok = false;
        var actual = ReadText(value, "actual") ?? ReadText(value, "value") ?? "ok";
        if (step.Sensitive) actual = "[masked]";
        return new WebResult(true, ok, actual, false, null);
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
        Dispose();
        if (_owned is not null) await _owned.DisposeAsync();
    }

    private void Dispose()
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
        var result = await CallAsync("Page.captureScreenshot", new { }, cancellationToken);
        var data = result.GetProperty("data").GetString() ?? "";
        return Convert.FromBase64String(data);
    }

    private Task WaitUntilResponsiveAsync(CancellationToken cancellationToken)
        => EvaluateAsync(new { op = "ping", selector = "", text = "", role = "", name = "", value = "", expected = "" }, cancellationToken);

    private async Task<JsonElement> EvaluateAsync(object request, CancellationToken cancellationToken)
    {
        var expression = "(() => {\nconst request = " + JsonSerializer.Serialize(request) + ";\n" + DomScript + "\n})()";
        var result = await CallAsync("Runtime.evaluate", new { expression, returnByValue = true }, cancellationToken);
        return result.GetProperty("result").GetProperty("value");
    }

    private const string DomScript = """
        const visible = (el) => !!el && el.getAttribute('aria-hidden') !== 'true';
        const roleOf = (el) => el.getAttribute('role') || ({BUTTON:'button',A:'link',INPUT:'textbox',TEXTAREA:'textbox',SELECT:'combobox'}[el.tagName] || '');
        const nameOf = (el) => el.getAttribute('aria-label') || el.innerText || '';
        const all = () => Array.from(document.querySelectorAll('body *'));
        if (request.op === 'ping') return {found:true, ok:true};
        const find = () => {
          if (request.selector) return document.querySelector(request.selector);
          const nodes = all();
          if (request.role) return nodes.find(el => roleOf(el) === request.role && (!request.name || nameOf(el).includes(request.name))) || null;
          if (request.text) return nodes.find(el => (el.innerText || '').includes(request.text)) || null;
          return null;
        };
        const el = find();
        if (!el && request.op !== 'snapshot' && request.op !== 'network' && request.op !== 'assert-network') return {found:false};
        if (request.op === 'click' || request.op === 'invoke' || request.op === 'expand') {
          const rect = el.getBoundingClientRect();
          const x = rect.left + rect.width / 2;
          const y = rect.top + rect.height / 2;
          el.click();
          return {found:true, ok:true, visible: visible(el), x, y};
        }
        if (request.op === 'type' || request.op === 'set-value' || request.op === 'open-file') {
          el.focus();
          el.value = request.value || '';
          el.dispatchEvent(new Event('input', {bubbles:true}));
          return {found:true, ok:true, value: el.value};
        }
        return {found:true};
        """;

    private async Task<JsonElement> CallAsync(string method, object parameters, CancellationToken cancellationToken)
    {
        if (_socket is not { State: WebSocketState.Open }) throw new ExecutorDataException(ExecutorError.DebugPortMissing);
        var id = _next++;
        var payload = JsonSerializer.Serialize(new { id, method, @params = parameters });
        await _socket.SendAsync(Encoding.UTF8.GetBytes(payload), WebSocketMessageType.Text, true, cancellationToken);
        var buffer = new byte[1024 * 256];
        using var message = new MemoryStream();
        WebSocketReceiveResult received;
        do
        {
            // A file browser loading a network path can block the renderer. Keep the same
            // in-flight call until the page answers or the step timeout cancels it.
            var pending = _socket.ReceiveAsync(buffer, cancellationToken);
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
        using var document = JsonDocument.Parse(message.ToArray());
        if (document.RootElement.TryGetProperty("error", out _)) throw new ExecutorDataException(ExecutorError.ActionFailed);
        return document.RootElement.GetProperty("result").Clone();
    }

    private string VersionUrl(string path) => "http://127.0.0.1:" + _port.ToString(System.Globalization.CultureInfo.InvariantCulture) + path;

    private static CdpPage? Pick(IReadOnlyList<CdpPage> pages, string? title, string? url)
    {
        var candidates = pages.Where(page => page.Type is null or "page").ToArray();
        if (title is { Length: > 0 })
            return candidates.FirstOrDefault(page => page.Title?.Contains(title, StringComparison.Ordinal) == true);
        if (url is { Length: > 0 })
            return candidates.FirstOrDefault(page => page.Url?.Contains(url, StringComparison.Ordinal) == true);
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

    private static object RequestFor(TestStep step) => new
    {
        op = step.Action,
        selector = step.Target?.Selector ?? "",
        text = step.Target?.Text ?? "",
        role = step.Target?.Role ?? "",
        name = step.Target?.Name ?? "",
        value = step.Input ?? "",
        expected = step.Expected ?? ""
    };

    private sealed record CdpPage(string? Id, string? Title, string? Url, string? Type, string? WebSocketDebuggerUrl);
}
