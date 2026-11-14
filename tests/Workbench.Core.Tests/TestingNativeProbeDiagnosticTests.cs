using System.Collections;
using System.Text.Json;
using Workbench.App;
using Workbench.Sdk;
using Xunit;

namespace Workbench.Core.Tests;

public sealed class TestingNativeProbeDiagnosticTests
{
    private const string PrivateText = "synthetic-private-token-body-path\n::error::synthetic\u001b[31m";

    [Theory]
    [InlineData(0, "VerifySelectInterruptedRun")]
    [InlineData(1, "VerifyAwaitRetryReady")]
    [InlineData(2, "VerifyInvokeRetry")]
    [InlineData(3, "VerifyDiscoverRetry")]
    [InlineData(4, "VerifyAwaitRetryCompletion")]
    [InlineData(5, "VerifyValidateFrozenRetry")]
    [InlineData(6, "VerifyRoster")]
    public void Failure_reports_only_the_last_admitted_retry_operation(int operation, string expected)
    {
        var progress = new TestingNativeProbe.ExecutionProgress();
        progress.Admit(TestingNativeProbe.RetryOperation.VerifySelectInterruptedRun);
        progress.Admit((TestingNativeProbe.RetryOperation)operation);
        var diagnostic = TestingNativeProbe.DescribeFailure(new HostileException(), progress);
        Assert.Equal("Unexpected", diagnostic.Category);
        Assert.Equal(expected, diagnostic.Operation);
        Assert.Null(diagnostic.TestingErrorCode);
        Assert.Null(diagnostic.SourceFile);
        Assert.DoesNotContain("synthetic", JsonSerializer.Serialize(diagnostic));
    }

    [Fact]
    public void Separate_executions_start_empty_and_cannot_change_each_others_failure_snapshot()
    {
        var error = new HostileException();
        var first = new TestingNativeProbe.ExecutionProgress();
        Assert.Null(TestingNativeProbe.DescribeFailure(error, first).Operation);
        first.Admit(TestingNativeProbe.RetryOperation.VerifyInvokeRetry);
        var snapshot = TestingNativeProbe.DescribeFailure(error, first);
        var second = new TestingNativeProbe.ExecutionProgress();
        Assert.Null(TestingNativeProbe.DescribeFailure(error, second).Operation);
        second.Admit(TestingNativeProbe.RetryOperation.VerifyRoster);
        Assert.Equal("VerifyInvokeRetry", TestingNativeProbe.DescribeFailure(error, first).Operation);
        Assert.Equal("VerifyRoster", TestingNativeProbe.DescribeFailure(error, second).Operation);
        first.Admit(TestingNativeProbe.RetryOperation.VerifyAwaitRetryCompletion);
        Assert.Equal("VerifyInvokeRetry", snapshot.Operation);
        Assert.Equal("VerifyRoster", TestingNativeProbe.DescribeFailure(error, second).Operation);
        Assert.Null(TestingNativeProbe.DescribeFailure(error, new TestingNativeProbe.ExecutionProgress()).Operation);
        Assert.Null(TestingNativeProbe.DescribeFailure(error).Operation);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(7)]
    [InlineData(int.MaxValue)]
    public void Undefined_checkpoint_values_are_never_projected(int value)
    {
        var progress = new TestingNativeProbe.ExecutionProgress();
        progress.Admit((TestingNativeProbe.RetryOperation)value);
        Assert.Null(TestingNativeProbe.DescribeFailure(new HostileException(), progress).Operation);
    }

