namespace Workbench.TestExecutor;

public sealed partial class SuiteRunner
{
    private async Task<StepOutcome> ExecuteWebFirstAsync(TestSuite suite, TestCase testCase, TestStep step, IUiAutomationSession session, IWebSession web, RunRequest request, IRunSink sink, CancellationToken cancellationToken)
    {
        var canDrive = web.IsConnected && (step.Kind == StepKind.Scene3d || step.Target?.HasDomLocator == true || step.Action is "screenshot" or "switch-window" or "open-file");
        if (canDrive)
        {
            var attempt = await AttemptWebAsync(suite, testCase, step, session, web, request, sink, cancellationToken);
            if (!attempt.Failed) return attempt.Outcome!;
            if (attempt.NativeDialog || (step.Action == "open-file" && step.Target?.HasUiTarget == true))
                return await ExecuteFlaAsync(suite, testCase, step, session, request, sink, cancellationToken);
            if (step.AllowFallback && testCase.AllowFallback && step.Target?.HasUiTarget == true)
                return await ExecuteFlaAsync(suite, testCase, step, session, request, sink, cancellationToken);
            if (step.AllowFallback && testCase.AllowFallback && request.Policy.CanUseComputer)
                return await ExecuteAiAsync(suite, testCase, step, session, request, sink, attempt.Log, cancellationToken);
            var failedEngine = step.Kind == StepKind.Scene3d ? "scene3d" : "web";
            return await FailAsync(suite, testCase, step, request, sink, failedEngine, attempt.Error, attempt.Actual, attempt.Attempts, false, attempt.Log, attempt.Masked, cancellationToken);
        }
        if (step.Target?.HasUiTarget == true || step.Action == "open-file")
            return await ExecuteFlaAsync(suite, testCase, step, session, request, sink, cancellationToken);
        if (step.AllowFallback && testCase.AllowFallback && request.Policy.CanUseComputer)
            return await ExecuteAiAsync(suite, testCase, step, session, request, sink, [], cancellationToken);
        var log = new List<string> { Line(0, "web", ExecutorError.DebugPortMissing) };
        return await FailAsync(suite, testCase, step, request, sink, "web", ExecutorError.DebugPortMissing, ExecutorError.DebugPortMissing, 0, false, log, null, cancellationToken);
    }

