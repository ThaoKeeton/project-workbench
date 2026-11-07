using System.Text.Json;
using Workbench.App;
using Workbench.Sdk;
using Xunit;

namespace Workbench.Core.Tests;

public sealed class NativeProbeEvidenceTests
{
    private const string PrivateMarker = "synthetic-provider-secret-must-not-be-reported";

    [Fact]
    public async Task Workspace_failure_sidecar_retains_lookup_checkpoint_and_safe_state_before_generic_cleanup()
    {
        using var disk = new TestDirectory();
        var report = Path.Combine(disk.Root, "close-actual-dispose.startup.json");
        var evidence = new NativeProbeEvidence("workspace-actual-dispose", []);
        evidence.At(NativeProbeCheckpoint.WorkspaceEditorLookup);
        evidence.Observations.WorkspaceClose = new()
        {
            FirstDisposeRejected = true, CloseAttemptSucceeded = false, HasPendingStops = false,
            DocumentEditable = true, RuntimeAdmitted = false, SameDocument = true, SameManager = true,
            SameGeneration = true, FactoryCalls = 2, RecoveryEditorMatches = 0, CloseRetryAvailable = true
        };
        var original = new HostileException();
        Assert.Same(original, await Record.ExceptionAsync(() => evidence.RunAsync(report, () => throw original)));
        var text = await File.ReadAllTextAsync(report + ".evidence.json");
        using var saved = JsonDocument.Parse(text);
        Assert.Equal("workspace-actual-dispose", saved.RootElement.GetProperty("phase").GetString());
        Assert.Equal("workspace-editor-lookup", saved.RootElement.GetProperty("checkpoint").GetString());
        var state = saved.RootElement.GetProperty("observations").GetProperty("workspaceClose");
        Assert.True(state.GetProperty("documentEditable").GetBoolean());
        Assert.False(state.GetProperty("runtimeAdmitted").GetBoolean());
        Assert.False(state.GetProperty("hasPendingStops").GetBoolean());
        Assert.Equal(2, state.GetProperty("factoryCalls").GetInt32());
        Assert.Equal(0, state.GetProperty("recoveryEditorMatches").GetInt32());
        Assert.Equal(JsonValueKind.Null, state.GetProperty("editorLoaded").ValueKind);
        Assert.Equal(0, original.VirtualAccesses);
        Assert.DoesNotContain(PrivateMarker, text);
        new RunOptions(false, disk.User, report).WriteFailure("shutdown", original);
        Assert.Equal(text, await File.ReadAllTextAsync(report + ".evidence.json"));
        // The actual probe finally has its own sidecar; changed cleanup observations must not
        // overwrite the earlier failed lookup's checkpoint or state tuple.
        evidence.At(NativeProbeCheckpoint.WorkspaceFixtureCleanup);
        evidence.Observations.WorkspaceClose.RecoveryEditorMatches = 1;
        var cleanupFailure = new HostileException();
        Assert.Same(cleanupFailure, await Record.ExceptionAsync(() => evidence.RunAsync(report + ".cleanup", () => throw cleanupFailure)));
        Assert.Equal(text, await File.ReadAllTextAsync(report + ".evidence.json"));
        using var cleanup = JsonDocument.Parse(await File.ReadAllTextAsync(report + ".cleanup.evidence.json"));
        Assert.Equal("workspace-fixture-cleanup", cleanup.RootElement.GetProperty("checkpoint").GetString());
        Assert.Equal(0, cleanupFailure.VirtualAccesses);
        Assert.Equal(0, original.VirtualAccesses);
    }

