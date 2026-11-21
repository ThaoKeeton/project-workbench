using System.Text.Json;

namespace Workbench.Service;

/// <summary>Which local plugins stay on the rail, and which were opened last.
/// Stored beside schema 1, never inside it. Only plugins already on this computer are accepted.</summary>
internal static class UiDeskStore
{
    public const string Schema = "workbench.ui-desk.v2.preview";
    public static readonly string[] Known = ["upcam", "ding", "test", "runner"];
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static DeskState Default { get; } = new([.. Known], []);

    public static async Task<DeskRead> ReadAsync(string userRoot, CancellationToken cancellationToken)
    {
        var path = Path.Combine(userRoot, "v2-preview", "desk.json");
        if (!File.Exists(path)) return new(Default, false);
        try
        {
            await using var stream = File.OpenRead(path);
            var document = await JsonSerializer.DeserializeAsync<DeskFile>(stream, Json, cancellationToken);
            var accepted = Accept(document, Allowed(userRoot));
            return accepted is null ? new(Default, true) : new(accepted, false);
        }
        catch (Exception error) when (error is JsonException or IOException)
        {
            return new(Default, true);
        }
    }

    public static async Task<DeskState> WriteAsync(string userRoot, IReadOnlyList<string>? pinned, string? touch, CancellationToken cancellationToken)
    {
        var allowed = Allowed(userRoot);
        if (!ValidPins(pinned, allowed)) throw new ArgumentOutOfRangeException(nameof(pinned));
        if (touch is not null && !allowed.Contains(touch)) throw new ArgumentOutOfRangeException(nameof(touch));
        var current = (await ReadAsync(userRoot, cancellationToken)).State;
        var recent = current.Recent;
        if (touch is not null)
        {
            recent = recent.Where(item => item.Id != touch).Prepend(new DeskOpen(touch, DateTimeOffset.UtcNow.ToString("O"))).Take(8).ToArray();
        }
        var next = new DeskState(pinned!, recent);
        var directory = Path.Combine(userRoot, "v2-preview");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "desk.json");
        var pins = next.Pinned.ToArray();
        var opens = next.Recent.Select(item => new DeskOpenFile(item.Id, item.At)).ToArray();
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new DeskFile(Schema, 2, pins, opens), Json);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        await File.WriteAllBytesAsync(temporary, bytes, cancellationToken);
        File.Move(temporary, path, overwrite: true);
        return next;
    }

    private static HashSet<string> Allowed(string userRoot)
    {
        var allowed = new HashSet<string>(Known, StringComparer.Ordinal);
        foreach (var plugin in PluginImportStore.Read(userRoot))
            if (plugin.Enabled) allowed.Add(plugin.Id);
        return allowed;
    }

    private static bool ValidPins(IReadOnlyList<string>? pinned, IReadOnlySet<string> allowed)
        => pinned is { Count: <= 6 } && pinned.All(allowed.Contains) && pinned.Distinct(StringComparer.Ordinal).Count() == pinned.Count;

    private static DeskState? Accept(DeskFile? document, IReadOnlySet<string> allowed)
    {
        if (document is not { Schema: Schema, SchemaVersion: 2, Pinned: { } pinned }) return null;
        if (pinned.Length > 6 || pinned.Distinct(StringComparer.Ordinal).Count() != pinned.Length) return null;
        var kept = pinned.Where(allowed.Contains).ToArray();
        document = document with { Pinned = kept };
        if (!ValidPins(document.Pinned, allowed)) return null;
        var recent = (document.Recent ?? []).Where(item => item.Id is not null && allowed.Contains(item.Id) && item.At is not null)
            .Select(item => new DeskOpen(item.Id!, item.At!))
            .GroupBy(item => item.Id, StringComparer.Ordinal)
            .Select(group => group.First())
            .Take(8)
            .ToArray();
        return new DeskState(document.Pinned!, recent);
    }

    private sealed record DeskFile(string Schema, int SchemaVersion, string[]? Pinned, DeskOpenFile[]? Recent);
    private sealed record DeskOpenFile(string? Id, string? At);
}

internal sealed record DeskRead(DeskState State, bool Recovered);

internal sealed record DeskState(IReadOnlyList<string> Pinned, IReadOnlyList<DeskOpen> Recent);

internal sealed record DeskOpen(string Id, string At);
