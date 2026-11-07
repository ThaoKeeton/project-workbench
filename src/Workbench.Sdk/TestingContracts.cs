namespace Workbench.Sdk;

/// <summary>Optional testing services from host 0.4.0. API/runtime/project services 1 remain unchanged.</summary>
public static class WorkbenchTestingServices
{
    public const int Version = 1;
}

public interface IPluginTestingContext : IPluginProjectContext
{
    IProjectTestWorkspace Testing { get; }
}

/// <summary>Project-scoped host records. Cancellation never establishes rollback; reread after work settles.</summary>
public interface IProjectTestWorkspace
{
    Task<TestSourceSnapshot> PublishSourceAsync(TestSourceDraft draft, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TestSourceSummary>> ListSourcesAsync(CancellationToken cancellationToken = default);
    Task<TestSourceSnapshot> ReadSourceAsync(string sourceId, CancellationToken cancellationToken = default);
    Task<TestCaseSetSnapshot> CreateCaseSetAsync(TestCaseSetDraft draft, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TestCaseSetSummary>> ListCaseSetsAsync(CancellationToken cancellationToken = default);
    Task<TestCaseSetSnapshot> ReadCaseSetAsync(string setId, CancellationToken cancellationToken = default);
    Task<TestCaseSetSnapshot> UpdateCaseSetAsync(string setId, TestCaseSetContent content, string expectedRevision, CancellationToken cancellationToken = default);
    Task<TestCaseSetSnapshot> ReadCaseRevisionAsync(string setId, string revision, CancellationToken cancellationToken = default);
    Task<TestRunSnapshot> CreateRunAsync(string setId, string expectedCaseRevision, IReadOnlyList<string> selectedCaseIds, CancellationToken cancellationToken = default);
    Task<TestRunSnapshot> CreateRetryRunAsync(string parentRunId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TestRunSummary>> ListRunsAsync(CancellationToken cancellationToken = default);
    Task<TestRunSnapshot> ReadRunAsync(string runId, CancellationToken cancellationToken = default);
    Task<TestRunSnapshot> AppendRunEventAsync(string runId, long expectedSequence, string expectedHeadHash, TestRunEventDraft draft, CancellationToken cancellationToken = default);
    Task<TestEvidenceReceipt> AddRunEvidenceAsync(string runId, TestEvidenceDraft draft, CancellationToken cancellationToken = default);
    Task<TestEvidence> ReadRunEvidenceAsync(string runId, string evidenceId, CancellationToken cancellationToken = default);
}

public enum TestSourceKind { Requirements, Figma }
public enum TestVirtualScenario { TaskFormPositive, TaskFormEmptyTitle, TaskFormAssertionFailure }

public sealed record TestSourceItem(string Locator, string Title, string Description, IReadOnlyList<string> AcceptanceCriteria, TestVirtualScenario? VirtualScenario);
public sealed record TestSourceDraft(TestSourceKind Kind, string ProviderId, string ProviderVersion, string DocumentId, string Title, byte[] OriginalBytes, IReadOnlyList<TestSourceItem> Items);
public sealed record TestSourceSnapshot(string Id, string ProjectId, string ProducerPluginId, DateTimeOffset CreatedUtc, string Revision, TestSourceKind Kind, string ProviderId, string ProviderVersion, string DocumentId, string Title, byte[] OriginalBytes, string OriginalSha256, IReadOnlyList<TestSourceItem> Items);
public sealed record TestSourceSummary(string Id, string ProjectId, string ProducerPluginId, DateTimeOffset CreatedUtc, string Revision, TestSourceKind Kind, string ProviderId, string ProviderVersion, string DocumentId, string Title, string OriginalSha256, int ItemCount);

/// <summary>CriterionIndex is zero-based. Locator is literal data, never a filesystem path.</summary>
public sealed record TestTrace(string SnapshotId, string SnapshotRevision, string Locator, int CriterionIndex);
public sealed record TestCaseSetDraft(string Name, IReadOnlyList<TestCaseDraft> Cases);
public sealed record TestCaseSetContent(string Name, IReadOnlyList<TestCase> Cases);
public sealed record TestCaseDraft(string Title, string Prerequisites, IReadOnlyList<TestTrace> Traces, IReadOnlyList<TestStepDraft> Steps);
public sealed record TestStepDraft(string Instruction, string Expected, IReadOnlyList<TestTrace> Traces, TestAction? Action);
public sealed record TestCase(string Id, string Title, string Prerequisites, IReadOnlyList<TestTrace> Traces, IReadOnlyList<TestStep> Steps);
public sealed record TestStep(string Id, string Instruction, string Expected, IReadOnlyList<TestTrace> Traces, TestAction? Action);
public sealed record TestCaseSetSnapshot(string Id, string ProjectId, string ProducerPluginId, DateTimeOffset UpdatedUtc, string Revision, string Name, IReadOnlyList<TestCase> Cases);
public sealed record TestCaseSetSummary(string Id, string ProjectId, string ProducerPluginId, DateTimeOffset UpdatedUtc, string Revision, string Name, int CaseCount, int StepCount);
public enum TestActionKind { SetText, Click, AssertText }
public sealed record TestAction(TestActionKind Kind, string ElementId, string? Value);

public enum TestRunState { Queued, Running, CancelRequested, Completed, Failed, Cancelled, Interrupted }
public enum TestRunEventKind { Queued, RunStarted, StepStarted, StepFinished, CancelRequested, Completed, Failed, Cancelled, Interrupted }
public enum TestStepOutcome { Passed, Failed }
public sealed record TestRunPlan(string Id, string SessionId, string ProjectId, string ProducerPluginId, DateTimeOffset CreatedUtc, string? ParentRunId, string CaseSetId, string CaseRevision, IReadOnlyList<TestCase> Cases, string TargetId, string PlanHash);
public sealed record TestRunSnapshot(TestRunPlan Plan, IReadOnlyList<TestRunEvent> Events, TestRunState State, long Sequence, string HeadHash);
public sealed record TestRunSummary(string Id, string SessionId, string ProjectId, string ProducerPluginId, DateTimeOffset CreatedUtc, string? ParentRunId, string CaseSetId, string CaseRevision, TestRunState State, long Sequence, string HeadHash);
public sealed record TestRunEventDraft(TestRunEventKind Kind, string? CaseId = null, string? StepId = null, TestStepOutcome? Outcome = null, string? ErrorCode = null, IReadOnlyList<string>? EvidenceIds = null);
public sealed record TestRunEvent(long Sequence, string PriorHash, string PlanHash, DateTimeOffset CreatedUtc, TestRunEventKind Kind, string? CaseId, string? StepId, TestStepOutcome? Outcome, string? ErrorCode, IReadOnlyList<string> EvidenceIds, string Hash);
public sealed record TestEvidenceDraft(string MimeType, byte[] Bytes, bool IsSimulated);
public sealed record TestEvidenceReceipt(string Id, string RunId, string MimeType, long Length, string Sha256, bool IsSimulated);
public sealed record TestEvidence(TestEvidenceReceipt Receipt, byte[] Bytes);

/// <summary>Trusted synchronous parsing, called off the dispatcher by its owner. No remote authority is implied.</summary>
public interface ITestSourceProvider
{
    string Id { get; }
    string Version { get; }
    TestSourceKind Kind { get; }
    TestSourceDraft Parse(ReadOnlyMemory<byte> bytes);
}

public interface ITestCaseGenerator
{
    TestCaseSetDraft Generate(IReadOnlyList<TestSourceSnapshot> sources);
}

public interface IControlledTestAdapter
{
    string TargetId { get; }
    Task<IControlledTestSession> OpenAsync(string sessionId, CancellationToken cancellationToken = default);
}

public interface IControlledTestSession : IAsyncDisposable
{
    Task ResetCaseAsync(string caseId, CancellationToken cancellationToken = default);
    Task<TestActionResult> ExecuteAsync(TestAction action, CancellationToken cancellationToken = default);
}

public sealed record TestActionResult(bool Passed, string? ErrorCode, string NativePlainTextState, byte[] PassiveFrame);

/// <summary>A safe code only; facades must allow-list even codes supplied by this public exception.</summary>
public sealed class PluginTestingException(string errorCode) : Exception("Testing operation failed.")
{
    public string ErrorCode { get; } = errorCode;
}
