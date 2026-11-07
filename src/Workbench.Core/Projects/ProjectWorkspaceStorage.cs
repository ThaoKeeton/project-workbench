using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Workbench.Core.Projects;

// These checks protect cooperating stores and refuse pre-existing aliases. The host
// still owns its lifetime lease; these operations are not an OS security sandbox.
internal static class ProjectWorkspaceStorage
{
    internal static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    internal static readonly UTF8Encoding Utf8 = new(false, true);

    internal static ProjectWorkspaceStoreException Error(string code) => new(code);

    internal static async Task<T> RunAsync<T>(Func<Task<T>> action)
    {
        try { return await action(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or SecurityException)
        {
            // Never inspect an exception's Message, ToString, or inner exceptions.
            throw Error("StoreIoFailure");
        }
    }

    internal static string NormalizeRoot(string root)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(root)) throw Error("UnsafePath");
            var full = Path.GetFullPath(root);
            FileSystemGuard.RejectLinks(full);
            return full;
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or IOException or UnauthorizedAccessException or SecurityException)
        {
            throw Error("UnsafePath");
        }
    }

    internal static string PathIn(string root, params string[] segments)
    {
        try { return FileSystemGuard.ContainedPath(root, Path.Combine(segments)); }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or IOException or UnauthorizedAccessException or SecurityException)
        {
            throw Error("UnsafePath");
        }
    }

    internal static bool Exists(string path)
    {
        FileSystemGuard.RejectLinks(path);
        try { _ = File.GetAttributes(path); return true; }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
    }

    internal static void DirectoryPath(string path, bool create)
    {
        _ = PathIn(path, ".");
        if (Exists(path))
        {
            if ((File.GetAttributes(path) & FileAttributes.Directory) == 0) throw Error("UnsafePath");
        }
        else if (create)
        {
            Directory.CreateDirectory(path);
            _ = PathIn(path, ".");
        }
    }

    internal static void RegularFile(string path)
    {
        try { FileSystemGuard.EnsureRegularFile(path); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or SecurityException)
        {
            throw Error("UnsafePath");
        }
    }

    internal static async Task<byte[]?> ReadAsync(string path, int limit, string limitCode, CancellationToken cancellationToken)
    {
        _ = PathIn(Path.GetDirectoryName(path)!, Path.GetFileName(path));
        if (!Exists(path)) return null;
        RegularFile(path);
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous);
        if (stream.Length > limit) throw Error(limitCode);
        using var result = new MemoryStream((int)stream.Length);
        var buffer = new byte[81920];
        int count;
        while ((count = await stream.ReadAsync(buffer, cancellationToken)) != 0)
        {
            if (result.Length + count > limit) throw Error(limitCode);
            result.Write(buffer, 0, count);
        }
        return result.ToArray();
    }

    internal static async Task<FileStream> LeaseAsync(string path, CancellationToken cancellationToken)
    {
        DirectoryPath(Path.GetDirectoryName(path)!, create: true);
        for (var attempt = 0; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _ = PathIn(Path.GetDirectoryName(path)!, Path.GetFileName(path));
            if (Exists(path)) RegularFile(path);
            try
            {
                var lease = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                if (lease.Length == 0) return lease;
                lease.Dispose();
                throw Error("UnsafePath");
            }
            catch (IOException) when (attempt < 79) { await Task.Delay(25, cancellationToken); }
            catch (IOException) { throw Error("StoreBusy"); }
        }
    }

    internal static async Task AtomicWriteAsync(string path, byte[] bytes, bool overwrite, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path)!;
        DirectoryPath(directory, create: true);
        if (Exists(path)) RegularFile(path);
        var temporary = PathIn(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        var owned = false;
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                owned = true;
                await stream.WriteAsync(bytes, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }
            cancellationToken.ThrowIfCancellationRequested();
            _ = PathIn(directory, Path.GetFileName(path));
            if (Exists(path)) RegularFile(path);
            File.Move(temporary, path, overwrite);
            owned = false;
        }
        finally
        {
            if (owned && Exists(temporary))
            {
                RegularFile(temporary);
                File.Delete(temporary); // Only this operation's randomly named staging file.
            }
        }
    }

    internal static string Revision(byte[]? bytes) => bytes is null ? "missing" : Convert.ToHexStringLower(SHA256.HashData(bytes));

    internal static void ExpectRevision(string expected, byte[]? current)
    {
        if (!string.Equals(expected, Revision(current), StringComparison.Ordinal)) throw Error("RevisionConflict");
    }

    internal static bool IsId(string? id) => id == "default" || id is { Length: 32 } && IsLowerHex(id);
    internal static bool IsLowerHex(string value) => value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    internal static void ValidateId(string id)
    {
        if (!IsId(id)) throw Error("InvalidId");
    }

    internal static bool ValidUnicode(string value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            if (!char.IsSurrogate(value[i])) continue;
            if (!char.IsHighSurrogate(value[i]) || ++i == value.Length || !char.IsLowSurrogate(value[i])) return false;
        }
        return true;
    }

    internal static string Name(string? name, string code = "InvalidName")
    {
        if (name is null || !ValidUnicode(name) || name.Any(char.IsControl)) throw Error(code);
        var trimmed = name.Trim();
        if (trimmed.Length is 0 or > 80) throw Error(code);
        return trimmed;
    }

    internal static T Parse<T>(byte[] bytes, string code, Func<JsonElement, T> read)
    {
        try
        {
            _ = Utf8.GetCharCount(bytes);
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 16 });
            return read(document.RootElement);
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException or DecoderFallbackException or KeyNotFoundException)
        {
            throw Error(code);
        }
    }

    internal static Dictionary<string, JsonElement> Object(JsonElement element, string code, params string[] fields)
    {
        if (element.ValueKind != JsonValueKind.Object) throw Error(code);
        var properties = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
            if (!fields.Contains(property.Name, StringComparer.Ordinal) || !properties.TryAdd(property.Name, property.Value))
                throw Error(code);
        if (properties.Count != fields.Length) throw Error(code);
        return properties;
    }
}
