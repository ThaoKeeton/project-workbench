using System.Text.Json;

namespace Workbench.Service;

/// <summary>Manifests a person confirmed on this computer. They are recorded, not loaded.
/// The files sit beside schema 1 and do not extend it.</summary>
internal static class PluginImportStore
{
    public const string Schema = "workbench.plugin-import.v2.preview";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static IReadOnlyList<ImportedPlugin> Read(string userRoot)
    {
        var path = IndexPath(userRoot);
        if (!File.Exists(path)) return [];
        try
        {
            var document = JsonSerializer.Deserialize<ImportIndex>(File.ReadAllText(path), Json);
            if (document is not { Schema: Schema, SchemaVersion: 2, Plugins: { } plugins }) return [];
            return plugins.Where(item => PluginV2Catalog.IsSafePluginId(item.Id) && item.Name is { Length: > 0 } && item.Version is { Length: > 0 })
                .GroupBy(item => item.Id, StringComparer.Ordinal)
                .Select(group => group.First())
                .ToArray();
        }
        catch (Exception error) when (error is JsonException or IOException)
        {
            return [];
        }
    }

    public static async Task CommitAsync(string userRoot, PluginImportReport report, string fileName, CancellationToken cancellationToken)
    {
        if (!report.Importable || report.PluginId is null || report.ManifestText is null || report.Name is null || report.Version is null)
            throw new InvalidOperationException("Only a checked manifest can be recorded.");
        var directory = Path.Combine(userRoot, "v2-preview", "imports", report.PluginId);
        Directory.CreateDirectory(directory);
        var manifestPath = Path.Combine(directory, "plugin.v2.json");
        var temporary = manifestPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        await File.WriteAllTextAsync(temporary, report.ManifestText, cancellationToken);
        File.Move(temporary, manifestPath, overwrite: true);
        var current = Read(userRoot).Where(item => item.Id != report.PluginId).Append(new ImportedPlugin(report.PluginId, report.Name, report.Version, Leaf(fileName))).ToArray();
        var index = JsonSerializer.Serialize(new ImportIndex(Schema, 2, current), Json);
        var indexPath = IndexPath(userRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(indexPath)!);
        var indexTemporary = indexPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        await File.WriteAllTextAsync(indexTemporary, index, cancellationToken);
        File.Move(indexTemporary, indexPath, overwrite: true);
    }

    private static string IndexPath(string userRoot) => Path.Combine(userRoot, "v2-preview", "imports", "index.json");

    private static string Leaf(string fileName)
    {
        var leaf = Path.GetFileName(fileName.Replace('\\', '/'));
        if (string.IsNullOrWhiteSpace(leaf) || leaf is "." or "..") return "plugin.v2.json";
        return leaf.Length <= 80 ? leaf : leaf[..80];
    }

    private sealed record ImportIndex(string Schema, int SchemaVersion, IReadOnlyList<ImportedPlugin> Plugins);
}

internal sealed record ImportedPlugin(string Id, string Name, string Version, string FileName);
