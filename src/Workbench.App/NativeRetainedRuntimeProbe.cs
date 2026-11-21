using System.Text.Json.Nodes;
using Microsoft.UI.Xaml.Controls;
using Workbench.Core;
using Workbench.Sdk;
using static Workbench.App.NativeRuntimeProbe;
using static Workbench.App.NativeManagementProbe;

namespace Workbench.App;

/// <summary>Native full-runtime Stop remains reachable after actual on-disk rediscovery failures.</summary>
internal static class NativeRetainedRuntimeProbe
{
    private const string PeerId = "com.projectworkbench.native-peer";

    internal static async Task RunAsync(RunOptions options, Action<string> record)
    {
        foreach (var change in new[] { "changed", "missing", "duplicate" })
        {
            var root = Path.Combine(options.UserRoot, "retained-" + change);
            using (HostRuntime.AcquireLease(root))
            {
                var store = new PluginPackageStore(new() { PluginsRoot = Path.Combine(root, "Plugins"), UserRoot = root });
                Require((await store.InstallAsync(FixtureSource)).Succeeded, "Retained fixture installation failed.");
                var peerSource = Path.Combine(root, "synthetic-peer-source");
                CopyFixture(peerSource);
                var manifestPath = Path.Combine(peerSource, "plugin.json");
                var manifest = JsonNode.Parse(await File.ReadAllTextAsync(manifestPath))!;
                manifest["id"] = PeerId;
                manifest["name"] = "Unrelated native worker fixture";
                await File.WriteAllTextAsync(manifestPath, manifest.ToJsonString());
                Require((await store.InstallAsync(peerSource)).Succeeded, "Unrelated native worker installation failed.");
            }
            var extra = new MainWindow(new(false, root, null) { DirectRuntime = true });
            extra.Activate();
            var release = Path.Combine(root, "data", Id, "release-work");
            try
            {
                await extra.InitializeAsync();
                Require(extra.SmokeStartupFailure is null, "Retained-runtime host failed to initialize.");
                var manager = extra.SmokeManager;
                var settings = manager.GetSettings(Id);
                var peerSettings = manager.GetSettings(PeerId);
                await settings.SetAsync("mode", "hold-task");
                Require((await manager.EnableAsync(Id, confirmTrust: true)).Succeeded && (await manager.EnableAsync(PeerId, confirmTrust: true)).Succeeded,
                    "Retained/runtime peer enable failed.");
                Require((await manager.StartBackgroundTaskAsync(Id, "worker")).Succeeded && (await manager.StartBackgroundTaskAsync(PeerId, "worker")).Succeeded,
                    "Registered native workers did not start.");
                await WaitAsync(async () => await settings.GetAsync("worker-runs", 0) == 1 && await peerSettings.GetAsync("worker-runs", 0) == 1);
                var original = manager.Plugins.Single(plugin => plugin.Manifest?.Id == Id);
                var packagePath = original.PackagePath;
                if (change == "changed") await File.WriteAllTextAsync(Path.Combine(packagePath, "synthetic-change.txt"), "retained package change");
                else if (change == "missing") File.Delete(Path.Combine(packagePath, "plugin.json")); // Never replace/delete a loaded DLL.
                else CopyFixture(Path.Combine(root, "Plugins", "synthetic-duplicate"));
                var plugins = await extra.SmokeNavigateAsync("plugins");
                Invoke(Find<Button>(plugins, "重新发现本地插件包"));
                await WaitAsync(() => Task.FromResult(!extra.SmokeActionBusy && manager.Plugins.Any(plugin => plugin.Manifest?.Id == Id && plugin.State != PluginState.Enabled)));
                Require(manager.Plugins.Any(plugin => plugin.Manifest?.Id == Id && plugin.HasRuntime), "Rediscovery lost the known runtime owner.");
                Require(!manager.Contributions.Views.Any(view => view.PluginId == Id) && !manager.Contributions.Commands.Any(command => command.PluginId == Id),
                    "Invalidated package retained published contributions.");
                Require(!(await manager.EnableAsync(Id, confirmTrust: true)).Succeeded && !(await manager.StartBackgroundTaskAsync(Id, "worker")).Succeeded,
                    "Invalidated package admitted a second instance/start.");
                plugins = await extra.SmokeNavigateAsync("plugins");
                Require(Find<Button>(plugins, $"停用 {original.Manifest!.Name}").IsEnabled, "Plugin page withheld owned-runtime Disable.");
                var background = await extra.SmokeNavigateAsync("background");
                Require(Find<Button>(background, $"停用后台插件 {Id}").IsEnabled, "Background page withheld owned-runtime Disable.");
                if (change == "changed")
                {
                    plugins = await extra.SmokeNavigateAsync("plugins");
                    Invoke(Find<Button>(plugins, $"停用 {original.Manifest.Name}"));
                }
                else Invoke(Find<Button>(background, $"停用后台插件 {Id}"));
                await WaitAsync(() => Task.FromResult(!extra.SmokeActionBusy && manager.Plugins.Any(plugin => plugin.Manifest?.Id == Id && plugin.HasPendingStop)));
                Require(await settings.GetAsync("stop-calls", 0) == 1, "Disable did not retain the original Stop task.");
                // Retry through the other native management surface while the same registered worker is still held.
                if (change == "changed")
                {
                    background = await extra.SmokeNavigateAsync("background");
                    var retry = Find<Button>(background, $"停用后台插件 {Id}");
                    Require(retry.IsEnabled && (string)retry.Content == "重试停用插件", "Background retry Stop was unavailable.");
                    Invoke(retry);
                }
                else
                {
                    plugins = await extra.SmokeNavigateAsync("plugins");
                    var retry = Find<Button>(plugins, $"停用 {original.Manifest.Name}");
                    Require(retry.IsEnabled && (string)retry.Content == "重试停用", "Plugin-page retry Stop was unavailable.");
                    Invoke(retry);
                }
                await File.WriteAllTextAsync(release, "release retained worker");
                await WaitAsync(() => Task.FromResult(!extra.SmokeActionBusy && manager.Plugins.Where(plugin => plugin.Manifest?.Id == Id).All(plugin => !plugin.HasRuntime && !plugin.HasPendingStop)));
                Require(await settings.GetAsync("stop-calls", 0) == 1 && await settings.GetAsync("instances", 0) == 1 && await settings.GetAsync("worker-runs", 0) == 1,
                    "Rediscovery/Disable/Retry created a second runtime, work item or Stop.");
                Require(manager.Plugins.Single(plugin => plugin.Manifest?.Id == PeerId) is { State: PluginState.Enabled, HasRuntime: true }
                    && manager.GetBackgroundTaskSnapshots(PeerId).Single(task => task.Id == "worker").State == PluginBackgroundTaskState.Running
                    && await peerSettings.GetAsync("instances", 0) == 1 && await peerSettings.GetAsync("stop-calls", 0) == 0,
                    "Stopping the invalidated runtime disturbed the unrelated active worker.");
                if (change == "duplicate") Require(manager.Plugins.Where(plugin => plugin.Manifest?.Id == Id).All(plugin => plugin.State == PluginState.Invalid),
                    "Stopping owned work made duplicate metadata valid.");
            }
            finally
            {
                Directory.CreateDirectory(Path.GetDirectoryName(release)!);
                await File.WriteAllTextAsync(release, "release retained worker during cleanup");
                await extra.FinishSmokeAsync();
            }
            record($"Actual {change} rediscovery retained native Disable/Retry Stop on both management pages, one runtime/work/Stop and the unrelated running worker");
        }
    }

    private static void CopyFixture(string destination)
    {
        foreach (var file in Directory.EnumerateFiles(FixtureSource, "*", SearchOption.AllDirectories))
        {
            var path = Path.Combine(destination, Path.GetRelativePath(FixtureSource, file));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.Copy(file, path);
        }
    }
}
