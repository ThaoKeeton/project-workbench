using Workbench.Core.Projects;
using Workbench.Sdk;

namespace Workbench.Core.Testing;

public sealed partial class ProjectTestStore
{
    private const long EvidenceTotalLimit = 128L * 1024 * 1024;
    private sealed record ReceiptEnvelope(int SchemaVersion, string Id, string RunId, string ProjectId, string ProducerPluginId,
        string MimeType, long Length, string Sha256, bool IsSimulated);

    public Task<TestEvidenceReceipt> AddRunEvidenceAsync(string producerPluginId, string runId, TestEvidenceDraft draft, CancellationToken cancellationToken = default)
        => CaptureAndSchedule(() =>
        {
            TestValidation.Producer(producerPluginId);
            TestValidation.Id(runId);
            return TestValidation.CopyEvidenceDraft(draft);
        }, copied => WithLeaseAsync(false, async lease =>
        {
            var run = RequireRun(lease, runId);
            RequireRunOwner(run, producerPluginId);
            if (TestRunProjection.IsTerminal(run.State)) throw TestValidation.Error("InvalidRun");
            var receipts = lease!.Evidence[runId];
            // Complete unreferenced receipts count too; nothing is pruned after a failed/cancelled append.
            if (receipts.Count >= 256 || receipts.Values.Sum(r => r.Length) + copied.Bytes.LongLength > EvidenceTotalLimit) throw TestValidation.Error("StoreLimitExceeded");
            var id = NewId();
            var receipt = new TestEvidenceReceipt(id, runId, copied.MimeType, copied.Bytes.LongLength, ProjectWorkspaceStorage.Revision(copied.Bytes), copied.IsSimulated);
            var root = ProjectWorkspaceStorage.PathIn(RunPath(runId), "evidence");
            var stage = ProjectWorkspaceStorage.PathIn(root, id + ".stage");
            if (ProjectWorkspaceStorage.Exists(stage)) throw TestValidation.Error("StoreBusy");
            ProjectWorkspaceStorage.DirectoryPath(stage, true);
            try
            {
                await ProjectWorkspaceStorage.AtomicWriteAsync(ProjectWorkspaceStorage.PathIn(stage, "frame.bmp"), copied.Bytes, false, cancellationToken).ConfigureAwait(false);
                var bytes = Encode(new ReceiptEnvelope(1, id, runId, _projectId, producerPluginId, receipt.MimeType, receipt.Length, receipt.Sha256, receipt.IsSimulated));
                await ProjectWorkspaceStorage.AtomicWriteAsync(ProjectWorkspaceStorage.PathIn(stage, "receipt.json"), bytes, false, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                Directory.Move(stage, ProjectWorkspaceStorage.PathIn(root, id));
            }
            finally { CleanupOwnedStage(stage, ["frame.bmp", "receipt.json"], []); }
            receipts.Add(id, receipt);
            return receipt;
        }, cancellationToken), "InvalidEvidence");

    public Task<TestEvidence> ReadRunEvidenceAsync(string runId, string evidenceId, CancellationToken cancellationToken = default)
        => CaptureAndSchedule(() => { TestValidation.Id(runId); TestValidation.Id(evidenceId); return evidenceId; }, id => WithLeaseAsync(false, async lease =>
        {
            var run = RequireRun(lease, runId);
            if (!lease!.Evidence[runId].TryGetValue(id, out var receipt)) throw TestValidation.Error("TestRecordNotFound");
            var evidence = await ReadEvidenceLockedAsync(run.Plan, id, cancellationToken).ConfigureAwait(false);
            if (evidence.Receipt != receipt) throw TestValidation.Error("TestStoreCorrupt");
            return evidence;
        }, cancellationToken), "InvalidId");

    private async Task<Dictionary<string, TestEvidenceReceipt>> ValidateEvidenceLockedAsync(TestRunPlan plan, CancellationToken cancellationToken)
    {
        var root = ProjectWorkspaceStorage.PathIn(RunPath(plan.Id), "evidence");
        ProjectWorkspaceStorage.DirectoryPath(root, false);
        var receipts = new Dictionary<string, TestEvidenceReceipt>(StringComparer.Ordinal);
        var count = 0;
        long length = 0;
        foreach (var path in Directory.EnumerateFileSystemEntries(root))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var id = Path.GetFileName(path);
            _ = ProjectWorkspaceStorage.PathIn(root, id);
            if (++count > 256) throw TestValidation.Error("StoreLimitExceeded");
            if (IsStageName(id)) { ValidateEvidenceStage(path); throw TestValidation.Error("RecoveryRequired"); }
            if (!IsManagedId(id)) throw TestValidation.Error("TestStoreCorrupt");
            var evidence = await ReadEvidenceLockedAsync(plan, id, cancellationToken).ConfigureAwait(false);
            length += evidence.Receipt.Length;
            if (length > EvidenceTotalLimit) throw TestValidation.Error("StoreLimitExceeded");
            receipts.Add(id, evidence.Receipt);
        }
        return receipts;
    }

    private async Task<TestEvidence> ReadEvidenceLockedAsync(TestRunPlan plan, string id, CancellationToken cancellationToken)
    {
        var root = ProjectWorkspaceStorage.PathIn(RunPath(plan.Id), "evidence", id);
        ProjectWorkspaceStorage.DirectoryPath(root, false);
        RequireEntries(root, ["receipt.json", "frame.bmp"]);
        var value = Decode<ReceiptEnvelope>(await ReadRequiredAsync(ProjectWorkspaceStorage.PathIn(root, "receipt.json"), 4096, cancellationToken).ConfigureAwait(false));
        if (value.SchemaVersion != 1 || value.Id != id || value.RunId != plan.Id || value.ProjectId != _projectId || value.ProducerPluginId != plan.ProducerPluginId
            || value.MimeType != "image/bmp" || value.Length != 172854 || !value.IsSimulated || value.Sha256 is not { Length: 64 } || !ProjectWorkspaceStorage.IsLowerHex(value.Sha256)) throw TestValidation.Error("TestStoreCorrupt");
        var bytes = await ReadRequiredAsync(ProjectWorkspaceStorage.PathIn(root, "frame.bmp"), TestValidation.EvidenceLimit, cancellationToken).ConfigureAwait(false);
        TestValidation.ValidatePassiveBmp(bytes, "TestStoreCorrupt");
        if (bytes.LongLength != value.Length || ProjectWorkspaceStorage.Revision(bytes) != value.Sha256) throw TestValidation.Error("TestStoreCorrupt");
        return new(new(value.Id, value.RunId, value.MimeType, value.Length, value.Sha256, value.IsSimulated), bytes);
    }

    private static void ValidateEvidenceStage(string path)
    {
        ProjectWorkspaceStorage.DirectoryPath(path, false);
        foreach (var entry in Directory.EnumerateFileSystemEntries(path))
        {
            var name = Path.GetFileName(entry);
            _ = ProjectWorkspaceStorage.PathIn(path, name);
            ProjectWorkspaceStorage.RegularFile(entry);
            if (name is not ("receipt.json" or "frame.bmp") && !IsAtomicTemporary(name, "receipt.json") && !IsAtomicTemporary(name, "frame.bmp")) throw TestValidation.Error("TestStoreCorrupt");
        }
    }
}
