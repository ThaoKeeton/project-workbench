using System.Text.Json;

namespace Workbench.Core.Projects;

/// <summary>Bounded, revision-checked project context. The host retains its project lifetime lease.</summary>
public sealed class ProjectContextDocumentStore
{
    private const int TextLimit = 1024 * 1024;
    private const int EncodedLimit = 8 * 1024 * 1024;
    private readonly string _directory;
    private readonly string _projectId;

    public ProjectContextDocumentStore(string userRoot, string projectId)
    {
        ProjectWorkspaceStorage.ValidateId(projectId);
        _projectId = projectId;
        _directory = ProjectWorkspaceStorage.PathIn(ProjectWorkspaceStorage.NormalizeRoot(userRoot), ".workbench-project");
    }

    public Task<ProjectContextDocument> ReadAsync(CancellationToken cancellationToken = default) => ProjectWorkspaceStorage.RunAsync(async () =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        ProjectWorkspaceStorage.DirectoryPath(_directory, create: false);
        // Missing reads do not create a root, metadata directory, lock, or document.
        if (!ProjectWorkspaceStorage.Exists(DocumentPath)) return new ProjectContextDocument("missing", "", true);
        await using var lease = await ProjectWorkspaceStorage.LeaseAsync(LeasePath, cancellationToken);
        return Parse(await ReadBytesAsync(cancellationToken));
    });

    public Task<ProjectContextDocument> WriteAsync(string text, string expectedRevision, CancellationToken cancellationToken = default) => ProjectWorkspaceStorage.RunAsync(async () =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateText(text);
        await using var lease = await ProjectWorkspaceStorage.LeaseAsync(LeasePath, cancellationToken);
        var current = await ReadBytesAsync(cancellationToken);
        _ = Parse(current); // Invalid or incompatible files cannot become editable defaults.
        ProjectWorkspaceStorage.ExpectRevision(expectedRevision, current);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new { schemaVersion = 1, projectId = _projectId, text, updatedUtc = DateTimeOffset.UtcNow }, ProjectWorkspaceStorage.JsonOptions);
        if (bytes.Length > EncodedLimit) throw ProjectWorkspaceStorage.Error("ContextLimitExceeded");
        await ProjectWorkspaceStorage.AtomicWriteAsync(DocumentPath, bytes, overwrite: true, cancellationToken);
        return new ProjectContextDocument(ProjectWorkspaceStorage.Revision(bytes), text, false);
    });

    /// <summary>Durably retains a host editor buffer without replacing or reading the current document.
    /// The returned path is an owned receipt, never a caller-selected destination.</summary>
    public Task<string> PreserveConflictAsync(string text, string expectedRevision, CancellationToken cancellationToken = default)
        => ProjectWorkspaceStorage.RunAsync(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateText(text);
            if (expectedRevision != "missing" && !(expectedRevision is { Length: 64 } && ProjectWorkspaceStorage.IsLowerHex(expectedRevision)))
                throw ProjectWorkspaceStorage.Error("InvalidContext");
            var bytes = JsonSerializer.SerializeToUtf8Bytes(new
            {
                schemaVersion = 1, projectId = _projectId, baseRevision = expectedRevision, text, savedUtc = DateTimeOffset.UtcNow
            }, ProjectWorkspaceStorage.JsonOptions);
            if (bytes.Length > EncodedLimit) throw ProjectWorkspaceStorage.Error("ContextLimitExceeded");
            var path = ProjectWorkspaceStorage.PathIn(_directory, "context-conflicts", $"{Guid.NewGuid():N}.json");
            await ProjectWorkspaceStorage.AtomicWriteAsync(path, bytes, overwrite: false, cancellationToken);
            return path;
        });

    private string DocumentPath => ProjectWorkspaceStorage.PathIn(_directory, "context.json");
    private string LeasePath => ProjectWorkspaceStorage.PathIn(_directory, "context.lock");
    private Task<byte[]?> ReadBytesAsync(CancellationToken cancellationToken)
        => ProjectWorkspaceStorage.ReadAsync(DocumentPath, EncodedLimit, "ContextLimitExceeded", cancellationToken);

    private ProjectContextDocument Parse(byte[]? bytes)
    {
        if (bytes is null) return new("missing", "", true);
        return ProjectWorkspaceStorage.Parse(bytes, "InvalidContext", root =>
        {
            var fields = ProjectWorkspaceStorage.Object(root, "InvalidContext", "schemaVersion", "projectId", "text", "updatedUtc");
            if (!fields["schemaVersion"].TryGetInt32(out var version) || version != 1 || fields["projectId"].GetString() != _projectId
                || !fields["updatedUtc"].TryGetDateTimeOffset(out var updated) || updated.Offset != TimeSpan.Zero)
                throw ProjectWorkspaceStorage.Error("InvalidContext");
            var text = fields["text"].GetString();
            ValidateText(text);
            return new ProjectContextDocument(ProjectWorkspaceStorage.Revision(bytes), text!, false);
        });
    }

    private static void ValidateText(string? text)
    {
        if (text is null || !ProjectWorkspaceStorage.ValidUnicode(text)) throw ProjectWorkspaceStorage.Error("InvalidContext");
        if (ProjectWorkspaceStorage.Utf8.GetByteCount(text) > TextLimit) throw ProjectWorkspaceStorage.Error("ContextLimitExceeded");
    }
}
