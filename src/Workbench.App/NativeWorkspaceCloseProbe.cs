using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Workbench.Core;
using Workbench.Core.Projects;
using static Workbench.App.NativeRuntimeProbe;
using static Workbench.App.NativeManagementProbe;

namespace Workbench.App;

/// <summary>Actual native close/disposal regressions, including WM_CLOSE while the caller is busy.
/// These run only in the shipping-EXE workspace seed probe with known synthetic child roots.</summary>
internal static class NativeWorkspaceCloseProbe
{
    internal static async Task VerifyBusySwitchAsync(RunOptions options)
    {
        var fixture = await OpenAsync(options, "close-busy-switch", "workspace-hold-first-then-fail");
        var window = fixture.Window;
        var data = Path.Combine(fixture.Root, "data", Id);
        var manager = window.SmokeManager;
        const string draft = "Synthetic native close keeps this private draft";
        try
        {
            var page = await window.SmokeNavigateAsync("view", Id, "fixture-page");
            Find<TextBox>(page, "合成项目私有草稿").Text = draft;
            window.WorkspaceSelector.SelectedItem = window.WorkspaceSelector.Items.OfType<ComboBoxItem>()
                .Single(item => (string)item.Tag == fixture.TargetId);
            await WaitAsync(() => Task.FromResult(window.SmokeActionBusy && File.Exists(Path.Combine(data, "prepare-started"))));
            RequestNativeClose(window);
            await WaitAsync(() => Task.FromResult(window.SmokeShutdownPending || window.SmokeCloseInProgress));
            var close = window.SmokeCloseCompletion;
            AssertHeld(fixture.Root);
            using (HostRuntime.AcquireLease(fixture.TargetRoot)) { }
            await File.WriteAllTextAsync(Path.Combine(data, "release-prepare"), "synthetic");
            await WaitAsync(() => Task.FromResult(!window.SmokeActionBusy));
            Require(FactoryCalls(fixture.Options) == 1 && TargetInstances(fixture.TargetRoot) == 1,
                "Actual native close during switch allowed the queued target factory/start.");
            Require(!await close.WaitAsync(TimeSpan.FromSeconds(20)) && !fixture.Closed.IsCompleted
                && window.Workspace.CanUseCurrentRuntime && ReferenceEquals(manager, window.SmokeManager),
                "Settled close preparation failure did not reopen the same native runtime for correction.");
            page = await window.SmokeNavigateAsync("context");
            Find<TextBox>(page, "项目上下文编辑器").Text = "Synthetic native close correction";
            await WaitAsync(() => Task.FromResult(window.Workspace.Document is { IsDirty: false, ErrorCode: null }));
            await File.WriteAllTextAsync(Path.Combine(data, "corrected-prepare"), "synthetic");
            Invoke(window.SmokeCloseRetryButton ?? throw new InvalidOperationException("Native close correction lost retry."));
            await fixture.Closed.WaitAsync(TimeSpan.FromSeconds(20));
            Require(await File.ReadAllTextAsync(Path.Combine(data, "prepared-draft.txt")) == draft,
                "Native close retry lost the retained private draft.");
            Require(FactoryCalls(fixture.Options) == 1 && TargetInstances(fixture.TargetRoot) == 1,
                "Native close retry activated the canceled target.");
            AssertReleased(fixture);
        }
        finally { await RetireAsync(fixture); }
    }

