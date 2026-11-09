using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Workbench.App;

internal enum HostDiagnosticStage
{
    Unknown, ApplicationResources, WindowCreation, WorkspaceOwnership, CatalogOpen,
    ProjectContext, RuntimeCreation, PackageMaintenance, PackageDiscovery, PluginRestore,
    CatalogCommit, UiReady
}

internal sealed class HostStartupDiagnostics
{
    private readonly object _gate = new();
    private HostDiagnosticStage _stage;
    private StartupErrorReport? _lastFailure;
    private bool _capturedForOperation;
    internal StartupErrorReport? LastFailure { get { lock (_gate) return _lastFailure; } }

    // Keep the previous immutable snapshot available until this attempt produces a failure.
    internal void BeginOperation() { lock (_gate) _capturedForOperation = false; }
    internal void Enter(HostDiagnosticStage stage)
    {
        lock (_gate) _stage = Enum.IsDefined(stage) ? stage : HostDiagnosticStage.Unknown;
    }

    internal void ObserveStartup(string stage)
    {
        HostDiagnosticStage? known = stage switch
        {
            "sdk-generated-main-application-constructor" or "initializing-compiled-application-xaml" or "application-resources-ready" => HostDiagnosticStage.ApplicationResources,
            "launch-callback-entered" or "constructing-main-window" or "window-constructor-entered" or "creating-navigation-view"
                or "creating-content-controls" or "creating-info-bar" or "window-content-ready" or "activating-main-window"
                or "main-window-activated" => HostDiagnosticStage.WindowCreation,
            "opening-host-runtime" or "constructing-project-runtime" => HostDiagnosticStage.RuntimeCreation,
            "applying-pending-package-maintenance" => HostDiagnosticStage.PackageMaintenance,
            "discovering-packages" => HostDiagnosticStage.PackageDiscovery,
            "restoring-enabled-packages" => HostDiagnosticStage.PluginRestore,
            "opening-home-page" or "host-initialization-completed" => HostDiagnosticStage.UiReady,
            _ => null
        };
        if (known is { } value) Enter(value); // Never retain unknown/free-form stage text.
    }

    internal StartupErrorReport Capture(Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);
        lock (_gate)
        {
            if (_capturedForOperation && _lastFailure is not null) return _lastFailure;
            _capturedForOperation = true;
            var hresult = error is Workbench.Core.Projects.ProjectWorkspaceStoreException { SystemErrorHResult: { } systemError }
                ? systemError : error.HResult;
            return _lastFailure = new(_stage, HostFailure.ProjectCode(error), hresult);
        }
    }

    internal StartupErrorReport CaptureCodeIfMissing(string? code)
    {
        lock (_gate)
        {
            if (_capturedForOperation && _lastFailure is not null) return _lastFailure;
            _capturedForOperation = true;
            return _lastFailure = new(_stage, code, null);
        }
    }
}

internal sealed class StartupErrorReport
{
    private readonly DateTimeOffset _createdUtc = DateTimeOffset.UtcNow;
    private readonly string _applicationVersion = ReadApplicationVersion();
    private readonly string? _buildCommit = ReadBuildCommit();
    private readonly string _runtimeVersion = Environment.Version.ToString();
    private readonly string _operatingSystemVersion = Environment.OSVersion.Version.ToString();
    private readonly string _processArchitecture = RuntimeInformation.ProcessArchitecture.ToString();
    private readonly string? _systemErrorCode;
    internal string ReportId { get; } = Guid.NewGuid().ToString("N");
    internal string Stage { get; }
    internal string ErrorCode { get; }
    internal string Category { get; }
    internal string FileName => $"Workbench-error-{ReportId}.json";

