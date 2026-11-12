using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using Workbench.Core;
using Workbench.Core.Projects;
using Workbench.Service.Abstractions;

namespace Workbench.Service;

public sealed partial class WorkbenchServiceHost : IAsyncDisposable
{
    public const string ApiVersion = "2.0.0-preview.1";
    private const int ActionBodyLimit = 262_144;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly WebApplication _app;
    private readonly PluginManager _plugins;
    private readonly string _userRoot;
    private readonly IReadOnlyList<V2Package> _v2Packages;
    private readonly PreviewEvents _events = new();

    private WorkbenchServiceHost(WebApplication app, PluginManager plugins, string userRoot, IReadOnlyList<V2Package> v2Packages, string sessionToken)
    {
        _app = app;
        _plugins = plugins;
        _userRoot = userRoot;
        _v2Packages = v2Packages;
        SessionToken = sessionToken;
        BaseUrl = "";
        ListenAddresses = [];
    }

    public string BaseUrl { get; private set; }
    public string SessionToken { get; }
    public IReadOnlyCollection<string> ListenAddresses { get; private set; }

    public static async Task<WorkbenchServiceHost> StartAsync(WorkbenchServiceOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.AllowLan)
            throw new InvalidOperationException("LAN access is not part of this preview. The service only binds to loopback.");
        var token = options.SessionToken ?? SessionTokens.Create();
        if (!SessionTokens.IsAcceptable(token))
            throw new ArgumentException("The session token is not acceptable.", nameof(options));

        var userRoot = Path.GetFullPath(options.UserRoot);
        var v1Root = Path.GetFullPath(options.V1PluginsRoot);
        var v2Root = Path.GetFullPath(options.V2PluginsRoot);
        Directory.CreateDirectory(userRoot);
        Directory.CreateDirectory(v1Root);
        var catalog = new ProjectWorkspaceCatalog(userRoot);
        await catalog.OpenAsync(cancellationToken);

