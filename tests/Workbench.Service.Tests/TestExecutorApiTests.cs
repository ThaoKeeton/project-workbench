using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Workbench.Service.Tests;

public sealed class TestExecutorApiTests
{
    [Fact]
    public async Task Suites_can_be_listed_and_a_run_streams_step_status()
    {
        await using var running = await Fixture.StartAsync();
        var plugins = (await running.Client.GetFromJsonAsync<JsonElement>("/api/v2/plugins")).GetProperty("plugins");
        var plugin = plugins.EnumerateArray().Single(item => item.GetProperty("id").GetString() == "com.projectworkbench.test-executor");
        Assert.Equal("Ready", plugin.GetProperty("state").GetString());
        Assert.Equal("v2-preview", plugin.GetProperty("contract").GetString());
        Assert.DoesNotContain(plugin.GetProperty("permissions").EnumerateArray(), item => item.GetProperty("sensitive").GetBoolean());

        var suites = await running.Client.GetFromJsonAsync<JsonElement>("/api/v2/plugins/com.projectworkbench.test-executor/suites");
        Assert.Equal("workbench.test-executor.suite-list.v2.preview", suites.GetProperty("schema").GetString());
        Assert.Equal(2, suites.GetProperty("schemaVersion").GetInt32());
        Assert.Contains(suites.GetProperty("suites").EnumerateArray(), item => item.GetProperty("id").GetString() == "synthetic-window");
        Assert.DoesNotContain(running.Root, suites.GetRawText(), StringComparison.Ordinal);

        var started = await running.Client.PostAsJsonAsync("/api/v2/plugins/com.projectworkbench.test-executor/runs", new { suiteId = "synthetic-window", localOnly = true });
        Assert.Equal(HttpStatusCode.Accepted, started.StatusCode);
        var start = await started.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(start.GetProperty("localOnly").GetBoolean());
        Assert.False(start.GetProperty("computerUseEnabled").GetBoolean());
        var runId = start.GetProperty("runId").GetString()!;

        using var eventsRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v2/plugins/com.projectworkbench.test-executor/runs/" + runId + "/events");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var streamed = await running.Client.SendAsync(eventsRequest, HttpCompletionOption.ResponseContentRead, timeout.Token);
        var body = await streamed.Content.ReadAsStringAsync(timeout.Token);
        Assert.Equal(HttpStatusCode.OK, streamed.StatusCode);
        Assert.Contains("text/event-stream", streamed.Content.Headers.ContentType?.MediaType, StringComparison.Ordinal);
        Assert.Contains("\"type\":\"step\"", body, StringComparison.Ordinal);
        Assert.Contains("\"type\":\"run\"", body, StringComparison.Ordinal);
        Assert.Contains("\"schemaVersion\":2", body, StringComparison.Ordinal);
        Assert.DoesNotContain(running.Root, body, StringComparison.Ordinal);

        var originals = Directory.GetFiles(Path.Combine(running.Root, "test-executor", "originals"), "*.png");
        var reports = Directory.GetFiles(Path.Combine(running.Root, "v2-plugins", "com.projectworkbench.test-executor", "runs", runId), "*.png");
        Assert.NotEmpty(originals);
        Assert.NotEmpty(reports);
        Assert.NotEqual(File.ReadAllBytes(originals[0]), File.ReadAllBytes(reports[0]));
        var markdown = File.ReadAllText(Directory.GetFiles(Path.Combine(running.Root, "v2-plugins", "com.projectworkbench.test-executor", "runs", runId), "*.md")[0]);
        Assert.Contains("指纹：", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain(running.Root, markdown, StringComparison.Ordinal);

        var missing = await running.Client.GetAsync("/api/v2/plugins/com.projectworkbench.test-executor/runs/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa/events");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        var other = await running.Client.GetAsync("/api/v2/plugins/com.projectworkbench.test-cases.v2/runs/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa/events");
        Assert.Equal(HttpStatusCode.NotFound, other.StatusCode);
    }

    private static class Fixture
    {
        public static async Task<Running> StartAsync()
        {
            var root = Path.Combine(Path.GetTempPath(), "workbench-v2-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var plugins = Path.Combine(root, "plugins-v2");
            var source = Path.Combine(AppContext.BaseDirectory, "preview-plugins");
            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                var target = Path.Combine(plugins, Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target, true);
            }
            var host = await WorkbenchServiceHost.StartAsync(new WorkbenchServiceOptions
            {
                UserRoot = root,
                V1PluginsRoot = Path.Combine(root, "plugins-v1"),
                V2PluginsRoot = plugins
            });
            var client = new HttpClient { BaseAddress = new Uri(host.BaseUrl) };
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", host.SessionToken);
            return new Running(host, client, root);
        }

        public sealed class Running : IAsyncDisposable
        {
            private readonly WorkbenchServiceHost _host;
            private readonly string _root;

            public Running(WorkbenchServiceHost host, HttpClient client, string root)
            {
                _host = host;
                Client = client;
                _root = root;
                Root = root;
            }

            public HttpClient Client { get; }
            public string Root { get; }

            public async ValueTask DisposeAsync()
            {
                Client.Dispose();
                await _host.DisposeAsync();
                try { Directory.Delete(_root, true); } catch (IOException) { }
            }
        }
    }
}
