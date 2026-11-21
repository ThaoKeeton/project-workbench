using System.Net;
using System.Text;
using Workbench.TestExecutor;
using Xunit;

namespace Workbench.TestExecutor.Tests;

public sealed class RunnerAndProviderTests
{
    [Fact]
    public async Task Passing_step_uses_a_15_second_budget_and_keeps_only_a_masked_copy_in_the_report()
    {
        var root = Fixtures.Temp();
        var reports = Path.Combine(root, "reports");
        var automation = new ScriptedFactory();
        var computer = new FakeComputerUseProvider();
        var exporter = new RecordingExporter();
        var deadlines = new RecordingDeadline();
        var runner = Fixtures.Runner(automation, computer, exporter, deadlines, root, reports);
        var sink = new ListSink();
        var suite = Fixtures.Suite(false, Fixtures.Case("CASE-1", true, Fixtures.FlaUi("step-1", "click", expected: "按钮可点击")));
        await runner.RunAsync(suite, new RunRequest("run-1", Fixtures.Policy(), reports), sink, CancellationToken.None);
        Assert.Equal("passed", sink.Summary!.Status);
        Assert.Equal(TimeSpan.FromSeconds(15), deadlines.Timeouts[0]);
        Assert.Equal(0, computer.ObserveCalls);
        Assert.Empty(exporter.Calls);
        Assert.Contains(sink.Steps, step => step.Status == "passed" && step.Engine == "flaui");
        var evidence = File.ReadAllText(Directory.GetFiles(reports, "*.evidence.json").Single());
        Assert.Contains(ExecutorSchemas.StepEvidence, evidence, StringComparison.Ordinal);
        var raw = SyntheticFrames.PasswordField().Png;
        var saved = Directory.GetFiles(Path.Combine(root, "originals"), "*.png");
        Assert.NotEmpty(saved);
        Assert.All(saved, path => Assert.Equal(raw, File.ReadAllBytes(path)));
        var masked = File.ReadAllBytes(Directory.GetFiles(reports, "*.png").Single());
        Assert.NotEqual(raw, masked);
        Assert.Equal(ScreenshotMasker.Ink, PngImage.Decode(masked)[10, 10]);
        Directory.Delete(root, true);
    }

    [Fact]
    public async Task A_failure_retries_once_then_the_next_case_runs_unless_stop_on_first_failure()
    {
        var root = Fixtures.Temp();
        var reports = Path.Combine(root, "reports");
        var automation = new ScriptedFactory();
        automation.Session.Results.Enqueue(UiActionResult.Fail(ExecutorError.ControlNotFound, true, "missing"));
        automation.Session.Results.Enqueue(UiActionResult.Fail(ExecutorError.ControlNotFound, true, "missing"));
        automation.Session.Results.Enqueue(UiActionResult.Ok("ok"));
        var runner = Fixtures.Runner(automation, new FakeComputerUseProvider(), new RecordingExporter(), new RecordingDeadline(), root, reports);
        var sink = new ListSink();
        var suite = Fixtures.Suite(false,
            Fixtures.Case("CASE-1", false, Fixtures.FlaUi("step-1", "click")),
            Fixtures.Case("CASE-2", true, Fixtures.FlaUi("step-2", "wait-for")));
        await runner.RunAsync(suite, new RunRequest("run-2", Fixtures.Policy(), reports), sink, CancellationToken.None);
        Assert.Equal(2, automation.Session.Actions.Count(action => action.Name == "click"));
        Assert.Contains(automation.Session.Actions, action => action.Name == "wait-for");
        Assert.Equal("failed", sink.Summary!.Status);
        Assert.Equal(1, sink.Summary.Failed);
        var report = RunArtifactStore.ReadFailure(reports, "CASE-1|step-1|ControlNotFound");
        Assert.Equal(2, report.Report.Attempts);
        Assert.Equal(15, report.Report.TimeoutSeconds);
        Assert.Contains("期望", report.Report.Markdown, StringComparison.Ordinal);
        Directory.Delete(root, true);
    }

