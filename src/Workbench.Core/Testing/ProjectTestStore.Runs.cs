using Workbench.Core.Projects;
using Workbench.Sdk;

namespace Workbench.Core.Testing;

public sealed partial class ProjectTestStore
{
    private const string VirtualTarget = "virtual.task-form.v1";
    // The bounded case body may fill 4MiB; run IDs/ancestry/revision/target add bounded host metadata.
    private const int PlanEnvelopeLimit = TestValidation.EnvelopeLimit + 4096;
    private sealed record PlanEnvelope(int SchemaVersion, string Id, string SessionId, string ProjectId, string ProducerPluginId, DateTimeOffset CreatedUtc,
        string? ParentRunId, string CaseSetId, string CaseRevision, IReadOnlyList<TestCase> Cases, string TargetId);

    public Task<TestRunSnapshot> CreateRunAsync(string producerPluginId, string setId, string expectedCaseRevision, IReadOnlyList<string> selectedCaseIds, CancellationToken cancellationToken = default)
        => CaptureAndSchedule(() =>
        {
            TestValidation.Producer(producerPluginId);
            TestValidation.Id(setId);
            TestValidation.Hash(expectedCaseRevision);
            return TestValidation.CopySelectedCaseIds(selectedCaseIds);
        }, copied => WithLeaseAsync(false, async lease =>
        {
            if (lease is null) throw TestValidation.Error("TestRecordNotFound");
            if (lease.Runs.Count >= 256) throw TestValidation.Error("StoreLimitExceeded");
            var current = await ReadCaseSetLockedAsync(lease, setId, cancellationToken).ConfigureAwait(false);
            if (current.Revision != expectedCaseRevision) throw TestValidation.Error("RevisionConflict");
            _ = SelectCases(current.Cases, copied, "InvalidRun");
            var frozen = await FreezeCaseRevisionLockedAsync(lease, setId, expectedCaseRevision, cancellationToken).ConfigureAwait(false);
            return await PublishRunLockedAsync(lease, producerPluginId, frozen, copied, null, cancellationToken).ConfigureAwait(false);
        }, cancellationToken), "InvalidRun");

    public Task<TestRunSnapshot> CreateRetryRunAsync(string producerPluginId, string parentRunId, CancellationToken cancellationToken = default)
        => CaptureAndSchedule(() => { TestValidation.Producer(producerPluginId); TestValidation.Id(parentRunId); return parentRunId; }, id => WithLeaseAsync(false, async lease =>
        {
            var parent = RequireRun(lease, id);
            RequireRunOwner(parent, producerPluginId);
            if (parent.State is not (TestRunState.Failed or TestRunState.Cancelled or TestRunState.Interrupted)) throw TestValidation.Error("InvalidRun");
            if (lease!.Runs.Count >= 256) throw TestValidation.Error("StoreLimitExceeded");
            var frozen = lease.Archives[parent.Plan.CaseSetId + "/" + parent.Plan.CaseRevision];
            return await PublishRunLockedAsync(lease, producerPluginId, frozen, parent.Plan.Cases.Select(c => c.Id).ToArray(), parent.Plan.Id, cancellationToken).ConfigureAwait(false);
        }, cancellationToken), "InvalidRun");

    public Task<IReadOnlyList<TestRunSummary>> ListRunsAsync(CancellationToken cancellationToken = default)
        => Schedule(() => WithLeaseAsync<IReadOnlyList<TestRunSummary>>(false, lease => Task.FromResult<IReadOnlyList<TestRunSummary>>(lease is null ? [] : lease.Runs.Values
            .OrderBy(r => r.Plan.Id, StringComparer.Ordinal).Select(r => new TestRunSummary(r.Plan.Id, r.Plan.SessionId, r.Plan.ProjectId, r.Plan.ProducerPluginId,
                r.Plan.CreatedUtc, r.Plan.ParentRunId, r.Plan.CaseSetId, r.Plan.CaseRevision, r.State, r.Sequence, r.HeadHash)).ToArray()), cancellationToken));

    public Task<TestRunSnapshot> ReadRunAsync(string runId, CancellationToken cancellationToken = default)
        => CaptureAndSchedule(() => { TestValidation.Id(runId); return runId; }, id => WithLeaseAsync(false,
            lease => Task.FromResult(CopyRun(RequireRun(lease, id))), cancellationToken), "InvalidId");

