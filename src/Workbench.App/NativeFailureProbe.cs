using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Workbench.App.NativeRuntimeProbe;
using static Workbench.App.NativeManagementProbe;

namespace Workbench.App;

/// <summary>Actual native factory failures through the shipping navigation and action handlers.</summary>
internal static class NativeFailureProbe
{
    internal static async Task RunAsync(RunOptions options, Action<string> record)
    {
        foreach (var mode in new[] { "page-fail", "settings-fail", "hostile-page", "hostile-settings" })
        {
            var extra = await OpenFixtureWindowAsync(options, mode);
            var manager = extra.SmokeManager;
            var data = Path.Combine(options.UserRoot, mode, "data", Id);
            try
            {
                await manager.GetSettings(Id).SetAsync("mode", mode);
                Require((await manager.EnableAsync(Id, confirmTrust: true)).Succeeded, "Throwing fixture enable failed.");
                var viewId = mode.EndsWith("settings", StringComparison.Ordinal) || mode == "settings-fail" ? "fixture-settings" : "fixture-page";
                var page = await extra.SmokeNavigateAsync("view", Id, viewId);
                Require(Descendants<TextBlock>(page).Any(text => text.Text.Contains("NativeViewFailed", StringComparison.Ordinal))
                    && extra.SmokeNoticeText.Contains("NativeViewFailed", StringComparison.Ordinal), "Factory failure did not render its stable code in page and notice.");
                Require(!File.Exists(Path.Combine(data, "unsafe-exception-access")), "Host evaluated a plugin exception virtual member.");
                AssertSafe(Descendants<TextBlock>(page).Select(text => text.Text).Append(extra.SmokeNoticeText)
                    .Concat(manager.Plugins.SelectMany(plugin => plugin.Diagnostics).Select(diagnostic => diagnostic.Message)));
                await extra.SmokeRunOwnedActionAsync(() => throw new InvalidOperationException("synthetic-private-native-provider-response"));
                Require(extra.SmokeNoticeText.Contains("HostOperationFailed", StringComparison.Ordinal), "Unknown action failure did not render its stable code.");
                AssertSafe([extra.SmokeNoticeText]);
                await extra.SmokeNavigateAsync("home");
                var plugins = await extra.SmokeNavigateAsync("plugins");
                var name = manager.Plugins.Single(plugin => plugin.Manifest?.Id == Id).Manifest!.Name;
                Invoke(Find<Button>(plugins, $"停用 {name}"));
                await WaitAsync(() => Task.FromResult(!manager.Plugins.Single(plugin => plugin.Manifest?.Id == Id).HasRuntime && !extra.SmokeActionBusy));
                Require(extra.SmokeManagementNavigationAvailable, "A failed factory stranded native navigation/Disable.");
                AssertSafe([extra.SmokeNoticeText]);
            }
            finally { await extra.FinishSmokeAsync(); }
            record($"Actual {mode} factory used safe page/notice codes without exception virtual access; native navigation and Disable remained usable");
        }
    }

    internal static async Task VerifyCorruptedThemeAsync(RunOptions options)
    {
        var root = Path.Combine(options.UserRoot, "corrupted-theme");
        var path = Path.Combine(root, "host-settings.json");
        const string corrupted = "{\"Theme\":\"synthetic-private-native-provider-response\"}";
        using (HostRuntime.AcquireLease(root)) await File.WriteAllTextAsync(path, corrupted);
        var extra = new MainWindow(new(false, root, null));
        extra.Activate();
        try
        {
            await extra.InitializeAsync();
            Require(extra.SmokeStartupFailure is null && extra.SmokeManagementNavigationAvailable,
                "Corrupted host theme blocked native startup or management navigation.");
            Require(extra.SmokeRequestedTheme == ElementTheme.Default
                && extra.SmokeNoticeText.Contains("ThemeSettingsFailed (InvalidData).", StringComparison.Ordinal),
                "Corrupted host theme did not fall back to the system theme with its stable safe code.");
            AssertSafe([extra.SmokeNoticeText]);
            foreach (var route in new[] { "home", "packages", "settings" })
            {
                var page = await extra.SmokeNavigateAsync(route);
                AssertSafe(Descendants<TextBlock>(page).Select(text => text.Text).Append(extra.SmokeNoticeText));
            }
            Require(await File.ReadAllTextAsync(path) == corrupted, "Theme fallback silently replaced the preserved settings file.");
            var settings = extra.SmokeCurrentPage ?? throw new InvalidOperationException("Theme fallback settings page was not attached.");
            Find<ComboBox>(settings, "应用主题").SelectedIndex = (int)ElementTheme.Dark;
            await WaitAsync(async () =>
            {
                if (extra.SmokeActionBusy || extra.SmokeRequestedTheme != ElementTheme.Dark) return false;
                using var saved = JsonDocument.Parse(await File.ReadAllTextAsync(path));
                return saved.RootElement.GetProperty("Theme").GetInt32() == (int)ElementTheme.Dark;
            });
            await extra.SmokeNavigateAsync("home");
            Require(extra.SmokeManagementNavigationAvailable, "Theme fallback did not allow explicit native preference repair.");
            AssertSafe([extra.SmokeNoticeText]);
        }
        finally { await extra.FinishSmokeAsync(); }
        using var nextOwner = HostRuntime.AcquireLease(root);
    }

    private static void AssertSafe(IEnumerable<string> text) => Require(text.All(value => !value.Contains("synthetic-private-native-provider-response", StringComparison.Ordinal)),
        "A plugin exception response escaped the native failure boundary.");
}