        var plugins = new PluginManager(new PluginManagerOptions { PluginsRoot = v1Root, UserRoot = userRoot, HostVersion = new Version(0, 4, 1) });
        await plugins.DiscoverAsync(cancellationToken);
        var v2Packages = await PluginV2Catalog.LoadAsync(v2Root, cancellationToken, userRoot);

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [] });
        builder.Logging.ClearProviders();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.WebHost.UseSetting(WebHostDefaults.ServerUrlsKey, "http://127.0.0.1:0");
        builder.WebHost.ConfigureKestrel(server => server.AddServerHeader = false);
        var app = builder.Build();
        var host = new WorkbenchServiceHost(app, plugins, userRoot, v2Packages, token);
        host.Map(app, token, options.UiRoot);

        try
        {
            await app.StartAsync(cancellationToken);
            var addresses = app.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>()
                .Features.Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>()?.Addresses
                ?? throw new InvalidOperationException("The service did not bind.");
            if (addresses.Count != 1 || addresses.Any(address => !address.StartsWith("http://127.0.0.1:", StringComparison.Ordinal)))
                throw new InvalidOperationException("The service bound outside loopback.");
            host.BaseUrl = addresses.Single();
            host.ListenAddresses = addresses.ToArray();
            return host;
        }
        catch
        {
            await app.DisposeAsync();
            await plugins.DisposeAsync();
            throw;
        }
    }

    public Task WaitForShutdownAsync(CancellationToken cancellationToken) => _app.WaitForShutdownAsync(cancellationToken);

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
        await _plugins.DisposeAsync();
    }

    private void Map(WebApplication app, string token, string? uiRoot)
    {
        app.Use(async (context, next) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            if (!LoopbackGuard.IsLoopback(context.Connection.RemoteIpAddress) || !LoopbackGuard.IsLoopbackHost(context.Request.Host.Value))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new { error = "LoopbackOnly" }, Json, context.RequestAborted);
                return;
            }
            await next();
        });

        if (uiRoot is not null && File.Exists(Path.Combine(uiRoot, "index.html")))
        {
            var root = Path.GetFullPath(uiRoot);
            app.UseStaticFiles(new StaticFileOptions { FileProvider = new PhysicalFileProvider(root), RequestPath = "" });
        }

        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/api") && !SessionTokens.Authorized(context, token))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new { error = "Unauthorized" }, Json, context.RequestAborted);
                return;
            }
            await next();
        });
        app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(30) });
        app.UseExceptionHandler(handler => handler.Run(async context =>
        {
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await context.Response.WriteAsJsonAsync(new { error = "ServiceFailed" }, Json, context.RequestAborted);
        }));

        app.MapGet("/api/v2/health", () => Results.Json(new { status = "ok", apiVersion = ApiVersion, bind = "loopback" }, Json));
        app.MapGet("/api/v2/info", () => Results.Json(new
        {
            product = "ProjectWorkbench",
            host = "local-service",
            apiVersion = ApiVersion,
            preview = true,
            device = new { id = "local", label = "本机", kind = "loopback" },
            lan = new { enabled = false, availableInPreview = false },
            autoSend = false,
            catalogSchemaVersion = 1,
            serviceVersion = "0.5.0-preview.1"
        }, Json));
        app.MapGet("/api/v2/projects", async (CancellationToken cancellationToken) =>
        {
            try { return Results.Json(ProjectBody(await new ProjectWorkspaceCatalog(_userRoot).OpenAsync(cancellationToken)), Json); }
            catch (ProjectWorkspaceStoreException error) { return ProjectError(error); }
        });
        app.MapPost("/api/v2/projects/active", async (SwitchBody body, CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(body.Id) || string.IsNullOrWhiteSpace(body.ExpectedRevision))
                return Results.Json(new { error = "InvalidId" }, Json, statusCode: StatusCodes.Status400BadRequest);
            try
            {
                var snapshot = await new ProjectWorkspaceCatalog(_userRoot).SetActiveAsync(body.Id, body.ExpectedRevision, cancellationToken);
                _events.Publish(JsonSerializer.Serialize(new { type = "project-changed", activeProjectId = snapshot.ActiveProjectId }, Json));
                return Results.Json(ProjectBody(snapshot), Json);
            }
            catch (ProjectWorkspaceStoreException error) { return ProjectError(error); }
        });
        app.MapGet("/api/v2/plugins", () => Results.Json(new { plugins = PluginBodies() }, Json));
        app.MapGet("/api/v2/diagnostics", async (CancellationToken cancellationToken) =>
        {
            try
            {
                var projects = await new ProjectWorkspaceCatalog(_userRoot).OpenAsync(cancellationToken);
                return Results.Json(DiagnosticsBody(projects), Json);
            }
            catch (ProjectWorkspaceStoreException error) { return ProjectError(error); }
        });
        app.MapGet("/api/v2/ui/theme", async (CancellationToken cancellationToken) =>
            Results.Json(new { schema = UiThemeStore.Schema, schemaVersion = 2, setting = await UiThemeStore.ReadAsync(_userRoot, cancellationToken) }, Json));
        app.MapPut("/api/v2/ui/theme", async (ThemeSettingBody body, CancellationToken cancellationToken) =>
        {
            if (!UiThemeStore.IsSetting(body.Setting))
                return Results.Json(new { error = "InvalidTheme" }, Json, statusCode: StatusCodes.Status400BadRequest);
            await UiThemeStore.WriteAsync(_userRoot, body.Setting!, cancellationToken);
            return Results.Json(new { schema = UiThemeStore.Schema, schemaVersion = 2, setting = body.Setting }, Json);
        });
        app.MapPost("/api/v2/session", (HttpContext context) =>
        {
            // Loopback preview speaks HTTP. Forcing Secure would make the browser drop the cookie.
#pragma warning disable S2092
            var cookie = new CookieOptions
            {
                HttpOnly = true,
                SameSite = SameSiteMode.Strict,
                Secure = context.Request.IsHttps,
                IsEssential = true,
                Path = "/api"
            };
#pragma warning restore S2092
            context.Response.Cookies.Append(SessionTokens.CookieName, token, cookie);
            return Results.NoContent();
        });
        app.Map("/api/v2/events", HandleEventsAsync);
        app.MapGet("/api/v2/plugins/{id}/cases", (string id, CancellationToken cancellationToken) => HandlePluginAsync(id, "cases.get", null, cancellationToken));
        app.MapPut("/api/v2/plugins/{id}/cases", async (string id, HttpRequest request, CancellationToken cancellationToken) =>
        {
            using var reader = new StreamReader(request.Body);
            var body = await reader.ReadToEndAsync(cancellationToken);
            if (body.Length > 32768)
                return Results.Json(new { error = "PayloadTooLarge" }, Json, statusCode: StatusCodes.Status413PayloadTooLarge);
            return await HandlePluginAsync(id, "cases.put", body, cancellationToken);
        });
        app.MapPost("/api/v2/plugins/{id}/export", (string id, CancellationToken cancellationToken) => HandlePluginAsync(id, "cases.export", null, cancellationToken));
        MapTestExecutor(app);
        app.MapGet("/api/v2/plugins/{id}/actions/{action}", (string id, string action, CancellationToken cancellationToken) =>
            IsPluginAction(action)
                ? HandlePluginAsync(id, action, null, cancellationToken)
                : Task.FromResult<IResult>(Results.Json(new { error = "NotFound" }, Json, statusCode: StatusCodes.Status404NotFound)));
        app.MapPost("/api/v2/plugins/{id}/actions/{action}", async (string id, string action, HttpRequest request, CancellationToken cancellationToken) =>
        {
            if (!IsPluginAction(action))
                return Results.Json(new { error = "NotFound" }, Json, statusCode: StatusCodes.Status404NotFound);
            if (request.ContentLength is > ActionBodyLimit)
                return Results.Json(new { error = "PayloadTooLarge" }, Json, statusCode: StatusCodes.Status413PayloadTooLarge);
            using var reader = new StreamReader(request.Body);
            var body = await reader.ReadToEndAsync(cancellationToken);
            if (body.Length > ActionBodyLimit)
                return Results.Json(new { error = "PayloadTooLarge" }, Json, statusCode: StatusCodes.Status413PayloadTooLarge);
            return await HandlePluginAsync(id, action, body.Length == 0 ? null : body, cancellationToken);
        });
        app.MapFallback(async context =>
        {
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                await context.Response.WriteAsJsonAsync(new { error = "NotFound" }, Json, context.RequestAborted);
                return;
            }
            if (uiRoot is null || !File.Exists(Path.Combine(uiRoot, "index.html")))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }
            context.Response.ContentType = "text/html; charset=utf-8";
            await context.Response.SendFileAsync(Path.Combine(Path.GetFullPath(uiRoot), "index.html"), context.RequestAborted);
        });
    }

    private async Task HandleEventsAsync(HttpContext context)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new { error = "WebSocketRequired" }, Json, context.RequestAborted);
            return;
        }
        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        var (id, channel) = _events.Join();
        try
        {
            await SendAsync(socket, JsonSerializer.Serialize(new { type = "ready", deviceId = "local" }, Json), context.RequestAborted);
            var receiveTask = socket.ReceiveAsync(new byte[128], context.RequestAborted);
            var readTask = channel.Reader.WaitToReadAsync(context.RequestAborted).AsTask();
            while (socket.State == WebSocketState.Open && !context.RequestAborted.IsCancellationRequested)
            {
                var completed = await Task.WhenAny(receiveTask, readTask);
                if (completed == receiveTask)
                {
                    var received = await receiveTask;
                    if (received.MessageType == WebSocketMessageType.Close) break;
                    receiveTask = socket.ReceiveAsync(new byte[128], context.RequestAborted);
                }
                else
                {
                    if (!await readTask) break;
                    if (channel.Reader.TryRead(out var message))
                        await SendAsync(socket, message, context.RequestAborted);
                    readTask = channel.Reader.WaitToReadAsync(context.RequestAborted).AsTask();
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException) { }
        finally
        {
            _events.Leave(id);
            if (socket.State == WebSocketState.Open)
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
        }
    }

    private async Task<IResult> HandlePluginAsync(string id, string action, string? body, CancellationToken cancellationToken)
    {
        if (!PluginV2Catalog.IsSafePluginId(id))
            return Results.Json(new { error = "PluginNotFound" }, Json, statusCode: StatusCodes.Status404NotFound);
        var package = _v2Packages.SingleOrDefault(candidate => candidate.Id == id);
        if (package?.Module is null || !package.Loaded)
            return Results.Json(new { error = "PluginUnavailable" }, Json, statusCode: StatusCodes.Status409Conflict);
        ProjectWorkspaceSnapshot snapshot;
        try { snapshot = await new ProjectWorkspaceCatalog(_userRoot).OpenAsync(cancellationToken); }
        catch (ProjectWorkspaceStoreException error) { return ProjectError(error); }
        var projectRoot = new ProjectWorkspaceCatalog(_userRoot).GetProjectRoot(snapshot.ActiveProjectId);
        var dataDirectory = Contain(projectRoot, Path.Combine("v2-plugins", id));
        var response = await package.Module.HandleAsync(new PluginModuleRequest(action, snapshot.ActiveProjectId, dataDirectory, package.GrantedPermissions, body), cancellationToken);
        if (response.ContentType.StartsWith("text/csv", StringComparison.Ordinal))
            return Results.File(response.Body, response.ContentType, "sample-test-cases.csv");
        return Results.Content(Encoding.UTF8.GetString(response.Body), response.ContentType, statusCode: response.StatusCode);
    }

    private object[] PluginBodies()
    {
        var v1 = _plugins.Plugins.Select(plugin => new
        {
            id = plugin.Manifest?.Id ?? "invalid",
            name = plugin.Manifest?.Name ?? "无效插件",
            version = plugin.Manifest?.Version ?? "0.0.0",
            contract = "v1",
            state = plugin.State.ToString(),
            trusted = plugin.IsTrusted,
            loaded = plugin.IsLoaded,
            diagnostics = plugin.Diagnostics.Take(20).Select(diagnostic => new { code = diagnostic.Code.ToString() }).ToArray(),
            permissions = Array.Empty<V2PermissionView>(),
            uiPath = (string?)null
        });
        var v2 = _v2Packages.Select(plugin => new
        {
            id = plugin.Id,
            name = plugin.Name,
            version = plugin.Version,
            contract = "v2-preview",
            state = plugin.State,
            trusted = plugin.Loaded,
            loaded = plugin.Loaded,
            diagnostics = plugin.DiagnosticCodes.Select(code => new { code }).ToArray(),
            permissions = plugin.Permissions.ToArray(),
            uiPath = plugin.UiPath
        });
        return v1.Cast<object>().Concat(v2).ToArray();
    }

    private object DiagnosticsBody(ProjectWorkspaceSnapshot projects)
    {
        var plugins = PluginBodies();
        var codes = _plugins.Diagnostics.Select(diagnostic => diagnostic.Code.ToString())
            .Concat(_plugins.Plugins.SelectMany(plugin => plugin.Diagnostics.Select(diagnostic => diagnostic.Code.ToString())))
            .Concat(_v2Packages.SelectMany(plugin => plugin.DiagnosticCodes))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .Select(code => new { code })
            .ToArray();
        return new
        {
            schema = "workbench.diagnostics.v2.preview",
            service = new { bind = "loopback", lanEnabled = false, autoSend = false },
            plugins = new
            {
                discovered = plugins.Length,
                ready = _v2Packages.Count(plugin => plugin.State == "Ready"),
                invalid = _v2Packages.Count(plugin => plugin.State == "Invalid") + _plugins.Plugins.Count(plugin => plugin.State == PluginState.Invalid),
                loaded = _v2Packages.Count(plugin => plugin.Loaded) + _plugins.Plugins.Count(plugin => plugin.IsLoaded)
            },
            projects = new { count = projects.Projects.Count, activePresent = projects.Projects.Any(project => project.Id == projects.ActiveProjectId) },
            issues = codes,
            nativeWindow = new { minimumWidth = 1100 }
        };
    }

    private static object ProjectBody(ProjectWorkspaceSnapshot snapshot) => new
    {
        schemaVersion = 1,
        revision = snapshot.Revision,
        activeProjectId = snapshot.ActiveProjectId,
        projects = snapshot.Projects.Select(project => new { id = project.Id, name = project.Name, isArchived = project.IsArchived }).ToArray()
    };

    private static IResult ProjectError(ProjectWorkspaceStoreException error)
    {
        var status = error.ErrorCode switch
        {
            "RevisionConflict" or "ActiveProjectArchived" => StatusCodes.Status409Conflict,
            "ProjectNotFound" => StatusCodes.Status404NotFound,
            _ => StatusCodes.Status400BadRequest
        };
        return Results.Json(new { error = error.ErrorCode }, Json, statusCode: status);
    }

    private static bool IsPluginAction(string? action)
    {
        if (action is not { Length: > 0 and <= 32 } || action[0] is < 'a' or > 'z') return false;
        foreach (var character in action)
        {
            if (character is not ((>= 'a' and <= 'z') or (>= '0' and <= '9') or '-')) return false;
        }
        return true;
    }

    private static string Contain(string root, string relative)
    {
        var fullRoot = Path.GetFullPath(root);
        var full = Path.GetFullPath(Path.Combine(fullRoot, relative));
        var fromRoot = Path.GetRelativePath(fullRoot, full);
        if (Path.IsPathRooted(fromRoot) || fromRoot == ".." || fromRoot.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidOperationException("The plugin data directory escapes the project root.");
        Directory.CreateDirectory(full);
        return full;
    }

    private static Task SendAsync(WebSocket socket, string message, CancellationToken cancellationToken)
        => socket.SendAsync(Encoding.UTF8.GetBytes(message), WebSocketMessageType.Text, endOfMessage: true, cancellationToken);

    private sealed record SwitchBody(string? Id, string? ExpectedRevision);
    private sealed record ThemeSettingBody(string? Setting);

    private sealed class PreviewEvents
    {
        private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, Channel<string>> _clients = new();

        public (Guid Id, Channel<string> Channel) Join()
        {
            var id = Guid.NewGuid();
            var channel = Channel.CreateBounded<string>(new BoundedChannelOptions(16) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
            _clients[id] = channel;
            return (id, channel);
        }

        public void Leave(Guid id)
        {
            if (_clients.TryRemove(id, out var channel)) channel.Writer.TryComplete();
        }

        public void Publish(string json)
        {
            foreach (var channel in _clients.Values) channel.Writer.TryWrite(json);
        }
    }
}
