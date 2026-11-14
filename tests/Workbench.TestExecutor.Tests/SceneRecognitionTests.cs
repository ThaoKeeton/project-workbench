using System.Text.Json;
using Workbench.TestExecutor;
using Xunit;

namespace Workbench.TestExecutor.Tests;

public sealed class SceneRecognitionTests
{
    [Fact]
    public void Scene_math_projects_css_pixels_and_device_pixels()
    {
        var center = SceneProjection.ToViewport(0, 0, new ViewportFrame(10, 20, 200, 100, 1, 1, 0, 0, false));
        Assert.Equal((110d, 70d), center);
        var device = SceneProjection.ToViewport(0, 0, new ViewportFrame(20, 40, 400, 200, 2, 1, 0, 0, true));
        Assert.Equal((110d, 70d), device);
        var zoomed = SceneProjection.ToViewport(0, 0, new ViewportFrame(40, 80, 800, 400, 2, 2, 0, 0, true));
        Assert.Equal((110d, 70d), zoomed);
        var shifted = SceneProjection.ToViewport(-1, 1, new ViewportFrame(8, 6, 40, 20, 1, 1, 3, 4, false));
        Assert.Equal((5d, 2d), shifted);
        var wheel = ScenePointer.Wheel(4, 5, 0, -120);
        Assert.Equal("mouseWheel", wheel[0].Type);
        Assert.Equal(-120, wheel[0].DeltaY);
        var path = ScenePointer.Along([(0, 0), (10, 0), (10, 4)]);
        Assert.Equal("mousePressed", path[0].Type);
        Assert.Equal("mouseMoved", path[1].Type);
        Assert.Equal(10, path[2].X);
        Assert.Equal("mouseReleased", path[^1].Type);
        var nudge = ScenePointer.Along([(3, 4)]);
        Assert.Equal(4, nudge[1].X);
        using var blocked = JsonDocument.Parse("{\"hit\":false,\"actual\":\"occluded-by:sphere\"}");
        Assert.True(SceneProjection.IsOccluded(blocked.RootElement, out var why));
        Assert.Equal("occluded-by:sphere", why);
        using var clear = JsonDocument.Parse("{\"hit\":true,\"points\":[{\"ndcX\":0,\"ndcY\":0},{\"ndcX\":0.5,\"ndcY\":0}],\"left\":0,\"top\":0,\"width\":100,\"height\":50}");
        Assert.False(SceneProjection.IsOccluded(clear.RootElement, out _));
        Assert.True(SceneProjection.TryReadPoints(clear.RootElement, out var points));
        Assert.Equal((50d, 25d), points[0]);
        Assert.Equal((75d, 25d), points[1]);
        using var incomplete = JsonDocument.Parse("{\"left\":0,\"top\":0,\"width\":10,\"height\":10}");
        Assert.False(SceneProjection.TryReadPoints(incomplete.RootElement, out _));
        var changed = PngImage.Create(2, 2, [9, 8, 7, 255, 9, 8, 7, 255, 9, 8, 7, 255, 1, 2, 3, 255]);
        var same = PngImage.Create(2, 2, [9, 8, 7, 255, 9, 8, 7, 255, 9, 8, 7, 255, 9, 8, 7, 255]);
        var diff = PngImage.Decode(VisualDiff.DiffImage(changed.Encode(), same.Encode(), 0, []));
        Assert.Equal(new Rgba8(255, 0, 0, 255), diff[1, 1]);
        Assert.Equal(new Rgba8(9, 8, 7, 255), diff[0, 0]);
    }

