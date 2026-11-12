using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Workbench.TestExecutor;
using Xunit;

namespace Workbench.TestExecutor.Tests;

public sealed class WebPathTests
{
    [Fact]
    public async Task Cdp_drives_the_synthetic_page_and_switches_windows()
    {
        await using var host = await SyntheticCdpHost.StartAsync();
        using var http = new HttpClient();
        var html = await http.GetStringAsync("http://127.0.0.1:" + host.Port + "/");
        Assert.Contains("id=\"ok\"", html, StringComparison.Ordinal);
        var session = await CdpWebSession.ConnectAsync(host.Port, "合成页面", null, CancellationToken.None);
        Assert.NotNull(session);
        await using var connected = session;
        var typed = await connected.PerformAsync(Step("type-name", "type", selector: "#name", input: "合成值"), CancellationToken.None);
        Assert.True(typed.Ok);
        var clicked = await connected.PerformAsync(Step("press-ok", "click", selector: "#ok"), CancellationToken.None);
        Assert.True(clicked.Ok);
        var read = await connected.PerformAsync(Step("read-value", "read", selector: "#value", expected: "合成值"), CancellationToken.None);
        Assert.Equal("合成值", read.Actual);
        var byRole = await connected.PerformAsync(Step("by-role", "assert-text", role: "button", name: "确定", expected: "确定"), CancellationToken.None);
        Assert.True(byRole.Ok);
        var visible = await connected.PerformAsync(Step("visible", "assert-visible", selector: "#ok"), CancellationToken.None);
        Assert.True(visible.Ok);
        var network = await connected.PerformAsync(Step("posted", "assert-network", selector: "#ok", expected: "http://synthetic.local/submit"), CancellationToken.None);
        Assert.True(network.Ok);
        var shot = await connected.PerformAsync(Step("shot", "screenshot", expected: null), CancellationToken.None);
        Assert.NotNull(shot.Png);
        Assert.Equal(new Rgba8(10, 180, 40, 255), PngImage.Decode(shot.Png!)[4, 4]);
        var snapshot = await connected.SnapshotAsync(4096, CancellationToken.None);
        Assert.Contains("selector=#ok", snapshot, StringComparison.Ordinal);
        Assert.DoesNotContain("hidden-secret-text", snapshot, StringComparison.Ordinal);
        Assert.True(Encoding.UTF8.GetByteCount(snapshot) <= 4096);
        await connected.StartRecordingAsync(CancellationToken.None);
        await connected.PerformAsync(Step("rec-click", "click", selector: "#ok"), CancellationToken.None);
        var recorded = CdpStepRecorder.ToSteps(await connected.ReadRecordingAsync(CancellationToken.None));
        Assert.Contains(recorded, step => step.Action == "click" && step.Target?.Selector == "#ok");
        var switched = await connected.PerformAsync(Step("other", "switch-window", title: "第二个窗口"), CancellationToken.None);
        Assert.True(switched.Ok);
        var other = await connected.PerformAsync(Step("read-other", "read", text: "第二页"), CancellationToken.None);
        Assert.Equal("第二页", other.Actual);
        var missing = await connected.PerformAsync(Step("missing", "click", selector: "#missing"), CancellationToken.None);
        Assert.False(missing.Found);
        var slow = Stopwatch.StartNew();
        var waited = await connected.PerformAsync(Step("slow", "click", selector: "#slow"), CancellationToken.None);
        slow.Stop();
        Assert.False(waited.Found);
        Assert.True(slow.ElapsedMilliseconds >= 300);
        var slower = Stopwatch.StartNew();
        await connected.PerformAsync(Step("slower", "click", selector: "#slower"), CancellationToken.None);
        slower.Stop();
        Assert.True(slower.ElapsedMilliseconds >= 2000);
        using var impatient = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => connected.PerformAsync(Step("slow-cancel", "click", selector: "#slow"), impatient.Token));
    }

    [Fact]
    public async Task Launcher_starts_the_synthetic_electron_stand_in_and_attaches_by_title()
    {
        var dll = FindSyntheticDll();
        var executable = DotnetMuxer();
        IReadOnlyList<string> arguments = ["exec", dll, "--window-title=自定义标题"];
        var port = ElectronTargetLauncher.FreePort();
        var launched = await ElectronTargetLauncher.LaunchAsync(new ApplicationTarget(null, "自定义标题", port, executable, arguments, new Dictionary<string, string> { ["SYNTHETIC_MARKER"] = "synthetic" }), CancellationToken.None);
        await using (launched)
        {
            using var http = new HttpClient();
            var version = await http.GetStringAsync("http://127.0.0.1:" + launched.Port + "/json/version");
            Assert.Contains("\"marker\":\"synthetic\"", version, StringComparison.Ordinal);
            var session = await CdpWebSession.ConnectAsync(launched.Port, "自定义标题", null, CancellationToken.None);
            Assert.NotNull(session);
            await using var connected = session;
            var read = await connected.PerformAsync(Step("read-ok", "read", role: "button", name: "确定"), CancellationToken.None);
            Assert.Equal("确定", read.Actual);
        }
    }

    [Fact]
    public async Task Runner_uses_cdp_first_and_does_not_call_image_ai_by_default()
    {
        await using var host = await SyntheticCdpHost.StartAsync();
        var root = Fixtures.Temp();
        var reports = Path.Combine(root, "reports");
        var computer = new FakeComputerUseProvider();
        var repair = new CountingRepair(_ => null);
        var runner = Fixtures.Runner(new ScriptedFactory(), computer, new RecordingExporter(), new RecordingDeadline(), root, reports, repair);
        var suite = new TestSuite("suite-web", "网页", false, true, new ApplicationTarget(null, "合成页面", host.Port, Attach: true), [], [
            Fixtures.Case("CASE-WEB", true,
                Step("type-name", "type", selector: "#name", input: "合成值"),
                Step("press-ok", "click", selector: "#ok"),
                Step("read-value", "read", selector: "#value", expected: "合成值"),
                Step("missing", "click", selector: "#missing"))
        ]);
        var sink = new ListSink();
        await runner.RunAsync(suite, new RunRequest("web-run", Fixtures.Policy(computer: true, repair: false), reports), sink, CancellationToken.None);
        Assert.Equal(0, computer.ObserveCalls);
        Assert.Equal(0, repair.Calls);
        Assert.Equal("failed", sink.Summary!.Status);
        Assert.Contains(sink.Steps, step => step.StepId == "read-value" && step.Status == "passed" && step.Engine == "web");
        Assert.Contains(sink.Steps, step => step.StepId == "missing" && step.ErrorType == ExecutorError.ControlNotFound);
        var usage = File.ReadAllText(Path.Combine(reports, "ai-usage.json"));
        Assert.Contains("\"tokens\":0", usage, StringComparison.Ordinal);
        Directory.Delete(root, true);
    }

    [Fact]
    public async Task Locator_repair_is_text_only_persisted_once_and_blocked_by_the_budget()
    {
        var root = Fixtures.Temp();
        var reports = Path.Combine(root, "reports");
        var suiteFile = Path.Combine(root, "case.json");
        File.WriteAllText(suiteFile, """
            {"schema":"workbench.test-executor.suite.v2.preview","schemaVersion":2,"id":"repair-suite","name":"修复","cases":[{"id":"CASE-1","name":"点击","steps":[{"id":"press","type":"web","action":"click","target":{"selector":"#missing"}}]}]}
            """);
        await using var host = await SyntheticCdpHost.StartAsync();
        var repair = new CountingRepair(_ => new LocatorSuggestion("#ok", 12));
        var runner = Fixtures.Runner(new ScriptedFactory(), new FakeComputerUseProvider(), new RecordingExporter(), new RecordingDeadline(), root, reports, repair);
        var suite = new SuiteLoader().Load(File.ReadAllText(suiteFile)) with { Application = new ApplicationTarget(null, "合成页面", host.Port, Attach: true) };
        var sink = new ListSink();
        await runner.RunAsync(suite, new RunRequest("repair-run", Fixtures.Policy(repair: true), reports, suiteFile), sink, CancellationToken.None);
        Assert.Equal(1, repair.Calls);
        Assert.Contains("selector=#ok", repair.LastSnapshot, StringComparison.Ordinal);
        Assert.DoesNotContain("hidden-secret-text", repair.LastSnapshot, StringComparison.Ordinal);
        Assert.Equal("passed", sink.Summary!.Status);
        var saved = File.ReadAllText(suiteFile);
        Assert.Contains("#ok", saved, StringComparison.Ordinal);
        var again = new CountingRepair(_ => new LocatorSuggestion("#ok", 12));
        var second = Fixtures.Runner(new ScriptedFactory(), new FakeComputerUseProvider(), new RecordingExporter(), new RecordingDeadline(), root, reports, again);
        var reloaded = new SuiteLoader().Load(saved) with { Application = suite.Application };
        await second.RunAsync(reloaded, new RunRequest("repair-run-2", Fixtures.Policy(repair: true), reports, suiteFile), new ListSink(), CancellationToken.None);
        Assert.Equal(0, again.Calls);

        var blocked = new CountingRepair(_ => throw new InvalidOperationException("budget should skip the provider"));
        var tight = Fixtures.Runner(new ScriptedFactory(), new FakeComputerUseProvider(), new RecordingExporter(), new RecordingDeadline(), root, reports, blocked);
        var broken = suite with
        {
            Cases = [new TestCase("CASE-1", "点击", true, [Step("press", "click", selector: "#missing")])]
        };
        var blockedSink = new ListSink();
        await tight.RunAsync(broken, new RunRequest("budget-run", Fixtures.Policy(repair: true) with { RunTokenCap = 1 }, reports), blockedSink, CancellationToken.None);
        Assert.Equal(0, blocked.Calls);
        Assert.Contains(ExecutorError.BudgetExceeded, File.ReadAllText(Directory.GetFiles(reports, "*.md").Last()), StringComparison.Ordinal);
        Directory.Delete(root, true);
    }

    [Fact]
    public async Task Visual_diff_stays_local_and_honors_ignore_regions()
    {
        var blue = Solid(20, 20, new Rgba8(10, 20, 200, 255));
        var changed = Solid(20, 20, new Rgba8(10, 20, 200, 255));
        var image = PngImage.Decode(changed);
        image[1, 1] = new Rgba8(255, 0, 0, 255);
        changed = image.Encode();
        Assert.True(VisualDiff.MismatchRatio(changed, blue, 8, [new PixelRect(0, 0, 4, 4)]) == 0);
        Assert.True(VisualDiff.MismatchRatio(changed, blue, 8, []) > 0);

        await using var host = await SyntheticCdpHost.StartAsync();
        var root = Fixtures.Temp();
        var reports = Path.Combine(root, "reports");
        var baselines = Path.Combine(root, "baselines");
        Directory.CreateDirectory(baselines);
        var session = await CdpWebSession.ConnectAsync(host.Port, "合成页面", null, CancellationToken.None);
        var shot = await session!.PerformAsync(Step("shot", "screenshot"), CancellationToken.None);
        var masked = new ScreenshotMasker().Apply(shot.Png!, []);
        await File.WriteAllBytesAsync(Path.Combine(baselines, "shot.png"), masked.Png);
        await session.DisposeAsync();
        var runner = Fixtures.Runner(new ScriptedFactory(), new FakeComputerUseProvider(), new RecordingExporter(), new RecordingDeadline(), root, reports);
        var suite = new TestSuite("suite-visual", "视觉", false, true, new ApplicationTarget(null, "合成页面", host.Port, Attach: true), [], [
            Fixtures.Case("CASE-1", false, Step("shot", "screenshot", expected: "baseline"))
        ]);
        var sink = new ListSink();
        await runner.RunAsync(suite, new RunRequest("visual-run", Fixtures.Policy(), reports, BaselineDirectory: baselines), sink, CancellationToken.None);
        Assert.Equal("passed", sink.Summary!.Status);
        Assert.Equal(0, new FakeComputerUseProvider().ObserveCalls);
        Directory.Delete(root, true);
    }

    [Fact]
    public async Task Native_file_dialog_falls_back_to_flaui_without_an_image_model()
    {
        await using var host = await SyntheticCdpHost.StartAsync();
        var root = Fixtures.Temp();
        var reports = Path.Combine(root, "reports");
        var automation = new ScriptedFactory();
        var computer = new FakeComputerUseProvider();
        var runner = Fixtures.Runner(automation, computer, new RecordingExporter(), new RecordingDeadline(), root, reports);
        var suite = new TestSuite("suite-file", "文件", false, true, new ApplicationTarget(null, "合成页面", host.Port, Attach: true), [], [
            Fixtures.Case("CASE-1", true, new TestStep("pick", StepKind.Web, "open-file", new ElementSelector("打开", "1148", "#native-file"), null, "synthetic.txt", 15, true, false))
        ]);
        var sink = new ListSink();
        await runner.RunAsync(suite, new RunRequest("file-run", Fixtures.Policy(computer: true), reports), sink, CancellationToken.None);
        Assert.Contains(automation.Session.Actions, action => action.Name == "open-file");
        Assert.Equal(0, computer.StartCalls);
        Assert.Equal("passed", sink.Summary!.Status);
        Directory.Delete(root, true);
    }

    [Fact]
    public async Task Text_repair_provider_sends_no_image_and_a_closed_port_does_not_hang()
    {
        var handler = new StubHandler
        {
            Responder = (_, body) =>
            {
                Assert.DoesNotContain("image_url", body, StringComparison.Ordinal);
                Assert.DoesNotContain("input_image", body, StringComparison.Ordinal);
                Assert.Contains("selector=#ok", body, StringComparison.Ordinal);
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"choices":[{"message":{"content":"{\"selector\":\"#ok\"}"}}],"usage":{"total_tokens":11}}""", Encoding.UTF8, "application/json")
                };
            }
        };
        var provider = new OpenAiLocatorRepairProvider(new StaticRepairKey("zz-synthetic-credential-zz"), () => Fixtures.Policy(repair: true), handler);
        var suggestion = await provider.SuggestAsync(new LocatorRepairRequest("CASE-1", "press", "#missing", "[button] selector=#ok"), CancellationToken.None);
        Assert.Equal("#ok", suggestion!.Selector);
        Assert.Equal(11, suggestion.Tokens);
        Assert.Contains(handler.Authorizations, value => value.Contains("zz-synthetic-credential-zz", StringComparison.Ordinal));
        Assert.All(handler.Bodies, body => Assert.DoesNotContain("zz-synthetic-credential-zz", body, StringComparison.Ordinal));
        var closed = await CdpWebSession.ConnectAsync(1, null, null, CancellationToken.None);
        Assert.Null(closed);
    }

    private static string FindSyntheticDll()
    {
        var staged = Path.Combine(AppContext.BaseDirectory, "synthetic-cdp", "TestExecutor.SyntheticCdp.dll");
        if (File.Exists(staged)) return staged;
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var depth = 0; depth < 8 && dir is not null; depth++, dir = dir.Parent)
        {
            IEnumerable<string> hits;
            try
            {
                hits = Directory.EnumerateFiles(dir.FullName, "TestExecutor.SyntheticCdp.dll", SearchOption.AllDirectories);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                hits = [];
            }
            foreach (var hit in hits)
            {
                if (hit.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                    && !hit.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                    return hit;
            }
        }
        throw new FileNotFoundException("The synthetic CDP host was not built.", staged);
    }

    private static string DotnetMuxer()
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(directory, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
            if (File.Exists(candidate)) return candidate;
        }
        throw new InvalidOperationException("dotnet muxer was not found");
    }

    private static TestStep Step(string id, string action, string? selector = null, string? text = null, string? role = null, string? name = null, string? title = null, string? input = null, string? expected = null)
        => new(id, StepKind.Web, action, new ElementSelector(name, null, selector, text, role, title), expected, input, 15, true, false);

    private static byte[] Solid(int width, int height, Rgba8 color)
    {
        var rgba = new byte[width * height * 4];
        for (var index = 0; index < rgba.Length; index += 4)
        {
            rgba[index] = color.R;
            rgba[index + 1] = color.G;
            rgba[index + 2] = color.B;
            rgba[index + 3] = color.A;
        }
        return PngImage.Create(width, height, rgba).Encode();
    }

    private sealed class CountingRepair(Func<LocatorRepairRequest, LocatorSuggestion?> script) : ILocatorRepairProvider
    {
        public string Name => "counting";
        public int Calls { get; private set; }
        public string LastSnapshot { get; private set; } = "";

        public Task<LocatorSuggestion?> SuggestAsync(LocatorRepairRequest request, CancellationToken cancellationToken)
        {
            Calls++;
            LastSnapshot = request.Snapshot;
            return Task.FromResult(script(request));
        }
    }

    private sealed class StaticRepairKey(string? value) : IApiKeyStore
    {
        public string? Read(string target) => value;
    }
}
