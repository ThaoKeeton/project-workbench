using Workbench.Core.Projects;
using Workbench.Sdk;

namespace Workbench.Core.Testing;

public sealed partial class ProjectTestStore
{
    private sealed record SourceEnvelope(int SchemaVersion, string Id, string ProjectId, string ProducerPluginId, DateTimeOffset CreatedUtc,
        TestSourceKind Kind, string ProviderId, string ProviderVersion, string DocumentId, string Title, byte[] OriginalBytes, string OriginalSha256, IReadOnlyList<TestSourceItem> Items);

    public Task<TestSourceSnapshot> PublishSourceAsync(string producerPluginId, TestSourceDraft draft, CancellationToken cancellationToken = default)
        => CaptureAndSchedule(() =>
        {
            TestValidation.Producer(producerPluginId);
            return TestValidation.CopySourceDraft(draft);
        }, copied => WithLeaseAsync(true, async lease =>
        {
            if (lease!.Sources.Count >= 64) throw TestValidation.Error("StoreLimitExceeded");
            var id = NewId();
            var envelope = new SourceEnvelope(1, id, _projectId, producerPluginId, DateTimeOffset.UtcNow, copied.Kind, copied.ProviderId,
                copied.ProviderVersion, copied.DocumentId, copied.Title, copied.OriginalBytes, ProjectWorkspaceStorage.Revision(copied.OriginalBytes), copied.Items);
            var bytes = Encode(envelope);
            await ProjectWorkspaceStorage.AtomicWriteAsync(ManagedPath("sources", id), bytes, overwrite: false, cancellationToken).ConfigureAwait(false);
            return ParseSource(id, bytes);
        }, cancellationToken), "InvalidTestSource");

    public Task<IReadOnlyList<TestSourceSummary>> ListSourcesAsync(CancellationToken cancellationToken = default)
        => Schedule(() => WithLeaseAsync<IReadOnlyList<TestSourceSummary>>(false, lease => Task.FromResult<IReadOnlyList<TestSourceSummary>>(lease is null ? [] :
            lease.Sources.Values.OrderBy(s => s.Id, StringComparer.Ordinal).Select(s => new TestSourceSummary(s.Id, s.ProjectId, s.ProducerPluginId, s.CreatedUtc,
                s.Revision, s.Kind, s.ProviderId, s.ProviderVersion, s.DocumentId, s.Title, s.OriginalSha256, s.Items.Count)).ToArray()), cancellationToken));

    public Task<TestSourceSnapshot> ReadSourceAsync(string sourceId, CancellationToken cancellationToken = default)
        => CaptureAndSchedule(() => { TestValidation.Id(sourceId); return sourceId; }, id => WithLeaseAsync(false,
            lease => lease is null ? throw TestValidation.Error("TestRecordNotFound") : ReadSourceLockedAsync(lease, id, cancellationToken), cancellationToken), "InvalidId");

    internal Task<TestSourceSnapshot> ReadSourceLockedAsync(StoreLease lease, string sourceId, CancellationToken cancellationToken)
    {
        RequireLease(lease);
        cancellationToken.ThrowIfCancellationRequested();
        TestValidation.Id(sourceId);
        if (!lease.Sources.TryGetValue(sourceId, out var snapshot)) throw TestValidation.Error("TestRecordNotFound");
        return Task.FromResult(TestValidation.CopySourceSnapshot(snapshot));
    }

    private TestSourceSnapshot ParseSource(string id, byte[] bytes)
    {
        try
        {
            var value = Decode<SourceEnvelope>(bytes);
            TestValidation.Id(value.Id);
            TestValidation.Producer(value.ProducerPluginId);
            TestValidation.Hash(value.OriginalSha256);
            if (value.SchemaVersion != 1 || value.Id != id || value.ProjectId != _projectId || value.CreatedUtc.Offset != TimeSpan.Zero
                || value.CreatedUtc == default) throw TestValidation.Error("TestStoreCorrupt");
            var draft = TestValidation.CopySourceDraft(new(value.Kind, value.ProviderId, value.ProviderVersion, value.DocumentId, value.Title, value.OriginalBytes, value.Items));
            if (value.OriginalSha256 != ProjectWorkspaceStorage.Revision(draft.OriginalBytes)) throw TestValidation.Error("TestStoreCorrupt");
            return new(value.Id, value.ProjectId, value.ProducerPluginId, value.CreatedUtc, ProjectWorkspaceStorage.Revision(bytes), draft.Kind,
                draft.ProviderId, draft.ProviderVersion, draft.DocumentId, draft.Title, draft.OriginalBytes, value.OriginalSha256, draft.Items);
        }
        catch (PluginTestingException error) when (error.ErrorCode != "StoreLimitExceeded") { throw TestValidation.Error("TestStoreCorrupt"); }
    }
}