    [Fact]
    public void Snapshot_diff_reports_moves_and_hash_assertions_keep_schema_1()
    {
        const string Before = """
            {"mount":"devtools","camera":{"x":0,"y":4,"z":9,"fov":50,"azimuth":0},"nodes":[
              {"name":"cube","uuid":"cube-1","type":"Mesh","path":"Scene/cube","visible":1,"position":{"x":0,"y":0,"z":0},"rotation":{"x":0,"y":0,"z":0},"min":{"x":-1,"y":-1,"z":-1},"max":{"x":1,"y":1,"z":1},"vertexCount":24,"faceCount":12,"volume":8,"hash":"box2","vertices":[{"x":-1,"y":-1,"z":1}]}
            ]}
            """;
        var normalized = SceneSnapshot.Normalize(Before);
        Assert.Contains("\"schema\":\"workbench.test-executor.scene-snapshot.v1\"", normalized, StringComparison.Ordinal);
        Assert.Contains("\"schemaVersion\":1", normalized, StringComparison.Ordinal);
        Assert.Contains("\"actionDiff\"", SceneSnapshot.AttachDiff(normalized, "{\"schemaVersion\":1,\"counts\":{\"deformed\":1}}"), StringComparison.Ordinal);
        Assert.Equal("nope", SceneSnapshot.AttachDiff("nope", "{}"));
        Assert.Contains("library=three", SceneSnapshot.TextOf(normalized), StringComparison.Ordinal);
        Assert.True(SceneAssert.Matches(normalized, "mesh.cube.position.x=0±0.01;mesh.cube.rotation.y=0;mesh.cube.vertexCount=24;mesh.cube.faceCount=12;mesh.cube.volume=8±0.01;mesh.cube.hash=box2;mesh.cube.visible=1", 0, out _));
        const string After = """
            {"schema":"workbench.test-executor.scene-snapshot.v1","schemaVersion":1,"meshes":[
              {"name":"cube","uuid":"cube-1","type":"Mesh","path":"Scene/cube","position":{"x":0.8,"y":0,"z":0},"rotation":{"x":0,"y":0.4,"z":0},"vertexCount":24,"faceCount":12,"volume":8,"hash":"box2"}
            ]}
            """;
        var diff = SceneDiff.Compare(normalized, After, 0.01);
        Assert.Contains("\"schema\":\"workbench.test-executor.scene-diff.v1\"", diff, StringComparison.Ordinal);
        Assert.True(SceneAssert.Matches(diff, "counts.added=0;counts.removed=0;counts.moved=1;counts.deformed=1", 0, out var detail), detail);
        var same = SceneDiff.Compare(normalized, normalized, 0.01);
        Assert.True(SceneAssert.Matches(same, "counts.moved=0;counts.deformed=0", 0, out detail), detail);
    }

    [Fact]
    public void Probe_recommends_devtools_then_fallbacks()
    {
        var devtools = SceneProbeReport.FromSignals("""
            {"signals":{"devtoolsScenes":1,"devtoolsRenderers":1,"windowScene":false,"windowCamera":false,"windowRenderer":false,"r3fCanvases":0,"webglContexts":1,"globals":{"THREE":false,"scene":false,"workbenchHook":false}},"diagnostics":["no scene, camera, or renderer on window"]}
            """);
        Assert.Contains("\"schemaVersion\":1", devtools, StringComparison.Ordinal);
        Assert.Contains("\"recommended\":\"devtools\"", devtools, StringComparison.Ordinal);
        Assert.Contains("\"workbenchHook\":false", devtools, StringComparison.Ordinal);
        Assert.Contains("vertexCount", devtools, StringComparison.Ordinal);
        Assert.DoesNotContain("data:image", devtools, StringComparison.Ordinal);
        var fiber = SceneProbeReport.FromSignals("""{"signals":{"devtoolsScenes":0,"r3fCanvases":1,"windowScene":false}}""");
        Assert.Contains("\"recommended\":\"r3f\"", fiber, StringComparison.Ordinal);
        var window = SceneProbeReport.FromSignals("""{"signals":{"devtoolsScenes":0,"r3fCanvases":0,"windowScene":true}}""");
        Assert.Contains("\"recommended\":\"window\"", window, StringComparison.Ordinal);
        var webgl = SceneProbeReport.FromSignals("""{"signals":{"devtoolsScenes":0,"r3fCanvases":0,"windowScene":false,"webglContexts":2,"drawCalls":4}}""");
        Assert.Contains("\"recommended\":\"webgl\"", webgl, StringComparison.Ordinal);
        Assert.Contains("drawCalls", webgl, StringComparison.Ordinal);
        var none = SceneProbeReport.FromSignals("{}");
        Assert.Contains("\"recommended\":\"none\"", none, StringComparison.Ordinal);
        Assert.Contains("canvas.__r3f", none, StringComparison.Ordinal);
    }

