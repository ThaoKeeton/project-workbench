using System.Text.Json;
using System.Text.Json.Serialization;
using Workbench.Core;
using Workbench.Sdk;

namespace Workbench.App;

internal enum NativeProbePhase { Unknown, Management, RestartSeed, RestartVerify, RestartDisabled, UpdateVerify, RollbackVerify, RemoveVerify, WorkspaceActualDispose }
internal enum NativeProbeReason { Unknown, UserRequest, Restore, HostShutdown, PluginDisabled, InitializationFailed, PackageChanged }
internal enum NativeProbeCheckpoint
{
    Enter, UiThread, Management, RestartChecks, SettingsLookup, RestorePublications, RestoreStartReason,
    GuardedLookup, GuardedAuthority, PersistenceData, PersistenceNote, PersistenceSecret,
    CredentialNavigation, CredentialLookup, CredentialEmpty, CredentialMetadata, FixturePage, FixtureSettings,
    Disable, DisableReason, FinalStop, FinalStopCompleted, FinalReport, FinalClose, Completed,
    WorkspaceTarget, WorkspaceFixtureMode, WorkspaceObstruction, WorkspaceContextNavigation, WorkspaceContextEdit,
    WorkspaceSaveFailure, WorkspaceFirstDispose, WorkspacePendingState, WorkspaceHeldLeases,
    WorkspaceReleaseStop, WorkspaceCleanupDispose, WorkspaceSettledState, WorkspaceActionAdmission,
    WorkspaceEditorLookup, WorkspaceEditorReady, WorkspaceOriginalBuffer, WorkspaceRemoveObstruction,
    WorkspaceCorrection, WorkspaceCloseRetry, WorkspaceClosed, WorkspacePersisted, WorkspaceReleasedLeases,
    WorkspaceFixtureCleanup
}

/// <summary>Only host-owned counts, identities-as-equalities and state booleans; no control text.</summary>
internal sealed class NativeWorkspaceCloseObservations
{
    public bool? FirstDisposeRejected { get; set; }
    public bool? CloseAttemptSucceeded { get; set; }
    public bool? RuntimeActionRejected { get; set; }
    public bool? HasPendingStops { get; set; }
    public bool? DocumentEditable { get; set; }
    public bool? DocumentHasPendingWork { get; set; }
    public bool? ExpectedDocumentError { get; set; }
    public bool? RuntimeAdmitted { get; set; }
    public bool? WindowClosed { get; set; }
    public bool? WindowClosing { get; set; }
    public bool? SameDocument { get; set; }
    public bool? SameManager { get; set; }
    public bool? SameGeneration { get; set; }
    public int? FactoryCalls { get; set; }
    public int? RecoveryEditorMatches { get; set; }
    public bool? EditorLoaded { get; set; }
    public bool? EditorEnabled { get; set; }
    public bool? EditorHasRoot { get; set; }
    public bool? EditorMatchesOriginal { get; set; }
    public bool? DocumentMatchesOriginal { get; set; }
    public bool? DocumentMatchesCorrection { get; set; }
    public bool? CloseRetryAvailable { get; set; }
    public int? HeldLeaseChecks { get; set; }
    public bool? FinalLeasesReleased { get; set; }
}

internal sealed record NativeWorkerPublication(int Runs, NativeProbeReason StartReason)
{
    internal bool IsRestored => Runs == 2 && StartReason == NativeProbeReason.Restore;
    internal static async Task<NativeWorkerPublication> ReadAsync(IPluginSettings settings) => new(
        await settings.GetAsync("worker-runs", 0), NativeProbeEvidence.Reason(await settings.GetAsync("worker-start", "")));
}

/// <summary>Only owned primitive observations; never settings, native text or exception objects.</summary>
internal sealed class NativeProbeObservations
{
    public NativeWorkspaceCloseObservations? WorkspaceClose { get; set; }
    public NativeWorkerPublication? WorkerPublication { get; set; }
    public NativeProbeReason? StartAssertionReason { get; set; }
    public int? GuardedMatches { get; set; }
    public PluginBackgroundTaskState? GuardedState { get; set; }
    public bool? GuardedResume { get; set; }
    public bool? GuardedDesired { get; set; }
    public int? ConfirmedRuns { get; set; }
    public bool? DataMatches { get; set; }
    public bool? NoteMatches { get; set; }
    public bool? SecretMatches { get; set; }
    public bool? PageLoaded { get; set; }
    public bool? PageHasRoot { get; set; }
    public int? CredentialMatches { get; set; }
    public bool? EditorEmpty { get; set; }
    public int? SecretMetadataMatches { get; set; }
    public bool? SecretConfigured { get; set; }
    public bool? DisableSucceeded { get; set; }
    public NativeProbeReason? StopAssertionReason { get; set; }
}

/// <summary>Probe-only sidecar survives generic failure-report replacement.</summary>
internal sealed class NativeProbeEvidence(string? phase, IReadOnlyList<string> completedChecks)
{
    private readonly NativeProbePhase _phase = phase switch
    {
        "management" => NativeProbePhase.Management,
        "restart-seed" => NativeProbePhase.RestartSeed,
        "restart-verify" => NativeProbePhase.RestartVerify,
        "restart-disabled" => NativeProbePhase.RestartDisabled,
        "update-verify" => NativeProbePhase.UpdateVerify,
        "rollback-verify" => NativeProbePhase.RollbackVerify,
        "remove-verify" => NativeProbePhase.RemoveVerify,
        "workspace-actual-dispose" => NativeProbePhase.WorkspaceActualDispose,
        _ => NativeProbePhase.Unknown
    };
    private NativeProbeCheckpoint _checkpoint = NativeProbeCheckpoint.Enter;
    internal NativeProbeObservations Observations { get; } = new();
    internal void At(NativeProbeCheckpoint checkpoint) => _checkpoint = checkpoint;
    internal static NativeProbeReason Reason(string value) => value switch
    {
        "UserRequest" => NativeProbeReason.UserRequest,
        "Restore" => NativeProbeReason.Restore,
        "HostShutdown" => NativeProbeReason.HostShutdown,
        "PluginDisabled" => NativeProbeReason.PluginDisabled,
        "InitializationFailed" => NativeProbeReason.InitializationFailed,
        "PackageChanged" => NativeProbeReason.PackageChanged,
        _ => NativeProbeReason.Unknown
    };

    internal async Task RunAsync(string? reportPath, Func<Task> action)
    {
        try { await action(); }
        catch (Exception error)
        {
            // This sidecar is independent of the principal failure report and
            // is written before generic host failure cleanup can replace it.
            try
            {
                if (reportPath is not null)
                {
                    var path = Path.GetFullPath(reportPath + ".evidence.json");
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    var options = new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
                    options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.KebabCaseLower));
                    File.WriteAllText(path, JsonSerializer.Serialize(new
                    {
                        schemaVersion = 1, processId = Environment.ProcessId, phase = _phase, checkpoint = _checkpoint,
                        completedChecks = completedChecks.ToArray(), observations = Observations,
                        error = HostFailure.Describe("NativeProbeFailed", error),
                        exceptionType = error.GetType().FullName,
                        errorHResult = error.HResult
                    }, options));
                }
            }
            catch (Exception) { /* Diagnostic I/O/serialization must never replace the original failure. */ }
            throw;
        }
    }
}
