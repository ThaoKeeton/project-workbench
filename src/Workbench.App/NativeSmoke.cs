using System.Text.Json;
using Microsoft.UI.Xaml;
using Workbench.Core;
using Workbench.WinUI.Sdk;

namespace Workbench.App;

/// <summary>Runs only inside the real WinUI process on its UI thread, with isolated local state.</summary>
internal static class NativeSmoke
{
    public static async Task RunAsync(MainWindow window, RunOptions options)
    {
        List<string> checks = [];
        void Record(string check)
        {
            checks.Add(check);
            options.WriteReport(new { succeeded = false, stage = "running", userRoot = options.UserRoot, checks });
        }
        window.AssertUiThread();
        Record("WinUI Application.Start and UI synchronization context initialized");
        var manager = window.SmokeManager;
        Require(manager.Plugins.Count == 2, "Smoke must discover exactly the two curated bundled packages.");
        Require(manager.Plugins.All(p => p.Manifest is not null && RunOptions.SampleIds.Contains(p.Manifest.Id)
            && !p.IsLoaded && !p.IsTrusted && !p.DesiredEnabled), "Fresh smoke packages must be disabled and untrusted.");
        Require(manager.Diagnostics.Count == 0 && manager.Plugins.All(p => p.Diagnostics.Count == 0), "Discovery produced diagnostics.");
        Record("Only curated samples discovered; no real user state loaded");
        try
        {
            await using var duplicate = new HostRuntime(options);
            throw new InvalidOperationException("Another owner acquired the same user store.");
        }
        catch (IOException) { Record("Exclusive user-root store lease rejects a second owner"); }

        foreach (var id in RunOptions.SampleIds)
        {
            // Explicitly authorized by --smoke-test, never used by ordinary application startup.
            var result = await manager.EnableAsync(id, confirmTrust: true);
            Require(result.Succeeded, $"Enable {id}: {result.Message}");
        }
        await AttachAllViewsAsync(window);
        Record("Both independent samples supplied shared WinUiView page/settings identities and attached native controls");
        foreach (var theme in new[] { ElementTheme.Default, ElementTheme.Light, ElementTheme.Dark })
            window.SmokeSetTheme(theme);
        Record("System/light/dark requested themes applied to the native root (visual appearance requires manual review)");
        Require(manager.Contributions.Commands.Count == 2, "Expected one local command per sample.");
        foreach (var command in manager.Contributions.Commands)
        {
            var result = await manager.ExecuteCommandAsync(command.PluginId, command.Id);
            Require(result.Succeeded, $"Command {command.Id}: {result.Message}");
        }
        var files = new[]
        {
            Path.Combine(options.UserRoot, "data", RunOptions.SampleIds[0], "summary-draft.txt"),
            Path.Combine(options.UserRoot, "data", RunOptions.SampleIds[1], "sample-test-cases.csv")
        };
        Require(files.All(file => File.Exists(file) && new FileInfo(file).Length > 0), "Local commands did not write isolated sample files.");
        Record("Local commands wrote nonempty draft and CSV under isolated DataDirectory paths");
        foreach (var id in RunOptions.SampleIds)
        {
            window.SmokeDetach();
            var disabled = await manager.DisableAsync(id);
            Require(disabled.Succeeded, $"Disable {id}: {disabled.Message}");
            Require(manager.Contributions.Views.All(v => v.PluginId != id)
                && manager.Contributions.Commands.All(c => c.PluginId != id), "Disabled plugin still has registrations.");
            var enabled = await manager.EnableAsync(id);
            Require(enabled.Succeeded, $"Re-enable {id}: {enabled.Message}");
        }
        await AttachAllViewsAsync(window);
        Record("Detached controls, disabled and re-enabled both samples, then attached fresh native factories");
        await window.SmokeStopAsync();
        Require(manager.Contributions.Views.Count == 0 && manager.Contributions.Commands.Count == 0, "Shutdown retained active contributions.");
        Require(manager.Plugins.All(p => p.Diagnostics.Count == 0), "Lifecycle produced diagnostics.");
        using var state = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(options.UserRoot, "plugin-state.json")));
        foreach (var id in RunOptions.SampleIds)
            Require(state.RootElement.GetProperty("plugins").GetProperty(id).GetProperty("enabled").GetBoolean(), "Shutdown changed desired enabled preference.");
        Record("Shutdown removed registrations and preserved desired-enabled preferences");
        options.WriteReport(new
        {
            succeeded = true, stage = "completed", userRoot = options.UserRoot,
            uiThreadId = Environment.CurrentManagedThreadId, checks,
            files = files.Select(path => new { path, bytes = new FileInfo(path).Length }),
            completedUtc = DateTimeOffset.UtcNow
        });
        await window.FinishSmokeAsync();
    }

    private static async Task AttachAllViewsAsync(MainWindow window)
    {
        var views = window.SmokeManager.Contributions.Views;
        Require(views.Count == 4, "Expected two native pages and two native settings factories.");
        foreach (var id in RunOptions.SampleIds)
        {
            var native = views.Where(v => v.PluginId == id).Select(v => v.View).OfType<WinUiView>().ToArray();
            Require(native.Length == 2 && native.Any(v => v.Kind == WinUiViewKind.Page)
                && native.Any(v => v.Kind == WinUiViewKind.Settings), "Shared native view contract identity or kinds failed.");
            foreach (var view in native) await window.SmokeAttachAsync(view);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
