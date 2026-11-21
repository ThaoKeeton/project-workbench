using System.Globalization;
using System.Text.Json;

namespace Workbench.Core.Projects;

/// <summary>Managed project roster. The host also retains its base/project lifetime leases.</summary>
public sealed class ProjectWorkspaceCatalog
{
    private readonly string _root;
    private string Metadata => ProjectWorkspaceStorage.PathIn(_root, ".workspaces");
    private string CatalogPath => ProjectWorkspaceStorage.PathIn(Metadata, "catalog.json");
    private string ProjectsPath => ProjectWorkspaceStorage.PathIn(Metadata, "projects");
    private string HistoryPath => ProjectWorkspaceStorage.PathIn(Metadata, "catalog-history");

    public ProjectWorkspaceCatalog(string baseRoot) => _root = ProjectWorkspaceStorage.NormalizeRoot(baseRoot);

    public Task<ProjectWorkspaceSnapshot> OpenAsync(CancellationToken cancellationToken = default) => WithLeaseAsync(async () =>
    {
        var bytes = await ReadCurrentAsync(cancellationToken);
        if (bytes is not null) return ReadDocument(bytes).Snapshot(bytes);
        if (Directory.EnumerateFileSystemEntries(Metadata).Any(path => Path.GetFileName(path) != "catalog.lock"))
            throw ProjectWorkspaceStorage.Error("RecoveryRequired");
        var initial = new ProjectWorkspaceCatalogDocument(1, "default", [new("default", "Default", false)]);
        var encoded = initial.Encode();
        await ProjectWorkspaceStorage.AtomicWriteAsync(CatalogPath, encoded, overwrite: false, cancellationToken);
        return initial.Snapshot(encoded);
    }, cancellationToken);

    public Task<ProjectWorkspaceCreation> CreateAsync(string name, string expectedRevision, CancellationToken cancellationToken = default) => WithLeaseAsync(async () =>
    {
        var normalized = ProjectWorkspaceStorage.Name(name);
        var (current, bytes) = await LoadAsync(expectedRevision, cancellationToken);
        if (current.Projects.Length == ProjectWorkspaceCatalogDocument.ProjectLimit) throw ProjectWorkspaceStorage.Error("CatalogLimitExceeded");
        var generation = current.NextGeneration();
        ProjectWorkspaceStorage.DirectoryPath(ProjectsPath, create: true);
        string id;
        do { id = Guid.NewGuid().ToString("N"); } while (ProjectWorkspaceStorage.Exists(GetProjectRoot(id)));
        var project = new ProjectWorkspaceInfo(id, normalized, false);
        var marker = ProjectWorkspaceStorage.PathIn(GetProjectRoot(id), ".workbench-project", "workspace.json");
        var markerBytes = JsonSerializer.SerializeToUtf8Bytes(new { schemaVersion = 1, id, name = normalized }, ProjectWorkspaceStorage.JsonOptions);
        await ProjectWorkspaceStorage.AtomicWriteAsync(marker, markerBytes, overwrite: false, cancellationToken);
        // A later publication failure deliberately retains this owned root/marker.
        var snapshot = await CommitAsync(current with { Generation = generation, Projects = [.. current.Projects, project] }, current, bytes, cancellationToken);
        return new ProjectWorkspaceCreation(project, snapshot);
    }, cancellationToken);

    public Task<ProjectWorkspaceSnapshot> SetActiveAsync(string id, string expectedRevision, CancellationToken cancellationToken = default)
        => ChangeAsync(id, expectedRevision, (data, project) => project.IsArchived
            ? throw ProjectWorkspaceStorage.Error("ActiveProjectArchived") : data with { ActiveProjectId = id }, cancellationToken);

    public Task<ProjectWorkspaceSnapshot> RenameAsync(string id, string name, string expectedRevision, CancellationToken cancellationToken = default)
        => ChangeAsync(id, expectedRevision, (data, _) => data with
        {
            Projects = data.Projects.Select(p => p.Id == id ? p with { Name = ProjectWorkspaceStorage.Name(name) } : p).ToArray()
        }, cancellationToken);

