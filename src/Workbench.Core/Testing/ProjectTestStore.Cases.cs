using Workbench.Core.Projects;
using Workbench.Sdk;

namespace Workbench.Core.Testing;

public sealed partial class ProjectTestStore
{
    private sealed record CaseEnvelope(int SchemaVersion, string Id, string ProjectId, string ProducerPluginId, DateTimeOffset UpdatedUtc, string Name, IReadOnlyList<TestCase> Cases);

    public Task<TestCaseSetSnapshot> CreateCaseSetAsync(string producerPluginId, TestCaseSetDraft draft, CancellationToken cancellationToken = default)
        => CaptureAndSchedule(() =>
        {
            TestValidation.Producer(producerPluginId);
            return TestValidation.CopyCaseSetDraft(draft);
        }, copied => WithLeaseAsync(true, async lease =>
        {
            if (lease!.Cases.Count >= 128) throw TestValidation.Error("StoreLimitExceeded");
            var cases = copied.Cases.Select(item => new TestCase(NewId(), item.Title, item.Prerequisites, item.Traces,
                item.Steps.Select(step => new TestStep(NewId(), step.Instruction, step.Expected, step.Traces, step.Action)).ToArray())).ToArray();
            ValidateTracesLocked(lease, cases, "InvalidCaseSet");
            var id = NewId();
            var bytes = Encode(new CaseEnvelope(1, id, _projectId, producerPluginId, DateTimeOffset.UtcNow, copied.Name, cases));
            await ProjectWorkspaceStorage.AtomicWriteAsync(ManagedPath("cases", id), bytes, overwrite: false, cancellationToken).ConfigureAwait(false);
            return ParseCaseSet(id, bytes);
        }, cancellationToken), "InvalidCaseSet");

    public Task<IReadOnlyList<TestCaseSetSummary>> ListCaseSetsAsync(CancellationToken cancellationToken = default)
        => Schedule(() => WithLeaseAsync<IReadOnlyList<TestCaseSetSummary>>(false, lease => Task.FromResult<IReadOnlyList<TestCaseSetSummary>>(lease is null ? [] :
            lease.Cases.Values.OrderBy(s => s.Id, StringComparer.Ordinal).Select(s => new TestCaseSetSummary(s.Id, s.ProjectId, s.ProducerPluginId,
                s.UpdatedUtc, s.Revision, s.Name, s.Cases.Count, s.Cases.Sum(c => c.Steps.Count))).ToArray()), cancellationToken));

    public Task<TestCaseSetSnapshot> ReadCaseSetAsync(string setId, CancellationToken cancellationToken = default)
        => CaptureAndSchedule(() => { TestValidation.Id(setId); return setId; }, id => WithLeaseAsync(false,
            lease => lease is null ? throw TestValidation.Error("TestRecordNotFound") : ReadCaseSetLockedAsync(lease, id, cancellationToken), cancellationToken), "InvalidId");

    public Task<TestCaseSetSnapshot> UpdateCaseSetAsync(string producerPluginId, string setId, TestCaseSetContent content, string expectedRevision, CancellationToken cancellationToken = default)
        => CaptureAndSchedule(() =>
        {
            TestValidation.Producer(producerPluginId);
            TestValidation.Id(setId);
            TestValidation.Hash(expectedRevision);
            return TestValidation.CopyCaseSetContent(content);
        }, copied => WithLeaseAsync(false, async lease =>
        {
            if (lease is null || !lease.Cases.TryGetValue(setId, out var current)) throw TestValidation.Error("TestRecordNotFound");
            if (current.ProducerPluginId != producerPluginId) throw TestValidation.Error("InvalidProducer");
            if (current.Revision != expectedRevision) throw TestValidation.Error("RevisionConflict");
            ValidateUpdate(current, copied);
            ValidateTracesLocked(lease, copied.Cases, "InvalidCaseSet");
            var bytes = Encode(new CaseEnvelope(1, setId, _projectId, producerPluginId, DateTimeOffset.UtcNow, copied.Name, copied.Cases));
            await ProjectWorkspaceStorage.AtomicWriteAsync(ManagedPath("cases", setId), bytes, overwrite: true, cancellationToken).ConfigureAwait(false);
            return ParseCaseSet(setId, bytes);
        }, cancellationToken), "InvalidCaseSet");

    public Task<TestCaseSetSnapshot> ReadCaseRevisionAsync(string setId, string revision, CancellationToken cancellationToken = default)
        => CaptureAndSchedule(() => { TestValidation.Id(setId); TestValidation.Hash(revision); return setId; }, id => WithLeaseAsync(false,
            lease => lease is null ? throw TestValidation.Error("TestRecordNotFound") : ReadCaseRevisionLockedAsync(lease, id, revision, cancellationToken), cancellationToken), "InvalidId");

    internal Task<TestCaseSetSnapshot> ReadCaseSetLockedAsync(StoreLease lease, string setId, CancellationToken cancellationToken)
    {
        RequireLease(lease);
        cancellationToken.ThrowIfCancellationRequested();
        TestValidation.Id(setId);
        if (!lease.Cases.TryGetValue(setId, out var snapshot)) throw TestValidation.Error("TestRecordNotFound");
        return Task.FromResult(CopyCaseSnapshot(snapshot));
    }

