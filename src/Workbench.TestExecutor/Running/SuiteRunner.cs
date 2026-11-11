using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Workbench.TestExecutor;

public sealed record RunRequest(string RunId, ExecutionPolicy Policy, string ReportDirectory, string? SuiteFile = null, string? BaselineDirectory = null);

public sealed record StepStatus(
    string RunId,
    string SuiteId,
    string CaseId,
    string StepId,
    string Status,
    string? Engine,
    string? ErrorType,
    int Attempt,
    bool NeedsHumanReview,
    string Message);

public sealed record RunSummary(string RunId, string Status, int Failed, int NeedsReview);

public interface IRunSink
{
    void Publish(StepStatus status);

    void Complete(RunSummary summary);
}

public sealed class RunnerDependencies
{
    public required IUiAutomationSessionFactory Automation { get; init; }
    public required IComputerUseProvider ComputerUse { get; init; }
    public required ScreenshotMasker Masker { get; init; }
    public required OriginalScreenshotStore Originals { get; init; }
    public required RunArtifactStore Artifacts { get; init; }
    public required IFailureExporter Exporter { get; init; }
    public required IDeadlineFactory Deadlines { get; init; }
    public TimeProvider Clock { get; init; } = TimeProvider.System;
    public IWebSessionSource Web { get; init; } = new DefaultWebSessionSource();
    public ILocatorRepairProvider Repair { get; init; } = new DisabledLocatorRepair();
    public AiBudgetLedger Budget { get; init; } = AiBudgetLedger.InMemory();
}

public sealed partial class SuiteRunner
{
    private readonly RunnerDependencies _deps;

    public SuiteRunner(RunnerDependencies dependencies) => _deps = dependencies;

