using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Workbench.Core;
using Workbench.Core.Projects;
using static Workbench.App.NativeRuntimeProbe;
using static Workbench.App.NativeManagementProbe;

namespace Workbench.App;

/// <summary>Opt-in synthetic checks of the actual shipping shell, controls, roots and retirement.
/// Fixtures remain outside app/Plugins. Nothing here enumerates a normal user/profile root.</summary>
internal static class NativeWorkspaceProbe
{
    private const string Marker = "ProjectWorkbench synthetic workspace probe v1";
    private const string ContextA = "Synthetic project A context";
    private const string ContextB = "Synthetic project B context";
    private const string AudienceA = "Synthetic audience A";
    private const string AudienceB = "Synthetic audience B";
    private const string SummaryDraft = "Synthetic unsaved first-party workspace draft";
    private const string PrivateDraft = "Synthetic hook-only private draft";
    private static string MarkerPath(string root) => Path.Combine(root, "isolated-workspace-probe.txt");
    private static string Data(string root) => Path.Combine(root, "data", Id);

    internal static async Task PrepareAsync(RunOptions options)
    {
        // CLI already bounded the root. Recheck before touching any synthetic filesystem state.
        var parsed = RunOptions.Parse(["--workspace-probe", options.WorkspaceProbe!, "--probe-root", options.UserRoot]);
        Require(parsed.UserRoot == options.UserRoot, "Workspace probe root changed.");
        HostRuntime.RejectLinks(options.UserRoot);
        if (options.WorkspaceProbe == "verify")
        {
            Require(File.Exists(MarkerPath(options.UserRoot)) && await File.ReadAllTextAsync(MarkerPath(options.UserRoot)) == Marker,
                "Workspace verify requires its actual seeded root.");
            return;
        }
        Require(!Directory.Exists(options.UserRoot) || Directory.EnumerateFileSystemEntries(options.UserRoot)
            .All(path => path == options.ReportPath), "Workspace seed requires a fresh isolated root.");
        using var owner = HostRuntime.AcquireLease(options.UserRoot);
        var packages = new PluginPackageStore(new() { UserRoot = options.UserRoot, PluginsRoot = Path.Combine(options.UserRoot, "Plugins") });
        Require((await packages.InstallAsync(FixtureSource)).Succeeded, "Workspace synthetic fixture install failed.");
        Directory.CreateDirectory(Path.Combine(options.UserRoot, "data"));
        await File.WriteAllBytesAsync(Path.Combine(options.UserRoot, "data", "legacy-owned.bin"), [0, 17, 34, 51, 255]);
        await File.WriteAllTextAsync(Path.Combine(options.UserRoot, "host-settings.json"), "{\"Theme\":2}");
        await File.WriteAllTextAsync(MarkerPath(options.UserRoot), Marker);
    }

    internal static async Task RunAsync(MainWindow window, RunOptions options)
    {
        List<string> checks = [];
        try { await RunCoreAsync(window, options, checks); }
        catch (Exception error)
        {
            try
            {
                if (options.ReportPath is not null)
                    (options with { ReportPath = options.ReportPath + ".evidence.json" }).WriteReport(new
                    {
                        schemaVersion = 1, processId = Environment.ProcessId, probe = options.WorkspaceProbe,
                        completedChecks = checks.ToArray(), error = HostFailure.Describe("WorkspaceProbeFailed", error), errorHResult = error.HResult
                    });
            }
            catch (Exception) { /* Diagnostic I/O must not replace the owned failure. */ }
            throw;
        }
    }