    internal static async Task VerifyBusyActionAsync(RunOptions options)
    {
        var fixture = await OpenAsync(options, "close-busy-action", "workspace-normal");
        var window = fixture.Window;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task action = Task.CompletedTask;
        try
        {
            // Select a real native theme while the real temporary path is obstructed.
            var obstruction = Path.Combine(fixture.Root, "host-settings.json.tmp");
            Directory.CreateDirectory(obstruction);
            var page = await window.SmokeNavigateAsync("settings");
            Find<ComboBox>(page, "应用主题").SelectedIndex = (int)ElementTheme.Dark;
            await WaitAsync(() => Task.FromResult(!window.SmokeActionBusy && window.SmokeRequestedTheme == ElementTheme.Dark));
            Require(!File.Exists(Path.Combine(fixture.Root, "host-settings.json")), "Synthetic theme obstruction did not fail the chosen write.");
            action = window.WorkspaceActionAsync(async runtime =>
            {
                entered.TrySetResult();
                await release.Task;
                await File.WriteAllTextAsync(Path.Combine(runtime.UserRoot, "owned-action-finished.txt"), "synthetic");
            });
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            // A real queued coordinator target behind the admitted native shell callback.
            var change = window.Workspace.SwitchAsync(fixture.TargetId);
            RequestNativeClose(window);
            await WaitAsync(() => Task.FromResult(window.SmokeShutdownPending || window.SmokeCloseInProgress));
            var close = window.SmokeCloseCompletion;
            AssertHeld(fixture.Root);
            release.TrySetResult();
            await action.WaitAsync(TimeSpan.FromSeconds(20));
            Require(!(await change.WaitAsync(TimeSpan.FromSeconds(20))).Succeeded && FactoryCalls(fixture.Options) == 1
                && TargetInstances(fixture.TargetRoot) == 1, "Actual native close behind a held action allowed target activation.");
            Require(!await close.WaitAsync(TimeSpan.FromSeconds(20)) && !fixture.Closed.IsCompleted
                && window.Workspace.CanUseCurrentRuntime && window.SmokeRequestedTheme == ElementTheme.Dark,
                "An early close seal bypassed the failed chosen theme write or lost its correction state.");
            Require(File.Exists(Path.Combine(fixture.Root, "owned-action-finished.txt")), "Native close detached the accepted action write.");
            page = await window.SmokeNavigateAsync("settings");
            Require(Find<ComboBox>(page, "应用主题").SelectedIndex == (int)ElementTheme.Dark,
                "Failed sealed close reset the intended native theme selection.");
            Directory.Delete(obstruction);
            Invoke(window.SmokeCloseRetryButton ?? throw new InvalidOperationException("Native theme correction lost close retry."));
            await fixture.Closed.WaitAsync(TimeSpan.FromSeconds(20));
            using var saved = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(fixture.Root, "host-settings.json")));
            Require(saved.RootElement.GetProperty("Theme").GetInt32() == (int)ElementTheme.Dark && action.IsCompletedSuccessfully
                && FactoryCalls(fixture.Options) == 1, "Native close did not join the actual action/theme retry before retirement.");
            AssertReleased(fixture);
        }
        finally { release.TrySetResult(); await action; await RetireAsync(fixture); }
    }

    internal static async Task VerifyBeforeInitializeAsync(RunOptions options)
    {
        var root = Path.Combine(options.UserRoot, "close-before-initialize");
        var local = Options(options, root, "close-before-initialize");
        var window = new MainWindow(local);
        var closed = Closed(window);
        window.Activate();
        RequestNativeClose(window);
        await closed.WaitAsync(TimeSpan.FromSeconds(15));
        await window.InitializeAsync(); // A later launch continuation must honor the retained close intent.
        Require(FactoryCalls(local) == 0 && !Directory.Exists(root), "Close before publication later started a project runtime or touched its root.");
    }

    internal static async Task VerifyActualDisposeCorrectionAsync(RunOptions options)
    {
        var fixture = await OpenAsync(options, "close-actual-dispose", "workspace-normal");
        var window = fixture.Window;
        var data = Path.Combine(fixture.TargetRoot, "data", Id);
        var obstruction = Path.Combine(fixture.TargetRoot, ".workbench-project", "context.json");
        const string original = "Synthetic bounded draft before failed actual Dispose";
        const string correction = "Synthetic correction through the actual recovery TextBox";
        try
        {
            window.WorkspaceSelector.SelectedItem = window.WorkspaceSelector.Items.OfType<ComboBoxItem>()
                .Single(item => (string)item.Tag == fixture.TargetId);
            await WaitAsync(() => Task.FromResult(!window.SmokeActionBusy && window.Workspace.CanUseCurrentRuntime
                && window.Workspace.ActiveProject?.Id == fixture.TargetId));
            await window.WorkspaceActionAsync(async runtime =>
            {
                Require((await runtime.Manager.DisableAsync(Id)).Succeeded, "Actual-dispose fixture disable failed.");
                await runtime.Manager.GetSettings(Id).SetAsync("mode", "hold-stop");
                Require((await runtime.Manager.EnableAsync(Id, confirmTrust: true)).Succeeded, "Actual-dispose fixture enable failed.");
            });
            var manager = window.SmokeManager;
            var generation = window.Workspace.RuntimeGeneration;
            var document = window.Workspace.Document!;
            var scope = window.Workspace.Scope!;
            // A real bounded IO obstruction, not a huge TextBox or an injected failure result.
            Directory.CreateDirectory(obstruction);
            var page = await window.SmokeNavigateAsync("context");
            Find<TextBox>(page, "项目上下文编辑器").Text = original;
            await WaitAsync(() => Task.FromResult(document.ErrorCode == "UnsafePath" && !document.HasPendingWork));
            var failed = false;
            // This existing native probe entry invokes shipping StopAsync -> actual Dispose,
            // deliberately bypassing normal preparation to exercise retained cleanup recovery.
            try { await window.SmokeStopAsync().WaitAsync(TimeSpan.FromSeconds(40)); }
            catch (ProjectWorkspaceStoreException error) when (error.ErrorCode == "UnsafePath") { failed = true; }
            Require(failed && manager.HasPendingStops && !document.IsEditable && !window.Workspace.CanUseCurrentRuntime
                && !fixture.Closed.IsCompleted, "Failed actual Dispose did not retain pending cleanup and editor ownership.");
            AssertHeld(fixture.Root); AssertHeld(fixture.TargetRoot);
            await File.WriteAllTextAsync(Path.Combine(data, "release-stop"), "synthetic");
            // A second owned actual Dispose pass confirms the same Stop has settled. The same
            // IO obstruction still fails document retirement, exposing document-only recovery.
            Require(!await window.SmokeCloseAttemptAsync().WaitAsync(TimeSpan.FromSeconds(40))
                && !manager.HasPendingStops && document.IsEditable && !window.Workspace.CanUseCurrentRuntime
                && ReferenceEquals(document, window.Workspace.Document) && ReferenceEquals(manager, window.SmokeManager)
                && generation == window.Workspace.RuntimeGeneration && FactoryCalls(fixture.Options) == 2,
                "Actual cleanup did not open only the retained document while keeping runtime actions sealed.");
            AssertHeld(fixture.Root); AssertHeld(fixture.TargetRoot);
            var rejected = false;
            try { await window.Workspace.RunCurrentAsync((_, _, _) => Task.CompletedTask); }
            catch (ProjectWorkspaceStoreException) { rejected = true; }
            Require(rejected, "Failed actual Dispose reopened plugin action admission.");
            var editor = Find<TextBox>(window.WorkspaceRoot, "项目上下文编辑器");
            await WaitAsync(() => Task.FromResult(editor.IsLoaded && editor.IsEnabled && editor.XamlRoot is not null));
            Require(editor.Text == original, "The actual recovery TextBox lost its retained dirty buffer.");
            Directory.Delete(obstruction);
            editor.Text = correction;
            Require(document.Text == correction && !window.Workspace.CanUseCurrentRuntime,
                "The real recovery TextBox silently dropped document-only correction.");
            Invoke(window.SmokeCloseRetryButton ?? throw new InvalidOperationException("Actual-dispose recovery lost close retry."));
            editor.Text = "Synthetic fresh edit refused after actual Dispose retry seal";
            await fixture.Closed.WaitAsync(TimeSpan.FromSeconds(20));
            Require((await scope.Documents.ReadAsync()).Text == correction && FactoryCalls(fixture.Options) == 2,
                "Actual Dispose retry lost the exact native correction or restarted a runtime.");
            AssertReleased(fixture);
        }
        finally
        {
            if (Directory.Exists(obstruction)) Directory.Delete(obstruction);
            await File.WriteAllTextAsync(Path.Combine(data, "release-stop"), "synthetic");
            await RetireAsync(fixture);
        }
    }

    private static async Task<Fixture> OpenAsync(RunOptions options, string name, string mode)
    {
        var root = Path.Combine(options.UserRoot, name);
        var local = Options(options, root, name);
        using (HostRuntime.AcquireLease(root))
        {
            var store = new PluginPackageStore(new() { UserRoot = root, PluginsRoot = Path.Combine(root, "Plugins") });
            Require((await store.InstallAsync(FixtureSource)).Succeeded, "Native busy-close fixture installation failed.");
        }
        var catalog = new ProjectWorkspaceCatalog(root);
        var snapshot = await catalog.OpenAsync();
        var created = await catalog.CreateAsync("Synthetic queued target", snapshot.Revision);
        var targetRoot = catalog.GetProjectRoot(created.Project.Id);
        // Seed the target's enabled intent offline, then retire it before opening the native host.
        await using (var seed = new HostRuntime(HostRuntimeOptions.Create(local, new(created.Project.Id, created.Project.Name, targetRoot))))
        {
            Require((await seed.Packages.InstallAsync(FixtureSource)).Succeeded, "Native queued target fixture installation failed.");
            await seed.InitializeAsync();
            await seed.Manager.GetSettings(Id).SetAsync("mode", "workspace-normal");
            Require((await seed.Manager.EnableAsync(Id, confirmTrust: true)).Succeeded, "Native queued target seed failed.");
        }
        var window = new MainWindow(local);
        var closed = Closed(window);
        window.Activate();
        await window.InitializeAsync();
        Require(window.SmokeStartupFailure is null && window.Workspace.CanUseCurrentRuntime, "Native busy-close host failed to initialize.");
        await window.WorkspaceActionAsync(async runtime =>
        {
            await runtime.Manager.GetSettings(Id).SetAsync("mode", mode);
            Require((await runtime.Manager.EnableAsync(Id, confirmTrust: true)).Succeeded, "Native busy-close current fixture enable failed.");
        });
        Require(FactoryCalls(local) == 1 && TargetInstances(targetRoot) == 1, "Native busy-close setup did not isolate current and queued target.");
        return new(window, local, root, targetRoot, created.Project.Id, closed);
    }

    private static RunOptions Options(RunOptions options, string root, string name) => new(false, root,
        Path.Combine(Path.GetDirectoryName(options.ReportPath!)!, name + ".startup.json"))
        { WorkspaceProbe = options.WorkspaceProbe, SkipAutomaticProbe = true };

    private static int FactoryCalls(RunOptions options) => options.StartupStages.Count(stage =>
        JsonSerializer.SerializeToElement(stage).GetProperty("stage").GetString() == "constructing-project-runtime");
    private static int TargetInstances(string root)
    {
        using var settings = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "settings", Id, "settings.json")));
        return settings.RootElement.GetProperty("instances").GetInt32();
    }
    private static Task Closed(MainWindow window)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        window.Closed += (_, _) => completion.TrySetResult();
        return completion.Task;
    }
    private static void RequestNativeClose(MainWindow window)
    {
        var handle = WinRT.Interop.WindowNative.GetWindowHandle(window);
        Require(handle != IntPtr.Zero && PostMessage(handle, 0x0112, 0xF060, 0), "Actual native WM_CLOSE request failed.");
    }
    private static void AssertHeld(string root)
    {
        try { using var owner = HostRuntime.AcquireLease(root); }
        catch (UserStoreInUseException) { return; }
        throw new InvalidOperationException("Busy native close released its actual root lease.");
    }
    private static void AssertReleased(Fixture fixture)
    {
        using var first = HostRuntime.AcquireLease(fixture.Root);
        using var second = HostRuntime.AcquireLease(fixture.TargetRoot);
    }
    private static async Task RetireAsync(Fixture fixture)
    {
        if (!fixture.Closed.IsCompleted)
        {
            var data = Path.Combine(fixture.Root, "data", Id);
            await File.WriteAllTextAsync(Path.Combine(data, "release-prepare"), "synthetic");
            await File.WriteAllTextAsync(Path.Combine(data, "corrected-prepare"), "synthetic");
            var obstruction = Path.Combine(fixture.Root, "host-settings.json.tmp");
            if (Directory.Exists(obstruction)) Directory.Delete(obstruction);
            // A concurrent attempt may first acknowledge an already settled failure. Each retry
            // joins the actual close task; no force termination or detached cleanup is used.
            for (var attempt = 0; attempt < 3 && !fixture.Closed.IsCompleted; attempt++)
                await fixture.Window.SmokeCloseAttemptAsync();
        }
        await fixture.Closed.WaitAsync(TimeSpan.FromSeconds(20));
    }
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(IntPtr handle, uint message, nuint wParam, nint lParam);
    private sealed record Fixture(MainWindow Window, RunOptions Options, string Root, string TargetRoot, string TargetId, Task Closed);
}