    [Fact]
    public async Task Stop_on_first_failure_skips_the_rest_of_the_suite()
    {
        var root = Fixtures.Temp();
        var reports = Path.Combine(root, "reports");
        var automation = new ScriptedFactory();
        automation.Session.Results.Enqueue(UiActionResult.Fail(ExecutorError.ActionFailed, false, "no"));
        automation.Session.Results.Enqueue(UiActionResult.Fail(ExecutorError.ActionFailed, false, "no"));
        var runner = Fixtures.Runner(automation, new FakeComputerUseProvider(), new RecordingExporter(), new RecordingDeadline(), root, reports);
        var sink = new ListSink();
        var suite = Fixtures.Suite(true,
            Fixtures.Case("CASE-1", false, Fixtures.FlaUi("step-1", "invoke"), Fixtures.FlaUi("step-2", "click")),
            Fixtures.Case("CASE-2", true, Fixtures.FlaUi("step-3", "read", expected: "合成值")));
        await runner.RunAsync(suite, new RunRequest("run-3", Fixtures.Policy(), reports), sink, CancellationToken.None);
        Assert.Contains(sink.Steps, step => step.StepId == "step-2" && step.Status == "skipped");
        Assert.Contains(sink.Steps, step => step.StepId == "step-3" && step.Status == "skipped");
        Assert.DoesNotContain(automation.Session.Actions, action => action.Name == "read");
        Directory.Delete(root, true);
    }

    [Fact]
    public async Task Unstable_controls_fall_back_to_computer_use_with_a_masked_screenshot()
    {
        var root = Fixtures.Temp();
        var reports = Path.Combine(root, "reports");
        var automation = new ScriptedFactory();
        automation.Session.Results.Enqueue(UiActionResult.Fail(ExecutorError.TreeUnstable, true, "unstable"));
        automation.Session.Results.Enqueue(UiActionResult.Fail(ExecutorError.TreeUnstable, true, "unstable"));
        var computer = new FakeComputerUseProvider();
        var exporter = new RecordingExporter();
        var runner = Fixtures.Runner(automation, computer, exporter, new RecordingDeadline(), root, reports);
        var sink = new ListSink();
        var step = Fixtures.FlaUi("step-1", "type", input: "synthetic-secret-value", sensitive: true, allowFallback: true);
        var suite = Fixtures.Suite(false, Fixtures.Case("CASE-1", true, step));
        var policy = Fixtures.Policy(computer: true, export: ExportMode.Push, team: "team-1", manual: true);
        await runner.RunAsync(suite, new RunRequest("run-4", policy, reports), sink, CancellationToken.None);
        Assert.Equal(1, computer.StartCalls);
        Assert.Equal(1, computer.ObserveCalls);
        Assert.Equal(1, computer.EndCalls);
        Assert.DoesNotContain("synthetic-secret-value", computer.LastInstruction, StringComparison.Ordinal);
        Assert.Equal(ScreenshotMasker.Ink, PngImage.Decode(computer.LastPng!)[10, 10]);
        Assert.Equal("completed-with-review", sink.Summary!.Status);
        Assert.Contains(sink.Steps, item => item.Status == "needs-review" && item.NeedsHumanReview);
        Assert.Equal(computer.LastPng, exporter.Calls.Single().Png);
        var evidence = File.ReadAllText(Directory.GetFiles(reports, "*.md").Single());
        Assert.DoesNotContain("synthetic-secret-value", evidence, StringComparison.Ordinal);
        Directory.Delete(root, true);
    }