    private static async Task RunCoreAsync(MainWindow window, RunOptions options, List<string> checks)
    {
        window.AssertUiThread();
        void Record(string check)
        {
            checks.Add(check);
            options.WriteReport(new { schemaVersion = 1, succeeded = false, probe = options.WorkspaceProbe,
                userRoot = options.UserRoot, processId = Environment.ProcessId, checks });
        }
        if (options.WorkspaceProbe == "verify")
        {
            var state = JsonSerializer.Deserialize<SeedState>(await File.ReadAllTextAsync(Path.Combine(options.UserRoot, "workspace-probe-state.json")))
                ?? throw new InvalidOperationException("Workspace seed state missing.");
            Require(state.SchemaVersion == 1 && state.ProcessId != Environment.ProcessId && state.SecondaryId.Length == 32,
                "Workspace verify was not a distinct actual process.");
            Require(window.Workspace.ActiveProject?.Id == state.SecondaryId && window.SmokeRequestedTheme == ElementTheme.Light,
                "Actual process did not restore active project and its theme.");
            VerifyLegacy(options.UserRoot);
            Record("process-restore");
            VerifySelector(window, visible: true);
            await VerifyProjectAsync(window, ContextB, AudienceB, secret: false);
            Record("restored-native-ui");
            await SelectAsync(window, "default");
            Require(window.SmokeRequestedTheme == ElementTheme.Dark, "Project A theme did not survive process restore.");
            await VerifyProjectAsync(window, ContextA, AudienceA, secret: true);
            await SelectAsync(window, state.SecondaryId);
            Record("restored-project-isolation");
            ReportOnClose(window, options, checks);
            await window.FinishSmokeAsync();
            return;
        }

        Require(window.Workspace.ActiveProject?.Id == "default" && window.SmokeRuntime.UserRoot == options.UserRoot,
            "Legacy data root was moved instead of adopted in place.");
        VerifyLegacy(options.UserRoot);
        Record("legacy-adoption");
        VerifySelector(window, visible: false);
        Require(window.SmokeRequestedTheme == ElementTheme.Dark, "Legacy theme was not read in place.");
        Require(window.WorkspaceRoot.XamlRoot is not null && window.WorkspaceMenu.Items.Count == 1,
            "Native single-project File discovery was not attached.");
        Record("single-native-ui");
        await window.WorkspaceActionAsync(async runtime =>
        {
            Require((await runtime.Manager.EnableAsync(RunOptions.SampleIds[0], confirmTrust: true)).Succeeded, "Summary enable failed.");
            Require((await runtime.Manager.EnableAsync(Id, confirmTrust: true)).Succeeded, "Synthetic credential enable failed.");
        });
        await EditContextAsync(window, ContextA);
        await EditAudienceAsync(window, AudienceA);
        await SaveSyntheticCredentialAsync(window);
        var summary = await window.SmokeNavigateAsync("view", RunOptions.SampleIds[0], "summary");
        Find<TextBox>(summary, "工作摘要示例草稿").Text = SummaryDraft;
        var rootA = window.SmokeRuntime.UserRoot;
        await CreateProjectAsync(window, "Synthetic B");
        var idB = window.Workspace.ActiveProject!.Id;
        var rootB = window.SmokeRuntime.UserRoot;
        Require(idB != "default" && rootB != rootA, "Native New reused the legacy project root.");
        VerifySelector(window, visible: true);
        Record("multi-native-ui");
        Require(window.SmokeRequestedTheme == ElementTheme.Default && !window.WorkspaceThemeNeedsRepair && window.Workspace.Document!.Text == "",
            "Missing B preferences/context inherited A state.");
        Require(!File.Exists(Path.Combine(rootB, "data", RunOptions.SampleIds[0], "summary-draft.txt")), "New project copied a draft.");
        Require(await File.ReadAllTextAsync(Path.Combine(rootA, "data", RunOptions.SampleIds[0], "summary-draft.txt")) == SummaryDraft,
            "Native project leave lost the unsaved first-party draft.");
        Record("draft-retention");
        await window.WorkspaceActionAsync(async runtime =>
        {
            Require((await runtime.Packages.InstallAsync(FixtureSource)).Succeeded, "B fixture install failed.");
            await runtime.Manager.DiscoverAsync();
            Require((await runtime.Manager.EnableAsync(RunOptions.SampleIds[0], confirmTrust: true)).Succeeded, "B Summary enable failed.");
            Require((await runtime.Manager.EnableAsync(Id, confirmTrust: true)).Succeeded, "B fixture enable failed.");
        });
        var credentialsB = await window.SmokeNavigateAsync("credentials");
        Require(Find<PasswordBox>(credentialsB, $"新凭据值 {Id}/{SecretId}").Password.Length == 0
            && await window.SmokeManager.ReadSecretAsync(Id, SecretId) is null, "B credential state or draft inherited A.");
        await EditContextAsync(window, ContextB);
        await EditAudienceAsync(window, AudienceB);
        await SetThemeAsync(window, ElementTheme.Light);
        await SelectAsync(window, "default");
        Require(window.SmokeRequestedTheme == ElementTheme.Dark, "Switch to A lost its theme.");
        await VerifyProjectAsync(window, ContextA, AudienceA, secret: true);
        summary = await window.SmokeNavigateAsync("view", RunOptions.SampleIds[0], "summary");
        Require(Find<TextBox>(summary, "工作摘要示例草稿").Text == SummaryDraft, "Reopened first-party draft differed from the actual file.");
        Record("project-isolation");

        await NativeWorkspaceCloseProbe.VerifyBusySwitchAsync(options);
        Record("busy-switch-native-close");
        await NativeWorkspaceCloseProbe.VerifyBusyActionAsync(options);
        Record("busy-action-native-close");
        await NativeWorkspaceCloseProbe.VerifyBeforeInitializeAsync(options);
        Record("before-initialize-native-close");

        // Actual cooperative old hook remains owned across its finite wait; a third target cannot bypass it.
        await ConfigureFixtureAsync(window, "workspace-hold-prepare");
        var fixturePage = await window.SmokeNavigateAsync("view", Id, "fixture-page");
        Find<TextBox>(fixturePage, "合成项目私有草稿").Text = PrivateDraft;
        try
        {
            await SelectBlockedAsync(window, idB);
            Require(File.Exists(Path.Combine(Data(rootA), "prepare-started")) && !window.Workspace.Document!.IsEditable,
                "Old actual preparation did not freeze the editor.");
            AssertHeld(rootA);
            Require(!(await window.Workspace.CreateAndSwitchAsync("Must not create")).Succeeded
                && window.Workspace.Catalog!.Projects.Count == 2, "Third project bypassed unresolved old work.");
        }
        finally { await File.WriteAllTextAsync(Path.Combine(Data(rootA), "release-prepare"), "synthetic"); }
        await RetryAsync(window, idB);
        Require(await File.ReadAllTextAsync(Path.Combine(Data(rootA), "prepared-draft.txt")) == PrivateDraft,
            "Retried preparation lost the private hook-only draft.");
        Record("old-pending-refusal");

        // B is normal while leaving it, but its next startup fails with a genuinely held Stop.
        await window.WorkspaceActionAsync(runtime => runtime.Manager.GetSettings(Id).SetAsync("mode", "workspace-init-fail-hold-stop"));
        await SelectAsync(window, "default");
        var previousManager = window.SmokeManager;
        var generation = window.Workspace.RuntimeGeneration;
        var previousBackground = await window.SmokeNavigateAsync("background");
        var staleStart = Find<Button>(previousBackground, $"启动后台任务 {Id}/worker");
        try
        {
            await SelectBlockedAsync(window, idB);
            AssertHeld(rootA); AssertHeld(rootB);
            Require(window.Workspace.ActiveProject?.Id == "default" && !window.Workspace.CanUseCurrentRuntime,
                "Failed candidate was published or old runtime revived too early.");
            Require(!window.WorkspaceSelector.IsEnabled && !window.WorkspaceMenu.IsEnabled,
                "Unresolved candidate exposed new native admission.");
        }
        finally { await File.WriteAllTextAsync(Path.Combine(Data(rootB), "release-stop"), "synthetic"); }
        await RetryAsync(window, "default");
        Require(window.Workspace.RuntimeGeneration > generation && !ReferenceEquals(previousManager, window.SmokeManager),
            "Completed rollback did not rebind the fresh manager generation.");
        if (staleStart.IsEnabled) Invoke(staleStart);
        var drained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Require(window.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => drained.TrySetResult()), "Stale-command observation could not queue.");
        await drained.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Require(window.SmokeManager.GetBackgroundTaskSnapshots(Id).Single(task => task.Id == "worker").State != Workbench.Sdk.PluginBackgroundTaskState.Running,
            "A stale native host command ran after rollback replaced the manager generation.");
        Record("candidate-pending-refusal");
        await ConfigureInactiveFixtureAsync(options, idB, rootB);
        await ConfigureFixtureAsync(window, "workspace-normal");

