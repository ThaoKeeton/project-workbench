using System.Net;
using System.Text.Json;
using Workbench.TestExecutor;
using Xunit;

namespace Workbench.TestExecutor.Tests;

[Collection("synthetic-electron")]
public sealed class ScriptAuthorTests
{
    private const string Goal = "切削工件后体积减小，邻件不变";
    private const string Dom = """
        [button] name=开始 text=开始 selector=#start
        [status] name=status text= selector=#status
        [canvas] name=view text= selector=#view object=stock
        """;
    private const string Snapshot = """
        {"library":"three","mount":"devtools","nodes":[
        {"name":"stock","path":"Scene/stock","type":"Mesh","uuid":"stock-1","visible":1,"position":{"x":0,"y":2,"z":0},"min":{"x":-1,"y":1,"z":-1},"max":{"x":1,"y":3,"z":1},"vertexCount":24,"faceCount":12,"volume":8,"hash":"box2"},
        {"name":"sphere","path":"Scene/sphere","type":"Mesh","uuid":"sphere-1","visible":1,"position":{"x":3,"y":0,"z":0},"min":{"x":2.2,"y":-0.8,"z":-0.8},"max":{"x":3.8,"y":0.8,"z":0.8},"vertexCount":117,"faceCount":96,"volume":2.144,"hash":"sph1"}]}
        """;
    private const string Key = "synthetic-author-key";

    [Fact]
    public async Task Electron_sample_authors_a_script_from_text_and_replays_it()
    {
        var preview = Fixtures.Temp();
        WriteGate(preview, true, 1, 0.01m);
        var model = new FixedScriptModel();
        var author = new ScriptAuthor(model, new AuthorSecrets(_ => null));
        var draft = await author.GenerateAsync(Sample(), preview, CancellationToken.None);
        Assert.Equal("review", draft.Status);
        Assert.Equal(AuthorMessages.Review, draft.Message);
        Assert.Equal(1, model.Calls);
        Assert.Equal(1, draft.ModelCalls);
        Assert.Contains("不要添加测试钩子", draft.Prompt, StringComparison.Ordinal);
        Assert.Contains("不要要求修改目标应用", draft.Prompt, StringComparison.Ordinal);
        Assert.Contains("schemaVersion 必须是 1", draft.Prompt, StringComparison.Ordinal);
        Assert.Contains("workbench.test-executor.scene-snapshot.v1", draft.Prompt, StringComparison.Ordinal);
        Assert.Contains("uuid=stock-1", draft.Prompt, StringComparison.Ordinal);
        Assert.Contains("boxMin=", draft.Prompt, StringComparison.Ordinal);
        Assert.Contains("hash=box2", draft.Prompt, StringComparison.Ordinal);
        Assert.Contains("vol=8", draft.Prompt, StringComparison.Ordinal);
        Assert.Contains("verts=117", draft.Prompt, StringComparison.Ordinal);
        Assert.Contains(Goal, draft.Prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("data:image", draft.Prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("input_image", draft.Prompt, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(preview, "confirmed-script.json")));
        ScriptAuthor.SaveConfirmed(preview, draft.ScriptJson!);
        var saved = await File.ReadAllTextAsync(Path.Combine(preview, "confirmed-script.json"));
        var suite = ScriptAuthor.ToSuite(saved);
        Assert.Contains(suite.Cases[0].Steps, step => step.Action == "drag" && step.Input == "world-path:0,2,0;0.4,2,0");
        Assert.Contains(suite.Cases[0].Steps, step => step.Kind == StepKind.Web && step.Action == "click");
        await ReplayAsync(suite);
        Assert.DoesNotContain("data:image", string.Join('\n', Directory.GetFiles(Path.Combine(preview, "author-log")).Select(File.ReadAllText)), StringComparison.Ordinal);
        Directory.Delete(preview, true);
    }

    [Fact]
    public async Task Electron_sample_authoring_stays_off_until_a_person_confirms()
    {
        var preview = Fixtures.Temp();
        var model = new FixedScriptModel();
        var closed = await new ScriptAuthor(model, new AuthorSecrets(_ => null)).GenerateAsync(Sample(), preview, CancellationToken.None);
        Assert.Equal(AuthorMessages.Disabled, closed.Message);
        Assert.Equal(0, model.Calls);
        Assert.Equal(0, closed.ModelCalls);
        Assert.Null(closed.ScriptJson);
        Assert.False(File.Exists(Path.Combine(preview, "confirmed-script.json")));

        var image = Sample() with { SceneSnapshot = Snapshot + "\ndata:image/png;base64,AAAA" };
        var refused = await new ScriptAuthor(model, new AuthorSecrets(_ => null)).GenerateAsync(image, preview, CancellationToken.None);
        Assert.Equal(AuthorMessages.Image, refused.Message);
        Assert.Equal(0, model.Calls);
        Assert.DoesNotContain("base64,AAAA", string.Join('\n', Directory.GetFiles(Path.Combine(preview, "author-log")).Select(File.ReadAllText)), StringComparison.Ordinal);

        WriteGate(preview, true, 1, 0.01m, Key);
        var leaked = await new ScriptAuthor(model, new AuthorSecrets(_ => null)).GenerateAsync(Sample() with { DomText = Dom + " " + Key }, preview, CancellationToken.None);
        Assert.Equal(AuthorMessages.KeyLeak, leaked.Message);
        Assert.Equal(0, model.Calls);
        Assert.DoesNotContain(Key, leaked.Prompt, StringComparison.Ordinal);
        Assert.Contains("[redacted]", leaked.Prompt, StringComparison.Ordinal);
        foreach (var log in Directory.GetFiles(Path.Combine(preview, "author-log")))
            Assert.DoesNotContain(Key, await File.ReadAllTextAsync(log), StringComparison.Ordinal);

        File.Delete(Path.Combine(preview, "author.local.json"));
        var repair = await new ScriptAuthor(model, new AuthorSecrets(_ => null)).RepairAsync(Sample() with { FailedStep = "cut-drag drag stock", DiffText = "counts.deformed=0;counts.moved=0" }, preview, CancellationToken.None);
        Assert.Equal(AuthorMessages.Disabled, repair.Message);
        Assert.Equal(0, model.Calls);
        Assert.Contains("失败步骤:", repair.Prompt, StringComparison.Ordinal);
        Assert.Contains("差异:", repair.Prompt, StringComparison.Ordinal);
        Assert.Contains("counts.deformed=0", repair.Prompt, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(preview, "confirmed-script.json")));
        Directory.Delete(preview, true);
    }

