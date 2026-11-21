using System.Collections;
using System.Text;
using System.Text.Json;
using Workbench.App;
using Xunit;

namespace Workbench.Core.Tests;

public sealed class HostStartupDiagnosticsTests
{
    [Fact]
    public void Ownership_failure_freezes_the_failing_checkpoint_before_recovery_and_retry()
    {
        var diagnostics = new HostStartupDiagnostics();
        diagnostics.BeginOperation();
        diagnostics.Enter(HostDiagnosticStage.WorkspaceOwnership);
        var first = diagnostics.Capture(new UserStoreInUseException(new IOException(PrivateText)));
        diagnostics.Enter(HostDiagnosticStage.CatalogOpen);
        Assert.Same(first, diagnostics.CaptureCodeIfMissing("ProjectOperationFailed"));
        Assert.Same(first, diagnostics.Capture(new IOException("secondary rollback failure")));
        Assert.Equal("WorkspaceOwnership", first.Stage);
        Assert.Equal("UserStoreInUse", first.ErrorCode);
        Assert.Equal("Storage", first.Category);
        diagnostics.BeginOperation();
        diagnostics.Enter(HostDiagnosticStage.ProjectContext);
        var second = diagnostics.Capture(new UnauthorizedAccessException(PrivateText));
        Assert.NotEqual(first.ReportId, second.ReportId);
        Assert.Equal("ProjectContext", second.Stage);
        Assert.Equal("StoreAccessDenied", second.ErrorCode);
        Assert.Equal("WorkspaceOwnership", first.Stage);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Exported_snapshot_has_only_fixed_safe_fields_and_never_evaluates_exception_text(bool hostile)
    {
        var diagnostics = new HostStartupDiagnostics();
        diagnostics.Enter(HostDiagnosticStage.PackageDiscovery);
        diagnostics.ObserveStartup(PrivateText);
        var error = hostile ? new HostileException() : new Exception(PrivateText, new Exception(PrivateText));
        var snapshot = diagnostics.Capture(error);
        var bytes = snapshot.Encode();
        Assert.InRange(bytes.Length, 1, 4096);
        var text = Encoding.UTF8.GetString(bytes);
        Assert.DoesNotContain("synthetic", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("C:\\", text);
        Assert.DoesNotContain("https:", text);
        using var json = JsonDocument.Parse(bytes);
        var root = json.RootElement;
        Assert.Equal(new[] { "applicationVersion", "buildCommit", "category", "createdUtc", "errorCode", "operatingSystemVersion", "processArchitecture", "product", "reportId", "runtimeVersion", "schemaVersion", "stage", "systemErrorCode" },
            root.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("ProjectWorkbench", root.GetProperty("product").GetString());
        Assert.Equal("PackageDiscovery", root.GetProperty("stage").GetString());
        Assert.Equal("ProjectOperationFailed", root.GetProperty("errorCode").GetString());
        Assert.True(Guid.TryParseExact(root.GetProperty("reportId").GetString(), "N", out _));
        Assert.True(Version.TryParse(root.GetProperty("applicationVersion").GetString(), out _));
        Assert.True(Version.TryParse(root.GetProperty("operatingSystemVersion").GetString(), out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(PrivateText)]
    [InlineData("StoreBusy ")]
    public void Arbitrary_failure_codes_and_undefined_stages_cannot_enter_the_report(string? code)
    {
        var diagnostics = new HostStartupDiagnostics();
        diagnostics.Enter((HostDiagnosticStage)int.MaxValue);
        var snapshot = diagnostics.CaptureCodeIfMissing(code);
        Assert.Equal("Unknown", snapshot.Stage);
        Assert.Equal("ProjectOperationFailed", snapshot.ErrorCode);
        Assert.DoesNotContain("synthetic", Encoding.UTF8.GetString(snapshot.Encode()));
    }

    [Fact]
    public void Normal_launch_tracks_safe_checkpoints_without_a_probe_output_path()
    {
        var options = RunOptions.Parse([]);
        Assert.Null(options.ReportPath);
        options.TraceStartup("discovering-packages");
        var report = options.StartupDiagnostics.Capture(new IOException(PrivateText));
        Assert.Equal("PackageDiscovery", report.Stage);
        Assert.Equal("StoreIoFailure", report.ErrorCode);
    }

    [Fact]
    public async Task Core_storage_wrapper_keeps_only_the_original_numeric_system_error_for_the_report()
    {
        var wrapped = await Assert.ThrowsAsync<Workbench.Core.Projects.ProjectWorkspaceStoreException>(() =>
            Workbench.Core.Projects.ProjectWorkspaceStorage.RunAsync<int>(() =>
                Task.FromException<int>(new UnauthorizedAccessException(PrivateText))));
        Assert.Null(wrapped.InnerException);
        var diagnostics = new HostStartupDiagnostics();
        diagnostics.Enter(HostDiagnosticStage.CatalogOpen);
        using var report = JsonDocument.Parse(diagnostics.Capture(wrapped).Encode());
        Assert.Equal("StoreIoFailure", report.RootElement.GetProperty("errorCode").GetString());
        Assert.Equal("0x80070005", report.RootElement.GetProperty("systemErrorCode").GetString());
        Assert.DoesNotContain("synthetic", report.RootElement.GetRawText());
    }

    internal const string PrivateText = "synthetic-private-token-project-machine C:\\Users\\private https://private.invalid/?token=synthetic";
    private sealed class HostileException : Exception
    {
        public override string Message => throw new InvalidOperationException(PrivateText);
        public override string? StackTrace => throw new InvalidOperationException(PrivateText);
        public override string? Source { get => throw new InvalidOperationException(PrivateText); set => throw new InvalidOperationException(PrivateText); }
        public override IDictionary Data => throw new InvalidOperationException(PrivateText);
        public override string ToString() => throw new InvalidOperationException(PrivateText);
        public override Exception GetBaseException() => throw new InvalidOperationException(PrivateText);
    }
}
