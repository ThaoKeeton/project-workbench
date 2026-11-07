using System.Text.Json;

namespace Workbench.App;

/// <summary>Host-caught failures never evaluate exception text or plugin-defined virtual members.</summary>
internal static class HostFailure
{
    internal static string ProjectCode(Exception error)
    {
        if (error is Workbench.Sdk.PluginTestingException testing)
        {
            var testingCode = testing.ErrorCode;
            return testingCode is "TestingContextClosed" or "InvalidTestSource" or "InvalidCaseSet" or "InvalidRun" or "InvalidEvidence"
                or "RevisionConflict" or "StoreLimitExceeded" or "StoreIoFailure" or "UnsafePath" or "TestStoreCorrupt"
                or "RecoveryRequired" or "ManualStepUnsupported" or "TargetUnavailable" or "AssertionFailed" or "AdapterFailed"
                or "TestRecordNotFound" or "StoreBusy" or "InvalidId" or "InvalidProducer" or "TestingFailed" ? testingCode : "ProjectOperationFailed";
        }
        var code = error switch
        {
            Workbench.Core.Projects.ProjectWorkspaceStoreException storage => storage.ErrorCode,
            Workbench.Sdk.PluginProjectContextException project => project.ErrorCode,
            OperationCanceledException => "Canceled",
            _ => null
        };
        // Public exception constructors do not establish the provenance of their code.
        return code is "InvalidId" or "InvalidName" or "InvalidCatalog" or "InvalidContext" or "RevisionConflict"
            or "CatalogLimitExceeded" or "ContextLimitExceeded" or "RecoveryRequired" or "RecoveryUnavailable"
            or "ActiveProjectArchived" or "ProjectNotFound" or "UnsafePath" or "StoreBusy" or "StoreIoFailure"
            or "ProjectContextClosed" or "ProjectContextFailed" or "Canceled" ? code : "ProjectOperationFailed";
    }
    internal static (T Theme, string? Error) ReadTheme<T>(Func<T> read, T systemTheme)
    {
        try { return (read(), null); }
        catch (Exception error) when (error is JsonException or IOException or UnauthorizedAccessException)
        {
            return (systemTheme, Describe("ThemeSettingsFailed", error));
        }
    }

    internal static string Describe(string code, Exception error) => $"{code} ({error switch
    {
        OperationCanceledException => "Canceled",
        JsonException => "InvalidData",
        IOException or UnauthorizedAccessException => "Storage",
        _ => "PluginFailure"
    }}).";

    internal static (T? Value, string? Error) CreateView<T>(Func<T?> factory) where T : class
    {
        try
        {
            var value = factory();
            return value is null ? (null, "NativeViewFailed (NullView).") : (value, null);
        }
        catch (Exception error) { return (null, Describe("NativeViewFailed", error)); }
    }

    internal static async Task<string?> RunAsync(Func<Task> action)
    {
        try { await action(); return null; }
        catch (Exception error) { return Describe("HostOperationFailed", error); }
    }
}
