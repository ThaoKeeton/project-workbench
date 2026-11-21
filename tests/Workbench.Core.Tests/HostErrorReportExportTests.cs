using System.Text.Json;
using Workbench.App;
using Xunit;

namespace Workbench.Core.Tests;

public sealed class HostErrorReportExportTests
{
    [Fact]
    public async Task Export_works_outside_a_blocked_user_store_without_reading_or_modifying_its_bytes()
    {
        using var disk = new TestDirectory();
        var original = "synthetic-private-project-configuration"u8.ToArray();
        await File.WriteAllBytesAsync(disk.User, original); // A file obstructs the app's data directory.
        var destination = Path.Combine(disk.Root, "exports");
        Directory.CreateDirectory(destination);
        var diagnostics = new HostStartupDiagnostics();
        diagnostics.Enter(HostDiagnosticStage.WorkspaceOwnership);
        var report = diagnostics.Capture(new IOException(HostStartupDiagnosticsTests.PrivateText));
        Assert.Equal(HostReportExportStatus.Succeeded, await HostErrorReportExport.TryWriteAsync(destination, report));
        var saved = Assert.Single(Directory.GetFiles(destination));
        Assert.Equal(report.FileName, Path.GetFileName(saved));
        Assert.Equal(report.Encode(), await File.ReadAllBytesAsync(saved));
        using var json = JsonDocument.Parse(await File.ReadAllBytesAsync(saved));
        Assert.Equal("WorkspaceOwnership", json.RootElement.GetProperty("stage").GetString());
        Assert.Equal(original, await File.ReadAllBytesAsync(disk.User));
        Assert.DoesNotContain("synthetic", await File.ReadAllTextAsync(saved));
    }

    [Fact]
    public async Task Repeated_export_never_overwrites_an_existing_report_or_leaves_staging_files()
    {
        using var disk = new TestDirectory();
        var report = new HostStartupDiagnostics().Capture(new InvalidOperationException());
        Assert.Equal(HostReportExportStatus.Succeeded, await HostErrorReportExport.TryWriteAsync(disk.Root, report));
        var path = Path.Combine(disk.Root, report.FileName);
        var retained = "existing report chosen by user"u8.ToArray();
        await File.WriteAllBytesAsync(path, retained);
        Assert.Equal(HostReportExportStatus.AlreadyExists, await HostErrorReportExport.TryWriteAsync(disk.Root, report));
        Assert.Equal(retained, await File.ReadAllBytesAsync(path));
        Assert.Equal([path], Directory.GetFiles(disk.Root));
    }

    [Fact]
    public async Task Cancel_or_unavailable_destination_does_not_create_a_report_or_change_an_existing_file()
    {
        using var disk = new TestDirectory();
        var report = new HostStartupDiagnostics().Capture(new IOException());
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        Assert.Equal(HostReportExportStatus.Canceled, await HostErrorReportExport.TryWriteAsync(disk.Root, report, canceled.Token));
        Assert.Empty(Directory.GetFiles(disk.Root));
        var file = Path.Combine(disk.Root, "not-a-folder");
        await File.WriteAllTextAsync(file, "keep");
        Assert.Equal(HostReportExportStatus.DestinationUnavailable, await HostErrorReportExport.TryWriteAsync(file, report));
        Assert.Equal("keep", await File.ReadAllTextAsync(file));
        Assert.Equal([file], Directory.GetFiles(disk.Root));
    }
}
