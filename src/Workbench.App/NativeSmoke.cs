using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Workbench.Core;
using Workbench.WinUI.Sdk;

namespace Workbench.App;

/// <summary>Runs only inside the real WinUI process on its UI thread, with isolated local state.</summary>
internal static class NativeSmoke
{
    private const string SummarySavedDraft = "Native smoke user draft: keep these edits.\n已编辑的本地摘要，不得由示例生成覆盖。";
    private const string SummaryPreparedDraft = "Native smoke buffered edit: preserve through project preparation.";
    private const string SummaryStoppedDraft = "Native smoke latest edit: preserve on disable and reload.";
    private const string SampleAudience = "Native autosaved audience";
    private const string SampleSuite = "Native autosaved suite";
    private const string SampleAction = "Native edited sample action";

    public static async Task RunAsync(MainWindow window, RunOptions options)
    {
        List<string> checks = [];
        void Record(string check)
        {
            checks.Add(check);
            options.WriteReport(new { succeeded = false, stage = "running", userRoot = options.UserRoot, processId = Environment.ProcessId, checks, startupStages = options.StartupStages });
        }
        window.AssertUiThread();
        Require(SynchronizationContext.Current is Microsoft.UI.Dispatching.DispatcherQueueSynchronizationContext,
            "SDK-generated startup did not install the WinUI synchronization context.");
        Record("WinUI Application.Start and UI synchronization context initialized");
        Require(Application.Current is IXamlMetadataProvider, "SDK-generated Application did not expose XAML metadata.");
        var metadata = (IXamlMetadataProvider)Application.Current;
        Require(metadata.GetXamlType(typeof(NavigationView)) is not null
            && metadata.GetXamlType("Microsoft.UI.Xaml.Controls.NavigationView") is not null,
            "Application could not resolve WinUI control metadata by type and full name.");
        Record("Application resolved WinUI control XAML metadata by type and full name");
        await VerifyShellAsync(window);
        Record("Native titlebar, bundled icon, content rows and system caption integration attached to the host window");
        var manager = window.SmokeManager;
        Require(manager.Plugins.Count == 2, "Smoke must discover exactly the two curated bundled packages.");
        Require(manager.Plugins.All(p => p.Manifest is not null && RunOptions.SampleIds.Contains(p.Manifest.Id)
            && !p.IsLoaded && !p.IsTrusted && !p.DesiredEnabled), "Fresh smoke packages must be disabled and untrusted.");
        Require(manager.Diagnostics.Count == 0 && manager.Plugins.All(p => p.Diagnostics.Count == 0), "Discovery produced diagnostics.");
        Record("Only curated samples discovered; no real user state loaded");
        var initialHome = await window.SmokeNavigateAsync("home");
        await window.SmokeCaptureGalleryAsync(options, "home", "home", null, null, initialHome, "curated-disabled", manager.Plugins.Count);
        var initialPlugins = await window.SmokeNavigateAsync("plugins");
        await window.SmokeCaptureGalleryAsync(options, "host-plugins-disabled", "plugins", null, null, initialPlugins, "curated-disabled", manager.Plugins.Count);
        // Fixed isolated-smoke checkpoints survive the existing sanitized failure report.
        options.TraceStartup("native-smoke-disabled-capture-complete");
        try
        {
            using var duplicate = HostRuntime.AcquireLease(options.UserRoot);
            throw new InvalidOperationException("Another owner acquired the same user store.");
        }
        catch (UserStoreInUseException) { Record("Actual lease acquisition rejects a second owner with a Windows sharing/lock violation"); }
        options.TraceStartup("native-smoke-occupied-window-begin");
        await VerifyOccupiedStartupAsync(options);
        options.TraceStartup("native-smoke-occupied-window-complete");
        Record("Occupied-store startup shows a manager-independent page, blocks Settings/Home navigation and closes safely");
        options.TraceStartup("native-smoke-live-card-begin");
        await window.SmokeVerifyLiveCardThemesAsync();
        options.TraceStartup("native-smoke-live-card-complete");
        Record("Same native host card re-resolved actual theme and fill/stroke across live Light/Dark/Light changes");
        options.TraceStartup("native-smoke-shell-themes-begin");
        await VerifyShellThemesAsync(window, options);
        options.TraceStartup("native-smoke-shell-themes-complete");
        Record("Native titlebar and caption colors followed live Light/Dark/Light host themes with Mica or an opaque themed fallback");
        foreach (var page in new[] { "background", "credentials", "packages" }) await window.SmokeNavigateAsync(page);
        Record("Initialized Background, Credentials and Package maintenance navigation attached native host pages");

        foreach (var id in RunOptions.SampleIds)
        {
            // Explicitly authorized by --smoke-test, never used by ordinary application startup.
            var result = await manager.EnableAsync(id, confirmTrust: true);
            Require(result.Succeeded, $"Enable {id}: {result.Message}");
        }
        await AttachAllViewsAsync(window);
        Record("Both independent samples supplied shared WinUiView page/settings identities and attached native controls");
        var enabledPlugins = await window.SmokeNavigateAsync("plugins");
        await window.SmokeCaptureGalleryAsync(options, "host-plugins", "plugins", null, null, enabledPlugins, "curated-enabled", manager.Plugins.Count);
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
        foreach (var (shot, route) in new[] { ("host-commands", "commands"), ("host-background", "background"),
            ("host-credentials", "credentials"), ("host-packages", "packages"), ("host-settings", "settings") })
        {
            var hostPage = await window.SmokeNavigateAsync(route);
            await window.SmokeCaptureGalleryAsync(options, shot, route, null, null, hostPage,
                route == "credentials" ? "blank-credentials" : "local-probe-state", route == "credentials" ? 0 : 1);
        }
        var preparedSummaryDraft = await VerifySummaryDraftAsync(window, files[0]);
        await VerifySamplePreparationAsync(window, options.UserRoot, preparedSummaryDraft, files[0]);
        var finalSummaryPage = await OpenSummaryAsync(window);
        var finalSummaryEditor = FindNamed<TextBox>(finalSummaryPage, "工作摘要示例草稿");
        await EditAsync(finalSummaryEditor, SummaryStoppedDraft);
        var savedSummaryDraft = finalSummaryEditor.Text;
        Record("Real Summary editor retained navigation edits, explicit Save and regeneration protection; sample preferences autosaved without activation and project preparation flushed both sample drafts");
        await window.SmokeCaptureGalleryAsync(options, "summary-page", "view", RunOptions.SampleIds[0], "summary", finalSummaryPage, "retained-draft", 1);
        foreach (var id in RunOptions.SampleIds)
        {
            window.SmokeDetach();
            var disabled = await manager.DisableAsync(id);
            Require(disabled.Succeeded, $"Disable {id}: {disabled.Message}");
            Require(manager.Contributions.Views.All(v => v.PluginId != id)
                && manager.Contributions.Commands.All(c => c.PluginId != id), "Disabled plugin still has registrations.");
            var enabled = await manager.EnableAsync(id);
            Require(enabled.Succeeded, $"Re-enable {id}: {enabled.Message}");
            if (id == RunOptions.SampleIds[0])
            {
                var page = await OpenSummaryAsync(window);
                Require(FindNamed<TextBox>(page, "工作摘要示例草稿").Text == savedSummaryDraft,
                    "Summary re-enable did not reload the latest draft retained during Stop.");
                Require(await File.ReadAllTextAsync(files[0]) == savedSummaryDraft,
                    "Summary disable/re-enable lost the latest buffered draft.");
            }
            else
            {
                var view = manager.Contributions.Views.Single(v => v.PluginId == id && v.View is WinUiView { Kind: WinUiViewKind.Page });
                var page = await window.SmokeNavigateAsync("view", id, view.View.Id);
                Require(FindNamed<TextBox>(page, "EX-001 示例步骤").Text == SampleAction, "Sample test-case draft did not survive disable/re-enable.");
            }
        }
        await AttachAllViewsAsync(window);
        Record("Detached controls, disabled and re-enabled both samples, then attached fresh native factories");
        Record("Summary Stop saved the latest buffered edit; both samples reloaded retained drafts after disable/re-enable");
        foreach (var (shot, id, viewId) in new[] {
            ("summary-settings", RunOptions.SampleIds[0], "preferences"),
            ("cases-page", RunOptions.SampleIds[1], "cases"),
            ("cases-settings", RunOptions.SampleIds[1], "preferences") })
        {
            var pluginPage = await window.SmokeNavigateAsync("view", id, viewId);
            await window.SmokeCaptureGalleryAsync(options, shot, "view", id, viewId, pluginPage, "asserted-sample-view", 1);
        }
        await window.SmokeStopAsync();
        Require(manager.Contributions.Views.Count == 0 && manager.Contributions.Commands.Count == 0, "Shutdown retained active contributions.");
        Require(manager.Plugins.All(p => p.Diagnostics.Count == 0), "Lifecycle produced diagnostics.");
        using var state = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(options.UserRoot, "plugin-state.json")));
        foreach (var id in RunOptions.SampleIds)
            Require(state.RootElement.GetProperty("plugins").GetProperty(id).GetProperty("enabled").GetBoolean(), "Shutdown changed desired enabled preference.");
        Record("Shutdown removed registrations and preserved desired-enabled preferences");
        options.WriteReport(new
        {
            succeeded = true,
            stage = "completed",
            userRoot = options.UserRoot,
            processId = Environment.ProcessId,
            uiThreadId = Environment.CurrentManagedThreadId,
            checks,
            startupStages = options.StartupStages,
            files = files.Select(path => new { path, bytes = new FileInfo(path).Length }),
            completedUtc = DateTimeOffset.UtcNow
        });
        await window.FinishSmokeAsync();
    }

    private static async Task<string> VerifySummaryDraftAsync(MainWindow window, string path)
    {
        var manager = window.SmokeManager;
        var generatedText = await File.ReadAllTextAsync(path);
        var page = await OpenSummaryAsync(window);
        var edited = FindNamed<TextBox>(page, "工作摘要示例草稿");
        List<string> editEvents = [];
        await EditAsync(edited, SummarySavedDraft, editEvents);
        var afterEdit = edited.Text;
        // WinUI canonicalizes LF input to CR. Validate content once, then require exact native text everywhere.
        Require(NormalizeNewlines(afterEdit) == NormalizeNewlines(SummarySavedDraft),
            $"Summary editor changed the entered content. {DescribeText("literal", SummarySavedDraft)}; "
            + $"{DescribeText("after edit", afterEdit)}; events={JsonSerializer.Serialize(editEvents)}");

        List<(string Page, string? PluginId, string? ViewId)> routes =
            [("home", null, null), ("commands", null, null), ("settings", null, null)];
        routes.AddRange(manager.Contributions.Views
            .Where(v => v.View is WinUiView { Kind: WinUiViewKind.Settings })
            .Select(v => ("view", (string?)v.PluginId, (string?)v.View.Id)));
        foreach (var route in routes)
        {
            var previous = page;
            await window.SmokeNavigateAsync(route.Page, route.PluginId, route.ViewId);
            page = await OpenSummaryAsync(window);
            Require(!ReferenceEquals(previous, page), "Summary navigation must create a fresh native view.");
            var actual = FindNamed<TextBox>(page, "工作摘要示例草稿").Text;
            Require(actual == afterEdit,
                $"Summary unsaved draft was lost after navigating through {route.Page}. "
                + $"{DescribeText("literal", SummarySavedDraft)}; {DescribeText("after edit", afterEdit)}; "
                + $"{DescribeText("old.Text now", edited.Text)}; {DescribeText("fresh.Text", actual)}; "
                + $"{DescribeDifference(afterEdit, actual)}; events={JsonSerializer.Serialize(editEvents)}");
        }

        var command = manager.Contributions.Commands.Single(c => c.PluginId == RunOptions.SampleIds[0]);
        await window.SmokeNavigateAsync("commands");
        var unsavedGeneration = await manager.ExecuteCommandAsync(command.PluginId, command.Id);
        Require(!unsavedGeneration.Succeeded && !string.IsNullOrWhiteSpace(unsavedGeneration.Message),
            "Summary generation must clearly refuse to replace an edited draft.");
        Require(await File.ReadAllTextAsync(path) == generatedText, "Summary regeneration replaced the existing file before Save.");
        page = await OpenSummaryAsync(window);
        Require(FindNamed<TextBox>(page, "工作摘要示例草稿").Text == afterEdit,
            "Summary regeneration replaced unsaved editor text.");

        var status = Descendants<TextBlock>(page).Single(text => text.Text.Contains(path, StringComparison.Ordinal));
        var saved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var subscription = status.RegisterPropertyChangedCallback(TextBlock.TextProperty, (_, _) => saved.TrySetResult());
        try
        {
            // Invoke the real native button and wait for its async UI result, not a plugin helper.
            new ButtonAutomationPeer(FindNamed<Button>(page, "保存工作摘要示例草稿")).Invoke();
            await saved.Task.WaitAsync(TimeSpan.FromSeconds(15));
        }
        finally { status.UnregisterPropertyChangedCallback(TextBlock.TextProperty, subscription); }
        Require(await File.ReadAllTextAsync(path) == afterEdit, "Summary Save did not persist the edited draft.");

        await window.SmokeNavigateAsync("commands");
        var savedGeneration = await manager.ExecuteCommandAsync(command.PluginId, command.Id);
        Require(!savedGeneration.Succeeded && !string.IsNullOrWhiteSpace(savedGeneration.Message),
            "Summary generation must clearly refuse to replace a saved draft.");
        Require(await File.ReadAllTextAsync(path) == afterEdit, "Summary regeneration replaced the user's saved draft file.");
        page = await OpenSummaryAsync(window);
        var editor = FindNamed<TextBox>(page, "工作摘要示例草稿");
        Require(editor.Text == afterEdit, "Summary regeneration replaced the saved draft model.");
        await EditAsync(editor, SummaryPreparedDraft);
        return editor.Text;
    }

    private static async Task VerifySamplePreparationAsync(MainWindow window, string userRoot, string summaryDraft, string summaryPath)
    {
        var manager = window.SmokeManager;
        foreach (var (id, name, value, key) in new[]
        {
            (RunOptions.SampleIds[0], "示例摘要读者", SampleAudience, "audience"),
            (RunOptions.SampleIds[1], "示例用例集名称", SampleSuite, "suiteName")
        })
        {
            var view = manager.Contributions.Views.Single(v => v.PluginId == id && v.View is WinUiView { Kind: WinUiViewKind.Settings });
            var page = await window.SmokeNavigateAsync("view", id, view.View.Id);
            await EditAsync(FindNamed<TextBox>(page, name), value);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            // Observe persistence before preparation; this catches a missing autosave event.
            while (await manager.GetSettings(id).GetAsync(key, "", timeout.Token) != value) await Task.Delay(20, timeout.Token);
        }
        var cases = manager.Contributions.Views.Single(v => v.PluginId == RunOptions.SampleIds[1] && v.View is WinUiView { Kind: WinUiViewKind.Page });
        var casePage = await window.SmokeNavigateAsync("view", cases.PluginId, cases.View.Id);
        await EditAsync(FindNamed<TextBox>(casePage, "EX-001 示例步骤"), SampleAction);
        var prepared = await manager.PrepareProjectLeaveAsync();
        Require(prepared.Succeeded && !prepared.HasPendingWork && !manager.HasPendingProjectWork, "Sample project preparation did not finish owned writes.");
        Require(await File.ReadAllTextAsync(summaryPath) == summaryDraft, "Project preparation lost the buffered Summary draft.");
        using var rows = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(userRoot, "data", RunOptions.SampleIds[1], "sample-test-cases.json")));
        Require(rows.RootElement[0].GetProperty("Action").GetString() == SampleAction, "Project preparation lost the edited sample test-case row.");
    }

    private static Task<FrameworkElement> OpenSummaryAsync(MainWindow window)
    {
        var view = window.SmokeManager.Contributions.Views.Single(v => v.PluginId == RunOptions.SampleIds[0]
            && v.View is WinUiView { Kind: WinUiViewKind.Page });
        return window.SmokeNavigateAsync("view", view.PluginId, view.View.Id);
    }

    private static async Task EditAsync(TextBox editor, string text, List<string>? trace = null)
    {
        var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnChanging(TextBox sender, TextBoxTextChangingEventArgs args) =>
            trace?.Add(DescribeText($"TextChanging(contentChanging={args.IsContentChanging})", editor.Text));
        void OnChanged(object sender, TextChangedEventArgs args)
        {
            trace?.Add(DescribeText("TextChanged", editor.Text));
            changed.TrySetResult();
        }
        editor.TextChanging += OnChanging;
        editor.TextChanged += OnChanged;
        try
        {
            trace?.Add(DescribeText("before Text setter", editor.Text));
            editor.Text = text;
            trace?.Add(DescribeText("after Text setter", editor.Text));
            await changed.Task.WaitAsync(TimeSpan.FromSeconds(15));
            trace?.Add(DescribeText("after TextChanged await", editor.Text));
        }
        finally
        {
            editor.TextChanging -= OnChanging;
            editor.TextChanged -= OnChanged;
        }
    }

    // Only isolated smoke literals/sample data are included in these failure diagnostics.
    private static string DescribeText(string label, string text) => $"{label}: length={text.Length}, text={JsonSerializer.Serialize(text)}";

    private static string NormalizeNewlines(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

    private static string DescribeDifference(string expected, string actual)
    {
        var index = 0;
        while (index < expected.Length && index < actual.Length && expected[index] == actual[index]) index++;
        if (index == expected.Length && index == actual.Length) return "no character difference";
        static string CodeAt(string text, int at) => at < text.Length ? $"U+{(int)text[at]:X4}" : "end";
        return $"first difference at {index}: expected {CodeAt(expected, index)}, actual {CodeAt(actual, index)}";
    }

    private static T FindNamed<T>(DependencyObject root, string name) where T : DependencyObject =>
        Descendants<T>(root).SingleOrDefault(element => AutomationProperties.GetName(element) == name)
        ?? throw new InvalidOperationException($"Native view did not expose {typeof(T).Name} '{name}'.");

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match) yield return match;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (var child in Descendants<T>(VisualTreeHelper.GetChild(root, index))) yield return child;
    }

    private static async Task VerifyOccupiedStartupAsync(RunOptions options)
    {
        var failed = new MainWindow(options with { SmokeTest = false, ReportPath = null, DirectRuntime = true });
        var closed = new TaskCompletionSource();
        failed.Closed += (_, _) => closed.TrySetResult();
        try
        {
            failed.Activate();
            options.TraceStartup("native-smoke-occupied-shell-begin");
            await VerifyShellAsync(failed, options);
            options.TraceStartup("native-smoke-occupied-shell-complete");
            options.TraceStartup("native-smoke-occupied-initialize-begin");
            await failed.InitializeAsync();
            options.TraceStartup("native-smoke-occupied-initialize-complete");
            Require(failed.SmokeStartupFailure is UserStoreInUseException, "Second host did not report the actual occupied-store failure.");
            Require(!failed.SmokeNavigationAvailable, "Navigation/settings remained available after startup failed.");
            failed.SmokeTryStartupNavigation();
        }
        finally
        {
            options.TraceStartup("native-smoke-occupied-close-begin");
            await failed.FinishSmokeAsync();
            options.TraceStartup("native-smoke-occupied-close-complete");
            options.TraceStartup("native-smoke-occupied-closed-wait-begin");
            await closed.Task.WaitAsync(TimeSpan.FromSeconds(15));
            options.TraceStartup("native-smoke-occupied-closed-wait-complete");
        }
    }

    // These assertions inspect the real loaded WinUI tree, including a window with no runtime.
    // Removing the constructor's shell hook must fail before plugin discovery is consulted.
    private static async Task VerifyShellAsync(MainWindow window, RunOptions? smokeTrace = null)
    {
        var root = window.Content as Grid ?? throw new InvalidOperationException("The host shell root is unavailable.");
        await SettleShellAsync(window, root, smokeTrace);
        var titleBar = FindNamed<TitleBar>(root, "工作台标题栏");
        Require(window.ExtendsContentIntoTitleBar && window.AppWindow.TitleBar.ExtendsContentIntoTitleBar,
            "The host still uses the stock non-client titlebar.");
        Require(window.Title == "Project Workbench" && titleBar.Title == "Project Workbench",
            "The native window and visible titlebar must identify Project Workbench.");
        Require(titleBar.IsLoaded && titleBar.XamlRoot == root.XamlRoot && titleBar.ActualHeight >= 32,
            "The native titlebar did not attach to the window's XamlRoot.");
        Require(!titleBar.IsBackButtonVisible && !titleBar.IsPaneToggleButtonVisible,
            "The titlebar duplicated navigation controls.");
        Require(titleBar.IconSource is ImageIconSource { ImageSource: SvgImageSource image }
            && image.UriSource.AbsoluteUri == "ms-appx:///Assets/Workbench.svg"
            && File.Exists(Path.Combine(AppContext.BaseDirectory, "Assets", "Workbench.svg"))
            && File.Exists(Path.Combine(AppContext.BaseDirectory, "Assets", "Workbench.ico")),
            "The unified titlebar or deployment is missing the bundled Workbench icon.");
        Require(Grid.GetRow(titleBar) == 0 && root.RowDefinitions[0].Height.IsAuto,
            "The titlebar did not reserve a separate content row.");
        var titleBottom = titleBar.TransformToVisual(root).TransformPoint(new(0, titleBar.ActualHeight)).Y;
        foreach (var sibling in root.Children.OfType<FrameworkElement>().Where(child => child != titleBar))
            Require(Grid.GetRow(sibling) > 0 && (!sibling.IsLoaded || sibling.ActualHeight == 0
                || sibling.TransformToVisual(root).TransformPoint(new(0, 0)).Y >= titleBottom - 1),
                "A notice, project header or body overlaps the native titlebar.");
        Require(!FindNamed<NavigationView>(root, "工作台导航").IsTitleBarAutoPaddingEnabled,
            "Navigation inserted a second titlebar spacer below the unified frame.");
        Require(window.SystemBackdrop is MicaBackdrop || root.Background is SolidColorBrush { Color.A: 255 },
            "The shell has neither Mica nor an opaque themed fallback.");
    }

    private static async Task VerifyShellThemesAsync(MainWindow window, RunOptions options)
    {
        var root = (Grid)window.Content;
        var titleBar = FindNamed<TitleBar>(root, "工作台标题栏");
        var previous = root.RequestedTheme;
        async Task<(Windows.UI.Color Text, Windows.UI.Color Caption, Windows.UI.Color? Fallback)> ReadAsync(ElementTheme theme)
        {
            window.SmokeSetTheme(theme);
            await SettleShellAsync(window, root, options);
            Require(root.ActualTheme == theme && titleBar.ActualTheme == theme,
                "The titlebar did not inherit the host's actual theme.");
            Require(titleBar.Foreground is SolidColorBrush, "The native titlebar text brush did not resolve.");
            var caption = window.AppWindow.TitleBar;
            Require(caption.ButtonBackgroundColor is { A: 0 } && caption.ButtonInactiveBackgroundColor is { A: 0 },
                "System caption buttons do not blend into the unified frame.");
            Require(caption.ButtonForegroundColor is not null && caption.ButtonInactiveForegroundColor is not null,
                "System caption colors did not follow the host theme.");
            var fallback = root.Background is SolidColorBrush { Color.A: 255 } brush ? (Windows.UI.Color?)brush.Color : null;
            Require(window.SystemBackdrop is MicaBackdrop || fallback is not null,
                "Theme change left the non-Mica shell without an opaque background.");
            return (((SolidColorBrush)titleBar.Foreground).Color, caption.ButtonForegroundColor!.Value, fallback);
        }
        try
        {
            options.TraceStartup("native-smoke-shell-light-begin");
            var light = await ReadAsync(ElementTheme.Light);
            options.TraceStartup("native-smoke-shell-dark-begin");
            var dark = await ReadAsync(ElementTheme.Dark);
            options.TraceStartup("native-smoke-shell-light-again-begin");
            var lightAgain = await ReadAsync(ElementTheme.Light);
            options.TraceStartup("native-smoke-shell-light-again-complete");
            Require(!light.Text.Equals(dark.Text) && !light.Caption.Equals(dark.Caption) && light.Equals(lightAgain),
                "The same native titlebar/caption did not re-resolve across Light/Dark/Light.");
            if (window.SystemBackdrop is null)
                Require(light.Fallback is not null && dark.Fallback is not null && !light.Fallback.Equals(dark.Fallback),
                    "The non-Mica background did not follow the host theme.");
        }
        finally { window.SmokeSetTheme(previous); }
    }

    private static async Task SettleShellAsync(MainWindow window, FrameworkElement root, RunOptions? smokeTrace = null)
    {
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnLoaded(object sender, RoutedEventArgs args) => ready.TrySetResult();
        root.Loaded += OnLoaded;
        try
        {
            smokeTrace?.TraceStartup("native-smoke-shell-loaded-wait-begin");
            if (!root.IsLoaded) await ready.Task.WaitAsync(TimeSpan.FromSeconds(15));
            smokeTrace?.TraceStartup("native-smoke-shell-loaded-wait-complete");
            var settled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Require(window.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
                () => settled.TrySetResult()), "The native shell dispatcher is unavailable.");
            smokeTrace?.TraceStartup("native-smoke-shell-dispatch-wait-begin");
            await settled.Task.WaitAsync(TimeSpan.FromSeconds(15));
            smokeTrace?.TraceStartup("native-smoke-shell-dispatch-wait-complete");
            root.UpdateLayout();
        }
        finally { root.Loaded -= OnLoaded; }
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

