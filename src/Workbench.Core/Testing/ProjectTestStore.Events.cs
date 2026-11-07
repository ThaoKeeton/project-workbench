using System.Globalization;
using System.Text;
using System.Text.Json;
using Workbench.Core.Projects;
using Workbench.Sdk;

namespace Workbench.Core.Testing;

public sealed partial class ProjectTestStore
{
    private const string GenesisHash = "0000000000000000000000000000000000000000000000000000000000000000";
    private const int EventLimit = 256 * 1024;
    private sealed record EventEnvelope(int SchemaVersion, string RunId, long Sequence, string PriorHash, string PlanHash, DateTimeOffset CreatedUtc,
        TestRunEventKind Kind, string? CaseId, string? StepId, TestStepOutcome? Outcome, string? ErrorCode, IReadOnlyList<string> EvidenceIds, string? NativePlainTextState, bool IsRecovery, string Hash);

    public Task<TestRunSnapshot> AppendRunEventAsync(string producerPluginId, string runId, long expectedSequence, string expectedHeadHash, TestRunEventDraft draft, CancellationToken cancellationToken = default)
        => CaptureAndSchedule(() =>
        {
            TestValidation.Producer(producerPluginId);
            TestValidation.Id(runId);
            TestValidation.Hash(expectedHeadHash);
            if (expectedSequence < 0) throw TestValidation.Error("InvalidRun");
            var copied = TestValidation.CopyRunEventDraft(draft);
            if (copied.Kind == TestRunEventKind.Queued) throw TestValidation.Error("InvalidRun");
            return copied;
        }, copied => WithLeaseAsync(false, async lease =>
        {
            var run = RequireRun(lease, runId);
            RequireRunOwner(run, producerPluginId);
            if (run.Sequence != expectedSequence || run.HeadHash != expectedHeadHash) throw TestValidation.Error("RevisionConflict");
            return CopyRun(await AppendEventLockedAsync(lease!, run, copied, cancellationToken).ConfigureAwait(false));
        }, cancellationToken), "InvalidRun");

    private async Task<TestRunSnapshot> AppendEventLockedAsync(StoreLease lease, TestRunSnapshot run, TestRunEventDraft draft, CancellationToken cancellationToken, bool isRecovery = false)
    {
        RequireLease(lease);
        if (TestRunProjection.IsTerminal(run.State)) throw TestValidation.Error("InvalidRun");
        if (run.Events.Count >= 512) throw TestValidation.Error("StoreLimitExceeded");
        var projection = new TestRunProjection(run.Plan);
        foreach (var existing in run.Events) projection.Apply(ToDraft(existing), lease.Evidence[run.Plan.Id], "TestStoreCorrupt");
        projection.Apply(draft, lease.Evidence[run.Plan.Id], "InvalidRun", isRecovery);
        var bytes = EncodeEvent(run.Plan, run.Sequence + 1, run.HeadHash, draft, isRecovery);
        await ProjectWorkspaceStorage.AtomicWriteAsync(ProjectWorkspaceStorage.PathIn(RunPath(run.Plan.Id), "events", EventFile(run.Sequence + 1)), bytes, false, cancellationToken).ConfigureAwait(false);
        var value = ParseEvent(run.Plan, run.Sequence + 1, run.HeadHash, bytes, out _);
        var result = new TestRunSnapshot(run.Plan, run.Events.Append(value).ToArray(), projection.State, value.Sequence, value.Hash);
        lease.Runs[run.Plan.Id] = result;
        return result;
    }

    private static byte[] EncodeEvent(TestRunPlan plan, long sequence, string prior, TestRunEventDraft draft, bool isRecovery = false)
    {
        var bytes = Encode(new EventEnvelope(1, plan.Id, sequence, prior, plan.PlanHash, DateTimeOffset.UtcNow, draft.Kind, draft.CaseId, draft.StepId,
            draft.Outcome, draft.ErrorCode, draft.EvidenceIds ?? [], draft.NativePlainTextState, isRecovery, GenesisHash));
        if (bytes.Length > EventLimit) throw TestValidation.Error("StoreLimitExceeded");
        var hash = ProjectWorkspaceStorage.Revision(bytes);
        Encoding.ASCII.GetBytes(hash).CopyTo(bytes, EventHashOffset(bytes));
        return bytes;
    }