    [Fact]
    public void Hook_is_read_only_and_scene_steps_stay_compatible()
    {
        Assert.Contains("window.__THREE_DEVTOOLS__", SceneHook.Source, StringComparison.Ordinal);
        Assert.Contains("addEventListener('observe'", SceneHook.Source, StringComparison.Ordinal);
        Assert.DoesNotContain("__workbenchScene", SceneHook.Source, StringComparison.Ordinal);
        Assert.Contains("drawArrays", SceneHook.Source, StringComparison.Ordinal);
        var expression = CdpExpression.Build(new { op = "probe", objectName = "cube" });
        Assert.Contains("canvas.__r3f", expression, StringComparison.Ordinal);
        Assert.False(CdpExpression.UsesAssignmentComma(expression));
        var suite = SuiteLoader.Load("""
            {"schema":"workbench.test-executor.suite.v2.preview","schemaVersion":2,"id":"scene","name":"场景","cases":[{"id":"c","name":"n","steps":[
              {"id":"probe","type":"scene3d","action":"probe"},
              {"id":"click","type":"scene3d","action":"click","target":{"uuid":"cube-1"}},
              {"id":"drag","type":"scene3d","action":"drag","target":{"path":"Scene/cube"},"input":"80,0"},
              {"id":"wheel","type":"scene3d","action":"wheel","target":{"object":"cube"},"input":"0,-120"}
            ]}]}
            """);
        Assert.Equal("cube-1", suite.Cases[0].Steps[1].Target!.Uuid);
        Assert.Equal("Scene/cube", suite.Cases[0].Steps[2].Target!.Path);
        Assert.Throws<ExecutorDataException>(() => SuiteLoader.Load("""
            {"schema":"workbench.test-executor.suite.v2.preview","schemaVersion":2,"id":"bad","name":"坏","cases":[{"id":"c","name":"n","steps":[{"id":"s","type":"scene3d","action":"click","target":{"selector":"#view"}}]}]}
            """));
    }

