using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Workbench.TestExecutor;

/// <summary>
/// Loopback CDP stand-in used by fast unit tests. The CI sample that stands in for a real
/// desktop app is the Electron fixture under tests/fixtures/TestExecutor.SyntheticElectron.
/// This host still accepts the same <c>--remote-debugging-port</c> flag.
/// </summary>
public sealed class SyntheticCdpHost : IAsyncDisposable
{
    public const string Html = """
        <!DOCTYPE html>
        <html><head><title>合成页面</title></head><body>
        <input id="name" aria-label="名称" />
        <button id="ok">确定</button>
        <div id="value" role="status"></div>
        <button id="hidden" hidden>hidden-secret-text</button>
        </body></html>
        """;

    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly List<TcpClient> _clients = [];
    private readonly object _gate = new();
    private readonly SyntheticModel _model;
    private readonly Task _accept;

    private SyntheticCdpHost(int port, string? title, string? marker)
    {
        _model = SyntheticModel.Create(title);
        _model.Marker = marker;
        _listener = new TcpListener(IPAddress.Loopback, port);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _accept = AcceptLoop();
    }

    public int Port { get; }

    public static Task<SyntheticCdpHost> StartAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(new SyntheticCdpHost(0, null, Environment.GetEnvironmentVariable("SYNTHETIC_MARKER")));

    public static SyntheticCdpHost StartOn(int port, string? title, string? marker)
        => new(port, title, marker);

    public async ValueTask DisposeAsync()
    {
        _shutdown.Cancel();
        _listener.Stop();
        TcpClient[] clients;
        lock (_gate) clients = _clients.ToArray();
        foreach (var client in clients) client.Dispose();
        try { await _accept; } catch (OperationCanceledException) { }
        _shutdown.Dispose();
    }