    internal StartupErrorReport(HostDiagnosticStage stage, string? errorCode, int? hresult)
    {
        Stage = Enum.IsDefined(stage) ? stage.ToString() : nameof(HostDiagnosticStage.Unknown);
        ErrorCode = SafeCode(errorCode);
        Category = ErrorCode switch
        {
            "UserStoreInUse" or "StoreBusy" or "StoreIoFailure" or "StoreAccessDenied" or "StoreDirectoryUnavailable" => "Storage",
            "ApplicationFileMissing" or "ApplicationFileInvalid" or "ApplicationFileLoadFailed" or "NativeDependencyMissing" => "Dependency",
            "InvalidCatalog" or "InvalidContext" or "RecoveryRequired" or "InvalidData" => "InvalidData",
            "Canceled" => "Canceled",
            "ProjectOperationFailed" => "Unexpected",
            _ => "Project"
        };
        _systemErrorCode = hresult is { } value ? $"0x{unchecked((uint)value):X8}" : null;
    }

    // Explicit projection is the privacy boundary. No exception object, arbitrary log,
    // startup argument, path, identity, project, environment, or plugin field enters it.
    internal byte[] Encode() => JsonSerializer.SerializeToUtf8Bytes(new
    {
        schemaVersion = 1,
        product = "ProjectWorkbench",
        reportId = ReportId,
        createdUtc = _createdUtc,
        applicationVersion = _applicationVersion,
        buildCommit = _buildCommit,
        runtimeVersion = _runtimeVersion,
        operatingSystemVersion = _operatingSystemVersion,
        processArchitecture = _processArchitecture,
        stage = Stage,
        errorCode = ErrorCode,
        category = Category,
        systemErrorCode = _systemErrorCode
    }, new JsonSerializerOptions { WriteIndented = true });

    private static string ReadApplicationVersion()
    {
        var text = typeof(StartupErrorReport).Assembly.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version;
        return Version.TryParse(text, out var version) ? version.ToString() : "0.0.0.0";
    }

    private static string? ReadBuildCommit()
    {
        var text = typeof(StartupErrorReport).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var separator = text?.LastIndexOf('+') ?? -1;
        if (separator < 0) return null;
        var candidate = text![(separator + 1)..];
        return candidate.Length == 40 && candidate.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f') ? candidate : null;
    }

    private static string SafeCode(string? code) => code is
        "UserStoreInUse" or "StoreAccessDenied" or "StoreDirectoryUnavailable" or "ApplicationFileMissing" or "ApplicationFileInvalid"
        or "ApplicationFileLoadFailed" or "NativeDependencyMissing" or "InvalidData"
        or "InvalidId" or "InvalidName" or "InvalidCatalog" or "InvalidContext" or "RevisionConflict"
        or "CatalogLimitExceeded" or "ContextLimitExceeded" or "RecoveryRequired" or "RecoveryUnavailable"
        or "ActiveProjectArchived" or "ProjectNotFound" or "UnsafePath" or "StoreBusy" or "StoreIoFailure"
        or "ProjectContextClosed" or "ProjectContextFailed" or "Canceled" or "ProjectOperationFailed"
        or "ProjectTransitionBusy" or "ProjectClosePending" or "ProjectClosing" or "ProjectNotReady"
        or "ProjectStartupPending" or "ProjectStopPending" or "ProjectRetirementPending" or "ProjectRestorePending"
        or "ProjectRestoreFailed" or "ProjectPreparationPending" or "ProjectPreparationCanceled" or "ProjectPreparationBusy"
        or "ProjectPreparationFailed" or "ProjectPluginUnavailable" or "ProjectCloseCleanupRequired" or "ProjectRuntimeMismatch"
        or "TestingContextClosed" or "InvalidTestSource" or "InvalidCaseSet" or "InvalidRun" or "InvalidEvidence"
        or "StoreLimitExceeded" or "TestStoreCorrupt" or "ManualStepUnsupported" or "TargetUnavailable"
        or "AssertionFailed" or "AdapterFailed" or "TestRecordNotFound" or "InvalidProducer" or "TestingFailed"
        ? code : "ProjectOperationFailed";
}