    [Theory]
    [InlineData("TestingContextClosed")]
    [InlineData("InvalidTestSource")]
    [InlineData("InvalidCaseSet")]
    [InlineData("InvalidRun")]
    [InlineData("InvalidEvidence")]
    [InlineData("RevisionConflict")]
    [InlineData("StoreLimitExceeded")]
    [InlineData("StoreIoFailure")]
    [InlineData("UnsafePath")]
    [InlineData("TestStoreCorrupt")]
    [InlineData("RecoveryRequired")]
    [InlineData("ManualStepUnsupported")]
    [InlineData("TargetUnavailable")]
    [InlineData("AssertionFailed")]
    [InlineData("AdapterFailed")]
    [InlineData("TestRecordNotFound")]
    [InlineData("StoreBusy")]
    [InlineData("InvalidId")]
    [InlineData("InvalidProducer")]
    [InlineData("TestingFailed")]
    public void Sealed_sdk_failures_project_known_testing_codes_without_changing_exception_semantics(string code)
    {
        var error = new PluginTestingException(code);
        var originalHResult = error.HResult;
        var diagnostic = TestingNativeProbe.DescribeFailure(error);
        Assert.Equal("Unexpected", diagnostic.Category);
        Assert.Equal(code, diagnostic.TestingErrorCode);
        Assert.Null(diagnostic.SourceFile);
        Assert.Null(diagnostic.SourceLine);
        Assert.Null(diagnostic.Operation);
        Assert.Equal(originalHResult, error.HResult);
        Assert.Equal(code, error.ErrorCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("storebusy")]
    [InlineData("StoreBusy ")]
    [InlineData("ProjectOperationFailed")]
    [InlineData("Canceled")]
    [InlineData("/synthetic/private/path")]
    [InlineData(PrivateText)]
    public void Public_sdk_constructor_text_is_not_trusted_as_a_testing_code(string? code)
    {
        var diagnostic = TestingNativeProbe.DescribeFailure(new PluginTestingException(code!));
        Assert.Equal("Unexpected", diagnostic.Category);
        Assert.Null(diagnostic.TestingErrorCode);
        Assert.DoesNotContain("synthetic", JsonSerializer.Serialize(diagnostic));
    }

    [Fact]
    public void Failed_first_party_assertion_keeps_invalid_operation_semantics_and_only_reports_known_source_location()
    {
        TestingNativeProbe.Require(true, PrivateText);
        var error = Assert.ThrowsAny<InvalidOperationException>(() =>
            TestingNativeProbe.Require(false, PrivateText, "/synthetic/private/TestingNativeProbe.Authoring.cs", 43));
        Assert.Equal(PrivateText, error.Message);
        Assert.Equal(unchecked((int)0x80131509), error.HResult);
        var diagnostic = TestingNativeProbe.DescribeFailure(error);
        Assert.Equal("Assertion", diagnostic.Category);
        Assert.Equal("TestingNativeProbe.Authoring.cs", diagnostic.SourceFile);
        Assert.Equal(43, diagnostic.SourceLine);
        Assert.DoesNotContain("synthetic", JsonSerializer.Serialize(diagnostic));
    }

    [Theory]
    [InlineData("/synthetic/unknown.cs", 43)]
    [InlineData("/synthetic/TestingNativeProbe.cs\n::error::private", 43)]
    [InlineData("/synthetic/TestingNativeProbe.cs", 0)]
    [InlineData("/synthetic/TestingNativeProbe.cs", 100001)]
    public void Unrecognized_assertion_location_is_omitted_without_disclosing_caller_text(string file, int line)
    {
        var error = Assert.ThrowsAny<InvalidOperationException>(() => TestingNativeProbe.Require(false, PrivateText, file, line));
        var diagnostic = TestingNativeProbe.DescribeFailure(error);
        Assert.Equal("Assertion", diagnostic.Category);
        Assert.Null(diagnostic.SourceFile);
        Assert.Null(diagnostic.SourceLine);
        Assert.Null(diagnostic.Operation);
        Assert.Null(diagnostic.TestingErrorCode);
        Assert.DoesNotContain("synthetic", JsonSerializer.Serialize(diagnostic));
    }

    [Theory]
    [InlineData("cancel", "Canceled")]
    [InlineData("json", "InvalidData")]
    [InlineData("io", "Storage")]
    [InlineData("access", "Storage")]
    [InlineData("unexpected", "Unexpected")]
    public void Other_failures_use_closed_categories_without_claiming_an_assertion_location(string kind, string category)
    {
        Exception error = kind switch
        {
            "cancel" => new OperationCanceledException(PrivateText),
            "json" => new JsonException(PrivateText),
            "io" => new IOException(PrivateText),
            "access" => new UnauthorizedAccessException(PrivateText),
            _ => new InvalidOperationException(PrivateText)
        };
        var diagnostic = TestingNativeProbe.DescribeFailure(error);
        Assert.Equal(category, diagnostic.Category);
        Assert.Null(diagnostic.SourceFile);
        Assert.Null(diagnostic.SourceLine);
        Assert.DoesNotContain("synthetic", JsonSerializer.Serialize(diagnostic));
    }

    [Fact]
    public void Diagnostic_extraction_never_evaluates_foreign_exception_properties()
    {
        var diagnostic = TestingNativeProbe.DescribeFailure(new HostileException());
        Assert.Equal("Unexpected", diagnostic.Category);
        Assert.Null(diagnostic.SourceFile);
        Assert.Null(diagnostic.SourceLine);
        Assert.DoesNotContain("synthetic", JsonSerializer.Serialize(diagnostic));
    }

    private sealed class HostileException : Exception
    {
        public string ErrorCode => throw new InvalidOperationException(PrivateText);
        public override string Message => throw new InvalidOperationException(PrivateText);
        public override string? StackTrace => throw new InvalidOperationException(PrivateText);
        public override string? Source { get => throw new InvalidOperationException(PrivateText); set => throw new InvalidOperationException(PrivateText); }
        public override string? HelpLink { get => throw new InvalidOperationException(PrivateText); set => throw new InvalidOperationException(PrivateText); }
        public override IDictionary Data => throw new InvalidOperationException(PrivateText);
        public override string ToString() => throw new InvalidOperationException(PrivateText);
        public override Exception GetBaseException() => throw new InvalidOperationException(PrivateText);
    }
}