    // Only a future retained once-per-context facade barrier calls this; never a plugin recovery command.
    internal Task InitializeOwnerAsync(string producerPluginId, CancellationToken cancellationToken = default)
        => CaptureAndSchedule(() => { TestValidation.Producer(producerPluginId); return producerPluginId; }, owner => WithLeaseAsync(false, async lease =>
        {
            if (lease is null) return true;
            var old = lease.Runs.Values.Where(r => r.Plan.ProducerPluginId == owner && !TestRunProjection.IsTerminal(r.State)).ToArray();
            // Whole-store replay completed during acquisition before the first recovery write.
            if (old.Any(r => r.Events.Count >= 512)) throw TestValidation.Error("StoreLimitExceeded");
            foreach (var run in old)
                await AppendEventLockedAsync(lease, run, new(TestRunEventKind.Interrupted, ErrorCode: "RecoveryRequired"), cancellationToken, isRecovery: true).ConfigureAwait(false);
            return true;
        }, cancellationToken), "InvalidProducer");

    private static TestCase[] SelectCases(IReadOnlyList<TestCase> all, IReadOnlyList<string> selected, string code)
    {
        var result = new List<TestCase>();
        var position = 0;
        foreach (var item in all)
            if (position < selected.Count && selected[position] == item.Id) { result.Add(item); position++; }
        if (selected.Count == 0 || position != selected.Count) throw TestValidation.Error(code);
        if (result.Sum(c => c.Steps.Count) > 200) throw TestValidation.Error("StoreLimitExceeded");
        if (result.Any(c => c.Steps.Any(s => s.Action is null))) throw TestValidation.Error(code == "InvalidRun" ? "ManualStepUnsupported" : code);
        return result.ToArray();
    }

