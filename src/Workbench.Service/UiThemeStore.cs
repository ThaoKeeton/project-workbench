using System.Text.Json;

namespace Workbench.Service;

/// <summary>Preview UI theme choice. Stored beside schema 1, never inside it.</summary>
internal static class UiThemeStore
{
    public const string Schema = "workbench.ui-theme.v2.preview";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly string[] Allowed = ["system", "light", "dark"];

    public static bool IsSetting(string? value) => value is not null && Allowed.Contains(value);

    public static async Task<string> ReadAsync(string userRoot, CancellationToken cancellationToken)
    {
        var path = Path.Combine(userRoot, "v2-preview", "ui-theme.json");
        if (!File.Exists(path)) return "system";
        await using var stream = File.OpenRead(path);
        var document = await JsonSerializer.DeserializeAsync<ThemeFile>(stream, Json, cancellationToken);
        return document is { Schema: Schema, SchemaVersion: 2 } && IsSetting(document.Setting) ? document.Setting : "system";
    }

    public static async Task WriteAsync(string userRoot, string setting, CancellationToken cancellationToken)
    {
        if (!IsSetting(setting)) throw new ArgumentOutOfRangeException(nameof(setting));
        var directory = Path.Combine(userRoot, "v2-preview");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "ui-theme.json");
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new ThemeFile(Schema, 2, setting), Json);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        await File.WriteAllBytesAsync(temporary, bytes, cancellationToken);
        File.Move(temporary, path, overwrite: true);
    }

    private sealed record ThemeFile(string Schema, int SchemaVersion, string Setting);
}