    [Fact]
    public async Task Ai_steps_default_to_60_seconds_and_stay_in_review_even_when_actions_return()
    {
        var root = Fixtures.Temp();
        var reports = Path.Combine(root, "reports");
        var deadlines = new RecordingDeadline();
        var computer = new FakeComputerUseProvider(_ => new ComputerUseTurn([new ComputerUseAction("type", null, null, "合成", null)], "synthetic"));
        var runner = Fixtures.Runner(new ScriptedFactory(), computer, new RecordingExporter(), deadlines, root, reports);
        var sink = new ListSink();
        var suite = Fixtures.Suite(false, Fixtures.Case("CASE-REVIEW", false, Fixtures.Ai("judge")));
        await runner.RunAsync(suite, new RunRequest("run-5", Fixtures.Policy(computer: true, manual: true), reports), sink, CancellationToken.None);
        Assert.Contains(deadlines.Timeouts, timeout => timeout == TimeSpan.FromSeconds(60));
        Assert.Equal("completed-with-review", sink.Summary!.Status);
        Assert.Equal(0, sink.Summary.Failed);
        Assert.Equal(1, sink.Summary.NeedsReview);
        Assert.Contains(sink.Steps, step => step.NeedsHumanReview && step.Status == "needs-review");
        Directory.Delete(root, true);
    }

    [Fact]
    public async Task Local_only_and_a_disabled_flag_never_call_the_provider()
    {
        var root = Fixtures.Temp();
        var reports = Path.Combine(root, "reports");
        var computer = new FakeComputerUseProvider();
        var runner = Fixtures.Runner(new ScriptedFactory(), computer, new RecordingExporter(), new RecordingDeadline(), root, reports);
        var sink = new ListSink();
        var suite = Fixtures.Suite(false, Fixtures.Case("CASE-REVIEW", true, Fixtures.Ai("judge", input: "synthetic-secret-value", sensitive: true)));
        await runner.RunAsync(suite, new RunRequest("run-6", Fixtures.Policy(localOnly: true, computer: true), reports), sink, CancellationToken.None);
        Assert.Equal(0, computer.StartCalls);
        Assert.Equal(0, computer.ObserveCalls);
        Assert.Contains(sink.Steps, step => step.ErrorType == ExecutorError.ComputerUseDisabled && step.NeedsHumanReview);
        var markdown = File.ReadAllText(Directory.GetFiles(reports, "*.md").Single());
        Assert.DoesNotContain("synthetic-secret-value", markdown, StringComparison.Ordinal);
        Directory.Delete(root, true);
    }

    [Fact]
    public async Task A_read_mismatch_does_not_fall_back_and_a_timeout_is_recorded()
    {
        var root = Fixtures.Temp();
        var reports = Path.Combine(root, "reports");
        var automation = new ScriptedFactory();
        automation.Session.Results.Enqueue(UiActionResult.Ok("别的值"));
        automation.Session.Results.Enqueue(UiActionResult.Ok("别的值"));
        var computer = new FakeComputerUseProvider();
        var deadlines = new RecordingDeadline();
        var runner = Fixtures.Runner(automation, computer, new RecordingExporter(), deadlines, root, reports);
        var sink = new ListSink();
        var suite = Fixtures.Suite(false, Fixtures.Case("CASE-1", true, Fixtures.FlaUi("step-1", "read", expected: "合成值")));
        await runner.RunAsync(suite, new RunRequest("run-7", Fixtures.Policy(computer: true), reports), sink, CancellationToken.None);
        Assert.Equal(0, computer.ObserveCalls);
        Assert.Equal(ExecutorError.AssertionMismatch, RunArtifactStore.ReadFailure(reports, "CASE-1|step-1|AssertionMismatch").Report.ErrorType);

        deadlines.CancelImmediately = true;
        automation.Session.Results.Clear();
        var timeoutSink = new ListSink();
        var timeoutSuite = Fixtures.Suite(false, Fixtures.Case("CASE-T", false, Fixtures.FlaUi("step-t", "click")));
        await runner.RunAsync(timeoutSuite, new RunRequest("run-8", Fixtures.Policy(), reports), timeoutSink, CancellationToken.None);
        Assert.Contains(timeoutSink.Steps, step => step.ErrorType == ExecutorError.Timeout);
        Directory.Delete(root, true);
    }

