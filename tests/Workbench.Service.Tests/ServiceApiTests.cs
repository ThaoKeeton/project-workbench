using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Workbench.Core.Projects;
using Xunit;
using Workbench.Service;

namespace Workbench.Service.Tests;

public sealed class ServiceApiTests
{
    [Fact]
    public void Loopback_guard_accepts_only_loopback()
    {
        Assert.True(LoopbackGuard.IsLoopback(IPAddress.Loopback));
        Assert.True(LoopbackGuard.IsLoopback(IPAddress.IPv6Loopback));
        Assert.True(LoopbackGuard.IsLoopback(IPAddress.Parse("::ffff:127.0.0.1")));
        Assert.False(LoopbackGuard.IsLoopback(IPAddress.Parse("10.0.0.8")));
        Assert.False(LoopbackGuard.IsLoopback(null));
        Assert.True(LoopbackGuard.IsLoopbackHost("127.0.0.1:5000"));
        Assert.True(LoopbackGuard.IsLoopbackHost("[::1]:5000"));
        Assert.True(LoopbackGuard.IsLoopbackHost("localhost"));
        Assert.False(LoopbackGuard.IsLoopbackHost("example.invalid"));
        Assert.False(LoopbackGuard.IsLoopbackHost("10.0.0.8"));
        Assert.False(LoopbackGuard.IsLoopbackHost(null));
        Assert.False(LoopbackGuard.IsLoopbackHost("  "));
        Assert.False(LoopbackGuard.IsLoopbackHost("["));
        Assert.True(LoopbackGuard.IsLoopbackHost("[::1]"));
        Assert.True(LoopbackGuard.IsLoopbackHost("::1"));
        Assert.False(SessionTokens.IsAcceptable("short"));
        Assert.False(SessionTokens.IsAcceptable(new string('a', 129)));
        Assert.False(SessionTokens.IsAcceptable("has space"));
        Assert.True(SessionTokens.IsAcceptable(new string('b', 16)));
        Assert.False(SessionTokens.Equals("abc", "abcd"));
        Assert.False(UiThemeStore.IsSetting(null));
        Assert.False(UiThemeStore.IsSetting("neon"));
    }