    private async Task<WebTry> AttemptWebAsync(TestSuite suite, TestCase testCase, TestStep step, IUiAutomationSession session, IWebSession web, RunRequest request, IRunSink sink, CancellationToken cancellationToken)
    {
        var log = new List<string>();
        var current = step;
        MaskedImage? masked = null;
        var error = ExecutorError.ActionFailed;
        var actual = "";
        var repaired = false;
        var engine = step.Kind == StepKind.Scene3d ? "scene3d" : "web";
        for (var attempt = 1; attempt <= ExecutorSchemas.MaxAttempts; attempt++)
        {
            Publish(sink, request, suite, testCase, current, attempt == 1 ? "running" : "retrying", engine, null, attempt, false, Describe(current));
            masked = await CaptureSurfaceAsync(suite, session, web, log, cancellationToken) ?? masked;
            WebResult result;
            using (var deadline = _deps.Deadlines.Start(TimeSpan.FromSeconds(current.TimeoutSeconds), cancellationToken))
            {
                try { result = await web.PerformAsync(current, deadline.Token); }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    result = new WebResult(false, false, ExecutorError.Timeout, false, null);
                }
                catch (Exception cdpError) when (!cancellationToken.IsCancellationRequested && cdpError is ExecutorDataException or HttpRequestException)
                {
                    var code = cdpError is ExecutorDataException data ? data.Code : ExecutorError.DebugPortMissing;
                    result = new WebResult(false, false, code, false, null);
                }
            }
            var after = await CaptureSurfaceAsync(suite, session, web, log, cancellationToken);
            if (after is not null) masked = after;
            log.Add(Line(attempt, engine, result.Actual + " " + Describe(current)));
            if (result.Error == ExecutorError.UnexpectedPopup)
                return new WebTry(true, null, result.Error, result.Actual, attempt, false, log, masked);
            if (result.Actual == "native-dialog")
                return new WebTry(true, null, ExecutorError.ControlNotFound, result.Actual, attempt, true, log, masked);
            if (result.Png is not null)
            {
                var compared = await CompareScreenshotAsync(suite, current, request, result.Png, log, cancellationToken);
                masked = compared.Masked ?? masked;
                if (!compared.Ok)
                    return new WebTry(true, null, compared.Error, compared.Error, attempt, false, log, masked);
            }
            if (result.Found && result.Ok && current.Action == "read" && current.Expected is not null && result.Actual != current.Expected)
                result = result with { Ok = false, Actual = result.Actual };
            if (result.Found && result.Ok)
            {
                Publish(sink, request, suite, testCase, current, "passed", engine, null, attempt, false, "passed");
                await _deps.Artifacts.WriteEvidenceAsync(request.ReportDirectory, testCase.Id, current.Id, "passed", engine, log, masked, cancellationToken);
                return new WebTry(false, StepOutcome.Pass(masked), "", result.Actual, attempt, false, log, masked);
            }
            error = result.Found ? ExecutorError.AssertionMismatch : result.Actual == ExecutorError.Timeout ? ExecutorError.Timeout : ExecutorError.ControlNotFound;
            actual = current.Sensitive ? "[masked]" : result.Actual;
            if (!repaired && error == ExecutorError.ControlNotFound)
            {
                var replacement = await RepairOnceAsync(testCase, current, web, request, log, cancellationToken);
                repaired = true;
                if (replacement is not null) current = replacement;
            }
        }
        return new WebTry(true, null, error, actual, ExecutorSchemas.MaxAttempts, false, log, masked);
    }

    private async Task<TestStep?> RepairOnceAsync(TestCase testCase, TestStep step, IWebSession web, RunRequest request, List<string> log, CancellationToken cancellationToken)
    {
        if (!request.Policy.CanRepair) return null;
        var dom = await web.SnapshotAsync(4096, cancellationToken);
        var scene = await web.SceneTextAsync(cancellationToken);
        var snapshot = scene.Length == 0 ? dom : "SCENE:\n" + scene + "\nDOM:\n" + dom;
        if (snapshot.Contains("data:image", StringComparison.Ordinal)) return null;
        var tokens = Math.Max(1, snapshot.Length / 4 + 50);
        var cost = tokens / 1000m * request.Policy.CostPerThousandTokens;
        if (!_deps.Budget.TryCharge(request.RunId, tokens, cost, request.Policy))
        {
            log.Add(Line(0, "web", ExecutorError.BudgetExceeded));
            return null;
        }
        var suggestion = await _deps.Repair.SuggestAsync(new LocatorRepairRequest(testCase.Id, step.Id, step.Target?.Display ?? "", snapshot), cancellationToken);
        if (suggestion is null || step.Target is null) return null;
        if (request.SuiteFile is { Length: > 0 }) SuiteLocatorPatch.Apply(request.SuiteFile, testCase.Id, step.Id, suggestion.Selector);
        log.Add(Line(0, "web", "repaired selector=" + suggestion.Selector));
        return step with { Target = step.Target with { Selector = suggestion.Selector } };
    }

    private async Task<ShotComparison> CompareScreenshotAsync(TestSuite suite, TestStep step, RunRequest request, byte[] png, List<string> log, CancellationToken cancellationToken)
    {
        await _deps.Originals.SaveAsync(png, cancellationToken);
        var rectangles = suite.Masks.OfType<RectangleMask>().Select(mask => new PixelRect(mask.X, mask.Y, mask.Width, mask.Height)).ToArray();
        MaskedImage masked;
        try { masked = _deps.Masker.Apply(png, rectangles); }
        catch (ExecutorDataException)
        {
            log.Add("mask-failed");
            return new ShotComparison(false, ExecutorError.CaptureFailed, null);
        }
        if (step.Expected != "baseline") return new ShotComparison(true, "", masked);
        if (request.BaselineDirectory is not { Length: > 0 })
            return new ShotComparison(false, ExecutorError.BaselineMissing, masked);
        var baseline = ContainedPath.Combine(request.BaselineDirectory, step.Id + ".png");
        if (!File.Exists(baseline)) return new ShotComparison(false, ExecutorError.BaselineMissing, masked);
        var visual = suite.Visual ?? VisualSettings.Default;
        var ratio = VisualDiff.MismatchRatio(masked.Png, await File.ReadAllBytesAsync(baseline, cancellationToken), visual.ChannelTolerance, visual.Ignore);
        log.Add("visual-mismatch=" + ratio.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
        return ratio <= visual.MaxMismatchRatio
            ? new ShotComparison(true, "", masked)
            : new ShotComparison(false, ExecutorError.AssertionMismatch, masked);
    }

    private sealed record WebTry(bool Failed, StepOutcome? Outcome, string Error, string Actual, int Attempts, bool NativeDialog, List<string> Log, MaskedImage? Masked);

    private sealed record ShotComparison(bool Ok, string Error, MaskedImage? Masked);
}