    [Fact]
    public void Sample_source_does_not_publish_a_workbench_hook()
    {
        var source = File.ReadAllText(Path.Combine(FixtureDirectory(), "renderer.js"));
        Assert.DoesNotContain("__workbenchScene", source, StringComparison.Ordinal);
        Assert.DoesNotContain("window.THREE", source, StringComparison.Ordinal);
        Assert.DoesNotContain("window.scene", source, StringComparison.Ordinal);
        Assert.Contains("from \"three\"", source, StringComparison.Ordinal);
        if (!BuildBundle()) return;
        var bundle = File.ReadAllText(Path.Combine(FixtureDirectory(), "app.js"));
        Assert.Contains("__THREE_DEVTOOLS__", bundle, StringComparison.Ordinal);
        Assert.DoesNotContain("__workbenchScene", bundle, StringComparison.Ordinal);
        Assert.DoesNotContain("window.THREE", bundle, StringComparison.Ordinal);
        Assert.DoesNotContain("window.scene", bundle, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Canvas_diff_stays_on_local_baselines()
    {
        await using var host = SyntheticCdpHost.StartOn(0, "合成页面", null);
        var session = await CdpWebSession.ConnectAsync(host.Port, "合成页面", null, CancellationToken.None);
        Assert.NotNull(session);
        await using var connected = session;
        var shot = await connected.PerformAsync(new TestStep("pixels", StepKind.Scene3d, "compare-pixels", new ElementSelector(null, null, "#view"), "baseline", null, 15, false, false), CancellationToken.None);
        Assert.NotNull(shot.Png);
        Assert.Equal("local-canvas", shot.Actual);
        Assert.DoesNotContain("data:image", shot.Actual, StringComparison.Ordinal);
        var root = Fixtures.Temp();
        var reports = Path.Combine(root, "reports");
        var baselines = Path.Combine(root, "baselines");
        Directory.CreateDirectory(baselines);
        var masked = ScreenshotMasker.Apply(shot.Png!, []);
        await File.WriteAllBytesAsync(Path.Combine(baselines, "pixels.png"), masked.Png);
        var computer = new FakeComputerUseProvider();
        var runner = Fixtures.Runner(new ScriptedFactory(), computer, new RecordingExporter(), new RecordingDeadline(), root, reports);
        var suite = new TestSuite("scene-pixels", "像素", false, true, new ApplicationTarget(null, "合成页面", host.Port, Attach: true), [], [
            new TestCase("CASE-1", "画布", false, [new TestStep("pixels", StepKind.Scene3d, "compare-pixels", new ElementSelector(null, null, "#view"), "baseline", null, 15, false, false)])
        ]);
        var sink = new ListSink();
        await runner.RunAsync(suite, new RunRequest("pixel-run", Fixtures.Policy(), reports, BaselineDirectory: baselines), sink, CancellationToken.None);
        Assert.Equal("passed", sink.Summary!.Status);
        Assert.Equal(0, computer.ObserveCalls);
        Assert.Contains("\"tokens\":0", await File.ReadAllTextAsync(Path.Combine(reports, "ai-usage.json")), StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(reports, "CASE-1-pixels.png")));
        Assert.True(File.Exists(Path.Combine(reports, "pixels.diff.png")));
        var evidence = await File.ReadAllTextAsync(Path.Combine(reports, "CASE-1-pixels.evidence.json"));
        Assert.Contains("local-diff=pixels.diff.png", evidence, StringComparison.Ordinal);
        Assert.DoesNotContain("data:image", evidence, StringComparison.Ordinal);
        var text = await connected.SceneTextAsync(CancellationToken.None);
        Assert.Contains("cube", text, StringComparison.Ordinal);
        Assert.DoesNotContain("data:image", text, StringComparison.Ordinal);
        var early = await connected.PerformAsync(new TestStep("early", StepKind.Scene3d, "diff", new ElementSelector(null, null, "#view"), null, null, 15, false, false), CancellationToken.None);
        Assert.False(early.Ok);
        Assert.Equal("no-earlier-snapshot", early.Actual);
        var wheeled = await connected.PerformAsync(new TestStep("roll", StepKind.Scene3d, "wheel", new ElementSelector(null, null, "#view", null, null, null, null, "cube"), null, "0,-40", 15, false, false), CancellationToken.None);
        Assert.True(wheeled.Ok, wheeled.Actual);
        var occluded = await connected.PerformAsync(new TestStep("blocked", StepKind.Scene3d, "click", new ElementSelector(null, null, "#view", null, null, null, null, "hidden"), null, null, 15, false, false), CancellationToken.None);
        Assert.False(occluded.Ok);
        Assert.Equal("occluded-by:sphere", occluded.Actual);
        Directory.Delete(root, true);
    }

    [Fact]
    public async Task Probe_command_writes_a_local_report_and_rejects_a_bad_port()
    {
        Assert.Equal(2, await SceneProbeCli.RunAsync(["--port", "0"]));
        Assert.Equal(2, await SceneProbeCli.RunAsync(["--nope"]));
        await using var host = SyntheticCdpHost.StartOn(0, "合成页面", null);
        var root = Fixtures.Temp();
        var output = Path.Combine(root, "scene-probe.json");
        var code = await SceneProbeCli.RunAsync(["--port", host.Port.ToString(System.Globalization.CultureInfo.InvariantCulture), "--out", output, "--title", "合成页面", "--reload"]);
        Assert.Equal(0, code);
        var report = await File.ReadAllTextAsync(output);
        Assert.Contains("\"recommended\":\"devtools\"", report, StringComparison.Ordinal);
        Assert.DoesNotContain("data:image", report, StringComparison.Ordinal);
        Directory.Delete(root, true);
    }

    [Fact]
    public async Task Unreadable_scene_documents_and_bad_probe_arguments_stay_safe()
    {
        Assert.Equal("nope", SceneSnapshot.Normalize("nope"));
        Assert.Equal("[]", SceneSnapshot.Normalize("[]"));
        Assert.Equal("", SceneSnapshot.TextOf("nope"));
        Assert.Equal("", SceneSnapshot.TextOf("{}"));
        Assert.Contains("\"recommended\":\"none\"", SceneProbeReport.FromSignals("nope"), StringComparison.Ordinal);
        Assert.Contains("\"schemaVersion\":1", SceneDiff.Compare("nope", "{\"meshes\":[{\"name\":\"only\"}]}", 0.01), StringComparison.Ordinal);
        using var partial = JsonDocument.Parse("{\"ndcX\":0,\"width\":10}");
        Assert.False(SceneProjection.TryRead(partial.RootElement, out _, out _, out _));
        Assert.Equal(2, await SceneProbeCli.RunAsync(["--port", "nope"]));
        Assert.Equal(2, await SceneProbeCli.RunAsync(["--port", "70000"]));
        Assert.Equal(2, await SceneProbeCli.RunAsync(["--out"]));
    }

    private static bool BuildBundle()
    {
        var directory = FixtureDirectory();
        var esbuild = Path.Combine(directory, "node_modules", "esbuild", "package.json");
        if (!File.Exists(esbuild))
        {
            if (Environment.GetEnvironmentVariable("EXECUTOR_ELECTRON") == "1")
                throw new InvalidOperationException("esbuild is not installed for the synthetic sample.");
            return false;
        }
        var start = new System.Diagnostics.ProcessStartInfo("node")
        {
            WorkingDirectory = directory,
            UseShellExecute = false
        };
        start.ArgumentList.Add("build.mjs");
        using var process = System.Diagnostics.Process.Start(start) ?? throw new InvalidOperationException("node did not start");
        process.WaitForExit();
        if (process.ExitCode != 0) throw new InvalidOperationException("esbuild failed");
        return true;
    }

    private static string FixtureDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var depth = 0; depth < 8 && dir is not null; depth++, dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "tests", "fixtures", "TestExecutor.SyntheticElectron");
            if (File.Exists(Path.Combine(candidate, "renderer.js"))) return candidate;
            candidate = Path.Combine(dir.FullName, "fixtures", "TestExecutor.SyntheticElectron");
            if (File.Exists(Path.Combine(candidate, "renderer.js"))) return candidate;
        }
        throw new DirectoryNotFoundException("Synthetic Electron fixture was not found.");
    }
}
