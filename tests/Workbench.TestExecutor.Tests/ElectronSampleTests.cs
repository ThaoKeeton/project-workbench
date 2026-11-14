using Workbench.TestExecutor;
using Xunit;

namespace Workbench.TestExecutor.Tests;

[Collection("synthetic-electron")]
public sealed class ElectronSampleTests
{
    [Fact]
    public void Cdp_expression_is_a_function_and_scene_numbers_allow_tolerance()
    {
        var expression = CdpExpression.Build(new { op = "wait-for", selector = "#status" });
        Assert.StartsWith("(() => {", expression, StringComparison.Ordinal);
        Assert.Contains("const request = ", expression, StringComparison.Ordinal);
        Assert.Contains("el.click()", expression, StringComparison.Ordinal);
        Assert.False(CdpExpression.UsesAssignmentComma(expression));
        Assert.False(CdpExpression.UsesAssignmentComma("(() => { const request = {\"op\":\"wait-for\"}; return 1; })()"));
        Assert.True(CdpExpression.UsesAssignmentComma("(x = document.querySelector('#a');, x.click())"));

        const string Scene = """
            {"camera":{"x":0,"y":4,"z":8,"fov":50,"azimuth":0},"meshes":[{"name":"cube","min":{"x":-1,"y":-1,"z":-1},"max":{"x":1,"y":1,"z":1},"vertices":[{"x":-1,"y":-1,"z":1}]}]}
            """;
        Assert.True(SceneAssert.Matches(Scene, "camera.y=4±0.2;mesh.cube.size=2±0.05;mesh.cube.vertex.0.z=1±0", 0, out _));
        Assert.False(SceneAssert.Matches(Scene, "camera.azimuth=2±0.1", 0, out var detail));
        Assert.Contains("camera.azimuth=", detail, StringComparison.Ordinal);
        Assert.False(SceneAssert.Matches("not-json", "camera.y=4", 0, out var unreadable));
        Assert.Equal("scene-unreadable", unreadable);
        Assert.False(SceneAssert.Matches(Scene, "camera", 0, out var bare));
        Assert.Contains("camera", bare, StringComparison.Ordinal);
        Assert.False(SceneAssert.Matches(Scene, "camera.y=nope", 0.1, out _));
        Assert.False(SceneAssert.Matches(Scene, "mesh.missing.size=1", 0, out _));
        Assert.False(SceneAssert.Matches(Scene, "mesh.cube.vertex.9.x=0", 0, out _));
        Assert.True(SceneAssert.Matches(Scene, "mesh.cube.min.x=-1", 0, out _));
        Assert.False(SceneAssert.Matches("{\"camera\":{\"y\":\"tall\"}}", "camera.y=1", 0, out _));
        var drag = ScenePointer.Drag(10, 20, 80, 0);
        Assert.Equal("mousePressed", drag[0].Type);
        Assert.Equal("mouseReleased", drag[^1].Type);
        Assert.Equal(90, drag[^1].X);
        Assert.Equal((40d, 10d), ScenePointer.ParseDelta("40,10"));
    }