    private static TestRunEvent ParseEvent(TestRunPlan plan, long sequence, string prior, byte[] bytes, out bool isRecovery)
    {
        try
        {
            var value = Decode<EventEnvelope>(bytes);
            isRecovery = value.IsRecovery;
            TestValidation.Hash(value.Hash);
            var payload = bytes.ToArray();
            payload.AsSpan(EventHashOffset(bytes), 64).Fill((byte)'0');
            if (value.SchemaVersion != 1 || value.RunId != plan.Id || value.Sequence != sequence || value.PriorHash != prior || value.PlanHash != plan.PlanHash
                || value.CreatedUtc == default || value.CreatedUtc.Offset != TimeSpan.Zero || value.EvidenceIds is null
                || ProjectWorkspaceStorage.Revision(payload) != value.Hash
                || value.ErrorCode is not null && TestValidation.SafeErrorCode(value.ErrorCode) != value.ErrorCode) throw TestValidation.Error("TestStoreCorrupt");
            var copied = TestValidation.CopyRunEventDraft(new(value.Kind, value.CaseId, value.StepId, value.Outcome, value.ErrorCode, value.EvidenceIds) { NativePlainTextState = value.NativePlainTextState });
            return new(value.Sequence, value.PriorHash, value.PlanHash, value.CreatedUtc, value.Kind, value.CaseId, value.StepId, value.Outcome, value.ErrorCode,
                copied.EvidenceIds!, value.Hash) { NativePlainTextState = copied.NativePlainTextState };
        }
        catch (PluginTestingException error) when (error.ErrorCode != "StoreLimitExceeded") { throw TestValidation.Error("TestStoreCorrupt"); }
    }

    // Hash every exact wire byte, masking only the hash string's 64 ASCII digits to zero.
    // A stored self-hash also detects a modified final event with no successor link.
    private static int EventHashOffset(byte[] bytes)
    {
        var reader = new Utf8JsonReader(bytes);
        while (reader.Read())
        {
            if (reader.TokenType != JsonTokenType.PropertyName || reader.CurrentDepth != 1 || !reader.ValueTextEquals("hash")) continue;
            if (!reader.Read() || reader.TokenType != JsonTokenType.String || reader.ValueIsEscaped || reader.ValueSpan.Length != 64) throw TestValidation.Error("TestStoreCorrupt");
            return checked((int)reader.TokenStartIndex + 1);
        }
        throw TestValidation.Error("TestStoreCorrupt");
    }

    private async Task<TestRunSnapshot> ReplayEventsLockedAsync(StoreLease lease, TestRunPlan plan, CancellationToken cancellationToken)
    {
        var root = ProjectWorkspaceStorage.PathIn(RunPath(plan.Id), "events");
        ProjectWorkspaceStorage.DirectoryPath(root, false);
        var files = new SortedDictionary<long, string>();
        foreach (var path in Directory.EnumerateFileSystemEntries(root))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = Path.GetFileName(path);
            _ = ProjectWorkspaceStorage.PathIn(root, name);
            ProjectWorkspaceStorage.RegularFile(path);
            if (IsEventTemporary(name)) throw TestValidation.Error("RecoveryRequired");
            if (name.Length != 15 || !name.EndsWith(".json", StringComparison.Ordinal) || !name[..10].All(c => c is >= '0' and <= '9')
                || !long.TryParse(name[..10], NumberStyles.None, CultureInfo.InvariantCulture, out var sequence) || !files.TryAdd(sequence, path)) throw TestValidation.Error("TestStoreCorrupt");
            if (files.Count > 512) throw TestValidation.Error("StoreLimitExceeded");
        }
        if (files.Count == 0) throw TestValidation.Error("TestStoreCorrupt");
        var projection = new TestRunProjection(plan);
        var events = new List<TestRunEvent>();
        var prior = GenesisHash;
        foreach (var (sequence, path) in files)
        {
            if (sequence != events.Count) throw TestValidation.Error("TestStoreCorrupt");
            var value = ParseEvent(plan, sequence, prior, await ReadRequiredAsync(path, EventLimit, cancellationToken).ConfigureAwait(false), out var isRecovery);
            projection.Apply(ToDraft(value), lease.Evidence[plan.Id], "TestStoreCorrupt", isRecovery);
            events.Add(value);
            prior = value.Hash;
        }
        return new(plan, events.ToArray(), projection.State, events[^1].Sequence, prior);
    }

    private static string EventFile(long sequence) => sequence.ToString("D10", CultureInfo.InvariantCulture) + ".json";
    private static bool IsEventTemporary(string name) => name.Length == 53 && name[0] == '.' && name.Substring(1, 10).All(c => c is >= '0' and <= '9')
        && IsAtomicTemporary(name, name.Substring(1, 15));
    private static TestRunEventDraft ToDraft(TestRunEvent value) => new(value.Kind, value.CaseId, value.StepId, value.Outcome, value.ErrorCode, value.EvidenceIds) { NativePlainTextState = value.NativePlainTextState };
}