internal sealed partial class MainWindow
{
    // Native test plumbing uses the ordinary navigation/factory path without caching a control.
    internal async Task<FrameworkElement> SmokeNavigateAsync(string page, string? pluginId = null, string? viewId = null)
    {
        AssertUiThread();
        // Probes can enable real contributions directly, without a package-page
        // action rebuilding the menu. Refresh those same registrations first.
        RefreshNavigation();
        // Select under the ordinary refresh guard before the single navigation /
        // factory invocation. A missing or ambiguous real target still fails.
        _refreshing = true;
        try
        {
            var target = new NavigationTarget(page, pluginId, viewId);
            _navigation.SelectedItem = page == "settings" ? _navigation.SettingsItem
                : _navigation.MenuItems.OfType<NavigationViewItem>().Single(item => Equals(item.Tag, target));
        }
        finally { _refreshing = false; }
        Navigate(new(page, pluginId, viewId));
        var element = _content.Content as FrameworkElement ?? throw new InvalidOperationException("Navigation did not supply a native page.");
        var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnLoaded(object sender, RoutedEventArgs args) => loaded.TrySetResult();
        element.Loaded += OnLoaded;
        try
        {
            if (!element.IsLoaded) await loaded.Task.WaitAsync(TimeSpan.FromSeconds(15));
            if (element.XamlRoot is null) throw new InvalidOperationException("Navigated native page was not attached to the host XamlRoot.");
            return element;
        }
        finally { element.Loaded -= OnLoaded; }
    }

}
