using System.Diagnostics.CodeAnalysis;
using System.Runtime.Loader;

namespace Workbench.TestExecutor;

public sealed record UiAction(string Name, ElementSelector? Target, string? Text, string? Expected, bool Sensitive);

public sealed record UiActionResult(bool Succeeded, string? Actual, string? ErrorType, bool FallbackEligible, string Log)
{
    public static UiActionResult Ok(string? actual) => new(true, actual, null, false, "ok");

    public static UiActionResult Fail(string errorType, bool fallback, string? actual)
        => new(false, actual, errorType, fallback, errorType);
}

public interface IUiAutomationSession : IAsyncDisposable
{
    Task<UiActionResult> ExecuteAsync(UiAction action, CancellationToken cancellationToken);

    Task<RawFrame> CaptureAsync(CancellationToken cancellationToken);

    PixelRect? Locate(ElementSelector selector);
}

public interface IUiAutomationSessionFactory
{
    Task<IUiAutomationSession> OpenAsync(ApplicationTarget? application, CancellationToken cancellationToken);
}

public interface IDeadlineFactory
{
    CancellationTokenSource Start(TimeSpan timeout, CancellationToken outer);
}

public sealed class DeadlineFactory : IDeadlineFactory
{
    public CancellationTokenSource Start(TimeSpan timeout, CancellationToken outer)
    {
        var linked = CancellationTokenSource.CreateLinkedTokenSource(outer);
        linked.CancelAfter(timeout <= TimeSpan.Zero ? TimeSpan.FromMilliseconds(1) : timeout);
        return linked;
    }
}

public sealed class UnavailableUiAutomationFactory : IUiAutomationSessionFactory
{
    public Task<IUiAutomationSession> OpenAsync(ApplicationTarget? application, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IUiAutomationSession>(new UnavailableSession());
    }

    private sealed class UnavailableSession : IUiAutomationSession
    {
        public Task<UiActionResult> ExecuteAsync(UiAction action, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(UiActionResult.Fail(ExecutorError.PlatformUnsupported, true, "windows-only"));
        }

        public Task<RawFrame> CaptureAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(SyntheticFrames.PasswordField());
        }

        public PixelRect? Locate(ElementSelector selector) => new PixelRect(8, 8, 16, 8);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

public static class UiAutomationFactory
{
    public static IUiAutomationSessionFactory Create()
    {
        if (!OperatingSystem.IsWindows()) return new UnavailableUiAutomationFactory();
        var directory = Path.GetDirectoryName(typeof(UiAutomationFactory).Assembly.Location);
        if (string.IsNullOrEmpty(directory)) return new UnavailableUiAutomationFactory();
        return TryLoad(Path.Combine(directory, "Workbench.TestExecutor.Windows.dll")) ?? new UnavailableUiAutomationFactory();
    }

    public static IUiAutomationSessionFactory? TryLoad(string assemblyPath)
    {
        if (string.IsNullOrWhiteSpace(assemblyPath) || !File.Exists(assemblyPath)) return null;
        if (!OperatingSystem.IsWindows()) return null;
        return LoadWindows(assemblyPath);
    }

    [ExcludeFromCodeCoverage(Justification = "Loads the Windows-only FlaUI assembly. The Linux build cannot execute it; the Windows CI job drives FlaUI directly.")]
    private static IUiAutomationSessionFactory? LoadWindows(string assemblyPath)
    {
        try
        {
            var context = AssemblyLoadContext.GetLoadContext(typeof(UiAutomationFactory).Assembly) ?? AssemblyLoadContext.Default;
            var assembly = context.LoadFromAssemblyPath(Path.GetFullPath(assemblyPath));
            var type = assembly.GetType("Workbench.TestExecutor.Windows.FlaUiSessionFactory", throwOnError: false);
            return type is null ? null : Activator.CreateInstance(type) as IUiAutomationSessionFactory;
        }
        catch (Exception error) when (error is FileLoadException or BadImageFormatException or TypeLoadException
            or FileNotFoundException or InvalidOperationException or MissingMethodException or System.Reflection.TargetInvocationException)
        {
            return null;
        }
    }
}
