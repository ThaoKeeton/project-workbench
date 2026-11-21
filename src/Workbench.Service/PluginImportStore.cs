using System.Text.Json;

namespace Workbench.Service;

/// <summary>Manifests a person confirmed on this computer. They stay recorded until that person enables one.
/// Enabling reads the stored manifest only. The files sit beside schema 1 and do not extend it.</summary>
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
        await WriteIndexAsync(userRoot, current, cancellationToken);
    }

    public static async Task<EnableResult> EnableAsync(string userRoot, string id, IReadOnlySet<string> existingIds, CancellationToken cancellationToken)
    {
        if (!PluginV2Catalog.IsSafePluginId(id))
            return EnableResult.Fail(404, "NotRecorded", "这台电脑上没有这份记下的清单，没有启用。");
        var current = Read(userRoot);
        var plugin = current.FirstOrDefault(item => item.Id == id);
        if (plugin is null)
            return EnableResult.Fail(404, "NotRecorded", "这台电脑上没有这份记下的清单，没有启用。");
        var manifestPath = Path.Combine(userRoot, "v2-preview", "imports", id, "plugin.v2.json");
        if (!File.Exists(manifestPath))
            return await RefuseAsync(userRoot, current, plugin, "InvalidManifest", "记下的清单已经不在，没有启用。", cancellationToken);
        string text;
        try { text = await File.ReadAllTextAsync(manifestPath, cancellationToken); }
        catch (IOException) { return await RefuseAsync(userRoot, current, plugin, "InvalidManifest", "记下的清单读不出来，没有启用。", cancellationToken); }
        var others = existingIds.Where(item => !string.Equals(item, id, StringComparison.Ordinal)).ToHashSet(StringComparer.Ordinal);
        var report = PluginV2Catalog.InspectText(text, others);
        if (!report.Importable || report.PluginId != id)
        {
            var detail = report.Checks.FirstOrDefault(check => !check.Ok).Text ?? "这份清单已经对不上。";
            return await RefuseAsync(userRoot, current, plugin, report.Error ?? "InvalidManifest", "没有启用。" + detail, cancellationToken);
        }
        if (!plugin.Enabled)
            await WriteIndexAsync(userRoot, current.Select(item => item.Id == id ? item with { Enabled = true } : item).ToArray(), cancellationToken);
        return EnableResult.Success(id);
    }

    public static async Task<EnableResult> RemoveAsync(string userRoot, string id, CancellationToken cancellationToken)
    {
        if (!PluginV2Catalog.IsSafePluginId(id))
            return EnableResult.Fail(404, "NotRecorded", "这不是本地导入记下的插件，没有卸掉。");
        var current = Read(userRoot);
        var plugin = current.FirstOrDefault(item => item.Id == id);
        if (plugin is null)
            return EnableResult.Fail(404, "NotRecorded", "这不是本地导入记下的插件，没有卸掉。");
        var desk = (await UiDeskStore.ReadAsync(userRoot, cancellationToken)).State;
        try { await UiDeskStore.ForgetAsync(userRoot, id, cancellationToken); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return EnableResult.Fail(400, "RemoveFailed", "没有卸掉。记下的清单还在。");
        }
        try { await WriteIndexAsync(userRoot, current.Where(item => item.Id != id).ToArray(), cancellationToken); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            await UiDeskStore.SaveAsync(userRoot, desk, cancellationToken);
            return EnableResult.Fail(400, "RemoveFailed", "没有卸掉。记下的清单还在。");
        }
        var directory = Path.Combine(userRoot, "v2-preview", "imports", id);
        try
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            await WriteIndexAsync(userRoot, current, cancellationToken);
            await UiDeskStore.SaveAsync(userRoot, desk, cancellationToken);
            return EnableResult.Fail(400, "RemoveFailed", "没有卸掉。记下的清单还在。");
        }
        return EnableResult.Success(id);
    }

    private static async Task<EnableResult> RefuseAsync(string userRoot, IReadOnlyList<ImportedPlugin> current, ImportedPlugin plugin, string error, string message, CancellationToken cancellationToken)
    {
        if (plugin.Enabled)
            await WriteIndexAsync(userRoot, current.Select(item => item.Id == plugin.Id ? item with { Enabled = false } : item).ToArray(), cancellationToken);
        return EnableResult.Fail(400, error, message);
    }

    private static async Task WriteIndexAsync(string userRoot, IReadOnlyList<ImportedPlugin> plugins, CancellationToken cancellationToken)
    {
        var index = JsonSerializer.Serialize(new ImportIndex(Schema, 2, plugins), Json);
        var indexPath = IndexPath(userRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(indexPath)!);
        var temporary = indexPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        await File.WriteAllTextAsync(temporary, index, cancellationToken);
        File.Move(temporary, indexPath, overwrite: true);
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

internal sealed record ImportedPlugin(string Id, string Name, string Version, string FileName, bool Enabled = false);

internal readonly record struct EnableResult(bool Ok, int Status, string Error, string Message, string Id)
{
    public static EnableResult Success(string id) => new(true, 200, "", "", id);
    public static EnableResult Fail(int status, string error, string message) => new(false, status, error, message, "");
}