        // Actual failed close keeps this same native plugin editor available for correction.
        await ConfigureFixtureAsync(window, "workspace-fail-prepare");
        fixturePage = await window.SmokeNavigateAsync("view", Id, "fixture-page");
        Find<TextBox>(fixturePage, "合成项目私有草稿").Text = "Synthetic corrected close draft";
        var liveManager = window.SmokeManager;
        Require(!await window.SmokeCloseAttemptAsync() && window.Workspace.CanUseCurrentRuntime
            && ReferenceEquals(liveManager, window.SmokeManager) && Find<TextBox>(fixturePage, "合成项目私有草稿").IsEnabled,
            "Settled failed close discarded the live native editor or manager.");
        await File.WriteAllTextAsync(Path.Combine(Data(rootA), "corrected-prepare"), "synthetic");
        // Leave is now correctable and must flush the same private buffer before any Stop.
        await SelectAsync(window, idB);
        Require(await File.ReadAllTextAsync(Path.Combine(Data(rootA), "prepared-draft.txt")) == "Synthetic corrected close draft",
            "Corrected close preparation did not retain the private buffer.");
        await SelectAsync(window, "default");
        var retainedContext = await File.ReadAllBytesAsync(Path.Combine(rootB, ".workbench-project", "context.json"));
        await OpenProjectAsync(window, idB, archive: true);
        VerifySelector(window, visible: false);
        var archivedContext = await File.ReadAllBytesAsync(Path.Combine(rootB, ".workbench-project", "context.json"));
        Require(window.Workspace.Catalog!.Projects.Single(p => p.Id == idB).IsArchived
            && retainedContext.SequenceEqual(archivedContext),
            "Archive changed retained project data.");
        Record("archive-retention");
        await OpenProjectAsync(window, idB, archive: false);
        await VerifyProjectAsync(window, ContextB, AudienceB, secret: false);
        await File.WriteAllTextAsync(Path.Combine(options.UserRoot, "workspace-probe-state.json"),
            JsonSerializer.Serialize(new SeedState(1, idB, Environment.ProcessId)));