    private async Task<TestRunSnapshot> PublishRunLockedAsync(StoreLease lease, string owner, TestCaseSetSnapshot frozen, IReadOnlyList<string> selected, string? parent, CancellationToken cancellationToken)
    {
        RequireLease(lease);
        var id = NewId();
        var bytes = Encode(new PlanEnvelope(1, id, NewId(), _projectId, owner, DateTimeOffset.UtcNow, parent, frozen.Id, frozen.Revision,
            SelectCases(frozen.Cases, selected, "InvalidRun"), VirtualTarget), PlanEnvelopeLimit);
        var plan = ParsePlan(lease, id, bytes);
        var queuedBytes = EncodeEvent(plan, 0, GenesisHash, new(TestRunEventKind.Queued));
        var queued = ParseEvent(plan, 0, GenesisHash, queuedBytes, out _);
        var runRoot = ProjectWorkspaceStorage.PathIn(_directory, "runs");
        ProjectWorkspaceStorage.DirectoryPath(runRoot, create: true);
        var stage = ProjectWorkspaceStorage.PathIn(runRoot, id + ".stage");
        if (ProjectWorkspaceStorage.Exists(stage)) throw TestValidation.Error("StoreBusy");
        ProjectWorkspaceStorage.DirectoryPath(stage, create: true);
        try
        {
            ProjectWorkspaceStorage.DirectoryPath(ProjectWorkspaceStorage.PathIn(stage, "events"), create: true);
            ProjectWorkspaceStorage.DirectoryPath(ProjectWorkspaceStorage.PathIn(stage, "evidence"), create: true);
            await ProjectWorkspaceStorage.AtomicWriteAsync(ProjectWorkspaceStorage.PathIn(stage, "plan.json"), bytes, false, cancellationToken).ConfigureAwait(false);
            await ProjectWorkspaceStorage.AtomicWriteAsync(ProjectWorkspaceStorage.PathIn(stage, "events", EventFile(0)), queuedBytes, false, cancellationToken).ConfigureAwait(false);
            await ProjectWorkspaceStorage.AtomicWriteAsync(ProjectWorkspaceStorage.PathIn(stage, "head.json"), EncodeRunHead(plan, 0, queued.Hash), false, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            Directory.Move(stage, RunPath(id)); // One publication: plan, genesis and its authoritative head are visible together.
        }
        finally { CleanupOwnedStage(stage, ["plan.json", "events/" + EventFile(0), "head.json"], ["events", "evidence"]); }
        var run = new TestRunSnapshot(plan, [queued], TestRunState.Queued, 0, queued.Hash);
        lease.Runs.Add(id, run);
        lease.Evidence.Add(id, new(StringComparer.Ordinal));
        return CopyRun(run);
    }

    private TestRunPlan ParsePlan(StoreLease lease, string id, byte[] bytes)
    {
        try
        {
            var value = Decode<PlanEnvelope>(bytes);
            TestValidation.Id(value.Id);
            TestValidation.Id(value.SessionId);
            TestValidation.Id(value.CaseSetId);
            TestValidation.Hash(value.CaseRevision);
            TestValidation.Producer(value.ProducerPluginId);
            if (value.ParentRunId is not null) TestValidation.Id(value.ParentRunId);
            if (value.SchemaVersion != 1 || value.Id != id || value.ProjectId != _projectId || value.TargetId != VirtualTarget || value.CreatedUtc == default
                || value.CreatedUtc.Offset != TimeSpan.Zero || !lease.Archives.TryGetValue(value.CaseSetId + "/" + value.CaseRevision, out var frozen)) throw TestValidation.Error("TestStoreCorrupt");
            var cases = TestValidation.CopyCaseSetContent(new("Run", value.Cases)).Cases;
            var selected = SelectCases(frozen.Cases, cases.Select(c => c.Id).ToArray(), "TestStoreCorrupt");
            if (!Encode(cases).AsSpan().SequenceEqual(Encode(selected))) throw TestValidation.Error("TestStoreCorrupt");
            return new(value.Id, value.SessionId, value.ProjectId, value.ProducerPluginId, value.CreatedUtc, value.ParentRunId, value.CaseSetId,
                value.CaseRevision, cases, value.TargetId, ProjectWorkspaceStorage.Revision(bytes));
        }
        catch (PluginTestingException error) when (error.ErrorCode != "StoreLimitExceeded") { throw TestValidation.Error("TestStoreCorrupt"); }
    }

    private async Task ValidateRunsLockedAsync(StoreLease lease, CancellationToken cancellationToken)
    {
        var root = ProjectWorkspaceStorage.PathIn(_directory, "runs");
        if (!ProjectWorkspaceStorage.Exists(root)) return;
        ProjectWorkspaceStorage.DirectoryPath(root, false);
        var count = 0;
        foreach (var path in Directory.EnumerateFileSystemEntries(root))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var id = Path.GetFileName(path);
            _ = ProjectWorkspaceStorage.PathIn(root, id);
            if (++count > 256) throw TestValidation.Error("StoreLimitExceeded");
            if (IsStageName(id)) { ValidateRunStage(path); throw TestValidation.Error("RecoveryRequired"); }
            if (!IsManagedId(id)) throw TestValidation.Error("TestStoreCorrupt");
            ProjectWorkspaceStorage.DirectoryPath(path, false);
            RequireRunEntries(path);
            var bytes = await ReadRequiredAsync(ProjectWorkspaceStorage.PathIn(path, "plan.json"), PlanEnvelopeLimit, cancellationToken).ConfigureAwait(false);
            var plan = ParsePlan(lease, id, bytes);
            lease.Evidence.Add(id, await ValidateEvidenceLockedAsync(plan, cancellationToken).ConfigureAwait(false));
            lease.Runs.Add(id, await ReplayEventsLockedAsync(lease, plan, cancellationToken).ConfigureAwait(false));
        }
        var sessions = new HashSet<string>(StringComparer.Ordinal);
        foreach (var run in lease.Runs.Values)
        {
            if (!sessions.Add(run.Plan.SessionId)) throw TestValidation.Error("TestStoreCorrupt");
            if (run.Plan.ParentRunId is not { } parentId) continue;
            if (!lease.Runs.TryGetValue(parentId, out var parent) || parent.State is not (TestRunState.Failed or TestRunState.Cancelled or TestRunState.Interrupted)
                || parent.Plan.ProducerPluginId != run.Plan.ProducerPluginId || parent.Plan.CaseSetId != run.Plan.CaseSetId || parent.Plan.CaseRevision != run.Plan.CaseRevision
                || !parent.Plan.Cases.Select(c => c.Id).SequenceEqual(run.Plan.Cases.Select(c => c.Id))) throw TestValidation.Error("TestStoreCorrupt");
            var seen = new HashSet<string>(StringComparer.Ordinal) { run.Plan.Id };
            while (parentId is not null)
            {
                if (!seen.Add(parentId) || !lease.Runs.TryGetValue(parentId, out var ancestor)) throw TestValidation.Error("TestStoreCorrupt");
                parentId = ancestor.Plan.ParentRunId;
            }
        }
    }

    private string RunPath(string runId) => ProjectWorkspaceStorage.PathIn(_directory, "runs", runId);
    private static bool IsManagedId(string name) => name.Length == 32 && ProjectWorkspaceStorage.IsLowerHex(name);
    private static bool IsStageName(string name) => name.Length == 38 && name.EndsWith(".stage", StringComparison.Ordinal) && IsManagedId(name[..32]);
    private static TestRunSnapshot RequireRun(StoreLease? lease, string id) => lease is not null && lease.Runs.TryGetValue(id, out var run) ? run : throw TestValidation.Error("TestRecordNotFound");
    private static void RequireRunOwner(TestRunSnapshot run, string owner) { if (run.Plan.ProducerPluginId != owner) throw TestValidation.Error("InvalidProducer"); }
    private static TestRunSnapshot CopyRun(TestRunSnapshot run) => run with
    {
        Plan = run.Plan with { Cases = TestValidation.CopyCaseSetContent(new("Run", run.Plan.Cases)).Cases },
        Events = run.Events.Select(e => e with { EvidenceIds = e.EvidenceIds.ToArray() }).ToArray()
    };

