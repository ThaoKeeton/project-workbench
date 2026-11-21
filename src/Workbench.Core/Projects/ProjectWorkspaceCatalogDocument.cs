using System.Text.Json;

namespace Workbench.Core.Projects;

internal sealed record ProjectWorkspaceCatalogDocument(long Generation, string ActiveProjectId, ProjectWorkspaceInfo[] Projects)
{
    internal const int EncodedLimit = 256 * 1024;
    internal const int ProjectLimit = 128;
    private static readonly string[] CatalogFields = ["schemaVersion", "generation", "activeProjectId", "projects"];
    private static readonly string[] ProjectFields = ["id", "name", "isArchived"];

    internal static ProjectWorkspaceCatalogDocument Parse(byte[] bytes) => ProjectWorkspaceStorage.Parse(bytes, "InvalidCatalog", root =>
    {
        var fields = ProjectWorkspaceStorage.Object(root, "InvalidCatalog", CatalogFields);
        if (!fields["schemaVersion"].TryGetInt32(out var version) || version != 1
            || !fields["generation"].TryGetInt64(out var generation) || generation < 1
            || fields["projects"].ValueKind != JsonValueKind.Array)
            throw ProjectWorkspaceStorage.Error("InvalidCatalog");
        var projects = new List<ProjectWorkspaceInfo>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in fields["projects"].EnumerateArray())
        {
            if (projects.Count == ProjectLimit) throw ProjectWorkspaceStorage.Error("CatalogLimitExceeded");
            var project = ProjectWorkspaceStorage.Object(entry, "InvalidCatalog", ProjectFields);
            var id = project["id"].GetString();
            var name = project["name"].GetString();
            if (!ProjectWorkspaceStorage.IsId(id) || !ids.Add(id!) || name != ProjectWorkspaceStorage.Name(name, "InvalidCatalog"))
                throw ProjectWorkspaceStorage.Error("InvalidCatalog");
            projects.Add(new(id!, name!, project["isArchived"].GetBoolean()));
        }
        var active = fields["activeProjectId"].GetString();
        if (!ids.Contains("default") || active is null || !projects.Any(p => p.Id == active && !p.IsArchived))
            throw ProjectWorkspaceStorage.Error("InvalidCatalog");
        return new ProjectWorkspaceCatalogDocument(generation, active, projects.ToArray());
    });

    internal byte[] Encode()
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new { schemaVersion = 1, generation = Generation, activeProjectId = ActiveProjectId, projects = Projects }, ProjectWorkspaceStorage.JsonOptions);
        if (bytes.Length > EncodedLimit) throw ProjectWorkspaceStorage.Error("CatalogLimitExceeded");
        return bytes;
    }

    internal long NextGeneration()
        => Generation == long.MaxValue ? throw ProjectWorkspaceStorage.Error("CatalogLimitExceeded") : Generation + 1;

    internal ProjectWorkspaceSnapshot Snapshot(byte[] bytes)
        => new(ProjectWorkspaceStorage.Revision(bytes), ActiveProjectId, Array.AsReadOnly(Projects));

    // A well-formed newer/extended format belongs to a compatible version, never
    // to generic corruption recovery. The encoded input already has a hard bound.
    internal static bool ContainsUnsupportedFormat(byte[] bytes)
    {
        try
        {
            _ = ProjectWorkspaceStorage.Utf8.GetCharCount(bytes);
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = EncodedLimit });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return false;
            foreach (var property in root.EnumerateObject())
            {
                if (!CatalogFields.Contains(property.Name, StringComparer.Ordinal)) return true;
                if (property.Name == "schemaVersion" && (property.Value.ValueKind != JsonValueKind.Number
                    || !property.Value.TryGetInt64(out var version) || version != 1)) return true;
                if (property.Name != "projects" || property.Value.ValueKind != JsonValueKind.Array) continue;
                foreach (var project in property.Value.EnumerateArray())
                    if (project.ValueKind == JsonValueKind.Object && project.EnumerateObject().Any(p => !ProjectFields.Contains(p.Name, StringComparer.Ordinal)))
                        return true;
            }
            return false;
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or System.Text.DecoderFallbackException) { return false; }
    }
}