    [Fact]
    public void Web_steps_wait_long_enough_for_a_blocked_renderer()
    {
        var suite = SuiteLoader.Load("""
            {"schema":"workbench.test-executor.suite.v2.preview","schemaVersion":2,"id":"blocked","name":"阻塞","popupSelector":".ant-modal","cases":[{"id":"CASE-1","name":"等待","steps":[{"id":"wait","type":"web","action":"wait-for","target":{"selector":"#status"}},{"id":"orbit","type":"scene3d","action":"orbit","target":{"selector":"#view","object":"cube"}}]}]}
            """);
        Assert.Equal(".ant-modal", suite.PopupSelector);
        Assert.Equal(ExecutorSchemas.RendererBlockSeconds, suite.Cases[0].Steps[0].TimeoutSeconds);
        Assert.Equal(StepKind.Scene3d, suite.Cases[0].Steps[1].Kind);
        Assert.Equal("cube", suite.Cases[0].Steps[1].Target!.ObjectName);
        Assert.Equal(ExecutorSchemas.RendererBlockSeconds, suite.Cases[0].Steps[1].TimeoutSeconds);
        Assert.Throws<ExecutorDataException>(() => SuiteLoader.Load("""
            {"schema":"workbench.test-executor.suite.v2.preview","schemaVersion":2,"id":"bad","name":"坏","cases":[{"id":"c","name":"n","steps":[{"id":"s","type":"scene3d","action":"click","target":{"selector":"#view"}}]}]}
            """));
        Assert.Throws<ExecutorDataException>(() => SuiteLoader.Load("""
            {"schema":"workbench.test-executor.suite.v2.preview","schemaVersion":2,"id":"bad2","name":"坏","cases":[{"id":"c","name":"n","steps":[{"id":"s","type":"web","action":"switch-window"}]}]}
            """));
        Assert.Throws<ExecutorDataException>(() => SuiteLoader.Load("""
            {"schema":"workbench.test-executor.suite.v2.preview","schemaVersion":2,"id":"bad3","name":"坏","cases":[{"id":"c","name":"n","steps":[{"id":"s","type":"scene3d","action":"orbit","tolerance":-1,"target":{"object":"cube"}}]}]}
            """));
    }

    [Fact]
    public async Task Screenshot_falls_back_when_the_debugger_refuses_capture()
    {
        await using var host = SyntheticCdpHost.StartOn(0, "合成页面", "no-shot");
        var session = await CdpWebSession.ConnectAsync(host.Port, "合成页面", null, CancellationToken.None);
        Assert.NotNull(session);
        await using var connected = session;
        var png = await connected.CapturePngAsync(CancellationToken.None);
        Assert.NotNull(png);
        var image = PngImage.Decode(png);
        Assert.Equal(8, image.Width);
        Assert.Equal(new Rgba8(9, 8, 7, 255), image[0, 0]);
    }

    [Fact]
    public async Task Synthetic_page_runs_click_retry_popup_and_scene()
    {
        await using var host = SyntheticCdpHost.StartOn(0, "合成电子样品", null);
        await RunSampleAsync(new ApplicationTarget(null, "合成电子样品", host.Port, Attach: true));
    }

    [Fact]
    public async Task Electron_sample_records_a_case_and_replays_it()
    {
        var electron = FindElectron();
        if (electron is not null)
        {
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(electron, File.GetUnixFileMode(electron) | UnixFileMode.UserExecute);
            var launched = await ElectronTargetLauncher.LaunchAsync(new ApplicationTarget(null, "合成电子样品", null, electron, [FixtureDirectory()], new Dictionary<string, string> { ["ELECTRON_DISABLE_SANDBOX"] = "1" }, Url: "index.html"), CancellationToken.None);
            await using var owned = launched;
            await RecordAndReplayAsync(launched.Port);
            return;
        }
        if (Environment.GetEnvironmentVariable("EXECUTOR_ELECTRON") == "1")
            throw new InvalidOperationException("The synthetic Electron binary was not installed.");
        await using var host = SyntheticCdpHost.StartOn(0, "合成电子样品", null);
        await RecordAndReplayAsync(host.Port);
    }

