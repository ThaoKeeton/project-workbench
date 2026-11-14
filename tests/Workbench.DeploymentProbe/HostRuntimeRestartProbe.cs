using System.Text.Json;
using Workbench.App;
using Workbench.Core;
using Workbench.Sdk;

internal static class HostRuntimeRestartProbe
{
    internal static async Task RunAsync(string[] args)
    {
        var id = args[2];
        var guarded = args[3].Contains("-guarded-", StringComparison.Ordinal);
        var phase = args[3].Split('-')[^1];
        // The synthetic monitor writes a few settings files while it stops. A busy
        // Windows runner can take longer than the shipping 5s budget, and this probe
        // is checking restart behavior, not that deadline.
        await using var runtime = new HostRuntime(new()
        {
            PluginsRoot = args[0],
            UserRoot = args[1],
            StopTimeout = TimeSpan.FromSeconds(20)
        });
        await runtime.InitializeAsync();
        var manager = runtime.Manager;
        var settings = manager.GetSettings(id);
        if (phase == "seed")
        {
            await settings.SetAsync("requiresPluginStart", guarded);
            await settings.SetAsync("persistent-note", "synthetic process restart");
            Require((await manager.EnableAsync(id, confirmTrust: true)).Succeeded);
            Require((await manager.SetBackgroundTaskResumeOnStartupAsync(id, "monitor", true)).Succeeded == !guarded);
            if (guarded) Require((await manager.ExecuteCommandAsync(id, "start-monitor")).Succeeded);
            else Require((await manager.StartBackgroundTaskAsync(id, "monitor")).Succeeded);
            await WaitAsync(async () => await settings.GetAsync("jobRuns", 0) == 1);
        }
        else if (phase == "verify")
        {
            if (!guarded) await WaitAsync(async () => await settings.GetAsync("jobRuns", 0) == 2);
            var snapshot = manager.GetBackgroundTaskSnapshots(id).Single();
            Require(guarded ? snapshot is { State: PluginBackgroundTaskState.Stopped, ResumeOnStartup: false, DesiredRunning: false }
                : snapshot is { State: PluginBackgroundTaskState.Running, ResumeOnStartup: true, DesiredRunning: true });
            Require(await settings.GetAsync("persistent-note", "") == "synthetic process restart");
        }
        else if (phase == "disabled") Require(manager.Plugins.Single() is { IsLoaded: false, HasRuntime: false, DesiredEnabled: false });
        else throw new ArgumentException("Unknown isolated host runtime phase.");
        var taskState = manager.GetBackgroundTaskSnapshots(id).SingleOrDefault()?.State.ToString() ?? "Unavailable";
        var runs = await settings.GetAsync("jobRuns", 0);
        var startReason = await settings.GetAsync("startReason", "");
        if (phase == "verify") Require((await manager.DisableAsync(id)).Succeeded);
        Console.WriteLine(JsonSerializer.Serialize(new { runs, startReason, taskState, hasRuntime = manager.Plugins.Single().HasRuntime }));
    }

    private static void Require(bool condition)
    {
        if (!condition) throw new InvalidOperationException("SyntheticHostRestartInvariantFailed");
    }

    private static async Task WaitAsync(Func<Task<bool>> condition)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!await condition()) await Task.Delay(10, deadline.Token);
    }
}