    internal Task<TestCaseSetSnapshot> ReadCaseRevisionLockedAsync(StoreLease lease, string setId, string revision, CancellationToken cancellationToken)
    {
        RequireLease(lease);
        cancellationToken.ThrowIfCancellationRequested();
        TestValidation.Id(setId);
        TestValidation.Hash(revision);
        if (lease.Cases.TryGetValue(setId, out var current) && current.Revision == revision) return Task.FromResult(CopyCaseSnapshot(current));
        if (lease.Archives.TryGetValue(setId + "/" + revision, out var archived)) return Task.FromResult(CopyCaseSnapshot(archived));
        throw TestValidation.Error("TestRecordNotFound");
    }

    /// <summary>Freeze exact current bytes before publishing a referencing run. No inner lease, reserialization or fallback.</summary>
    internal async Task<TestCaseSetSnapshot> FreezeCaseRevisionLockedAsync(StoreLease lease, string setId, string expectedRevision, CancellationToken cancellationToken)
    {
        RequireLease(lease);
        cancellationToken.ThrowIfCancellationRequested();
        TestValidation.Id(setId);
        TestValidation.Hash(expectedRevision);
        if (!lease.Cases.TryGetValue(setId, out var current)) throw TestValidation.Error("TestRecordNotFound");
        if (current.Revision != expectedRevision) throw TestValidation.Error("RevisionConflict");
        var key = setId + "/" + expectedRevision;
        if (!lease.Archives.ContainsKey(key))
        {
            if (lease.Archives.Count >= 256) throw TestValidation.Error("StoreLimitExceeded");
            var path = ProjectWorkspaceStorage.PathIn(_directory, "case-revisions", setId, expectedRevision + ".json");
            await ProjectWorkspaceStorage.AtomicWriteAsync(path, lease.CaseBytes[setId], overwrite: false, cancellationToken).ConfigureAwait(false);
            lease.Archives.Add(key, current);
        }
        return CopyCaseSnapshot(current);
    }

    internal void ValidateTracesLocked(StoreLease lease, IReadOnlyList<TestCase> cases, string invalidCode)
    {
        RequireLease(lease);
        // Only captured host-owned arrays enter this helper, never plugin enumeration.
        foreach (var item in cases)
        {
            foreach (var trace in item.Traces) ValidateTrace(trace);
            foreach (var step in item.Steps)
                foreach (var trace in step.Traces) ValidateTrace(trace);
        }
        void ValidateTrace(TestTrace trace)
        {
            if (!lease.Sources.TryGetValue(trace.SnapshotId, out var source) || source.Revision != trace.SnapshotRevision) throw TestValidation.Error(invalidCode);
            var item = source.Items.FirstOrDefault(item => item.Locator == trace.Locator);
            if (item is null || trace.CriterionIndex < 0 || trace.CriterionIndex >= item.AcceptanceCriteria.Count) throw TestValidation.Error(invalidCode);
        }
    }

    private static void ValidateUpdate(TestCaseSetSnapshot current, TestCaseSetContent changed)
    {
        if (current.Cases.Count != changed.Cases.Count) throw TestValidation.Error("InvalidCaseSet");
        for (var c = 0; c < current.Cases.Count; c++)
        {
            var before = current.Cases[c];
            var after = changed.Cases[c];
            if (before.Id != after.Id || !before.Traces.SequenceEqual(after.Traces) || before.Steps.Count != after.Steps.Count) throw TestValidation.Error("InvalidCaseSet");
            for (var s = 0; s < before.Steps.Count; s++)
            {
                var oldStep = before.Steps[s];
                var step = after.Steps[s];
                var textChanged = oldStep.Instruction != step.Instruction || oldStep.Expected != step.Expected;
                if (oldStep.Id != step.Id || !oldStep.Traces.SequenceEqual(step.Traces) || (textChanged ? step.Action is not null : step.Action is not null && step.Action != oldStep.Action))
                    throw TestValidation.Error("InvalidCaseSet");
            }
        }
    }

    private static TestCaseSetSnapshot CopyCaseSnapshot(TestCaseSetSnapshot snapshot)
        => snapshot with { Cases = TestValidation.CopyCaseSetContent(new(snapshot.Name, snapshot.Cases)).Cases };

    private TestCaseSetSnapshot ParseCaseSet(string id, byte[] bytes)
    {
        try
        {
            var value = Decode<CaseEnvelope>(bytes);
            TestValidation.Id(value.Id);
            TestValidation.Producer(value.ProducerPluginId);
            if (value.SchemaVersion != 1 || value.Id != id || value.ProjectId != _projectId || value.UpdatedUtc.Offset != TimeSpan.Zero
                || value.UpdatedUtc == default) throw TestValidation.Error("TestStoreCorrupt");
            var content = TestValidation.CopyCaseSetContent(new(value.Name, value.Cases));
            return new(value.Id, value.ProjectId, value.ProducerPluginId, value.UpdatedUtc, ProjectWorkspaceStorage.Revision(bytes), content.Name, content.Cases);
        }
        catch (PluginTestingException error) when (error.ErrorCode != "StoreLimitExceeded") { throw TestValidation.Error("TestStoreCorrupt"); }
    }
}
