using System.Text.Encodings.Web;
using System.Text.Json;

namespace Workbench.Service.Abstractions;

/// <summary>
/// Shared helpers for preview modules. They do not open sockets or send messages.
/// </summary>
public static class PluginPermissions
{
    public static void Require(IReadOnlySet<string> granted, string permission)
    {
        if (!granted.Contains(permission)) throw new PluginPermissionDeniedException();
    }
}

public static class PluginResponses
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        // Draft files are JSON text shown by the host, not HTML. Control characters stay escaped.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static async Task<PluginModuleResponse> Run(Func<Task<PluginModuleResponse>> action)
    {
        try
        {
            return await action();
        }
        catch (PluginPermissionDeniedException)
        {
            return Fail(403, "PermissionDenied");
        }
        catch (PluginDataException)
        {
            return Fail(400, "InvalidPluginData");
        }
        catch (JsonException)
        {
            return Fail(400, "InvalidPluginData");
        }
    }

    public static PluginModuleResponse Ok(object body) => Body(200, body);

    public static PluginModuleResponse Fail(int status, string code) => Body(status, new { error = code });

    public static PluginModuleResponse Body(int status, object body)
        => new(status, "application/json; charset=utf-8", JsonSerializer.SerializeToUtf8Bytes(body, Json));
}

public static class PluginDataFile
{
    public static string Contain(string directory, string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) || fileName.IndexOfAny(['/', '\\', ':']) >= 0 || fileName is "." or "..")
            throw new PluginDataException();
        var root = Path.GetFullPath(directory);
        var path = Path.GetFullPath(Path.Combine(root, fileName));
        var relative = Path.GetRelativePath(root, path);
        if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative))
            throw new PluginDataException();
        return path;
    }

    public static string BaseName(string? name)
    {
        var value = name ?? "";
        var slash = Math.Max(value.LastIndexOf('/'), value.LastIndexOf('\\'));
        var baseName = slash >= 0 ? value[(slash + 1)..] : value;
        if (baseName is "." or ".." || baseName.Length is 0 or > 80 || baseName.Any(char.IsControl))
            return "未命名记录";
        return baseName;
    }

    public static async Task WriteAtomicAsync(string path, byte[] bytes, CancellationToken cancellationToken)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        await File.WriteAllBytesAsync(temporary, bytes, cancellationToken);
        File.Move(temporary, path, overwrite: true);
    }
}

/// <summary>Shanghai calendar day, so “today” does not follow the server’s UTC date.</summary>
public static class PreviewClock
{
    private static readonly string[] DefaultZones = ["Asia/Shanghai", "China Standard Time"];

    public static DateOnly Today() => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Zone()));

    public static TimeZoneInfo Zone(IEnumerable<string>? candidates = null)
    {
        foreach (var id in candidates ?? DefaultZones)
        {
            if (TimeZoneInfo.TryFindSystemTimeZoneById(id, out var zone)) return zone;
        }
        throw new InvalidOperationException("shanghai-time-zone-missing");
    }
}

public sealed class PluginPermissionDeniedException : Exception
{
    public PluginPermissionDeniedException() : base("PermissionDenied") { }
}

public sealed class PluginDataException : Exception
{
    public PluginDataException() : base("InvalidPluginData") { }
}