    private async Task AcceptLoop()
    {
        while (!_shutdown.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await _listener.AcceptTcpClientAsync(_shutdown.Token); }
            catch (Exception error) when (error is OperationCanceledException or ObjectDisposedException or SocketException) { break; }
            lock (_gate) _clients.Add(client);
            _ = Task.Run(() => Serve(client), _shutdown.Token);
        }
    }

    private async Task Serve(TcpClient client)
    {
        try
        {
            using var stream = client.GetStream();
            var request = await ReadHeaders(stream, _shutdown.Token);
            if (request is null) return;
            if (request.Contains("Upgrade: websocket", StringComparison.OrdinalIgnoreCase))
            {
                var path = RequestPath(request);
                await AcceptWebSocket(stream, request, _shutdown.Token);
                var pageId = path.Contains("/page/other", StringComparison.Ordinal) ? "other" : "main";
                await SocketLoop(stream, pageId, _shutdown.Token);
                return;
            }
            var route = RequestPath(request);
            if (route.StartsWith("/json/version", StringComparison.Ordinal))
            {
                var marker = _model.Marker is { Length: > 0 } ? ",\"marker\":" + JsonSerializer.Serialize(_model.Marker) : "";
                await WriteHttp(stream, "application/json", "{\"webSocketDebuggerUrl\":\"ws" + "://127.0.0.1:" + Port + "/devtools/browser\"" + marker + "}");
            }
            else if (route.StartsWith("/json/list", StringComparison.Ordinal))
            {
                await WriteHttp(stream, "application/json", _model.ListJson(Port));
            }
            else
            {
                await WriteHttp(stream, "text/html; charset=utf-8", Html);
            }
        }
        catch (Exception error) when (error is IOException or OperationCanceledException or SocketException)
        {
        }
        finally
        {
            client.Dispose();
        }
    }

    private async Task SocketLoop(NetworkStream stream, string pageId, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var text = await ReadFrame(stream, cancellationToken);
            if (text is null) return;
            string response;
            lock (_gate) response = _model.Handle(pageId, text);
            if (response.Length > 0) await WriteFrame(stream, response, cancellationToken);
        }
    }

    private static async Task AcceptWebSocket(NetworkStream stream, string request, CancellationToken cancellationToken)
    {
        var keyLine = request.Split("\r\n").First(line => line.StartsWith("Sec-WebSocket-Key:", StringComparison.OrdinalIgnoreCase));
        var key = keyLine.Split(':', 2)[1].Trim();
        // RFC 6455 section 1.3 requires this exact SHA-1 accept value for the handshake. It is not a password hash.
#pragma warning disable S4790
        var accept = Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
#pragma warning restore S4790
        var handshake = "HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: " + accept + "\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(handshake), cancellationToken);
    }

    private static async Task<string?> ReadHeaders(NetworkStream stream, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var one = new byte[1];
        while (buffer.Length < 8192)
        {
            if (await stream.ReadAsync(one, cancellationToken) == 0) return null;
            buffer.Write(one);
            if (buffer.Length >= 4)
            {
                var tail = buffer.GetBuffer();
                var length = (int)buffer.Length;
                if (tail[length - 4] == '\r' && tail[length - 3] == '\n' && tail[length - 2] == '\r' && tail[length - 1] == '\n')
                    return Encoding.ASCII.GetString(tail, 0, length);
            }
        }
        return null;
    }

    private static string RequestPath(string request)
    {
        var first = request.Split("\r\n", 2)[0];
        var parts = first.Split(' ');
        return parts.Length > 1 ? parts[1] : "/";
    }

    private static async Task WriteHttp(NetworkStream stream, string contentType, string body)
    {
        var payload = Encoding.UTF8.GetBytes(body);
        var header = "HTTP/1.1 200 OK\r\nContent-Type: " + contentType + "\r\nContent-Length: " + payload.Length + "\r\nConnection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(header));
        await stream.WriteAsync(payload);
    }

    private static async Task<string?> ReadFrame(NetworkStream stream, CancellationToken cancellationToken)
    {
        var header = new byte[2];
        if (!await ReadExact(stream, header, cancellationToken)) return null;
        var opcode = header[0] & 0x0F;
        var length = header[1] & 0x7F;
        var masked = (header[1] & 0x80) != 0;
        if (length == 126)
        {
            var extended = new byte[2];
            if (!await ReadExact(stream, extended, cancellationToken)) return null;
            length = (extended[0] << 8) | extended[1];
        }
        if (length is 127 or > 1_000_000) return null;
        var mask = new byte[4];
        if (masked && !await ReadExact(stream, mask, cancellationToken)) return null;
        var payload = new byte[length];
        if (length > 0 && !await ReadExact(stream, payload, cancellationToken)) return null;
        if (masked)
        {
            for (var index = 0; index < payload.Length; index++) payload[index] ^= mask[index % 4];
        }
        if (opcode == 8) return null;
        if (opcode == 9)
        {
            await WriteFrame(stream, Encoding.UTF8.GetString(payload), cancellationToken, opcode: 0xA);
            return await ReadFrame(stream, cancellationToken);
        }
        return opcode == 1 ? Encoding.UTF8.GetString(payload) : "";
    }

    private static async Task WriteFrame(NetworkStream stream, string text, CancellationToken cancellationToken, int opcode = 0x1)
    {
        var payload = Encoding.UTF8.GetBytes(text);
        using var frame = new MemoryStream();
        frame.WriteByte((byte)(0x80 | opcode));
        if (payload.Length < 126) frame.WriteByte((byte)payload.Length);
        else
        {
            frame.WriteByte(126);
            frame.WriteByte((byte)(payload.Length >> 8));
            frame.WriteByte((byte)payload.Length);
        }
        frame.Write(payload);
        await stream.WriteAsync(frame.ToArray(), cancellationToken);
    }

    private static async Task<bool> ReadExact(NetworkStream stream, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var count = await stream.ReadAsync(buffer[read..], cancellationToken);
            if (count == 0) return false;
            read += count;
        }
        return true;
    }

    private sealed class SyntheticModel
    {
        private readonly List<Page> _pages;
        public string? Marker { get; set; }
        private bool _recording;
        private readonly List<RecordedInput> _recorded = [];
        private bool _modal;
        private bool _armStart;
        private bool _dragging;
        private double _lastX;
        private double _azimuth;
        private double _cubeX;
        private bool _cut;
        private string _dragMode = "camera";

        private SyntheticModel(List<Page> pages) => _pages = pages;

        public static SyntheticModel Create(string? title) => new(
        [
            new Page("main", title ?? "合成页面", "http" + "://synthetic.local/main",
            [
                SyntheticNode.Button("#ok", "确定", "确定", true),
                SyntheticNode.Input("#name", "名称"),
                SyntheticNode.Status("#value", ""),
                SyntheticNode.Button("#hidden", "隐藏", "hidden-secret-text", false),
                SyntheticNode.Button("#long", "长文本", new string('字', 400), true),
                SyntheticNode.Input("#file", "文件"),
                SyntheticNode.Input("#native-file", "原生文件"),
                SyntheticNode.Button("#start", "开始", "开始", true),
                SyntheticNode.Button("#popup", "弹出", "弹出", true),
                SyntheticNode.Status("#status", ""),
                SyntheticNode.Button("#view", "视图", "canvas", true)
            ]),
            new Page("other", "第二个窗口", "http" + "://synthetic.local/other", [SyntheticNode.Status("#other", "第二页")])
        ]);

        public string ListJson(int port)
        {
            var pages = _pages.Select(page => new
            {
                id = page.Id,
                title = page.Title,
                url = page.Url,
                type = "page",
                webSocketDebuggerUrl = "ws" + "://127.0.0.1:" + port + "/devtools/page/" + page.Id
            });
            return JsonSerializer.Serialize(pages);
        }

        public string Handle(string pageId, string message)
        {
            using var document = JsonDocument.Parse(message);
            var root = document.RootElement;
            var id = root.TryGetProperty("id", out var idValue) ? idValue.GetInt32() : 0;
            var method = root.TryGetProperty("method", out var methodValue) ? methodValue.GetString() : "";
            if (method == "Page.captureScreenshot")
            {
                if (Marker == "no-shot")
                    return "{\"id\":" + id + ",\"error\":{\"code\":-32000,\"message\":\"shot-blocked\"}}";
                var page = _pages.Single(item => item.Id == pageId);
                return "{\"id\":" + id + ",\"result\":{\"data\":\"" + Convert.ToBase64String(page.Screenshot()) + "\"}}";
            }
            if (method == "Input.dispatchMouseEvent")
            {
                Pointer(root.GetProperty("params"));
                return "{\"id\":" + id + ",\"result\":{}}";
            }
            if (method != "Runtime.evaluate")
                return "{\"id\":" + id + ",\"result\":{}}";
            var expression = root.GetProperty("params").GetProperty("expression").GetString() ?? "";
            if (expression.Contains("__WB_RELOAD_MARKER", StringComparison.Ordinal))
                return "{\"id\":" + id + ",\"result\":{\"result\":{\"type\":\"object\",\"value\":{\"marker\":false,\"hook\":\"object\",\"ready\":\"complete\"}}}}";
            var embedded = EmbeddedRequest(expression);
            if (embedded is null) return "{\"id\":" + id + ",\"result\":{\"result\":{\"type\":\"object\",\"value\":{\"found\":false}}}}";
            var value = Execute(pageId, embedded.Value);
            return "{\"id\":" + id + ",\"result\":{\"result\":{\"type\":\"object\",\"value\":" + value + "}}}";
        }

        private string Execute(string pageId, JsonElement request)
        {
            var page = _pages.Single(item => item.Id == pageId);
            var op = request.GetProperty("op").GetString();
            if (op == "record-start")
            {
                _recording = true;
                _recorded.Clear();
                return "{\"found\":true}";
            }
            if (op == "record-export")
            {
                var events = _recorded.Select(item => new { op = item.Op, selector = item.Selector, text = item.Text, value = item.Value });
                return "{\"found\":true,\"events\":" + JsonSerializer.Serialize(events) + "}";
            }
            if (op == "snapshot") return Snapshot(page);
            if (op == "ping") return "{\"found\":true,\"ok\":true}";
            if (op == "screenshot")
            {
                var rgba = new byte[8 * 8 * 4];
                for (var index = 0; index < rgba.Length; index += 4)
                {
                    rgba[index] = 9;
                    rgba[index + 1] = 8;
                    rgba[index + 2] = 7;
                    rgba[index + 3] = 255;
                }
                return "{\"found\":true,\"ok\":true,\"width\":8,\"height\":8,\"rgba\":" + JsonSerializer.Serialize(Convert.ToBase64String(rgba)) + "}";
            }
            if (op == "popup")
                return _modal
                    ? "{\"found\":true,\"ok\":false,\"popup\":true,\"actual\":" + JsonSerializer.Serialize("合成弹窗：请先完成加载") + "}"
                    : "{\"found\":false}";
            if (op == "read-body")
                return "{\"found\":true,\"ok\":true,\"actual\":" + JsonSerializer.Serialize(Body(page)) + "}";
            if (op == "ready") return "{\"found\":true,\"ok\":true}";
            if (op == "probe")
                return "{\"found\":true,\"ok\":true,\"actual\":" + JsonSerializer.Serialize(ProbeJson()) + "}";
            if (op == "canvas-rect")
                return "{\"found\":true,\"ok\":true,\"left\":0,\"top\":0,\"width\":32,\"height\":32}";
            if (op is "read-scene" or "assert-scene" or "snapshot" or "diff")
                return "{\"found\":true,\"ok\":true,\"actual\":" + JsonSerializer.Serialize(SceneJson()) + "}";
            if (op == "project")
            {
                var name = Text(request, "objectName");
                var value = Text(request, "value");
                if (name == "hidden")
                    return "{\"found\":true,\"ok\":false,\"hit\":false,\"actual\":\"occluded-by:sphere\"}";
                _dragMode = name == "stock" ? "cut" : name.Length > 0 ? "mesh" : "camera";
                var points = value.StartsWith("world-path:", StringComparison.Ordinal)
                    ? "[{\"ndcX\":-0.2,\"ndcY\":0.2},{\"ndcX\":0,\"ndcY\":0.2},{\"ndcX\":0.2,\"ndcY\":0.2}]"
                    : "[{\"ndcX\":0,\"ndcY\":0}]";
                return "{\"found\":true,\"ok\":true,\"hit\":true,\"x\":160,\"y\":120,\"ndcX\":0,\"ndcY\":0,\"points\":" + points + ",\"left\":0,\"top\":0,\"width\":320,\"height\":240,\"dpr\":1,\"zoom\":1,\"offsetLeft\":0,\"offsetTop\":0,\"deviceBox\":false,\"actual\":\"projected\"}";
            }
            if (Text(request, "selector") == "#slow") Thread.Sleep(400);
            if (Text(request, "selector") == "#slower") Thread.Sleep(2500);
            if (op is "network" or "assert-network")
                return "{\"found\":true,\"ok\":" + (page.Requests.Any(url => url.Contains(Text(request, "expected"), StringComparison.Ordinal)) ? "true" : "false") + ",\"actual\":" + JsonSerializer.Serialize(string.Join(',', page.Requests)) + ",\"urls\":" + JsonSerializer.Serialize(page.Requests) + "}";
            var node = Find(page, request);
            if (node is null) return "{\"found\":false}";
            switch (op)
            {
                case "click":
                case "invoke":
                case "expand":
                    if (node.Selector == "#ok")
                    {
                        var input = page.Nodes.Single(item => item.Selector == "#name");
                        page.Nodes.Single(item => item.Selector == "#value").Text = input.Value;
                        page.Requests.Add("http" + "://synthetic.local/submit");
                    }
                    if (node.Selector == "#start")
                    {
                        _armStart = true;
                        Remember("click", node.Selector, node.Text, "");
                        return "{\"found\":true,\"ok\":true,\"x\":40,\"y\":30,\"actual\":" + JsonSerializer.Serialize(Body(page)) + "}";
                    }
                    if (node.Selector == "#popup") _modal = true;
                    Remember("click", node.Selector, node.Text, "");
                    return "{\"found\":true,\"visible\":" + (node.Visible ? "true" : "false") + ",\"actual\":" + JsonSerializer.Serialize(Body(page)) + "}";
                case "open-file":
                    if (node.Selector == "#native-file")
                        return "{\"found\":true,\"ok\":false,\"actual\":\"native-dialog\"}";
                    node.Value = Text(request, "value");
                    return "{\"found\":true,\"ok\":true,\"value\":" + JsonSerializer.Serialize(node.Value) + "}";
                case "type":
                case "set-value":
                    node.Value = Text(request, "value");
                    Remember("type", node.Selector, "", node.Value);
                    return "{\"found\":true,\"value\":" + JsonSerializer.Serialize(node.Value) + "}";
                case "read":
                case "wait-for":
                    return "{\"found\":true,\"ok\":true,\"visible\":" + (node.Visible ? "true" : "false") + ",\"value\":" + JsonSerializer.Serialize(node.Read()) + "}";
                case "assert-text":
                    var actual = node.Read();
                    var matches = actual == Text(request, "expected");
                    return "{\"found\":true,\"ok\":" + (matches ? "true" : "false") + ",\"actual\":" + JsonSerializer.Serialize(actual) + ",\"visible\":" + (node.Visible ? "true" : "false") + "}";
                case "assert-visible":
                    return "{\"found\":true,\"ok\":" + (node.Visible ? "true" : "false") + ",\"visible\":" + (node.Visible ? "true" : "false") + ",\"actual\":\"" + (node.Visible ? "visible" : "hidden") + "\"}";
                default:
                    return "{\"found\":false}";
            }
        }

        private void Pointer(JsonElement parameters)
        {
            var type = parameters.TryGetProperty("type", out var typeValue) ? typeValue.GetString() : "";
            var x = parameters.TryGetProperty("x", out var xValue) && xValue.TryGetDouble(out var parsed) ? parsed : 0;
            if (type == "mousePressed")
            {
                _dragging = true;
                _lastX = x;
            }
            else if (type == "mouseMoved" && _dragging)
            {
                var delta = (x - _lastX) * 0.01;
                if (_dragMode == "cut") _cut = true;
                else if (_dragMode == "mesh") _cubeX += delta;
                else _azimuth += delta;
                _lastX = x;
            }
            else if (type == "mouseReleased")
            {
                if (_armStart)
                {
                    _armStart = false;
                    var page = _pages.Single(item => item.Id == "main");
                    page.Nodes.Single(item => item.Selector == "#status").Text = "已开始";
                }
                _dragging = false;
            }
        }

        private string Body(Page page)
        {
            var parts = page.Nodes.Where(node => node.Visible && node.Text.Length > 0).Select(node => node.Text).ToList();
            if (_modal) parts.Add("合成弹窗：请先完成加载");
            return string.Join('\n', parts);
        }

        private static string ProbeJson()
        {
            var globals = new { THREE = false, scene = false, workbenchHook = false };
            var signals = new
            {
                devtoolsScenes = 1,
                devtoolsRenderers = 1,
                windowScene = false,
                windowCamera = false,
                windowRenderer = false,
                r3fCanvases = 0,
                webglContexts = 1,
                drawCalls = 0,
                vertices = 0,
                bufferBytes = 0,
                frames = 0,
                globals
            };
            return JsonSerializer.Serialize(new { signals, diagnostics = new[] { "synthetic page" } });
        }

        private string SceneJson()
        {
            var camera = new { x = Math.Sin(_azimuth) * 9, y = 4d, z = Math.Cos(_azimuth) * 9, fov = 50d, azimuth = _azimuth };
            var cube = Mesh("cube", "cube-1", "Scene/cube", _cubeX, 0, -1, 1, -1, 1, -1, 1, 24, 12, 8, "box2");
            var sphere = Mesh("sphere", "sphere-1", "Scene/sphere", 3, 0, 2.2, 3.8, -0.8, 0.8, -0.8, 0.8, 117, 168, 1.97, "sphere");
            var cone = Mesh("cone", "cone-1", "Scene/cone", -3, 0, -3.7, -2.3, -0.7, 0.7, -0.7, 0.7, 24, 12, 8, "cone");
            var stock = Mesh("stock", "stock-1", "Scene/stock", 0, 2, -1, 1, -1, _cut ? 0 : 1, -1, 1, _cut ? 12 : 24, _cut ? 6 : 12, _cut ? 4 : 8, _cut ? "cut" : "stock");
            return JsonSerializer.Serialize(new { mount = "devtools", camera, nodes = new object[] { cube, sphere, cone, stock }, diagnostics = Array.Empty<string>() });
        }

        private static object Mesh(string name, string uuid, string path, double x, double y, double minX, double maxX, double minY, double maxY, double minZ, double maxZ, int vertices, int faces, double volume, string hash)
            => new
            {
                name,
                uuid,
                type = "Mesh",
                path,
                visible = 1,
                position = new { x, y, z = 0d },
                rotation = new { x = 0d, y = 0d, z = 0d },
                min = new { x = minX, y = minY, z = minZ },
                max = new { x = maxX, y = maxY, z = maxZ },
                vertexCount = vertices,
                faceCount = faces,
                volume,
                hash,
                vertices = new[] { new { x = -1d, y = -1d, z = 1d } }
            };

        private void Remember(string op, string selector, string text, string value)
        {
            if (_recording) _recorded.Add(new RecordedInput(op, selector, text, value));
        }

        private static string Snapshot(Page page)
        {
            var nodes = page.Nodes.Where(node => node.Visible && node.Interactive).Take(40).Select(node => new
            {
                role = node.Role,
                name = Clip(node.Name, 80),
                text = Clip(node.Text, 80),
                selector = node.Selector
            });
            return "{\"found\":true,\"nodes\":" + JsonSerializer.Serialize(nodes) + "}";
        }

        private static SyntheticNode? Find(Page page, JsonElement request)
        {
            var selector = Text(request, "selector");
            if (selector.Length > 0) return page.Nodes.FirstOrDefault(node => node.Selector == selector);
            var role = Text(request, "role");
            var name = Text(request, "name");
            if (role.Length > 0)
                return page.Nodes.FirstOrDefault(node => node.Role == role && (name.Length == 0 || node.Name.Contains(name, StringComparison.Ordinal) || node.Text.Contains(name, StringComparison.Ordinal)));
            var text = Text(request, "text");
            if (text.Length > 0)
                return page.Nodes.FirstOrDefault(node => node.Text.Contains(text, StringComparison.Ordinal) || node.Name.Contains(text, StringComparison.Ordinal));
            return null;
        }

        private static JsonElement? EmbeddedRequest(string expression)
        {
            const string marker = "const request = ";
            var start = expression.IndexOf(marker, StringComparison.Ordinal);
            if (start < 0) return null;
            start += marker.Length;
            var end = expression.IndexOf("};", start, StringComparison.Ordinal);
            if (end < 0) return null;
            using var document = JsonDocument.Parse(expression[start..(end + 1)]);
            return document.RootElement.Clone();
        }

        private static string Text(JsonElement request, string name)
            => request.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

        private static string Clip(string value, int limit) => value.Length <= limit ? value : value[..limit];

        private sealed class Page(string id, string title, string url, IReadOnlyList<SyntheticNode> nodes)
        {
            public string Id { get; } = id;
            public string Title { get; } = title;
            public string Url { get; } = url;
            public List<SyntheticNode> Nodes { get; } = nodes.ToList();
            public List<string> Requests { get; } = [];

            public byte[] Screenshot()
            {
                var marked = Nodes.Any(node => node.Selector == "#value" && node.Text.Length > 0);
                var rgba = new byte[32 * 32 * 4];
                for (var index = 0; index < rgba.Length; index += 4)
                {
                    rgba[index] = 10;
                    rgba[index + 1] = marked ? (byte)180 : (byte)20;
                    rgba[index + 2] = marked ? (byte)40 : (byte)200;
                    rgba[index + 3] = 255;
                }
                return PngImage.Create(32, 32, rgba).Encode();
            }
        }
    }

    private sealed class SyntheticNode
    {
        public required string Selector { get; init; }
        public required string Role { get; init; }
        public required string Name { get; init; }
        public string Text { get; set; } = "";
        public string Value { get; set; } = "";
        public bool Visible { get; init; }
        public bool Interactive { get; init; }
        public bool IsField { get; init; }
        public string Read() => IsField ? Value : Text;

        public static SyntheticNode Button(string selector, string name, string text, bool visible) => new()
        {
            Selector = selector, Role = "button", Name = name, Text = text, Visible = visible, Interactive = true
        };

        public static SyntheticNode Input(string selector, string name) => new()
        {
            Selector = selector, Role = "textbox", Name = name, Visible = true, Interactive = true, IsField = true
        };

        public static SyntheticNode Status(string selector, string text) => new()
        {
            Selector = selector, Role = "status", Name = text, Text = text, Visible = true, Interactive = false
        };
    }
}

public sealed record RecordedInput(string Op, string Selector, string Text, string Value);

public static class CdpStepRecorder
{
    public static IReadOnlyList<TestStep> ToSteps(IReadOnlyList<RecordedInput> events)
        => events.Select((item, index) => new TestStep(
            "rec-" + (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
            StepKind.Web,
            item.Op == "type" ? "type" : "click",
            new ElementSelector(null, null, item.Selector.Length > 0 ? item.Selector : null, item.Text.Length > 0 ? item.Text : null),
            null,
            item.Op == "type" ? item.Value : null,
            ExecutorSchemas.DefaultFlaUiTimeoutSeconds,
            true,
            false)).ToArray();
}