    [Fact]
    public async Task Electron_sample_authoring_refuses_a_billed_call_over_the_daily_cap()
    {
        var preview = Fixtures.Temp();
        var prompt = AuthorPrompt.Build(Sample());
        var estimate = ScriptAuthor.Estimate(prompt, 20m);
        WriteGate(preview, true, estimate + 0.01m, 20m, Key);
        var model = new PricedModel();
        var clock = new ManualClock();
        var author = new ScriptAuthor(model, new AuthorSecrets(_ => null), clock);
        var first = await author.GenerateAsync(Sample(), preview, CancellationToken.None);
        Assert.Equal("review", first.Status);
        Assert.Equal(1, model.Calls);
        var second = await author.GenerateAsync(Sample(), preview, CancellationToken.None);
        Assert.Equal(AuthorMessages.OverBudget, second.Message);
        Assert.Equal(1, model.Calls);
        Assert.False(File.Exists(Path.Combine(preview, "confirmed-script.json")));
        clock.Now = clock.Now.AddDays(1);
        var third = await author.GenerateAsync(Sample(), preview, CancellationToken.None);
        Assert.Equal("review", third.Status);
        Assert.Equal(2, model.Calls);
        var blocked = Fixtures.Temp();
        WriteGate(blocked, true, 0, 0.01m, Key);
        var handler = new StubHandler { Responder = (_, _) => throw new InvalidOperationException("cap zero must not send") };
        var none = await new ScriptAuthor(new OpenAiScriptModel(handler), new AuthorSecrets(_ => null)).GenerateAsync(Sample(), blocked, CancellationToken.None);
        Assert.Equal(AuthorMessages.OverBudget, none.Message);
        Assert.Equal(0, handler.Calls);
        Directory.Delete(preview, true);
        Directory.Delete(blocked, true);
    }