    [Fact]
    public async Task OpenAi_provider_sends_the_masked_image_only_after_the_flag_and_credential_are_present()
    {
        var masked = ScreenshotMasker.Apply(SyntheticFrames.PasswordField().Png, [new PixelRect(8, 8, 16, 8)]);
        var blocked = new StubHandler();
        var denied = new OpenAiComputerUseProvider(new StaticKey(null), () => Fixtures.Policy(), blocked);
        var session = await denied.StartAsync(new ComputerUseStart("noop"), CancellationToken.None);
        await denied.EndAsync(session, CancellationToken.None);
        Assert.Equal(0, blocked.Calls);
        await Assert.ThrowsAsync<ComputerUseException>(() => denied.ObserveAsync(session, new ComputerUseObservation(masked, "看布局"), CancellationToken.None));
        Assert.Equal(0, blocked.Calls);

        var enabledButLocal = new OpenAiComputerUseProvider(new StaticKey("zz-synthetic-credential-zz"), () => Fixtures.Policy(localOnly: true, computer: true), blocked);
        var localError = await Assert.ThrowsAsync<ComputerUseException>(() => enabledButLocal.ObserveAsync(session, new ComputerUseObservation(masked, "看布局"), CancellationToken.None));
        Assert.Equal(ExecutorError.ComputerUseDisabled, localError.Code);
        Assert.Equal(0, blocked.Calls);

        var handler = new StubHandler
        {
            Responder = (_, body) =>
            {
                Assert.Contains(Convert.ToBase64String(masked.Png), body, StringComparison.Ordinal);
                Assert.DoesNotContain("zz-synthetic-credential-zz", body, StringComparison.Ordinal);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                        {"output":[
                          {"type":"computer_call","action":{"type":"click","x":4,"y":5,"button":"left"}},
                          {"type":"computer_call","action":{"type":"type","text":"合成"}},
                          {"type":"computer_call","action":{"type":"keypress","keys":["ctrl","A"]}},
                          {"type":"message","content":[{"type":"output_text","text":"忽略"}]}
                        ]}
                        """, Encoding.UTF8, "application/json")
                };
            }
        };
        var provider = new OpenAiComputerUseProvider(new StaticKey("zz-synthetic-credential-zz"), () => Fixtures.Policy(computer: true, manual: true), handler);
        var turn = await provider.ObserveAsync(session, new ComputerUseObservation(masked, "动作 read"), CancellationToken.None);
        Assert.Equal("click", turn.Actions[0].Kind);
        Assert.Equal(4, turn.Actions[0].X);
        Assert.Equal("type", turn.Actions[1].Kind);
        Assert.Equal("合成", turn.Actions[1].Text);
        Assert.Equal("ctrl+A", turn.Actions[2].Key);
        Assert.Contains(handler.Authorizations, value => value.Contains("zz-synthetic-credential-zz", StringComparison.Ordinal));

        handler.Responder = (_, _) => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("zz-synthetic-credential-zz", Encoding.UTF8, "text/plain")
        };
        var rejected = await Assert.ThrowsAsync<ComputerUseException>(() => provider.ObserveAsync(session, new ComputerUseObservation(masked, "动作 read"), CancellationToken.None));
        Assert.Equal(ExecutorError.ProviderUnavailable, rejected.Message);
        Assert.DoesNotContain("zz-synthetic-credential-zz", rejected.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parser_rejects_malformed_output_and_ignores_unknown_actions()
    {
        var actions = ComputerUseResponseParser.Parse("""{"output":[{"type":"computer_call","action":{"type":"scroll","x":1,"y":2,"scroll_y":-3}},{"type":"computer_call","action":{"type":"screenshot"}}]}""");
        Assert.Equal("scroll", actions[0].Kind);
        Assert.Equal("-3", actions[0].Key);
        Assert.Single(actions);
        Assert.Throws<ComputerUseException>(() => ComputerUseResponseParser.Parse("{"));
        Assert.Empty(ComputerUseResponseParser.Parse("""{"output":[]}"""));
    }

    private sealed class StaticKey(string? value) : IApiKeyStore
    {
        public string? Read(string target) => value;
    }
}