    public async Task RunAsync(TestSuite suite, RunRequest request, IRunSink sink, CancellationToken cancellationToken)
    {
        _deps.Originals.PurgeExpired();
        var failed = 0;
        var review = 0;
        IWebSession? web = null;
        try
        {
            await using var session = await _deps.Automation.OpenAsync(suite.Application, cancellationToken);
            web = await _deps.Web.OpenAsync(suite, cancellationToken);
            var stop = false;
            foreach (var testCase in suite.Cases)
            {
                if (stop)
                {
                    SkipCase(suite, testCase, request, sink);
                    continue;
                }
                var caseFailed = false;
                foreach (var step in testCase.Steps)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (caseFailed)
                    {
                        Publish(sink, request, suite, testCase, step, "skipped", null, null, 0, false, "skipped");
                        continue;
                    }
                    var outcome = await DispatchAsync(suite, testCase, step, session, web, request, sink, cancellationToken);
                    if (outcome.NeedsReview) review++;
                    if (outcome.Failed) { failed++; caseFailed = true; }
                    if (outcome.Report is not null)
                        await ExportAsync(outcome, request, cancellationToken);
                }
                if (caseFailed && suite.StopOnFirstFailure) stop = true;
            }
        }
        catch (OperationCanceledException)
        {
            sink.Complete(new RunSummary(request.RunId, "cancelled", failed, review));
            return;
        }
        finally
        {
            if (web is not null) await web.DisposeAsync();
            _deps.Budget.WriteRun(request.RunId, request.ReportDirectory);
        }
        sink.Complete(new RunSummary(request.RunId, Summary(failed, review), failed, review));
    }

    private Task<StepOutcome> DispatchAsync(TestSuite suite, TestCase testCase, TestStep step, IUiAutomationSession session, IWebSession web, RunRequest request, IRunSink sink, CancellationToken cancellationToken)
    {
        var preferWeb = step.Kind is StepKind.Web or StepKind.Scene3d || (step.Kind == StepKind.Ai && web.IsConnected && step.Target?.HasDomLocator == true);
        if (preferWeb) return ExecuteWebFirstAsync(suite, testCase, step, session, web, request, sink, cancellationToken);
        return step.Kind == StepKind.Ai
            ? ExecuteAiAsync(suite, testCase, step, session, request, sink, [], cancellationToken)
            : ExecuteFlaAsync(suite, testCase, step, session, request, sink, cancellationToken);
    }

    private async Task ExportAsync(StepOutcome outcome, RunRequest request, CancellationToken cancellationToken)
    {
        if (outcome.Report is null || !request.Policy.CanPush) return;
        await _deps.Exporter.ExportAsync(outcome.Report, outcome.Masked?.Png, request.Policy, cancellationToken);
    }

    private async Task<StepOutcome> ExecuteFlaAsync(TestSuite suite, TestCase testCase, TestStep step, IUiAutomationSession session, RunRequest request, IRunSink sink, CancellationToken cancellationToken)
    {
        var log = new List<string>();
        UiActionResult last = UiActionResult.Fail(ExecutorError.ActionFailed, false, "not-run");
        MaskedImage? masked = null;
        for (var attempt = 1; attempt <= ExecutorSchemas.MaxAttempts; attempt++)
        {
            Publish(sink, request, suite, testCase, step, attempt == 1 ? "running" : "retrying", "flaui", null, attempt, false, Describe(step));
            masked = await CaptureAsync(suite, session, log, cancellationToken) ?? masked;
            last = await InvokeAsync(session, step, cancellationToken);
            log.Add(Line(attempt, "flaui", last.Log + " " + Describe(step)));
            if (last.Succeeded && Mismatch(step, last))
                last = UiActionResult.Fail(ExecutorError.AssertionMismatch, false, last.Actual);
            var after = await CaptureAsync(suite, session, log, cancellationToken);
            if (after is not null) masked = after;
            if (!last.Succeeded) continue;
            Publish(sink, request, suite, testCase, step, "passed", "flaui", null, attempt, false, "passed");
            await _deps.Artifacts.WriteEvidenceAsync(request.ReportDirectory, testCase.Id, step.Id, "passed", "flaui", log, masked, cancellationToken);
            return StepOutcome.Pass(masked);
        }
        if (last.FallbackEligible && step.AllowFallback && testCase.AllowFallback && request.Policy.CanUseComputer)
        {
            Publish(sink, request, suite, testCase, step, "fallback", "flaui", last.ErrorType, ExecutorSchemas.MaxAttempts, true, "fallback");
            return await ExecuteAiAsync(suite, testCase, step, session, request, sink, log, cancellationToken);
        }
        return await FailAsync(suite, testCase, step, request, sink, "flaui", last.ErrorType ?? ExecutorError.ActionFailed, Actual(step, last.Actual), ExecutorSchemas.MaxAttempts, false, log, masked, cancellationToken);
    }

    private async Task<UiActionResult> InvokeAsync(IUiAutomationSession session, TestStep step, CancellationToken cancellationToken)
    {
        using var deadline = _deps.Deadlines.Start(TimeSpan.FromSeconds(step.TimeoutSeconds), cancellationToken);
        try
        {
            return await session.ExecuteAsync(new UiAction(step.Action, step.Target, step.Input, step.Expected, step.Sensitive), deadline.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return UiActionResult.Fail(ExecutorError.Timeout, false, "timeout");
        }
        catch (ExecutorDataException error)
        {
            return UiActionResult.Fail(error.Code, false, error.Code);
        }
    }

    private async Task<StepOutcome> ExecuteAiAsync(TestSuite suite, TestCase testCase, TestStep step, IUiAutomationSession session, RunRequest request, IRunSink sink, List<string> log, CancellationToken cancellationToken)
    {
        if (!request.Policy.CanUseComputer)
        {
            log.Add(Line(0, "ai", request.Policy.LocalOnly ? "local-only" : "feature-flag-off"));
            return await FailAsync(suite, testCase, step, request, sink, "ai", ExecutorError.ComputerUseDisabled, "computer-use disabled", 0, true, log, null, cancellationToken);
        }

        ComputerUseSession? computer = null;
        MaskedImage? masked = null;
        var error = ExecutorError.ProviderUnavailable;
        var attempts = 0;
        try
        {
            computer = await _deps.ComputerUse.StartAsync(new ComputerUseStart(Instruction(step)), cancellationToken);
            for (var attempt = 1; attempt <= ExecutorSchemas.MaxAttempts; attempt++)
            {
                attempts = attempt;
                Publish(sink, request, suite, testCase, step, attempt == 1 ? "running" : "retrying", "ai", null, attempt, true, Describe(step));
                masked = await CaptureAsync(suite, session, log, cancellationToken) ?? masked;
                if (masked is null)
                {
                    error = ExecutorError.CaptureFailed;
                    log.Add(Line(attempt, "ai", error));
                    continue;
                }
                var observed = await ObserveAsync(computer, step, masked, cancellationToken);
                log.Add(Line(attempt, "ai", observed.Detail));
                if (observed.Turn is not null)
                {
                    Publish(sink, request, suite, testCase, step, "needs-review", "ai", ExecutorError.NeedsReview, attempt, true, "needs-review");
                    var report = await WriteFailureAsync(testCase, step, request, "ai", ExecutorError.NeedsReview, Summarize(observed.Turn), attempt, true, log, masked, cancellationToken);
                    return StepOutcome.Review(masked, report);
                }
                error = observed.Error ?? ExecutorError.ProviderUnavailable;
                if (!Retriable(error)) break;
            }
        }
        finally
        {
            if (computer is not null) await _deps.ComputerUse.EndAsync(computer, CancellationToken.None);
        }
        return await FailAsync(suite, testCase, step, request, sink, "ai", error, error, attempts, true, log, masked, cancellationToken);
    }

    private async Task<Observed> ObserveAsync(ComputerUseSession computer, TestStep step, MaskedImage masked, CancellationToken cancellationToken)
    {
        using var deadline = _deps.Deadlines.Start(TimeSpan.FromSeconds(step.TimeoutSeconds), cancellationToken);
        try
        {
            var turn = await _deps.ComputerUse.ObserveAsync(computer, new ComputerUseObservation(masked, Instruction(step)), deadline.Token);
            return new Observed(turn, null, "actions=" + turn.Actions.Count.ToString(CultureInfo.InvariantCulture));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new Observed(null, ExecutorError.Timeout, ExecutorError.Timeout);
        }
        catch (ComputerUseException error)
        {
            return new Observed(null, error.Code, error.Code);
        }
    }

    private async Task<MaskedImage?> CaptureSurfaceAsync(TestSuite suite, IUiAutomationSession session, IWebSession web, List<string> log, CancellationToken cancellationToken)
    {
        if (web.IsConnected)
        {
            try
            {
                var png = await web.CapturePngAsync(cancellationToken);
                if (png is { Length: > 8 })
                {
                    await _deps.Originals.SaveAsync(png, cancellationToken);
                    var rectangles = suite.Masks.OfType<RectangleMask>().Select(mask => new PixelRect(mask.X, mask.Y, mask.Width, mask.Height)).ToArray();
                    return _deps.Masker.Apply(png, rectangles);
                }
            }
            catch (Exception error) when (error is ExecutorDataException or IOException)
            {
                log.Add("capture-failed");
            }
        }
        return await CaptureAsync(suite, session, log, cancellationToken);
    }

    private async Task<MaskedImage?> CaptureAsync(TestSuite suite, IUiAutomationSession session, List<string> log, CancellationToken cancellationToken)
    {
        RawFrame frame;
        try
        {
            frame = await session.CaptureAsync(cancellationToken);
        }
        catch (ExecutorDataException)
        {
            log.Add("capture-failed");
            return null;
        }
        if (frame.Png is not { Length: > 8 })
        {
            log.Add("capture-empty");
            return null;
        }
        await _deps.Originals.SaveAsync(frame.Png, cancellationToken);
        var regions = Regions(suite, session, frame, log);
        try
        {
            return _deps.Masker.Apply(frame.Png, regions);
        }
        catch (ExecutorDataException)
        {
            log.Add("mask-failed");
            return null;
        }
    }

    private static List<PixelRect> Regions(TestSuite suite, IUiAutomationSession session, RawFrame frame, List<string> log)
    {
        var regions = new List<PixelRect>();
        if (suite.MaskPasswordControls) regions.AddRange(frame.SensitiveRegions);
        foreach (var mask in suite.Masks)
        {
            if (mask is RectangleMask rectangle)
            {
                regions.Add(new PixelRect(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height));
                continue;
            }
            if (mask is not SelectorMask selector) continue;
            var located = session.Locate(selector.Selector);
            if (located is PixelRect rect) regions.Add(rect);
            else log.Add("mask-selector-missing");
        }
        return regions;
    }

    private async Task<StepOutcome> FailAsync(TestSuite suite, TestCase testCase, TestStep step, RunRequest request, IRunSink sink, string engine, string error, string actual, int attempts, bool review, List<string> log, MaskedImage? masked, CancellationToken cancellationToken)
    {
        var status = review && error == ExecutorError.NeedsReview ? "needs-review" : "failed";
        Publish(sink, request, suite, testCase, step, status, engine, error, attempts, review, error);
        var report = await WriteFailureAsync(testCase, step, request, engine, error, actual, attempts, review, log, masked, cancellationToken);
        return new StepOutcome(status != "needs-review", review, masked, report);
    }

    private async Task<FailureReport> WriteFailureAsync(TestCase testCase, TestStep step, RunRequest request, string engine, string error, string actual, int attempts, bool review, List<string> log, MaskedImage? masked, CancellationToken cancellationToken)
    {
        var report = new FailureReport
        {
            CaseId = testCase.Id,
            StepId = step.Id,
            Expected = TextRules.Clip(step.Expected ?? step.Action, 500),
            Actual = TextRules.Clip(actual, 500),
            Engine = engine,
            ErrorType = error,
            TimeoutSeconds = step.TimeoutSeconds,
            Attempts = attempts,
            NeedsHumanReview = review,
            Log = log,
            Fingerprint = FailureReport.FingerprintFor(testCase.Id, step.Id, error)
        };
        return await _deps.Artifacts.WriteFailureAsync(request.ReportDirectory, report, masked, cancellationToken);
    }

    private static void SkipCase(TestSuite suite, TestCase testCase, RunRequest request, IRunSink sink)
    {
        foreach (var step in testCase.Steps)
            Publish(sink, request, suite, testCase, step, "skipped", null, null, 0, false, "stop-on-first-failure");
    }

    private static void Publish(IRunSink sink, RunRequest request, TestSuite suite, TestCase testCase, TestStep step, string status, string? engine, string? error, int attempt, bool review, string message)
        => sink.Publish(new StepStatus(request.RunId, suite.Id, testCase.Id, step.Id, status, engine, error, attempt, review, message));

    private string Line(int attempt, string engine, string message)
        => _deps.Clock.GetUtcNow().ToString("O", CultureInfo.InvariantCulture) + " attempt=" + attempt.ToString(CultureInfo.InvariantCulture) + " engine=" + engine + " " + TextRules.Clip(message, 240);

    private static bool Mismatch(TestStep step, UiActionResult result)
        => step.Action == "read" && step.Expected is not null && !string.Equals(result.Actual, step.Expected, StringComparison.Ordinal);

    private static string Actual(TestStep step, string? actual) => step.Sensitive ? "[masked]" : (actual ?? "");

    private static string Describe(TestStep step)
    {
        var target = step.Target?.Display ?? "none";
        if (step.Sensitive || string.IsNullOrEmpty(step.Input)) return step.Action + " " + target;
        return step.Action + " " + target + " input=" + TextRules.Clip(step.Input, 80);
    }

    private static string Instruction(TestStep step)
    {
        var text = "动作 " + step.Action;
        if (step.Target is not null) text += "。目标 " + step.Target.Display;
        if (!string.IsNullOrEmpty(step.Expected)) text += "。期望 " + TextRules.Clip(step.Expected, 200);
        if (!step.Sensitive && !string.IsNullOrEmpty(step.Input)) text += "。输入 " + TextRules.Clip(step.Input, 200);
        return text;
    }

    private static string Summarize(ComputerUseTurn turn)
        => "actions=" + turn.Actions.Count.ToString(CultureInfo.InvariantCulture);

    private static bool Retriable(string code)
        => code is ExecutorError.Timeout or ExecutorError.ProviderUnavailable or ExecutorError.CaptureFailed;

    private static string Summary(int failed, int review)
    {
        if (failed > 0) return "failed";
        return review > 0 ? "completed-with-review" : "passed";
    }

    private sealed record StepOutcome(bool Failed, bool NeedsReview, MaskedImage? Masked, FailureReport? Report)
    {
        public static StepOutcome Pass(MaskedImage? masked) => new(false, false, masked, null);
        public static StepOutcome Review(MaskedImage? masked, FailureReport report) => new(false, true, masked, report);
    }

    private readonly record struct Observed(ComputerUseTurn? Turn, string? Error, string Detail);
}

