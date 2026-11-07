using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Workbench.App;

internal static partial class TestingNativeProbe
{
    internal sealed record FailureDiagnostic(string Category, string? SourceFile = null, int? SourceLine = null);

    internal static void Require(bool condition, string message, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
    {
        if (!condition) throw new AssertionFailure(message, file, line);
    }

    // Only our sealed first-party assertion carries location. Foreign exception properties are never evaluated.
    internal static FailureDiagnostic DescribeFailure(Exception error) => error switch
    {
        AssertionFailure assertion => assertion.Diagnostic,
        OperationCanceledException => new("Canceled"),
        JsonException => new("InvalidData"),
        IOException or UnauthorizedAccessException => new("Storage"),
        _ => new("Unexpected")
    };

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
