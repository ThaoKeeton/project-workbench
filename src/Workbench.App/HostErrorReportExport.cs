namespace Workbench.App;

internal enum HostReportExportStatus { Succeeded, DestinationUnavailable, AlreadyExists, Canceled }

internal static class HostErrorReportExport
{
    internal static async Task<HostReportExportStatus> TryWriteAsync(string directory, StartupErrorReport report, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(report);
        string? staging = null;
        var ownsStaging = false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var folder = Path.GetFullPath(directory);
            HostRuntime.RejectLinks(folder);
            if (!Directory.Exists(folder)) return HostReportExportStatus.DestinationUnavailable;
            var target = Path.Combine(folder, report.FileName);
            HostRuntime.RejectLinks(target);
            if (Path.Exists(target)) return HostReportExportStatus.AlreadyExists;
            staging = Path.Combine(folder, $".Workbench-error-{Guid.NewGuid():N}.tmp");
            HostRuntime.RejectLinks(staging);
            await using (var stream = new FileStream(staging, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                ownsStaging = true;
                await stream.WriteAsync(report.Encode(), cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }
            cancellationToken.ThrowIfCancellationRequested();
            HostRuntime.RejectLinks(target);
            if (Path.Exists(target)) return HostReportExportStatus.AlreadyExists;
            File.Move(staging, target, overwrite: false);
            ownsStaging = false;
            return HostReportExportStatus.Succeeded;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return HostReportExportStatus.Canceled; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException
            or ArgumentException or NotSupportedException)
        {
            return HostReportExportStatus.DestinationUnavailable;
        }
        finally
        {
            if (ownsStaging && staging is not null)
            {
                try
                {
                    HostRuntime.RejectLinks(staging);
                    File.Delete(staging); // Only this invocation's randomly named new staging file.
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
            }
        }
    }
}
