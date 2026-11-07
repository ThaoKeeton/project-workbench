using Workbench.Sdk;

namespace Workbench.Core.Testing;

// One fixed replay policy, shared by appends and reopened journals. No adapter work runs here.
internal sealed class TestRunProjection(TestRunPlan plan)
{
    private readonly (string Case, string Step)[] _steps = plan.Cases.SelectMany(c => c.Steps.Select(s => (c.Id, s.Id))).ToArray();
    private int _finished;
    private bool _queued;
    private bool _inFlight;
    private bool _failed;
    internal TestRunState State { get; private set; } = TestRunState.Queued;
    internal static bool IsTerminal(TestRunState state) => state is TestRunState.Completed or TestRunState.Failed or TestRunState.Cancelled or TestRunState.Interrupted;

    internal void Apply(TestRunEventDraft value, IReadOnlyDictionary<string, TestEvidenceReceipt> receipts, string code, bool isRecovery = false)
    {
        void Refuse() => throw TestValidation.Error(code);
        var evidence = value.EvidenceIds ?? [];
        var stepEvent = value.Kind is TestRunEventKind.StepStarted or TestRunEventKind.StepFinished;
        if (isRecovery && (value.Kind != TestRunEventKind.Interrupted || value.ErrorCode != "RecoveryRequired")) Refuse();
        if (stepEvent ? value.CaseId is null || value.StepId is null : value.CaseId is not null || value.StepId is not null) Refuse();
        if (value.Kind != TestRunEventKind.StepFinished && (value.Outcome is not null || evidence.Count != 0 || value.NativePlainTextState is not null)) Refuse();
        if (value.ErrorCode is not null && value.Kind is not (TestRunEventKind.StepFinished or TestRunEventKind.Failed or TestRunEventKind.Cancelled or TestRunEventKind.Interrupted)) Refuse();
        if (!_queued)
        {
            if (value.Kind != TestRunEventKind.Queued) Refuse();
            _queued = true;
            return;
        }
        if (IsTerminal(State) || _failed && value.Kind != TestRunEventKind.Failed && !isRecovery) Refuse();
        switch (value.Kind)
        {
            case TestRunEventKind.RunStarted:
                if (State != TestRunState.Queued) Refuse();
                State = TestRunState.Running;
                break;
            case TestRunEventKind.StepStarted:
                if (State != TestRunState.Running || _inFlight || _finished >= _steps.Length || _steps[_finished] != (value.CaseId, value.StepId)) Refuse();
                _inFlight = true;
                break;
            case TestRunEventKind.StepFinished:
                if (State is not (TestRunState.Running or TestRunState.CancelRequested) || !_inFlight || _steps[_finished] != (value.CaseId, value.StepId)
                    || value.Outcome is null || evidence.Count == 0 || evidence.Any(id => !receipts.ContainsKey(id))
                    || value.Outcome == TestStepOutcome.Passed && value.ErrorCode is not null) Refuse();
                _inFlight = false;
                _finished++;
                _failed = value.Outcome == TestStepOutcome.Failed;
                break;
            case TestRunEventKind.CancelRequested:
                if (State is not (TestRunState.Queued or TestRunState.Running)) Refuse();
                State = TestRunState.CancelRequested;
                break;
            case TestRunEventKind.Completed:
                if (State != TestRunState.Running || _inFlight || _finished != _steps.Length) Refuse();
                State = TestRunState.Completed;
                break;
            case TestRunEventKind.Failed: State = TestRunState.Failed; break;
            case TestRunEventKind.Cancelled: State = TestRunState.Cancelled; break;
            case TestRunEventKind.Interrupted: State = TestRunState.Interrupted; break;
            default: Refuse(); break;
        }
    }
}
