using Workbench.Core.Projects;
using Workbench.Sdk;

namespace Workbench.Core.Testing;

public sealed partial class ProjectTestStore
{
    private const int RunHeadLimit = 4096;
    private sealed record RunHeadEnvelope(int SchemaVersion, string RunId, string PlanHash, long Sequence, string EventHash);

    private static byte[] EncodeRunHead(TestRunPlan plan, long sequence, string eventHash)
        => Encode(new RunHeadEnvelope(1, plan.Id, plan.PlanHash, sequence, eventHash), RunHeadLimit);

    private async Task<RunHeadEnvelope> ReadRunHeadLockedAsync(StoreLease lease, TestRunPlan plan, CancellationToken cancellationToken)
    {
        RequireLease(lease);
        try
        {
            var bytes = await ReadRequiredAsync(ProjectWorkspaceStorage.PathIn(RunPath(plan.Id), "head.json"), RunHeadLimit, cancellationToken).ConfigureAwait(false);
            var head = Decode<RunHeadEnvelope>(bytes);
            TestValidation.Id(head.RunId);
            TestValidation.Hash(head.PlanHash);
            TestValidation.Hash(head.EventHash);
            if (head.SchemaVersion != 1 || head.RunId != plan.Id || head.PlanHash != plan.PlanHash || head.Sequence is < 0 or >= 512)
                throw TestValidation.Error("TestStoreCorrupt");
            return head;
        }
        catch (PluginTestingException error) when (error.ErrorCode != "StoreLimitExceeded") { throw TestValidation.Error("TestStoreCorrupt"); }
    }

    private async Task CommitRunHeadLockedAsync(StoreLease lease, TestRunPlan plan, TestRunEvent value, CancellationToken cancellationToken)
    {
        RequireLease(lease);
        // The immutable event is already durable. A failed head publication leaves it retained,
        // unanchored and diagnostic; no cache advance, event rollback or inferred repair.
        await ProjectWorkspaceStorage.AtomicWriteAsync(ProjectWorkspaceStorage.PathIn(RunPath(plan.Id), "head.json"),
            EncodeRunHead(plan, value.Sequence, value.Hash), true, cancellationToken).ConfigureAwait(false);
    }

    private static void RequireRunEntries(string path)
    {
        var remaining = new HashSet<string>(["plan.json", "head.json", "events", "evidence"], StringComparer.Ordinal);
        foreach (var entry in Directory.EnumerateFileSystemEntries(path))
        {
            var name = Path.GetFileName(entry);
            _ = ProjectWorkspaceStorage.PathIn(path, name);
            if (IsAtomicTemporary(name, "head.json"))
            {
                ProjectWorkspaceStorage.RegularFile(entry);
                throw TestValidation.Error("RecoveryRequired");
            }
            if (!remaining.Remove(name)) throw TestValidation.Error("TestStoreCorrupt");
        }
        if (remaining.Count != 0) throw TestValidation.Error("TestStoreCorrupt");
    }
}