    private static async Task<byte[]> ReadRequiredAsync(string path, int cap, CancellationToken cancellationToken)
        => await ProjectWorkspaceStorage.ReadAsync(path, cap, "StoreLimitExceeded", cancellationToken).ConfigureAwait(false) ?? throw TestValidation.Error("TestStoreCorrupt");

    private static void RequireEntries(string directory, string[] expected)
    {
        var remaining = new HashSet<string>(expected, StringComparer.Ordinal);
        foreach (var path in Directory.EnumerateFileSystemEntries(directory))
        {
            _ = ProjectWorkspaceStorage.PathIn(directory, Path.GetFileName(path));
            if (!remaining.Remove(Path.GetFileName(path))) throw TestValidation.Error("TestStoreCorrupt");
        }
        if (remaining.Count != 0) throw TestValidation.Error("TestStoreCorrupt");
    }

    private static void ValidateRunStage(string path)
    {
        ProjectWorkspaceStorage.DirectoryPath(path, false);
        foreach (var entry in Directory.EnumerateFileSystemEntries(path))
        {
            var name = Path.GetFileName(entry);
            _ = ProjectWorkspaceStorage.PathIn(path, name);
            if (name is "plan.json" or "head.json") ProjectWorkspaceStorage.RegularFile(entry);
            else if (name == "events")
            {
                ProjectWorkspaceStorage.DirectoryPath(entry, false);
                foreach (var file in Directory.EnumerateFileSystemEntries(entry))
                {
                    _ = ProjectWorkspaceStorage.PathIn(entry, Path.GetFileName(file));
                    ProjectWorkspaceStorage.RegularFile(file);
                    if (Path.GetFileName(file) != EventFile(0) && !IsEventTemporary(Path.GetFileName(file))) throw TestValidation.Error("TestStoreCorrupt");
                }
            }
            else if (name == "evidence")
            {
                ProjectWorkspaceStorage.DirectoryPath(entry, false);
                if (Directory.EnumerateFileSystemEntries(entry).Any()) throw TestValidation.Error("TestStoreCorrupt");
            }
            else if (IsAtomicTemporary(name, "plan.json") || IsAtomicTemporary(name, "head.json")) ProjectWorkspaceStorage.RegularFile(entry);
            else throw TestValidation.Error("TestStoreCorrupt");
        }
    }

    private static bool IsAtomicTemporary(string name, string file)
        => name.StartsWith("." + file + ".", StringComparison.Ordinal) && name.EndsWith(".tmp", StringComparison.Ordinal)
            && name.Length == file.Length + 38 && IsManagedId(name.Substring(file.Length + 2, 32));

    // Cleanup may touch only this call's generated stage, and only its known regular files/directories.
    private static void CleanupOwnedStage(string stage, string[] files, string[] directories)
    {
        if (!ProjectWorkspaceStorage.Exists(stage)) return;
        ProjectWorkspaceStorage.DirectoryPath(stage, false);
        var expected = files.Concat(directories).Select(p => Path.GetFullPath(Path.Combine(stage, p))).ToHashSet(StringComparer.Ordinal);
        var actual = new List<string>();
        void Inspect(string root)
        {
            foreach (var path in Directory.EnumerateFileSystemEntries(root))
            {
                _ = ProjectWorkspaceStorage.PathIn(root, Path.GetFileName(path));
                actual.Add(path);
                if ((File.GetAttributes(path) & FileAttributes.Directory) != 0) { ProjectWorkspaceStorage.DirectoryPath(path, false); Inspect(path); }
                else ProjectWorkspaceStorage.RegularFile(path);
            }
        }
        Inspect(stage);
        if (actual.Any(p => !expected.Contains(p))) return;
        foreach (var path in files.Select(p => ProjectWorkspaceStorage.PathIn(stage, p))) if (ProjectWorkspaceStorage.Exists(path)) File.Delete(path);
        foreach (var path in directories.Reverse().Select(p => ProjectWorkspaceStorage.PathIn(stage, p))) if (ProjectWorkspaceStorage.Exists(path)) Directory.Delete(path);
        Directory.Delete(stage);
    }
}