        // Keep a second actual native window/dispatcher alive while the first one retires.
        var reopened = new MainWindow(options with { ReportPath = null, SkipAutomaticProbe = true });
        var closed = Closed(window);
        reopened.Activate();
        await window.FinishSmokeAsync();
        await closed.WaitAsync(TimeSpan.FromSeconds(15));
        AssertReleased(rootA); AssertReleased(rootB);
        await reopened.InitializeAsync();
        Require(reopened.SmokeStartupFailure is null && reopened.Workspace.ActiveProject?.Id == idB
            && reopened.SmokeRequestedTheme == ElementTheme.Light, "Native reopen did not restore the selected project.");
        await VerifyProjectAsync(reopened, ContextB, AudienceB, secret: false);
        Record("native-reopen");
        ReportOnClose(reopened, options, checks);
        await reopened.FinishSmokeAsync();
    }

    private static async Task ConfigureFixtureAsync(MainWindow window, string mode)
        => await window.WorkspaceActionAsync(async runtime =>
        {
            // Current fixture mode is immutable per runtime instance, as with a real loaded plugin.
            Require((await runtime.Manager.DisableAsync(Id)).Succeeded, "Workspace fixture disable failed.");
            await runtime.Manager.GetSettings(Id).SetAsync("mode", mode);
            Require((await runtime.Manager.EnableAsync(Id, confirmTrust: true)).Succeeded, "Workspace fixture re-enable failed.");
        });

    private static async Task ConfigureInactiveFixtureAsync(RunOptions options, string id, string root)
    {
        // Only a known synthetic inactive root; take its actual exclusive lease for this fixture edit.
        await using var offline = new HostRuntime(HostRuntimeOptions.Create(options, new(id, "Synthetic B", root)));
        await offline.Manager.GetSettings(Id).SetAsync("mode", "workspace-normal");
    }

    private static void VerifyLegacy(string root) => Require(File.ReadAllBytes(Path.Combine(root, "data", "legacy-owned.bin"))
        .SequenceEqual(new byte[] { 0, 17, 34, 51, 255 }), "Legacy bytes were modified.");

    private static void VerifySelector(MainWindow window, bool visible)
    {
        var selector = Find<ComboBox>(window.WorkspaceRoot, "切换项目");
        Require(selector.Visibility == (visible ? Visibility.Visible : Visibility.Collapsed), "Actual single/multi native selector visibility failed.");
        Require(!visible || selector.IsLoaded && selector.XamlRoot is not null && selector.Items.Count == 2,
            "Multi selector was not attached with the actual two native choices.");
        Require(!Descendants<ListView>(window.WorkspaceRoot).Any(list => list.Visibility == Visibility.Visible),
            "Ordinary shell exposed a project manager list.");
    }

    private static async Task EditContextAsync(MainWindow window, string text)
    {
        var page = await window.SmokeNavigateAsync("context");
        Find<TextBox>(page, "项目上下文编辑器").Text = text;
        await WaitAsync(() => Task.FromResult(window.Workspace.Document is { IsDirty: false, ErrorCode: null }));
        Require((await window.Workspace.Scope!.Documents.ReadAsync()).Text == text, "Native context autosave did not reach the actual document.");
    }

    private static async Task EditAudienceAsync(MainWindow window, string text)
    {
        var page = await window.SmokeNavigateAsync("view", RunOptions.SampleIds[0], "preferences");
        Find<TextBox>(page, "示例摘要读者").Text = text;
        await WaitAsync(async () => await window.SmokeManager.GetSettings(RunOptions.SampleIds[0]).GetAsync("audience", "") == text);
    }

    private static async Task SetThemeAsync(MainWindow window, ElementTheme value)
    {
        var page = await window.SmokeNavigateAsync("settings");
        Find<ComboBox>(page, "应用主题").SelectedIndex = (int)value;
        await WaitAsync(() => Task.FromResult(!window.SmokeActionBusy && window.SmokeRequestedTheme == value));
        using var actual = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(window.SmokeRuntime.UserRoot, "host-settings.json")));
        Require(actual.RootElement.GetProperty("Theme").GetInt32() == (int)value, "Native theme autosave did not reach the effective root.");
    }

    private static async Task SaveSyntheticCredentialAsync(MainWindow window)
    {
        var page = await window.SmokeNavigateAsync("credentials");
        var editor = Find<PasswordBox>(page, $"新凭据值 {Id}/{SecretId}");
        Require(editor.Password.Length == 0, "Initial synthetic credential editor was not empty.");
        editor.Password = SyntheticSecret;
        Invoke(Find<Button>(page, $"保存凭据 {Id}/{SecretId}"));
        await WaitAsync(async () => !window.SmokeActionBusy && await window.SmokeManager.ReadSecretAsync(Id, SecretId) == SyntheticSecret);
        Require(editor.Password.Length == 0, "Synthetic credential save retained its draft.");
    }

    private static async Task VerifyProjectAsync(MainWindow window, string context, string audience, bool secret)
    {
        var page = await window.SmokeNavigateAsync("context");
        Require(Find<TextBox>(page, "项目上下文编辑器").Text == context, "Native context editor crossed project roots.");
        page = await window.SmokeNavigateAsync("view", RunOptions.SampleIds[0], "preferences");
        Require(Find<TextBox>(page, "示例摘要读者").Text == audience, "Native sample preferences crossed project roots.");
        page = await window.SmokeNavigateAsync("credentials");
        Require(Find<PasswordBox>(page, $"新凭据值 {Id}/{SecretId}").Password.Length == 0, "Project switch or reopen reused a credential draft.");
        Require((await window.SmokeManager.ReadSecretAsync(Id, SecretId) == SyntheticSecret) == secret,
            "Synthetic ciphertext storage crossed project roots.");
    }

    private static async Task CreateProjectAsync(MainWindow window, string name)
    {
        var pending = window.WorkspaceNewDialogAsync();
        var dialog = await DialogAsync(window);
        Find<TextBox>((DependencyObject)dialog.Content, "项目名称").Text = name;
        Invoke(Descendants<Button>(dialog).Single(button => Equals(button.Content, "创建并打开")));
        await pending.WaitAsync(TimeSpan.FromSeconds(40));
        Require(window.Workspace.CanUseCurrentRuntime, "Actual New dialog did not finish opening its project.");
    }

    private static async Task OpenProjectAsync(MainWindow window, string id, bool archive)
    {
        var pending = window.WorkspaceOpenDialogAsync();
        var dialog = await DialogAsync(window);
        var list = Find<ListView>((DependencyObject)dialog.Content, "打开项目列表");
        list.SelectedItem = list.Items.OfType<ListViewItem>().Single(item => item.Tag is ProjectWorkspaceInfo p && p.Id == id);
        var label = archive ? "归档选中项" : dialog.PrimaryButtonText;
        Invoke(Descendants<Button>(dialog).Single(button => Equals(button.Content, label)));
        await pending.WaitAsync(TimeSpan.FromSeconds(40));
    }

    private static async Task<ContentDialog> DialogAsync(MainWindow window)
    {
        await WaitAsync(() => Task.FromResult(window.WorkspaceDialog is { IsLoaded: true } dialog && Descendants<Button>(dialog).Any()));
        return window.WorkspaceDialog!;
    }

    private static void Select(MainWindow window, string id)
    {
        var selector = window.WorkspaceSelector;
        Require(selector.IsEnabled, "Native project selector did not permit admission.");
        selector.SelectedItem = selector.Items.OfType<ComboBoxItem>().Single(item => (string)item.Tag == id);
    }

    private static async Task SelectAsync(MainWindow window, string id)
    {
        Select(window, id);
        await WaitAsync(() => Task.FromResult(!window.SmokeActionBusy && window.Workspace.CanUseCurrentRuntime && window.Workspace.ActiveProject?.Id == id));
    }

    private static async Task SelectBlockedAsync(MainWindow window, string id)
    {
        Select(window, id);
        await WaitAsync(() => Task.FromResult(!window.SmokeActionBusy && !window.Workspace.CanUseCurrentRuntime
            && window.Workspace.Transition.State == ProjectTransitionState.Blocked));
    }

    private static async Task RetryAsync(MainWindow window, string expected)
    {
        Invoke(Find<Button>(window.WorkspaceRoot, "重试打开项目"));
        await WaitAsync(() => Task.FromResult(!window.SmokeActionBusy && window.Workspace.CanUseCurrentRuntime && window.Workspace.ActiveProject?.Id == expected));
    }

    private static void AssertHeld(string root)
    {
        try { using var duplicate = HostRuntime.AcquireLease(root); }
        catch (UserStoreInUseException) { return; }
        throw new InvalidOperationException("Workspace pending work lost its actual root lease.");
    }
    private static void AssertReleased(string root) { using var nextOwner = HostRuntime.AcquireLease(root); }
    private static Task Closed(MainWindow window)
    {
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        window.Closed += (_, _) => closed.TrySetResult();
        return closed.Task;
    }
    private static void ReportOnClose(MainWindow window, RunOptions options, List<string> checks)
    {
        var roots = window.Workspace.Catalog!.Projects.Select(project => new ProjectWorkspaceCatalog(options.UserRoot).GetProjectRoot(project.Id)).ToArray();
        window.Closed += (_, _) =>
        {
            try
            {
                foreach (var root in roots) AssertReleased(root);
                options.WriteReport(new { schemaVersion = 1, succeeded = true, probe = options.WorkspaceProbe, userRoot = options.UserRoot,
                    processId = Environment.ProcessId, actualWindowClosed = true, rootsRetired = true, checks,
                    completedUtc = DateTimeOffset.UtcNow, limits = "Synthetic local WinUI/DPAPI only. OS exit is runner-owned; Windows11 human/provider acceptance is deferred." });
            }
            catch (Exception error) { Environment.ExitCode = 1; options.WriteFailure("workspace-real-close-verification", error); }
        };
    }
    private sealed record SeedState(int SchemaVersion, string SecondaryId, int ProcessId);
}

internal sealed partial class MainWindow
{
    internal WorkspaceRuntimeCoordinator Workspace => _workspace ?? throw new InvalidOperationException("Workspace is unavailable.");
    internal Grid WorkspaceRoot => _root;
    internal MenuBar WorkspaceMenu => _workspaceMenu!;
    internal ComboBox WorkspaceSelector => _workspaceSelector!;
    internal ContentDialog? WorkspaceDialog => _projectDialog;
    internal bool WorkspaceThemeNeedsRepair => _themeSettingsNeedRepair;
    internal Task WorkspaceNewDialogAsync() => ShowProjectNameDialogAsync(create: true);
    internal Task WorkspaceOpenDialogAsync() => ShowOpenProjectDialogAsync();
    internal Task WorkspaceActionAsync(Func<HostRuntime, Task> action) => RunAsync(action);
}
