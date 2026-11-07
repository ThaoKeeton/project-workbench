using System.Collections;
using System.Text.Json;
using Workbench.App;
using Xunit;

namespace Workbench.Core.Tests;

public sealed class TestingNativeProbeDiagnosticTests
{
    private const string PrivateText = "synthetic-private-token-body-path\n::error::synthetic\u001b[31m";

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
        public override string Message => throw new InvalidOperationException(PrivateText);
        public override string? StackTrace => throw new InvalidOperationException(PrivateText);
        public override string? Source { get => throw new InvalidOperationException(PrivateText); set => throw new InvalidOperationException(PrivateText); }
        public override string? HelpLink { get => throw new InvalidOperationException(PrivateText); set => throw new InvalidOperationException(PrivateText); }
        public override IDictionary Data => throw new InvalidOperationException(PrivateText);
        public override string ToString() => throw new InvalidOperationException(PrivateText);
        public override Exception GetBaseException() => throw new InvalidOperationException(PrivateText);
    }
}