    [Fact]
    public async Task Electron_sample_authoring_sends_text_only_and_keeps_schema_1()
    {
        var preview = Fixtures.Temp();
        WriteGate(preview, true, 5, 0.01m, Key);
        var handler = new StubHandler
        {
            Responder = (_, body) =>
            {
                Assert.DoesNotContain("image_url", body, StringComparison.Ordinal);
                Assert.DoesNotContain("input_image", body, StringComparison.Ordinal);
                Assert.DoesNotContain("data:image", body, StringComparison.Ordinal);
                Assert.DoesNotContain(Key, body, StringComparison.Ordinal);
                var fenced = "```json\n" + FixedScriptModel.Script.Trim() + "\n```";
                var payload = JsonSerializer.Serialize(new
                {
                    choices = new[] { new { message = new { content = fenced } } },
                    usage = new { total_tokens = 12 }
                });
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(payload) };
            }
        };
        var draft = await new ScriptAuthor(new OpenAiScriptModel(handler), new AuthorSecrets(_ => null)).GenerateAsync(Sample(), preview, CancellationToken.None);
        Assert.Equal("review", draft.Status);
        Assert.Equal(1, handler.Calls);
        Assert.Equal("Bearer " + Key, handler.Authorizations[0]);
        Assert.Contains("cut-drag", draft.ScriptJson, StringComparison.Ordinal);
        foreach (var log in Directory.GetFiles(Path.Combine(preview, "author-log")))
            Assert.DoesNotContain(Key, await File.ReadAllTextAsync(log), StringComparison.Ordinal);
        var spend = Path.Combine(preview, "author-spend.json");
        if (File.Exists(spend)) Assert.DoesNotContain(Key, await File.ReadAllTextAsync(spend), StringComparison.Ordinal);

        var missing = Fixtures.Temp();
        WriteGate(missing, true, 5, 0.01m);
        var quiet = new StubHandler { Responder = (_, _) => throw new InvalidOperationException("missing key must not send") };
        var noKey = await new ScriptAuthor(new OpenAiScriptModel(quiet), new AuthorSecrets(_ => null)).GenerateAsync(Sample(), missing, CancellationToken.None);
        Assert.Equal(AuthorMessages.MissingKey, noKey.Message);
        Assert.Equal(0, quiet.Calls);

