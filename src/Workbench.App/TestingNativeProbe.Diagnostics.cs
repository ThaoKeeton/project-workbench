using System.Runtime.CompilerServices;
using System.Text.Json;
using Workbench.Sdk;

namespace Workbench.App;

internal static partial class TestingNativeProbe
{
    internal sealed record FailureDiagnostic(string Category, string? SourceFile = null, int? SourceLine = null,
        string? Operation = null, string? TestingErrorCode = null);

    internal enum RetryOperation
    {
        VerifySelectInterruptedRun,
        VerifyAwaitRetryReady,
        VerifyInvokeRetry,
        VerifyDiscoverRetry,
        VerifyAwaitRetryCompletion,
        VerifyValidateFrozenRetry,
        VerifyRoster
    }

    internal sealed class ExecutionProgress
    {
        private RetryOperation? operation;

        // Last admitted operation only: this does not identify a cause or claim completion.
        internal string? Operation => operation switch
        {
            RetryOperation.VerifySelectInterruptedRun => "VerifySelectInterruptedRun",
            RetryOperation.VerifyAwaitRetryReady => "VerifyAwaitRetryReady",
            RetryOperation.VerifyInvokeRetry => "VerifyInvokeRetry",
            RetryOperation.VerifyDiscoverRetry => "VerifyDiscoverRetry",
            RetryOperation.VerifyAwaitRetryCompletion => "VerifyAwaitRetryCompletion",
            RetryOperation.VerifyValidateFrozenRetry => "VerifyValidateFrozenRetry",
            RetryOperation.VerifyRoster => "VerifyRoster",
            _ => null
        };

        internal void Admit(RetryOperation value) => operation = value;
    }

    internal static void Require(bool condition, string message, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
    {
        if (!condition) throw new AssertionFailure(message, file, line);
    }

    // Only our sealed first-party assertion carries location. Foreign exception properties are never evaluated.
    internal static FailureDiagnostic DescribeFailure(Exception error, ExecutionProgress? progress = null)
    {
        var diagnostic = error switch
        {
            AssertionFailure assertion => assertion.Diagnostic,
            OperationCanceledException => new FailureDiagnostic("Canceled"),
            JsonException => new FailureDiagnostic("InvalidData"),
            IOException or UnauthorizedAccessException => new FailureDiagnostic("Storage"),
            _ => new FailureDiagnostic("Unexpected")
        };
        // The SDK type is sealed; its readonly ErrorCode still comes from a public constructor.
        var code = error is PluginTestingException ? HostFailure.ProjectCode(error) : null;
        return diagnostic with { Operation = progress?.Operation, TestingErrorCode = code is "ProjectOperationFailed" ? null : code };
    }

    private sealed class AssertionFailure : InvalidOperationException
    {
        internal FailureDiagnostic Diagnostic { get; }

        internal AssertionFailure(string message, string file, int line) : base(message)
        {
            var name = Path.GetFileName(file);
            Diagnostic = name is "TestingNativeProbe.cs" or "TestingNativeProbe.Authoring.cs" or "TestingNativeProbe.Execution.cs"
                && line is > 0 and <= 100000 ? new("Assertion", name, line) : new("Assertion");
        }
    }
}