    [Fact]
    public async Task Lan_opt_in_is_refused_before_listen()
    {
        var root = TempRoot();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => WorkbenchServiceHost.StartAsync(Options(root, allowLan: true)));
        Assert.Contains("loopback", error.Message, StringComparison.OrdinalIgnoreCase);
        Delete(root);
    }

    [Fact]
    public async Task Health_requires_the_session_token_and_binds_loopback()
    {
        await using var running = await StartAsync();
        Assert.Single(running.Host.ListenAddresses);
        Assert.StartsWith("http://127.0.0.1:", running.Host.BaseUrl, StringComparison.Ordinal);

        using var anonymous = new HttpClient { BaseAddress = running.Client.BaseAddress };
        var missing = await anonymous.GetAsync("/api/v2/health");
        Assert.Equal(HttpStatusCode.Unauthorized, missing.StatusCode);

        using var wrong = new HttpRequestMessage(HttpMethod.Get, "/api/v2/health");
        wrong.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "not-the-session-value");
        var rejected = await running.Client.SendAsync(wrong);
        Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);

        var health = await running.Client.GetFromJsonAsync<JsonElement>("/api/v2/health");
        Assert.Equal("ok", health.GetProperty("status").GetString());
        Assert.Equal("loopback", health.GetProperty("bind").GetString());
        Assert.Equal(WorkbenchServiceHost.ApiVersion, health.GetProperty("apiVersion").GetString());
        var page = await running.Client.GetAsync("/not-a-page");
        Assert.Equal(HttpStatusCode.NotFound, page.StatusCode);
        Assert.Equal(0, page.Content.Headers.ContentLength ?? (await page.Content.ReadAsByteArrayAsync()).Length);
    }

    [Fact]
    public async Task Info_describes_the_local_device_without_lan_or_sending()
    {
        await using var running = await StartAsync();
        var info = await running.Client.GetFromJsonAsync<JsonElement>("/api/v2/info");
        Assert.Equal("本机", info.GetProperty("device").GetProperty("label").GetString());
        Assert.Equal("local", info.GetProperty("device").GetProperty("id").GetString());
        Assert.False(info.GetProperty("lan").GetProperty("enabled").GetBoolean());
        Assert.False(info.GetProperty("lan").GetProperty("availableInPreview").GetBoolean());
        Assert.False(info.GetProperty("autoSend").GetBoolean());
        Assert.Equal(1, info.GetProperty("catalogSchemaVersion").GetInt32());
        Assert.True(info.GetProperty("preview").GetBoolean());
        Assert.DoesNotContain(running.Root, info.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Session_cookie_authorizes_a_later_call()
    {
        await using var running = await StartAsync(authenticated: false);
        using var open = new HttpRequestMessage(HttpMethod.Post, "/api/v2/session");
        open.Headers.Authorization = new AuthenticationHeaderValue("Bearer", running.Host.SessionToken);
        var created = await running.Client.SendAsync(open);
        Assert.Equal(HttpStatusCode.NoContent, created.StatusCode);
        var setCookie = created.Headers.GetValues("Set-Cookie").Single();
        Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secure", setCookie, StringComparison.OrdinalIgnoreCase);
        var health = await running.Client.GetAsync("/api/v2/health");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
    }

    [Fact]
    public async Task Projects_list_and_switch_keep_catalog_schema_1()
    {
        var root = TempRoot();
        var catalog = new ProjectWorkspaceCatalog(root);
        var opened = await catalog.OpenAsync();
        var created = await catalog.CreateAsync("示例项目", opened.Revision);
        await using var running = await StartAsync(root);
        var listed = await running.Client.GetFromJsonAsync<JsonElement>("/api/v2/projects");
        Assert.Equal(1, listed.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("default", listed.GetProperty("activeProjectId").GetString());
        Assert.Equal(2, listed.GetProperty("projects").GetArrayLength());

        using var conflict = new HttpRequestMessage(HttpMethod.Post, "/api/v2/projects/active")
        {
            Content = JsonContent.Create(new { id = created.Project.Id, expectedRevision = "0" })
        };
        var conflicted = await running.Client.SendAsync(conflict);
        Assert.Equal(HttpStatusCode.Conflict, conflicted.StatusCode);
        Assert.Equal("RevisionConflict", (await conflicted.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());

        var current = await running.Client.GetFromJsonAsync<JsonElement>("/api/v2/projects");
        var switched = await running.Client.PostAsJsonAsync("/api/v2/projects/active", new
        {
            id = created.Project.Id,
            expectedRevision = current.GetProperty("revision").GetString()
        });
        Assert.Equal(HttpStatusCode.OK, switched.StatusCode);
        var body = await switched.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(created.Project.Id, body.GetProperty("activeProjectId").GetString());
        Assert.Equal(1, JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(root, ".workspaces", "catalog.json"))).RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.False(Directory.Exists(Path.Combine(root, ".workbench-project", "testing")));

        var missing = await running.Client.PostAsJsonAsync("/api/v2/projects/active", new
        {
            id = new string('b', 32),
            expectedRevision = body.GetProperty("revision").GetString()
        });
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Plugins_report_v1_status_without_loading_and_v2_sample_ready()
    {
        var root = TempRoot();
        var package = Path.Combine(root, "plugins-v1", "com.projectworkbench.fixture");
        Directory.CreateDirectory(package);
        await File.WriteAllTextAsync(Path.Combine(package, "plugin.json"), """
        {
          "id": "com.projectworkbench.fixture",
          "name": "合成夹具",
          "version": "0.1.0",
          "apiVersion": 1,
          "minimumHostVersion": "0.3.0",
          "entryAssembly": "Fixture.dll",
          "entryType": "Fixture.Plugin",
          "description": "仅用于服务发现测试的合成包，不会被加载。",
          "capabilities": ["local-files"]
        }
        """);
        await File.WriteAllBytesAsync(Path.Combine(package, "Fixture.dll"), [0]);
        await using var running = await StartAsync(root);
        var plugins = (await running.Client.GetFromJsonAsync<JsonElement>("/api/v2/plugins")).GetProperty("plugins");
        var v1 = plugins.EnumerateArray().Single(item => item.GetProperty("contract").GetString() == "v1");
        Assert.Equal("Disabled", v1.GetProperty("state").GetString());
        Assert.False(v1.GetProperty("loaded").GetBoolean());
        Assert.False(v1.GetProperty("trusted").GetBoolean());
        Assert.Equal("合成夹具", v1.GetProperty("name").GetString());
        var v2 = plugins.EnumerateArray().Single(item => item.GetProperty("id").GetString() == "com.projectworkbench.test-cases.v2");
        Assert.Equal("Ready", v2.GetProperty("state").GetString());
        Assert.True(v2.GetProperty("loaded").GetBoolean());
        Assert.Equal("/plugins/test-cases", v2.GetProperty("uiPath").GetString());
        Assert.Contains(v2.GetProperty("permissions").EnumerateArray(), item => item.GetProperty("id").GetString() == "local.file.export" && !item.GetProperty("sensitive").GetBoolean());
        Assert.DoesNotContain(running.Root, plugins.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Diagnostics_summary_hides_paths_and_records_the_native_width_floor()
    {
        await using var running = await StartAsync();
        var diagnostics = await running.Client.GetFromJsonAsync<JsonElement>("/api/v2/diagnostics");
        Assert.Equal("workbench.diagnostics.v2.preview", diagnostics.GetProperty("schema").GetString());
        Assert.Equal("loopback", diagnostics.GetProperty("service").GetProperty("bind").GetString());
        Assert.False(diagnostics.GetProperty("service").GetProperty("lanEnabled").GetBoolean());
        Assert.False(diagnostics.GetProperty("service").GetProperty("autoSend").GetBoolean());
        Assert.True(diagnostics.GetProperty("plugins").GetProperty("ready").GetInt32() >= 1);
        Assert.Equal(1100, diagnostics.GetProperty("nativeWindow").GetProperty("minimumWidth").GetInt32());
        Assert.DoesNotContain(running.Root, diagnostics.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sample_cases_stay_inside_the_active_project_and_export_locally()
    {
        var root = TempRoot();
        var catalog = new ProjectWorkspaceCatalog(root);
        var opened = await catalog.OpenAsync();
        var created = await catalog.CreateAsync("示例项目", opened.Revision);
        await using var running = await StartAsync(root);
        var cases = await running.Client.GetFromJsonAsync<JsonElement>("/api/v2/plugins/com.projectworkbench.test-cases.v2/cases");
        Assert.Equal(2, cases.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("workbench.test-cases.v2.preview", cases.GetProperty("schema").GetString());
        Assert.Equal("EX-001", cases.GetProperty("rows")[0].GetProperty("id").GetString());
        Assert.DoesNotContain(root, cases.GetRawText(), StringComparison.Ordinal);

        var saved = await running.Client.PutAsJsonAsync("/api/v2/plugins/com.projectworkbench.test-cases.v2/cases", new
        {
            schema = "workbench.test-cases.v2.preview",
            schemaVersion = 2,
            suiteName = "原生工作台演示",
            rows = new[]
            {
                new { id = "EX-001", action = "查看本地示例", expected = "页面出现在主窗口" },
                new { id = "EX-002", action = "编辑摘要示例", expected = "草稿保存到本地数据目录" },
                new { id = "EX-003", action = "停用示例插件", expected = "页面与命令入口撤销" }
            }
        });
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var again = await running.Client.GetFromJsonAsync<JsonElement>("/api/v2/plugins/com.projectworkbench.test-cases.v2/cases");
        Assert.Equal("查看本地示例", again.GetProperty("rows")[0].GetProperty("action").GetString());

        var projects = await running.Client.GetFromJsonAsync<JsonElement>("/api/v2/projects");
        var switched = await running.Client.PostAsJsonAsync("/api/v2/projects/active", new
        {
            id = created.Project.Id,
            expectedRevision = projects.GetProperty("revision").GetString()
        });
        Assert.Equal(HttpStatusCode.OK, switched.StatusCode);
        var other = await running.Client.GetFromJsonAsync<JsonElement>("/api/v2/plugins/com.projectworkbench.test-cases.v2/cases");
        Assert.Equal("启用本地插件", other.GetProperty("rows")[0].GetProperty("action").GetString());

        var exported = await running.Client.PostAsync("/api/v2/plugins/com.projectworkbench.test-cases.v2/export", null);
        Assert.Equal(HttpStatusCode.OK, exported.StatusCode);
        Assert.Equal("text/csv", exported.Content.Headers.ContentType?.MediaType);
        var csv = await exported.Content.ReadAsStringAsync();
        Assert.Contains("启用本地插件", csv, StringComparison.Ordinal);
        Assert.DoesNotContain(root, csv, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(catalog.GetProjectRoot(created.Project.Id), "v2-plugins", "com.projectworkbench.test-cases.v2", "sample-test-cases.csv")));
        Assert.False(Directory.Exists(Path.Combine(catalog.GetProjectRoot(created.Project.Id), ".workbench-project", "testing")));
    }

    [Fact]
    public async Task Rejected_permissions_are_not_loaded()
    {
        var root = TempRoot();
        var v2 = Path.Combine(root, "plugins-v2", "com.projectworkbench.rejected");
        Directory.CreateDirectory(v2);
        await File.WriteAllTextAsync(Path.Combine(v2, "plugin.v2.json"), """
        {
          "contract": "workbench.plugin.v2",
          "contractVersion": "2.0.0-preview.1",
          "id": "com.projectworkbench.rejected",
          "name": "拒绝示例",
          "version": "0.1.0",
          "description": "声明了发送权限，预览宿主必须拒绝加载。",
          "autoSend": true,
          "backend": { "entryAssembly": "Missing.dll", "entryType": "Missing.Module" },
          "ui": { "kind": "web-package", "module": "./ui/Missing.tsx", "mount": "embedded" },
          "permissions": [ { "id": "message.send", "reason": "发送消息", "sensitive": true } ]
        }
        """);
        await using var running = await StartAsync(root, v2Root: Path.Combine(root, "plugins-v2"));
        var plugins = (await running.Client.GetFromJsonAsync<JsonElement>("/api/v2/plugins")).GetProperty("plugins");
        var rejected = plugins.EnumerateArray().Single(item => item.GetProperty("id").GetString() == "com.projectworkbench.rejected");
        Assert.Equal("Invalid", rejected.GetProperty("state").GetString());
        Assert.False(rejected.GetProperty("loaded").GetBoolean());
        var codes = rejected.GetProperty("diagnostics").EnumerateArray().Select(item => item.GetProperty("code").GetString()).ToArray();
        Assert.Contains("AutoSendRejected", codes);
        Assert.Contains("PermissionRejected", codes);
        Assert.Contains("SensitiveActionRequiresConfirmation", codes);
    }

    [Fact]
    public async Task Events_announce_the_local_device_and_project_changes()
    {
        var root = TempRoot();
        var catalog = new ProjectWorkspaceCatalog(root);
        var opened = await catalog.OpenAsync();
        var created = await catalog.CreateAsync("示例项目", opened.Revision);
        await using var running = await StartAsync(root);
        using var socket = new ClientWebSocket();
        socket.Options.SetRequestHeader("Authorization", "Bearer " + running.Host.SessionToken);
        await socket.ConnectAsync(new Uri("ws://127.0.0.1:" + new Uri(running.Host.BaseUrl).Port + "/api/v2/events"), CancellationToken.None);
        var ready = JsonDocument.Parse(await ReceiveAsync(socket));
        Assert.Equal("ready", ready.RootElement.GetProperty("type").GetString());
        Assert.Equal("local", ready.RootElement.GetProperty("deviceId").GetString());

        var projects = await running.Client.GetFromJsonAsync<JsonElement>("/api/v2/projects");
        var switched = await running.Client.PostAsJsonAsync("/api/v2/projects/active", new
        {
            id = created.Project.Id,
            expectedRevision = projects.GetProperty("revision").GetString()
        });
        Assert.Equal(HttpStatusCode.OK, switched.StatusCode);
        var changed = JsonDocument.Parse(await ReceiveAsync(socket));
        Assert.Equal("project-changed", changed.RootElement.GetProperty("type").GetString());
        Assert.Equal(created.Project.Id, changed.RootElement.GetProperty("activeProjectId").GetString());
    }

    [Fact]
    public async Task Theme_choice_is_stored_beside_schema_1()
    {
        await using var running = await StartAsync();
        var initial = await running.Client.GetFromJsonAsync<JsonElement>("/api/v2/ui/theme");
        Assert.Equal(2, initial.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("system", initial.GetProperty("setting").GetString());
        var saved = await running.Client.PutAsJsonAsync("/api/v2/ui/theme", new { setting = "dark" });
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var again = await running.Client.GetFromJsonAsync<JsonElement>("/api/v2/ui/theme");
        Assert.Equal("dark", again.GetProperty("setting").GetString());
        var rejected = await running.Client.PutAsJsonAsync("/api/v2/ui/theme", new { setting = "neon" });
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        var catalog = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(running.Root, ".workspaces", "catalog.json")));
        Assert.Equal(1, catalog.RootElement.GetProperty("schemaVersion").GetInt32());
        var themeFile = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(running.Root, "v2-preview", "ui-theme.json")));
        Assert.Equal(2, themeFile.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("workbench.ui-theme.v2.preview", themeFile.RootElement.GetProperty("schema").GetString());
    }

    [Fact]
    public async Task Startup_rejects_a_missing_host_and_a_weak_token()
    {
        var root = TempRoot();
        await Assert.ThrowsAsync<ArgumentNullException>(() => WorkbenchServiceHost.StartAsync(null!));
        await Assert.ThrowsAsync<ArgumentException>(() => WorkbenchServiceHost.StartAsync(Options(root) with { SessionToken = "short" }));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => UiThemeStore.WriteAsync(root, "neon", CancellationToken.None));
        Delete(root);
    }

    [Fact]
    public async Task Unknown_routes_plugins_and_theme_files_stay_local()
    {
        var root = TempRoot();
        var ui = Path.Combine(root, "ui");
        Directory.CreateDirectory(ui);
        await File.WriteAllTextAsync(Path.Combine(ui, "index.html"), "<!doctype html><title>preview</title>");
        await File.WriteAllTextAsync(Path.Combine(ui, "app.js"), "console.log(1)");
        var v2 = Path.Combine(root, "plugins-v2", "com.projectworkbench.rejected");
        Directory.CreateDirectory(v2);
        await File.WriteAllTextAsync(Path.Combine(v2, "plugin.v2.json"), """
        {
          "contract": "workbench.plugin.v2",
          "contractVersion": "2.0.0-preview.1",
          "id": "com.projectworkbench.rejected",
          "name": "拒绝示例",
          "version": "0.1.0",
          "description": "声明了发送权限，预览宿主必须拒绝加载。",
          "autoSend": false,
          "backend": { "entryAssembly": "Missing.dll", "entryType": "Missing.Module" },
          "ui": { "kind": "web-package", "module": "./ui/Missing.tsx", "mount": "embedded" },
          "permissions": [ { "id": "project.read", "reason": "读取", "sensitive": false } ]
        }
        """);
        await using var running = await StartAsync(root, v2Root: Path.Combine(root, "plugins-v2"), uiRoot: ui);
        var missingApi = await running.Client.GetAsync("/api/v2/missing");
        Assert.Equal(HttpStatusCode.NotFound, missingApi.StatusCode);
        Assert.Equal("NotFound", (await missingApi.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());
        var page = await running.Client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("preview", await page.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        var script = await running.Client.GetAsync("/app.js");
        Assert.Equal(HttpStatusCode.OK, script.StatusCode);

        var invalidSwitch = await running.Client.PostAsJsonAsync("/api/v2/projects/active", new { id = "", expectedRevision = "" });
        Assert.Equal(HttpStatusCode.BadRequest, invalidSwitch.StatusCode);
        var projects = await running.Client.GetFromJsonAsync<JsonElement>("/api/v2/projects");
        var absent = await running.Client.PostAsJsonAsync("/api/v2/projects/active", new
        {
            id = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
            expectedRevision = projects.GetProperty("revision").GetString()
        });
        Assert.Equal(HttpStatusCode.NotFound, absent.StatusCode);
        var malformed = await running.Client.PostAsJsonAsync("/api/v2/projects/active", new
        {
            id = "not-an-id",
            expectedRevision = projects.GetProperty("revision").GetString()
        });
        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
        Assert.Equal("InvalidId", (await malformed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());

        var unsafePlugin = await running.Client.GetAsync("/api/v2/plugins/not-safe/cases");
        Assert.Equal(HttpStatusCode.NotFound, unsafePlugin.StatusCode);
        var unavailable = await running.Client.GetAsync("/api/v2/plugins/com.projectworkbench.rejected/cases");
        Assert.Equal(HttpStatusCode.Conflict, unavailable.StatusCode);
        var oversized = await running.Client.PutAsync("/api/v2/plugins/com.projectworkbench.test-cases.v2/cases", new StringContent(new string('x', 32769)));
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, oversized.StatusCode);
        var socket = await running.Client.GetAsync("/api/v2/events");
        Assert.Equal(HttpStatusCode.BadRequest, socket.StatusCode);

        var themePath = Path.Combine(root, "v2-preview", "ui-theme.json");
        Directory.CreateDirectory(Path.GetDirectoryName(themePath)!);
        await File.WriteAllTextAsync(themePath, """{"schema":"nope","schemaVersion":1,"setting":"dark"}""");
        var theme = await running.Client.GetFromJsonAsync<JsonElement>("/api/v2/ui/theme");
        Assert.Equal("system", theme.GetProperty("setting").GetString());
        var diagnostics = await running.Client.GetFromJsonAsync<JsonElement>("/api/v2/diagnostics");
        Assert.Contains("MissingAssembly", diagnostics.GetProperty("issues").EnumerateArray().Select(item => item.GetProperty("code").GetString()));
    }

    [Fact]
    public async Task RunAsync_starts_on_loopback_and_stops_when_canceled()
    {
        var root = TempRoot();
        using var cancel = new CancellationTokenSource();
        var plugins = Path.Combine(root, "plugins-v2");
        CopySample(plugins);
        Directory.CreateDirectory(Path.Combine(root, "empty-ui"));
        var output = new StringWriter();
        var previousOut = Console.Out;
        Console.SetOut(output);
        var task = Program.RunAsync(
        [
            "--user-root", root,
            "--v1-plugins", Path.Combine(root, "plugins-v1"),
            "--v2-plugins", plugins,
            "--ui-dist", Path.Combine(root, "empty-ui"),
            "--print-token"
        ], cancel.Token);
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!output.ToString().Contains("listening: http://127.0.0.1:", StringComparison.Ordinal) && !task.IsCompleted && DateTime.UtcNow < deadline)
            await Task.Delay(50);
        cancel.Cancel();
        var code = await task.WaitAsync(TimeSpan.FromSeconds(15));
        Console.SetOut(previousOut);
        Assert.Equal(0, code);
        Assert.Contains("session-token: ", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_reports_failure_when_startup_is_refused()
    {
        var beside = Path.Combine(AppContext.BaseDirectory, "ui");
        Directory.CreateDirectory(beside);
        var besideIndex = Path.Combine(beside, "index.html");
        await File.WriteAllTextAsync(besideIndex, "<!doctype html>");
        var error = new StringWriter();
        var previous = Console.Error;
        Console.SetError(error);
        try
        {
            var withBeside = await Program.RunAsync(["--allow-lan", "--user-root"], CancellationToken.None);
            File.Delete(besideIndex);
            var withoutUi = await Program.RunAsync(["--allow-lan"], CancellationToken.None);
            Assert.Equal(1, withBeside);
            Assert.Equal(1, withoutUi);
            Assert.Contains("service-start-failed", error.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Console.SetError(previous);
            if (File.Exists(besideIndex)) File.Delete(besideIndex);
        }
    }

    private static async Task<string> ReceiveAsync(ClientWebSocket socket)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var buffer = new byte[4096];
        var received = await socket.ReceiveAsync(buffer, timeout.Token);
        return Encoding.UTF8.GetString(buffer, 0, received.Count);
    }

    private static async Task<Running> StartAsync(string? root = null, bool authenticated = true, string? v2Root = null, string? uiRoot = null)
    {
        root ??= TempRoot();
        var pluginsV2 = v2Root ?? Path.Combine(root, "plugins-v2");
        CopySample(pluginsV2);
        var host = await WorkbenchServiceHost.StartAsync(Options(root, pluginsV2, uiRoot: uiRoot));
        var handler = new HttpClientHandler { CookieContainer = new CookieContainer(), UseCookies = true };
        var client = new HttpClient(handler) { BaseAddress = new Uri(host.BaseUrl) };
        if (authenticated)
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", host.SessionToken);
        return new Running(host, client, root);
    }

    private static WorkbenchServiceOptions Options(string root, string? v2Root = null, bool allowLan = false, string? uiRoot = null) => new()
    {
        UserRoot = root,
        V1PluginsRoot = Path.Combine(root, "plugins-v1"),
        V2PluginsRoot = v2Root ?? Path.Combine(root, "plugins-v2"),
        UiRoot = uiRoot,
        AllowLan = allowLan
    };

    private static void CopySample(string destination)
    {
        var source = Path.Combine(AppContext.BaseDirectory, "preview-plugins");
        if (!Directory.Exists(source)) throw new InvalidOperationException("The preview plugin was not staged.");
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    private static string TempRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "workbench-v2-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void Delete(string path)
    {
        try { Directory.Delete(path, recursive: true); }
        catch (IOException) { }
    }

    private sealed class Running(WorkbenchServiceHost host, HttpClient client, string root) : IAsyncDisposable
    {
        public WorkbenchServiceHost Host { get; } = host;
        public HttpClient Client { get; } = client;
        public string Root { get; } = root;
        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await Host.DisposeAsync();
            Delete(Root);
        }
    }
}