    [Fact]
    public async Task Failure_sidecar_retains_checkpoint_observed_tuple_and_partial_checks_without_exception_text()
    {
        using var disk = new TestDirectory();
        var report = Path.Combine(disk.Root, "restart-verify.json");
        List<string> checks = ["Completed host-owned synthetic check"];
        var evidence = new NativeProbeEvidence("restart-verify", checks);
        evidence.At(NativeProbeCheckpoint.RestoreStartReason);
        evidence.Observations.WorkerPublication = new(2, NativeProbeReason.UserRequest);
        evidence.Observations.StartAssertionReason = NativeProbeReason.UserRequest;
        evidence.Observations.ConfirmedRuns = 1;
        var original = new HostileException();
        Assert.Same(original, await Record.ExceptionAsync(() => evidence.RunAsync(report, () => throw original)));
        Assert.Equal(0, original.VirtualAccesses);
        Assert.True(File.Exists(report + ".evidence.json"), "The probe must retain its evidence independently of the main failure report.");
        var text = await File.ReadAllTextAsync(report + ".evidence.json");
        Assert.DoesNotContain(PrivateMarker, text);
        using var saved = JsonDocument.Parse(text);
        Assert.Equal(Environment.ProcessId, saved.RootElement.GetProperty("processId").GetInt32());
        Assert.Equal("restart-verify", saved.RootElement.GetProperty("phase").GetString());
        Assert.Equal("restore-start-reason", saved.RootElement.GetProperty("checkpoint").GetString());
        Assert.Equal(checks.Single(), saved.RootElement.GetProperty("completedChecks")[0].GetString());
        Assert.Equal(2, saved.RootElement.GetProperty("observations").GetProperty("workerPublication").GetProperty("runs").GetInt32());
        Assert.Equal("user-request", saved.RootElement.GetProperty("observations").GetProperty("startAssertionReason").GetString());
        Assert.Equal("NativeProbeFailed (PluginFailure).", saved.RootElement.GetProperty("error").GetString());
        new RunOptions(true, disk.User, report).WriteFailure("initialize-or-native-smoke", original);
        // A later generic cleanup failure may replace the principal report,
        // but must not replace the first probe failure's independent evidence.
        new RunOptions(true, disk.User, report).WriteFailure("shutdown", new IOException(PrivateMarker));
        Assert.Equal(text, await File.ReadAllTextAsync(report + ".evidence.json"));
        Assert.Equal(0, original.VirtualAccesses);
    }

    [Fact]
    public async Task Unknown_reason_and_phase_text_are_never_serialized()
    {
        using var disk = new TestDirectory();
        var report = Path.Combine(disk.Root, "privacy.json");
        var evidence = new NativeProbeEvidence(PrivateMarker, []);
        evidence.At(NativeProbeCheckpoint.FinalStop);
        evidence.Observations.StartAssertionReason = NativeProbeEvidence.Reason(PrivateMarker);
        evidence.Observations.SecretMatches = false;
        var original = new InvalidOperationException(PrivateMarker);
        Assert.Same(original, await Record.ExceptionAsync(() => evidence.RunAsync(report, () => Task.FromException(original))));
        Assert.True(File.Exists(report + ".evidence.json"));
        var text = await File.ReadAllTextAsync(report + ".evidence.json");
        Assert.DoesNotContain(PrivateMarker, text);
        using var saved = JsonDocument.Parse(text);
        Assert.Equal("unknown", saved.RootElement.GetProperty("phase").GetString());
        Assert.Equal("final-stop", saved.RootElement.GetProperty("checkpoint").GetString());
        Assert.Equal("unknown", saved.RootElement.GetProperty("observations").GetProperty("startAssertionReason").GetString());
        Assert.False(saved.RootElement.GetProperty("observations").GetProperty("secretMatches").GetBoolean());
    }

    [Fact]
    public async Task Diagnostic_io_failure_does_not_replace_the_original_failure()
    {
        using var disk = new TestDirectory();
        var report = Path.Combine(disk.Root, "occupied.json");
        Directory.CreateDirectory(report + ".evidence.json");
        var evidence = new NativeProbeEvidence("restart-verify", []);
        var original = new HostileException();
        Assert.Same(original, await Record.ExceptionAsync(() => evidence.RunAsync(report, () => throw original)));
        Assert.Equal(0, original.VirtualAccesses);
    }

    [Fact]
    public async Task Success_runs_normally_without_creating_a_failure_sidecar()
    {
        using var disk = new TestDirectory();
        var report = Path.Combine(disk.Root, "success.json");
        var ran = false;
        await new NativeProbeEvidence("restart-verify", []).RunAsync(report, () => { ran = true; return Task.CompletedTask; });
        Assert.True(ran);
        Assert.False(File.Exists(report + ".evidence.json"));
    }

    private sealed class HostileException : Exception
    {
        internal int VirtualAccesses { get; private set; }
        public override string Message { get { VirtualAccesses++; throw new InvalidOperationException(PrivateMarker); } }
        public override string ToString() { VirtualAccesses++; throw new InvalidOperationException(PrivateMarker); }
        public override Exception GetBaseException() { VirtualAccesses++; throw new InvalidOperationException(PrivateMarker); }
    }
}
