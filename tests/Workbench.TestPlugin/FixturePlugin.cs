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
