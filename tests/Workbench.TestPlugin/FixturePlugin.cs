using System.Runtime.CompilerServices;
using Workbench.Sdk;
using Workbench.TestDependency;

namespace Workbench.TestPlugin;

internal static class ModuleProbe
{
    [ModuleInitializer]
    internal static void OnLoad()
    {
        var marker = Environment.GetEnvironmentVariable("WORKBENCH_TEST_ACTIVATION_MARKER");
        if (marker is not null)
            File.WriteAllText(marker, "module initialized");
    }
}

public sealed class FixturePlugin : IWorkbenchPlugin
{
    private IPluginContext? _context;
    private string _mode = "normal";

    public async Task InitializeAsync(IPluginContext context, CancellationToken cancellationToken)
    {
        _context = context;
        _mode = await context.Settings.GetAsync("mode", "normal", cancellationToken);
        var marker = Environment.GetEnvironmentVariable("WORKBENCH_TEST_ACTIVATION_MARKER");
        if (marker is not null)
            await File.WriteAllTextAsync(marker, "initialized", cancellationToken);
        var dependency = ReadDependency();
        context.RegisterView(new FixtureView());
        context.RegisterSetting(new PluginSetting("draft", "Draft", "demo draft"));
        context.RegisterCommand(new PluginCommand("ping", "Ping", async token =>
        {
            var draft = await context.Settings.GetAsync("draft", "demo draft", token);
            return new PluginCommandResult(true, $"{context.PluginId}:{dependency}:{draft}");
        }));
        context.RegisterCommand(new PluginCommand("throw", "Throw", _ => throw new InvalidOperationException("fixture command failure")));
        context.RegisterCommand(new PluginCommand("wait", "Wait", async token =>
        {
            await context.Settings.SetAsync("commandStarted", true, token);
            try { await Task.Delay(Timeout.Infinite, token); }
            catch (OperationCanceledException)
            {
                await context.Settings.SetAsync("commandCanceled", true);
                throw;
            }
            return new(true, "finished");
        }));
        context.RegisterCommand(new PluginCommand("late", "Late registration", _ =>
        {
            context.RegisterView(new FixtureView("late"));
            return Task.FromResult(new PluginCommandResult(true, "unexpectedly registered"));
        }));
        context.RegisterCommand(new PluginCommand("dependency-context", "Dependency context", _ =>
            Task.FromResult(new PluginCommandResult(true, ReadDependencyContext()))));
        context.RegisterCommand(new PluginCommand("write-data", "Write scoped data", async token =>
        {
            var directory = context.DataDirectory;
            var path = Path.Combine(directory, "fixture.txt");
            await File.WriteAllTextAsync(path, context.PluginId, token);
            return new(true, directory);
        }));
        switch (_mode)
        {
            case "init-fail": throw new InvalidOperationException("fixture initialization failure");
            case "duplicate-view": context.RegisterView(new FixtureView()); break;
            case "duplicate-command": context.RegisterCommand(new PluginCommand("ping", "Duplicate", _ => Task.FromResult(new PluginCommandResult(true, "bad")))); break;
            case "duplicate-setting": context.RegisterSetting(new PluginSetting("draft", "Duplicate", "bad")); break;
            case "invalid-view": context.RegisterView(new FixtureView("../escape")); break;
            case "init-cancel":
                await context.Settings.SetAsync("initializing", true, cancellationToken);
                await Task.Delay(Timeout.Infinite, cancellationToken);
                break;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_context is not null)
        {
            await _context.Settings.SetAsync("stopped", true, cancellationToken);
            await _context.Settings.SetAsync("stopLifetimeCanceled", _context.Lifetime.IsCancellationRequested, cancellationToken);
        }
        if (_mode == "stop-fail")
            throw new InvalidOperationException("fixture stop failure");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string ReadDependency() => DependencyProbe.GetMessage();

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string ReadDependencyContext() => System.Runtime.Loader.AssemblyLoadContext.GetLoadContext(typeof(DependencyProbe).Assembly)?.Name ?? "unknown";

    private sealed class FixtureView(string id = "overview") : IPluginView
    {
        public string Id => id;
        public string Title => "Fixture overview";
    }
}

public sealed class ThrowingConstructorPlugin : IWorkbenchPlugin
{
    public ThrowingConstructorPlugin() => throw new InvalidOperationException("fixture constructor failure");
    public Task InitializeAsync(IPluginContext context, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class ControlledStopPlugin : IWorkbenchPlugin
{
    private IPluginContext _context = null!;
    public async Task InitializeAsync(IPluginContext context, CancellationToken cancellationToken)
    {
        _context = context;
        await context.Settings.SetAsync("instanceCount", await context.Settings.GetAsync("instanceCount", 0) + 1);
        context.RegisterView(new ControlledView());
        if (await context.Settings.GetAsync("lifetimeCallback", false))
            context.Lifetime.Register(() =>
            {
                context.Settings.SetAsync("callbackStarted", true).GetAwaiter().GetResult();
                while (!context.Settings.GetAsync("releaseCancellation", false).GetAwaiter().GetResult()) Thread.Sleep(10);
                context.Settings.SetAsync("callbackFinished", true).GetAwaiter().GetResult();
            });
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _context.Settings.SetAsync("stopCalls", await _context.Settings.GetAsync("stopCalls", 0) + 1);
        while (!await _context.Settings.GetAsync("releaseStop", false)) await Task.Delay(10);
        await _context.Settings.SetAsync("stopFinished", true);
        if (await _context.Settings.GetAsync("throwStop", false)) throw new InvalidOperationException("synthetic-secret-never-log");
    }

    private sealed class ControlledView : IPluginView
    {
        public string Id => "controlled";
        public string Title => "Controlled stop";
    }
}

public sealed class SensitiveFailurePlugin : IWorkbenchPlugin
{
    private string _failure = "";
    public async Task InitializeAsync(IPluginContext context, CancellationToken cancellationToken)
    {
        _failure = await context.Settings.GetAsync("failure", "");
        context.RegisterCommand(new("throw", "Throw", _ => throw Failure()));
        if (_failure.StartsWith("initialize", StringComparison.Ordinal)) throw Failure();
    }
    public Task StopAsync(CancellationToken cancellationToken) => _failure.StartsWith("stop", StringComparison.Ordinal)
        ? Task.FromException(Failure()) : Task.CompletedTask;
    private Exception Failure() => _failure.EndsWith("-virtual", StringComparison.Ordinal)
        ? new SensitiveException() : new InvalidOperationException("synthetic-secret-never-log");
    private sealed class SensitiveException : Exception
    {
        public override Exception GetBaseException() => throw new InvalidOperationException("synthetic-secret-never-log");
    }
}

public sealed class RuntimeFixturePlugin : IWorkbenchPlugin
{
    private IPluginRuntimeContext _context = null!;
    private string _mode = "";
    private int _runCount;

    public async Task InitializeAsync(IPluginContext context, CancellationToken cancellationToken)
    {
        _context = context as IPluginRuntimeContext ?? throw new InvalidOperationException("Runtime services are unavailable.");
        _mode = await context.Settings.GetAsync("runtimeMode", "cooperative", cancellationToken);
        _runCount = await context.Settings.GetAsync("jobRuns", 0, cancellationToken);
        await context.Settings.SetAsync("instanceCount", await context.Settings.GetAsync("instanceCount", 0) + 1);
        _context.BackgroundTasks.Register(new("monitor", "Synthetic monitor", RunAsync)
        {
            RequiresPluginStart = await context.Settings.GetAsync("requiresPluginStart", false)
        });
        _context.Secrets.Register(new("openai.api-key", "Synthetic API key", "Test credential"));
        _context.Secrets.Register(new("dingtalk.webhook-url", "Synthetic webhook URL"));
        if (_mode is "initialize-probe" or "initialize-fail")
        {
            var attempted = await _context.BackgroundTasks.StartAsync("monitor", cancellationToken);
            await context.Settings.SetAsync("startedDuringInitialize", attempted.Succeeded);
            if (_mode == "initialize-fail") throw new InvalidOperationException("synthetic-secret-never-log");
        }
        if (_mode == "initialize-wait")
        {
            await context.Settings.SetAsync("initializing", true);
            while (!await context.Settings.GetAsync("releaseInitialize", false)) await Task.Delay(10, cancellationToken);
        }
        context.RegisterCommand(new("late-task", "Late task registration", _ =>
        {
            _context.BackgroundTasks.Register(new("late", "Late task", _ => Task.CompletedTask));
            return Task.FromResult(new PluginCommandResult(true, "unexpectedly registered"));
        }));
        context.RegisterCommand(new("late-secret", "Late secret registration", _ =>
        {
            _context.Secrets.Register(new("late", "Late secret"));
            return Task.FromResult(new PluginCommandResult(true, "unexpectedly registered"));
        }));
        context.RegisterCommand(new("start-monitor", "Synthetic plugin confirmation completed", async token =>
        {
            var result = await _context.BackgroundTasks.StartAsync("monitor", token);
            return new(result.Succeeded, result.Message);
        }));
        context.RegisterCommand(new("resume-monitor", "Synthetic plugin resume attempt", async token =>
        {
            var result = await _context.BackgroundTasks.SetResumeOnStartupAsync("monitor", true, token);
            return new(result.Succeeded, result.Message);
        }));
    }

    private async Task RunAsync(IPluginBackgroundExecution execution)
    {
        var settings = _context.Settings;
        var run = Interlocked.Increment(ref _runCount);
        await File.WriteAllTextAsync(Path.Combine(_context.DataDirectory, $"run-{run}.marker"), "synthetic execution");
        await settings.SetAsync("jobRuns", run);
        await settings.SetAsync("startReason", execution.StartReason.ToString());
        if (_mode == "callback-held")
            execution.CancellationToken.Register(() =>
            {
                settings.SetAsync("jobCallbackStarted", true).GetAwaiter().GetResult();
                while (!settings.GetAsync("releaseJobCallback", false).GetAwaiter().GetResult()) Thread.Sleep(10);
                settings.SetAsync("callbackFinalReason", execution.StopReason.ToString()).GetAwaiter().GetResult();
            });
        if (_mode == "fault") throw new InvalidOperationException("synthetic-secret-never-log");
        if (_mode == "complete") return;
        if (_mode == "synchronous")
        {
            while (!File.Exists(Path.Combine(_context.DataDirectory, "release-sync"))) Thread.Sleep(10);
            return;
        }
        try { await Task.Delay(Timeout.Infinite, execution.CancellationToken); }
        catch (OperationCanceledException)
        {
            await settings.SetAsync("jobStopReason", execution.StopReason.ToString());
            if (_mode == "held")
                while (!await settings.GetAsync("releaseJob", false)) await Task.Delay(10);
        }
        await settings.SetAsync("jobFinished", true);
        await settings.SetAsync("jobFinalStopReason", execution.StopReason.ToString());
        await settings.SetAsync("contextFinalStopReason", _context.StopReason.ToString());
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _context.Settings.SetAsync("stopCalls", await _context.Settings.GetAsync("stopCalls", 0) + 1);
        await _context.Settings.SetAsync("contextStopReason", _context.StopReason.ToString());
    }
}