        Assert.Equal("{\"a\":1}", OpenAiScriptModel.Unwrap("```json\n{\"a\":1}\n```"));
        var minimal = """
            {"schema":"workbench.test-executor.authored-script.v1","schemaVersion":1,"id":"CASE-MIN","name":"最小","steps":[{"id":"wait","type":"web","action":"wait-for","target":{"text":"开始"}}]}
            """;
        Assert.Equal("CASE-MIN", ScriptAuthor.ToSuite(minimal).Cases[0].Id);
        Assert.Throws<ExecutorDataException>(() => ScriptAuthor.ToSuite(minimal.Replace("\"schemaVersion\":1", "\"schemaVersion\":2", StringComparison.Ordinal)));
        Assert.Throws<ExecutorDataException>(() => ScriptAuthor.ToSuite(minimal.Replace("\"action\":\"wait-for\"", "\"action\":\"drag\"", StringComparison.Ordinal)));
        Assert.Throws<ExecutorDataException>(() => ScriptAuthor.ToSuite("{\"note\":\"请添加测试钩子\"}"));
        var broken = new PricedModel { Text = "{\"note\":\"not a script\"}" };
        WriteGate(preview, true, 20, 0.01m, Key);
        var invalid = await new ScriptAuthor(broken, new AuthorSecrets(_ => null)).GenerateAsync(Sample(), preview, CancellationToken.None);
        Assert.Equal(AuthorMessages.Invalid, invalid.Message);
        Assert.Null(invalid.ScriptJson);
        Assert.False(File.Exists(Path.Combine(preview, "confirmed-script.json")));
        Directory.Delete(preview, true);
        Directory.Delete(missing, true);
    }

    [Fact]
    public async Task Electron_sample_authoring_rejects_empty_goals_and_failed_transport()
    {
        var preview = Fixtures.Temp();
        var model = new FixedScriptModel();
        var author = new ScriptAuthor(model, new AuthorSecrets(_ => null));
        var empty = await author.GenerateAsync(Sample() with { Goal = "  " }, preview, CancellationToken.None);
        Assert.Equal(AuthorMessages.Empty, empty.Message);
        Assert.Equal(0, model.Calls);
        var noFailure = await author.RepairAsync(Sample(), preview, CancellationToken.None);
        Assert.Equal(AuthorMessages.NoFailure, noFailure.Message);
        WriteGate(preview, true, 5, 0.01m);
        var missingModel = await new ScriptAuthor(null, new AuthorSecrets(_ => null)).GenerateAsync(Sample(), preview, CancellationToken.None);
        Assert.Equal(AuthorMessages.MissingModel, missingModel.Message);
        WriteGate(preview, true, 5, 0.01m, "local-file-key");
        var handler = new StubHandler
        {
            Responder = (_, _) => new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("no") }
        };
        var failed = await new ScriptAuthor(new OpenAiScriptModel(handler), new AuthorSecrets(name => name == AuthorSecrets.EnvironmentVariable ? Key : null))
            .GenerateAsync(Sample(), preview, CancellationToken.None);
        Assert.Equal(AuthorMessages.Unavailable, failed.Message);
        Assert.Equal("Bearer " + Key, handler.Authorizations[0]);
        File.WriteAllText(Path.Combine(preview, "author.local.json"), "{");
        var corrupt = await new ScriptAuthor(new FixedScriptModel(), new AuthorSecrets(_ => null)).GenerateAsync(Sample(), preview, CancellationToken.None);
        Assert.Equal(AuthorMessages.Disabled, corrupt.Message);
        Assert.Throws<ExecutorDataException>(() => ScriptAuthor.SaveConfirmed(preview, "{\"note\":\"请添加测试钩子\"}"));
        Assert.False(File.Exists(Path.Combine(preview, "confirmed-script.json")));
        Directory.Delete(preview, true);
    }

    private static AuthorRequest Sample() => new(Goal, Dom, Snapshot);

    private static void WriteGate(string directory, bool enabled, decimal cap, decimal rate, string? key = null)
    {
        Directory.CreateDirectory(directory);
        var keyField = key is null ? "" : ",\"apiKey\":\"" + key + "\"";
        File.WriteAllText(Path.Combine(directory, "author.local.json"),
            "{\"schema\":\"workbench.test-executor.author-local.v1\",\"schemaVersion\":1,\"enabled\":" + (enabled ? "true" : "false")
            + ",\"dailySpendCap\":" + cap.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + ",\"costPerThousandTokens\":" + rate.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + keyField + "}");
    }

    private static async Task ReplayAsync(TestSuite authored)
    {
        var electron = FindElectron();
        SyntheticCdpHost? host = null;
        ApplicationTarget application;
        if (electron is not null)
        {
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(electron, File.GetUnixFileMode(electron) | UnixFileMode.UserExecute);
            application = new ApplicationTarget(null, "合成电子样品", null, electron, [FixtureDirectory()], new Dictionary<string, string> { ["ELECTRON_DISABLE_SANDBOX"] = "1" }, Url: "index.html");
        }
        else if (Environment.GetEnvironmentVariable("EXECUTOR_ELECTRON") == "1")
        {
            throw new InvalidOperationException("The synthetic Electron binary was not installed.");
        }
        else
        {
            host = SyntheticCdpHost.StartOn(0, "合成电子样品", null);
            application = new ApplicationTarget(null, "合成电子样品", host.Port, Attach: true);
        }
        try
        {
            string? failure = null;
            for (var attempt = 1; attempt <= 2; attempt++)
            {
                failure = await RunOnceAsync(authored with { Application = application });
                if (failure is null) return;
                await Task.Delay(1000);
            }
            Assert.Fail(failure);
        }
        finally
        {
            if (host is not null) await host.DisposeAsync();
        }
    }

    private static async Task<string?> RunOnceAsync(TestSuite suite)
    {
        var root = Fixtures.Temp();
        var reports = Path.Combine(root, "reports");
        var computer = new FakeComputerUseProvider();
        var runner = Fixtures.Runner(new ScriptedFactory(), computer, new RecordingExporter(), new RecordingDeadline(), root, reports);
        var sink = new ListSink();
        await runner.RunAsync(suite, new RunRequest("author-replay", Fixtures.Policy(), reports), sink, CancellationToken.None);
        var trace = string.Join(" || ", sink.Steps.Select(step => step.StepId + "=" + step.Status + "/" + step.ErrorType + "/" + step.Message));
        var passed = computer.ObserveCalls == 0
            && File.ReadAllText(Path.Combine(reports, "ai-usage.json")).Contains("\"tokens\":0", StringComparison.Ordinal)
            && sink.Steps.Any(step => step.StepId == "start" && step.Status == "passed" && step.Engine == "web")
            && sink.Steps.Any(step => step.StepId == "status" && step.Status == "passed")
            && sink.Steps.Any(step => step.StepId == "before-cut" && step.Status == "passed" && step.Engine == "scene3d")
            && sink.Steps.Any(step => step.StepId == "cut-drag" && step.Status == "passed" && step.Engine == "scene3d")
            && sink.Steps.Any(step => step.StepId == "cut-diff" && step.Status == "passed" && step.Engine == "scene3d");
        Directory.Delete(root, true);
        return passed ? null : trace;
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

    private sealed class PricedModel : IScriptModel
    {
        public int Calls { get; private set; }
        public string Text { get; init; } = FixedScriptModel.Script;
        public string Name => "priced-local";
        public bool Billed => true;
        public Task<ScriptModelReply> CompleteAsync(ScriptModelCall call, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            return Task.FromResult(new ScriptModelReply(Text, 4, 0));
        }
    }
}
