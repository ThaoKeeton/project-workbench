using Workbench.Sdk;

namespace Workbench.TestPlugin;

public sealed class ProjectFixturePlugin : IWorkbenchPlugin
{
    private IPluginProjectContext _context = null!;
    private string _mode = "";
    public async Task InitializeAsync(IPluginContext context, CancellationToken cancellationToken)
    {
        _context = (IPluginProjectContext)context;
        _mode = await context.Settings.GetAsync("projectMode", "normal", cancellationToken);
        _context.RegisterBeforeProjectLeave(PrepareAsync);
        context.RegisterCommand(new("identity", "Identity", _ => Task.FromResult(new PluginCommandResult(true,
            $"{_context.Project.Identity.Id}:{_context.Project.Identity.Name}"))));
        context.RegisterCommand(new("update", "Update context", async token =>
        {
            var read = await _context.Project.ReadAsync(token);
            await _context.Project.UpdateAsync("fixture instructions", read.Revision, token);
            return new(true, "updated");
        }));
        context.RegisterCommand(new("late-project-hook", "Late hook", _ =>
        {
            _context.RegisterBeforeProjectLeave(_ => Task.CompletedTask);
            return Task.FromResult(new PluginCommandResult(true, "unexpected"));
        }));
        context.RegisterCommand(new("unawaited-update", "Unawaited update", async token =>
        {
            var revision = await context.Settings.GetAsync("contextRevision", "missing", token);
            _ = _context.Project.UpdateAsync("unawaited instructions", revision);
            return new(true, "owned by project service");
        }));
        if (_mode == "duplicate") _context.RegisterBeforeProjectLeave(_ => Task.CompletedTask);
        if (_mode == "init-fail") throw new InvalidOperationException("synthetic-secret-never-log");
    }

    private async Task PrepareAsync(CancellationToken cancellationToken)
    {
        var settings = _context.Settings;
        var path = Path.Combine(_context.DataDirectory, "prepare-started");
        var release = Path.Combine(_context.DataDirectory, "release-prepare");
        settings.SetAsync("prepareThread", Thread.CurrentThread.Name ?? "").GetAwaiter().GetResult();
        if (_mode == "sync-held")
        {
            settings.SetAsync("prepareCalls", settings.GetAsync("prepareCalls", 0).GetAwaiter().GetResult() + 1).GetAwaiter().GetResult();
            File.WriteAllText(path, "synthetic synchronous prefix");
            while (!File.Exists(release)) Thread.Sleep(10);
        }
        else
        {
            await settings.SetAsync("prepareCalls", await settings.GetAsync("prepareCalls", 0) + 1);
            File.WriteAllText(path, "synthetic preparation");
        }
        if (_mode == "async-held") while (!File.Exists(release)) await Task.Delay(10); // Deliberately ignores cancellation.
        if (!await settings.GetAsync("allowPreparation", false))
        {
            if (_mode == "hostile") throw new HostileException();
            if (_mode == "spoof-code") throw new PluginProjectContextException("synthetic-secret-never-log");
        }
        // Retained ownership keeps even the lifetime source usable until this callback settles.
        using var registration = _context.Lifetime.Register(() => { });
        await settings.SetAsync("prepareLifetimeUsable", true);
        await settings.SetAsync("prepareFinished", true);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _context.Settings.SetAsync("stopCalls", await _context.Settings.GetAsync("stopCalls", 0) + 1);
        if (_mode == "stop-held")
        {
            try { await _context.Project.ReadAsync(); }
            catch (PluginProjectContextException error) { await _context.Settings.SetAsync("projectStopCode", error.ErrorCode); }
            while (!await _context.Settings.GetAsync("releaseStop", false)) await Task.Delay(10);
        }
    }

    private sealed class HostileException : Exception
    {
        public override string Message => throw new InvalidOperationException("synthetic-secret-never-log");
        public override string ToString() => throw new InvalidOperationException("synthetic-secret-never-log");
        public override Exception GetBaseException() => throw new InvalidOperationException("synthetic-secret-never-log");
    }
}