    private static async Task RecordAndReplayAsync(int port)
    {
        var session = await CdpWebSession.ConnectAsync(port, "合成电子样品", null, CancellationToken.None);
        Assert.NotNull(session);
        await using var connected = session!;
        var snapshot = "";
        var ready = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < ready)
        {
            snapshot = await connected.SnapshotAsync(4096, CancellationToken.None);
            if (snapshot.Contains("#start", StringComparison.Ordinal)) break;
            await Task.Delay(100, CancellationToken.None);
        }
        Assert.Contains("#start", snapshot, StringComparison.Ordinal);
        Assert.DoesNotContain("data:image", snapshot, StringComparison.Ordinal);
        await connected.StartRecordingAsync(CancellationToken.None);
        var click = new TestStep("tap", StepKind.Web, "click", new ElementSelector(null, null, "#start", "开始"), "已开始", null, 15, false, false);
        var tapped = await connected.PerformAsync(click, CancellationToken.None);
        Assert.True(tapped.Ok, tapped.Actual);
        var recorded = CdpStepRecorder.ToSteps(await connected.ReadRecordingAsync(CancellationToken.None));
        Assert.Contains(recorded, step => step.Action == "click" && step.Target?.Selector == "#start");
        var steps = recorded.Append(new TestStep("status", StepKind.Web, "assert-text", new ElementSelector(null, null, "#status"), "已开始", null, 15, false, false)).ToArray();
        var suite = new TestSuite("recorded", "录制回放", false, true, new ApplicationTarget(null, "合成电子样品", port, Attach: true), [], [new TestCase("CASE-REC", "录制", false, steps)]);
        var root = Fixtures.Temp();
        var reports = Path.Combine(root, "reports");
        var sink = new ListSink();
        await Fixtures.Runner(new ScriptedFactory(), new FakeComputerUseProvider(), new RecordingExporter(), new RecordingDeadline(), root, reports)
            .RunAsync(suite, new RunRequest("record-replay", Fixtures.Policy(), reports), sink, CancellationToken.None);
        Assert.Contains(sink.Steps, step => step.StepId == "rec-1" && step.Status == "passed");
        Assert.Contains(sink.Steps, step => step.StepId == "status" && step.Status == "passed");
        Directory.Delete(root, true);
    }

    [Fact]
    public async Task Electron_sample_runs_the_cdp_suite()
    {
        var electron = FindElectron();
        if (electron is null)
        {
            if (Environment.GetEnvironmentVariable("EXECUTOR_ELECTRON") == "1")
                throw new InvalidOperationException("The synthetic Electron binary was not installed.");
            return;
        }
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(electron, File.GetUnixFileMode(electron) | UnixFileMode.UserExecute);
        var directory = FixtureDirectory();
        var application = new ApplicationTarget(null, "合成电子样品", null, electron, [directory], new Dictionary<string, string> { ["ELECTRON_DISABLE_SANDBOX"] = "1" }, Url: "index.html");
        // The page can miss the first probe while Electron is still painting.
        // A fresh launch settles that. Two misses in a row still get one more try.
        Xunit.Sdk.XunitException? missed = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                await RunSampleAsync(application);
                return;
            }
            catch (Xunit.Sdk.XunitException error) when (error.Message.Contains("ControlNotFound", StringComparison.Ordinal))
            {
                missed = error;
                await Task.Delay(TimeSpan.FromSeconds(1));
            }
        }
        throw missed!;
    }

    private static async Task RunSampleAsync(ApplicationTarget application)
    {
        var loaded = SuiteLoader.Load(File.ReadAllText(Path.Combine(FixtureDirectory(), "suite.json")));
        var suite = loaded with { Application = application };
        var root = Fixtures.Temp();
        var reports = Path.Combine(root, "reports");
        var computer = new FakeComputerUseProvider();
        var runner = Fixtures.Runner(new ScriptedFactory(), computer, new RecordingExporter(), new RecordingDeadline(), root, reports);
        var sink = new ListSink();
        await runner.RunAsync(suite, new RunRequest("electron-run", Fixtures.Policy(), reports), sink, CancellationToken.None);
        Assert.Equal(0, computer.ObserveCalls);
        Assert.Contains("\"tokens\":0", File.ReadAllText(Path.Combine(reports, "ai-usage.json")), StringComparison.Ordinal);
        var trace = string.Join(" || ", sink.Steps.Select(step => step.StepId + "=" + step.Status + "/" + step.ErrorType + "/" + step.Message));
        Assert.True(sink.Steps.Any(step => step.StepId == "start" && step.Status == "passed" && step.Engine == "web"), trace);
        Assert.True(sink.Steps.Any(step => step.StepId == "status" && step.Status == "passed"), trace);
        Assert.True(sink.Steps.Any(step => step.StepId == "probe" && step.Status == "passed" && step.Engine == "scene3d"), trace);
        Assert.True(sink.Steps.Any(step => step.StepId == "read-camera" && step.Status == "passed" && step.Engine == "scene3d"), trace);
        Assert.True(sink.Steps.Any(step => step.StepId == "drag-cube" && step.Status == "passed" && step.Engine == "scene3d"), trace);
        Assert.True(sink.Steps.Any(step => step.StepId == "diff-cube" && step.Status == "passed" && step.Engine == "scene3d"), trace);
        Assert.True(sink.Steps.Any(step => step.StepId == "orbit" && step.Status == "passed" && step.Engine == "scene3d"), trace);
        Assert.True(sink.Steps.Any(step => step.StepId == "cut-drag" && step.Status == "passed" && step.Engine == "scene3d"), trace);
        Assert.True(sink.Steps.Any(step => step.StepId == "cut-diff" && step.Status == "passed" && step.Engine == "scene3d"), trace);
        using (var evidence = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(reports, "CASE-SCENE-probe.evidence.json"))))
        {
            var log = string.Join('\n', evidence.RootElement.GetProperty("log").EnumerateArray().Select(item => item.GetString()));
            Assert.Contains("\"recommended\":\"devtools\"", log, StringComparison.Ordinal);
            Assert.Contains("\"workbenchHook\":false", log, StringComparison.Ordinal);
        }
        Assert.True(sink.Steps.Any(step => step.StepId == "open-modal" && step.Status == "failed" && step.ErrorType == ExecutorError.UnexpectedPopup), trace);
        foreach (var name in new[] { "CASE-UI-wait-start", "CASE-UI-start", "CASE-UI-status", "CASE-UI-shot-ui", "CASE-SCENE-read-camera", "CASE-SCENE-orbit", "CASE-SCENE-shot-scene" })
            Assert.True(File.Exists(Path.Combine(reports, name + ".png")), name);
        var sceneShot = Path.Combine(reports, "CASE-SCENE-shot-scene.png");
        var png = await File.ReadAllBytesAsync(sceneShot);
        Assert.Equal(0, VisualDiff.MismatchRatio(png, png, 0, []));
        var mutated = PngImage.Decode(png);
        mutated[0, 0] = new Rgba8(255, 0, 0, 255);
        Assert.True(VisualDiff.MismatchRatio(mutated.Encode(), png, 0, []) > 0);
        var report = System.Text.Json.JsonSerializer.Deserialize<FailureReport>(await File.ReadAllTextAsync(Path.Combine(reports, "CASE-POPUP-open-modal-UnexpectedPopup.json")), new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        Assert.Equal("合成弹窗：请先完成加载", report!.Actual);
        var markdown = await File.ReadAllTextAsync(Path.Combine(reports, "CASE-POPUP-open-modal-UnexpectedPopup.md"));
        Assert.Contains("合成弹窗：请先完成加载", markdown, StringComparison.Ordinal);
        Directory.Delete(root, true);
    }

    private static string? FindElectron()
    {
        var directory = FixtureDirectory();
        foreach (var name in new[] { "electron", "electron.exe" })
        {
            var candidate = Path.Combine(directory, "node_modules", "electron", "dist", name);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    private static string FixtureDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var depth = 0; depth < 8 && dir is not null; depth++, dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "tests", "fixtures", "TestExecutor.SyntheticElectron");
            if (File.Exists(Path.Combine(candidate, "suite.json"))) return candidate;
            candidate = Path.Combine(dir.FullName, "fixtures", "TestExecutor.SyntheticElectron");
            if (File.Exists(Path.Combine(candidate, "suite.json"))) return candidate;
        }
        throw new DirectoryNotFoundException("Synthetic Electron fixture was not found.");
    }
}
