using System.Text.Json;

namespace Workbench.Core;

internal static class AtomicJsonFile
{
    internal static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    /// <summary>Test-only count of sharing violations the next replace should survive.</summary>
    internal static readonly AsyncLocal<int> TransientSharingViolations = new();

    internal static async Task WriteAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        FileSystemGuard.RejectLinks(path);
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        var temporary = FileSystemGuard.ContainedPath(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.WriteThrough);
            try
            {
                await JsonSerializer.SerializeAsync(stream, value, Options, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }
            finally
            {
                // The replace below retries when Windows still reports the file busy.
                await stream.DisposeAsync();
            }
            cancellationToken.ThrowIfCancellationRequested();
            await ReplaceAsync(temporary, path, cancellationToken);
        }
        finally
        {
            await RemoveTemporaryAsync(temporary);
        }
    }

    private static Task ReplaceAsync(string temporary, string path, CancellationToken cancellationToken) =>
        ReleaseAsync(() =>
        {
            FileSystemGuard.RejectLinks(path);
            if (File.Exists(path))
                FileSystemGuard.EnsureRegularFile(path);
            File.Move(temporary, path, overwrite: true);
        }, cancellationToken);

    private static Task RemoveTemporaryAsync(string temporary) =>
        ReleaseAsync(() =>
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }, CancellationToken.None);

    private static async Task ReleaseAsync(Action action, CancellationToken cancellationToken)
    {
        const int sharingViolation = unchecked((int)0x80070020);
        for (var attempt = 0; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var pending = TransientSharingViolations.Value;
                if (pending > 0)
                {
                    TransientSharingViolations.Value = pending - 1;
                    throw new IOException("The process cannot access the file because it is being used by another process.", sharingViolation);
                }
                action();
                return;
            }
            catch (IOException error) when (attempt < 19 && error.HResult is 32 or sharingViolation)
            {
                // Windows can keep a write-through file locked for a moment after Close.
                await Task.Delay(25, CancellationToken.None);
            }
        }
    }
}
