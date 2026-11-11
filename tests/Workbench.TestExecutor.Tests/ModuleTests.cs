using System.Text;
using System.Text.Json;
using Workbench.Service.Abstractions;
using Workbench.TestExecutor;
using Workbench.TestExecutor.Plugin;
using Xunit;

namespace Workbench.TestExecutor.Tests;

public sealed class ModuleTests
{
    [Fact]
    public async Task The_module_lists_suites_starts_a_run_and_streams_step_status()
    {
        var root = Fixtures.Temp();
        var data = Path.Combine(root, "data");
        Directory.CreateDirectory(data);
        var computer = new FakeComputerUseProvider();
        var exporter = new RecordingExporter();
        var module = new TestExecutorModule(new SuiteCatalog(new SuiteLoader()), new ExecutorSettingsStore(), new StaticKey(null), computer, exporter, new ScriptedFactory());
        await module.StartAsync(Granted(root), CancellationToken.None);
        var listed = await module.HandleAsync(new PluginModuleRequest("suites.list", "project-1", data, Permissions(), null), CancellationToken.None);
        var list = JsonDocument.Parse(listed.Body);
        Assert.Equal(200, listed.StatusCode);
        Assert.Equal(ExecutorSchemas.SuiteList, list.RootElement.GetProperty("schema").GetString());
        Assert.Equal(2, list.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Contains(list.RootElement.GetProperty("suites").EnumerateArray(), item => item.GetProperty("id").GetString() == ExecutorSchemas.BuiltinSuiteId);

        var started = await module.HandleAsync(new PluginModuleRequest("runs.start", "project-1", data, Permissions(), """{"suiteId":"synthetic-window","localOnly":true}"""), CancellationToken.None);
        Assert.Equal(202, started.StatusCode);
        var start = JsonDocument.Parse(started.Body);
        Assert.True(start.RootElement.GetProperty("localOnly").GetBoolean());
        Assert.False(start.RootElement.GetProperty("computerUseEnabled").GetBoolean());
        var runId = start.RootElement.GetProperty("runId").GetString()!;
        Assert.True(module.TryOpenRun(runId));
        var events = new List<string>();
        await foreach (var item in module.ReadEventsAsync(runId, new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token))
            events.Add(item);
        Assert.Contains(events, item => item.Contains("\"type\":\"run\"", StringComparison.Ordinal));
        Assert.Contains(events, item => item.Contains("\"schemaVersion\":2", StringComparison.Ordinal));
        Assert.Equal(0, computer.ObserveCalls);
        Assert.Empty(exporter.Calls);
        Assert.DoesNotContain(events, item => item.Contains(root, StringComparison.Ordinal));
        Directory.Delete(root, true);
    }

    [Fact]
    public async Task Export_is_a_separate_confirmation_and_missing_permission_is_denied()
    {
        var root = Fixtures.Temp();
        var data = Path.Combine(root, "data");
        Directory.CreateDirectory(data);
        var exporter = new RecordingExporter();
        var module = new TestExecutorModule(new SuiteCatalog(new SuiteLoader()), new ExecutorSettingsStore(), new StaticKey(null), new FakeComputerUseProvider(), exporter, new ScriptedFactory());
        await module.StartAsync(Granted(root), CancellationToken.None);
        var denied = await module.HandleAsync(new PluginModuleRequest("suites.list", "project-1", data, new HashSet<string>(StringComparer.Ordinal), null), CancellationToken.None);
        Assert.Equal(403, denied.StatusCode);
        Assert.Contains("PermissionDenied", Encoding.UTF8.GetString(denied.Body), StringComparison.Ordinal);

        var started = await module.HandleAsync(new PluginModuleRequest("runs.start", "project-1", data, Permissions(), """{"suiteId":"synthetic-window"}"""), CancellationToken.None);
        var runId = JsonDocument.Parse(started.Body).RootElement.GetProperty("runId").GetString()!;
        await foreach (var unused in module.ReadEventsAsync(runId, new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token))
            _ = unused;
        var reports = Directory.GetFiles(Path.Combine(data, "runs", runId), "*.json").Where(path => !path.EndsWith(".evidence.json", StringComparison.Ordinal) && !path.EndsWith("ai-usage.json", StringComparison.Ordinal)).ToArray();
        var fingerprint = JsonDocument.Parse(File.ReadAllText(reports[0])).RootElement.GetProperty("fingerprint").GetString();
        var exported = await module.HandleAsync(new PluginModuleRequest("runs.export", "project-1", data, Permissions(), "{\"runId\":\"" + runId + "\",\"fingerprint\":\"" + fingerprint + "\"}"), CancellationToken.None);
        Assert.Equal(200, exported.StatusCode);
        Assert.Equal("created", JsonDocument.Parse(exported.Body).RootElement.GetProperty("status").GetString());
        Assert.Single(exporter.Calls);
        var missing = await module.HandleAsync(new PluginModuleRequest("runs.export", "project-1", data, Permissions(), """{"runId":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","fingerprint":"CASE-1|step-1|Timeout"}"""), CancellationToken.None);
        Assert.Equal(404, missing.StatusCode);
        Directory.Delete(root, true);
    }

    [Fact]
    public async Task Unknown_actions_and_bad_bodies_stay_on_the_error_code_contract()
    {
        var root = Fixtures.Temp();
        var module = new TestExecutorModule(new SuiteCatalog(new SuiteLoader()), new ExecutorSettingsStore(), new StaticKey(null), new FakeComputerUseProvider(), new RecordingExporter(), new ScriptedFactory());
        await module.StartAsync(Granted(root), CancellationToken.None);
        var unknown = await module.HandleAsync(new PluginModuleRequest("nope", "project-1", root, Permissions(), null), CancellationToken.None);
        Assert.Equal(404, unknown.StatusCode);
        var bad = await module.HandleAsync(new PluginModuleRequest("runs.start", "project-1", root, Permissions(), """{"suiteId":"synthetic-window","token":"nope"}"""), CancellationToken.None);
        Assert.Equal(400, bad.StatusCode);
        Assert.DoesNotContain("nope", Encoding.UTF8.GetString(bad.Body), StringComparison.Ordinal);
        await Assert.ThrowsAsync<TestExecutorModule.PermissionDeniedException>(() => module.StartAsync(new PluginModuleStart(new HashSet<string>(StringComparer.Ordinal)), CancellationToken.None));
        Directory.Delete(root, true);
    }

    private static PluginModuleStart Granted(string root)
        => new(Permissions()) { PreviewUserRoot = root };

    private static HashSet<string> Permissions()
        => new(["project.read", "plugin.data.read", "plugin.data.write", "local.file.export"], StringComparer.Ordinal);

    private sealed class StaticKey(string? value) : IApiKeyStore
    {
        public string? Read(string target) => value;
    }
}