public sealed class RunBuffer : IRunSink
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly List<string> _events = [];
    private readonly object _gate = new();
    private TaskCompletionSource<bool> _signal = NewSignal();
    private bool _completed;

    public RunBuffer(string id) => Id = id;

    public string Id { get; }

    public bool IsCompleted
    {
        get { lock (_gate) return _completed; }
    }

    public void Publish(StepStatus status) => Add(Serialize(new
    {
        schema = ExecutorSchemas.StepEvent,
        schemaVersion = ExecutorSchemas.Version,
        type = "step",
        runId = status.RunId,
        suiteId = status.SuiteId,
        caseId = status.CaseId,
        stepId = status.StepId,
        status = status.Status,
        engine = status.Engine,
        errorType = status.ErrorType,
        attempt = status.Attempt,
        needsHumanReview = status.NeedsHumanReview,
        message = TextRules.Clip(status.Message, 200)
    }));

    public void Complete(RunSummary summary) => Add(Serialize(new
    {
        schema = ExecutorSchemas.Run,
        schemaVersion = ExecutorSchemas.Version,
        type = "run",
        runId = summary.RunId,
        status = summary.Status,
        failed = summary.Failed,
        needsReview = summary.NeedsReview,
        completed = true
    }), terminal: true);

    public async IAsyncEnumerable<string> ReadAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var index = 0;
        while (true)
        {
            string[] batch;
            Task<bool> wait;
            bool done;
            lock (_gate)
            {
                batch = index >= _events.Count ? [] : _events.GetRange(index, _events.Count - index).ToArray();
                index += batch.Length;
                done = _completed && index >= _events.Count;
                wait = _signal.Task;
            }
            foreach (var item in batch) yield return item;
            if (done) yield break;
            await wait.WaitAsync(cancellationToken);
        }
    }

    private void Add(string json, bool terminal = false)
    {
        lock (_gate)
        {
            _events.Add(json);
            if (terminal) _completed = true;
            var current = _signal;
            _signal = NewSignal();
            current.TrySetResult(true);
        }
    }

    private static string Serialize(object value) => JsonSerializer.Serialize(value, Json);

    private static TaskCompletionSource<bool> NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
