namespace Workbench.TestExecutor;

public static class ExecutorSchemas
{
    public const string Suite = "workbench.test-executor.suite.v2.preview";
    public const string SuiteList = "workbench.test-executor.suite-list.v2.preview";
    public const string Settings = "workbench.test-executor.settings.v2.preview";
    public const string Run = "workbench.test-executor.run.v2.preview";
    public const string StepEvent = "workbench.test-executor.step-event.v2.preview";
    public const string StepEvidence = "workbench.test-executor.step-evidence.v2.preview";
    public const string FailureReport = "workbench.test-executor.failure-report.v2.preview";
    public const int Version = 2;
    public const int DefaultFlaUiTimeoutSeconds = 15;
    public const int DefaultAiTimeoutSeconds = 60;
    public const int MaxAttempts = 2;
    public const string BuiltinSuiteId = "synthetic-window";
    public const string DefaultComputerUseModel = "computer-use-preview";
}

public static class ExecutorError
{
    public const string InvalidSuite = "InvalidSuite";
    public const string SuiteNotFound = "SuiteNotFound";
    public const string DuplicateSuiteId = "DuplicateSuiteId";
    public const string InvalidSettings = "InvalidSettings";
    public const string InvalidRun = "InvalidRun";
    public const string RunNotFound = "RunNotFound";
    public const string ReportNotFound = "ReportNotFound";
    public const string RunLimit = "RunLimit";
    public const string Timeout = "Timeout";
    public const string ControlNotFound = "ControlNotFound";
    public const string TreeUnstable = "TreeUnstable";
    public const string AssertionMismatch = "AssertionMismatch";
    public const string ActionFailed = "ActionFailed";
    public const string PlatformUnsupported = "PlatformUnsupported";
    public const string ProviderUnavailable = "ProviderUnavailable";
    public const string CredentialMissing = "CredentialMissing";
    public const string ComputerUseDisabled = "ComputerUseDisabled";
    public const string CaptureFailed = "CaptureFailed";
    public const string NeedsReview = "NeedsReview";
    public const string DebugPortMissing = "DebugPortMissing";
    public const string LaunchFailed = "LaunchFailed";
    public const string WindowNotFound = "WindowNotFound";
    public const string BaselineMissing = "BaselineMissing";
    public const string BudgetExceeded = "BudgetExceeded";
    public const string Cancelled = "Cancelled";
}