    public Task<ProjectWorkspaceSnapshot> SetArchivedAsync(string id, bool archived, string expectedRevision, CancellationToken cancellationToken = default)
        => ChangeAsync(id, expectedRevision, (data, _) => archived && data.ActiveProjectId == id
            ? throw ProjectWorkspaceStorage.Error("ActiveProjectArchived")
            : data with { Projects = data.Projects.Select(p => p.Id == id ? p with { IsArchived = archived } : p).ToArray() }, cancellationToken);

    public string GetProjectRoot(string id)
    {
        ProjectWorkspaceStorage.ValidateId(id);
        return id == "default" ? ProjectWorkspaceStorage.PathIn(_root, ".") : ProjectWorkspaceStorage.PathIn(ProjectsPath, id);
    }

    public Task<IReadOnlyList<ProjectWorkspaceInfo>> GetRecoverableAsync(CancellationToken cancellationToken = default)
        => WithLeaseAsync<IReadOnlyList<ProjectWorkspaceInfo>>(async () =>
        {
            var (document, _) = await LoadAsync(cancellationToken);
            var retained = new List<ProjectWorkspaceInfo>();
            ProjectWorkspaceStorage.DirectoryPath(ProjectsPath, create: false);
            if (!ProjectWorkspaceStorage.Exists(ProjectsPath)) return retained.AsReadOnly();
            foreach (var path in Directory.EnumerateFileSystemEntries(ProjectsPath))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var id = Path.GetFileName(path);
                if (id == "default" || !ProjectWorkspaceStorage.IsId(id) || document.Projects.Any(p => p.Id == id)) continue;
                try
                {
                    var project = await ReadMarkerAsync(id, cancellationToken);
                    if (project is not null) retained.Add(project);
                }
                catch (ProjectWorkspaceStoreException error) when (error.ErrorCode is "RecoveryUnavailable" or "UnsafePath" or "CatalogLimitExceeded")
                {
                    // Unknown/unowned folders and markers stay untouched, never auto-adopted.
                }
            }
            return retained.OrderBy(p => p.Id, StringComparer.Ordinal).ToArray();
        }, cancellationToken);

    public Task<ProjectWorkspaceSnapshot> RecoverProjectAsync(string id, string expectedRevision, CancellationToken cancellationToken = default)
        => WithLeaseAsync(async () =>
        {
            ProjectWorkspaceStorage.ValidateId(id);
            var (current, bytes) = await LoadAsync(expectedRevision, cancellationToken);
            if (id == "default" || current.Projects.Any(p => p.Id == id)) throw ProjectWorkspaceStorage.Error("RecoveryUnavailable");
            if (current.Projects.Length == ProjectWorkspaceCatalogDocument.ProjectLimit) throw ProjectWorkspaceStorage.Error("CatalogLimitExceeded");
            var project = await ReadMarkerAsync(id, cancellationToken) ?? throw ProjectWorkspaceStorage.Error("RecoveryUnavailable");
            return await CommitAsync(current with { Generation = current.NextGeneration(), Projects = [.. current.Projects, project] }, current, bytes, cancellationToken);
        }, cancellationToken);

    public Task<ProjectWorkspaceSnapshot> RecoverCatalogAsync(CancellationToken cancellationToken = default) => WithLeaseAsync(async () =>
    {
        var original = await ReadCurrentAsync(cancellationToken);
        if (original is not null && !CanRecover(original)) throw ProjectWorkspaceStorage.Error("RecoveryUnavailable");
        var retained = await FindSnapshotAsync(cancellationToken) ?? throw ProjectWorkspaceStorage.Error("RecoveryUnavailable");
        var restored = retained with { Generation = retained.NextGeneration() };
        var bytes = restored.Encode();
        ProjectWorkspaceStorage.ExpectRevision(ProjectWorkspaceStorage.Revision(original), await ReadCurrentAsync(cancellationToken));
        if (original is not null)
        {
            var path = ProjectWorkspaceStorage.PathIn(Metadata, "catalog-recovery", $"{Guid.NewGuid():N}.json");
            await ProjectWorkspaceStorage.AtomicWriteAsync(path, original, overwrite: false, cancellationToken);
        }
        await ProjectWorkspaceStorage.AtomicWriteAsync(CatalogPath, bytes, overwrite: true, cancellationToken);
        return restored.Snapshot(bytes);
    }, cancellationToken);

    private bool CanRecover(byte[] original)
    {
        if (ProjectWorkspaceCatalogDocument.ContainsUnsupportedFormat(original)) return false;
        try { _ = ReadDocument(original); return false; }
        catch (ProjectWorkspaceStoreException error) when (error.ErrorCode == "InvalidCatalog") { return true; }
    }

    private Task<T> WithLeaseAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken) => ProjectWorkspaceStorage.RunAsync(async () =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        await using var lease = await ProjectWorkspaceStorage.LeaseAsync(ProjectWorkspaceStorage.PathIn(Metadata, "catalog.lock"), cancellationToken);
        return await operation();
    });

    private Task<byte[]?> ReadCurrentAsync(CancellationToken cancellationToken)
        => ProjectWorkspaceStorage.ReadAsync(CatalogPath, ProjectWorkspaceCatalogDocument.EncodedLimit, "CatalogLimitExceeded", cancellationToken);

    private ProjectWorkspaceCatalogDocument ReadDocument(byte[] bytes)
    {
        var document = ProjectWorkspaceCatalogDocument.Parse(bytes);
        foreach (var project in document.Projects) _ = GetProjectRoot(project.Id);
        return document;
    }

    private async Task<(ProjectWorkspaceCatalogDocument Document, byte[] Bytes)> LoadAsync(CancellationToken cancellationToken)
    {
        var bytes = await ReadCurrentAsync(cancellationToken) ?? throw ProjectWorkspaceStorage.Error("RecoveryRequired");
        var document = ReadDocument(bytes);
        return (document, bytes);
    }

    private async Task<(ProjectWorkspaceCatalogDocument Document, byte[] Bytes)> LoadAsync(string expectedRevision, CancellationToken cancellationToken)
    {
        var current = await LoadAsync(cancellationToken);
        ProjectWorkspaceStorage.ExpectRevision(expectedRevision, current.Bytes);
        return current;
    }

    private Task<ProjectWorkspaceSnapshot> ChangeAsync(string id, string expectedRevision,
        Func<ProjectWorkspaceCatalogDocument, ProjectWorkspaceInfo, ProjectWorkspaceCatalogDocument> change, CancellationToken cancellationToken)
        => WithLeaseAsync(async () =>
        {
            ProjectWorkspaceStorage.ValidateId(id);
            var (current, bytes) = await LoadAsync(expectedRevision, cancellationToken);
            var project = current.Projects.SingleOrDefault(p => p.Id == id) ?? throw ProjectWorkspaceStorage.Error("ProjectNotFound");
            var updated = change(current, project);
            if (updated.ActiveProjectId == current.ActiveProjectId && updated.Projects.SequenceEqual(current.Projects)) return current.Snapshot(bytes);
            return await CommitAsync(updated with { Generation = current.NextGeneration() }, current, bytes, cancellationToken);
        }, cancellationToken);

    private async Task<ProjectWorkspaceSnapshot> CommitAsync(ProjectWorkspaceCatalogDocument next, ProjectWorkspaceCatalogDocument previous, byte[] original, CancellationToken cancellationToken)
    {
        var bytes = next.Encode();
        var snapshotPath = ProjectWorkspaceStorage.PathIn(HistoryPath, $"{previous.Generation:D20}-{ProjectWorkspaceStorage.Revision(original)}.json");
        var retained = await ProjectWorkspaceStorage.ReadAsync(snapshotPath, ProjectWorkspaceCatalogDocument.EncodedLimit, "CatalogLimitExceeded", cancellationToken);
        if (retained is not null)
        {
            if (!retained.SequenceEqual(original)) throw ProjectWorkspaceStorage.Error("InvalidCatalog");
        }
        else await ProjectWorkspaceStorage.AtomicWriteAsync(snapshotPath, original, overwrite: false, cancellationToken);
        ProjectWorkspaceStorage.ExpectRevision(ProjectWorkspaceStorage.Revision(original), await ReadCurrentAsync(cancellationToken));
        await ProjectWorkspaceStorage.AtomicWriteAsync(CatalogPath, bytes, overwrite: true, cancellationToken);
        return next.Snapshot(bytes);
    }

    private async Task<ProjectWorkspaceInfo?> ReadMarkerAsync(string id, CancellationToken cancellationToken)
    {
        var path = ProjectWorkspaceStorage.PathIn(GetProjectRoot(id), ".workbench-project", "workspace.json");
        var bytes = await ProjectWorkspaceStorage.ReadAsync(path, 4096, "CatalogLimitExceeded", cancellationToken);
        if (bytes is null) return null;
        return ProjectWorkspaceStorage.Parse(bytes, "RecoveryUnavailable", root =>
        {
            var fields = ProjectWorkspaceStorage.Object(root, "RecoveryUnavailable", "schemaVersion", "id", "name");
            var name = fields["name"].GetString();
            if (!fields["schemaVersion"].TryGetInt32(out var version) || version != 1 || fields["id"].GetString() != id
                || name != ProjectWorkspaceStorage.Name(name, "RecoveryUnavailable"))
                throw ProjectWorkspaceStorage.Error("RecoveryUnavailable");
            return new ProjectWorkspaceInfo(id, name!, false);
        });
    }

    private async Task<ProjectWorkspaceCatalogDocument?> FindSnapshotAsync(CancellationToken cancellationToken)
    {
        ProjectWorkspaceStorage.DirectoryPath(HistoryPath, create: false);
        if (!ProjectWorkspaceStorage.Exists(HistoryPath)) return null;
        ProjectWorkspaceCatalogDocument? latest = null;
        string? latestRevision = null;
        var ambiguous = false;
        foreach (var path in Directory.EnumerateFileSystemEntries(HistoryPath))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = Path.GetFileName(path);
            if (name.Length != 90 || name[20] != '-' || !name.EndsWith(".json", StringComparison.Ordinal)
                || !long.TryParse(name.AsSpan(0, 20), NumberStyles.None, CultureInfo.InvariantCulture, out var generation) || generation < 1
                || generation.ToString("D20", CultureInfo.InvariantCulture) != name[..20] || !ProjectWorkspaceStorage.IsLowerHex(name.Substring(21, 64))) continue;
            try
            {
                var bytes = await ProjectWorkspaceStorage.ReadAsync(ProjectWorkspaceStorage.PathIn(HistoryPath, name), ProjectWorkspaceCatalogDocument.EncodedLimit, "CatalogLimitExceeded", cancellationToken);
                if (bytes is null || ProjectWorkspaceStorage.Revision(bytes) != name.Substring(21, 64)) continue;
                var document = ReadDocument(bytes);
                if (document.Generation != generation) continue;
                if (latest is null || generation > latest.Generation)
                {
                    latest = document;
                    latestRevision = ProjectWorkspaceStorage.Revision(bytes);
                    ambiguous = false;
                }
                else if (generation == latest.Generation && latestRevision != ProjectWorkspaceStorage.Revision(bytes)) ambiguous = true;
            }
            catch (ProjectWorkspaceStoreException error) when (error.ErrorCode is "InvalidCatalog" or "CatalogLimitExceeded" or "UnsafePath")
            {
                // Every invalid or unknown retained entry remains byte-for-byte in place.
            }
        }
        if (ambiguous) throw ProjectWorkspaceStorage.Error("RecoveryUnavailable");
        return latest;
    }
}
