using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Workbench.Core;
using Workbench.WinUI.Sdk;

namespace Workbench.App;

internal sealed partial class MainWindow : Window
{
    private readonly RunOptions _options;
    private readonly Grid _root;
    private readonly NavigationView _navigation;
    private readonly ContentControl _content;
    private readonly ContentControl _body;
    private readonly InfoBar _notice;
    private HostRuntime? _runtime;
    private WorkspaceRuntimeCoordinator? _workspace;
    private long _runtimeGeneration = -1;
    private string EffectiveRoot => _runtime?.UserRoot ?? _options.UserRoot;
    private bool UsesWorkspace => !_options.SmokeTest && _options.RuntimeProbe is null && _options.BusinessProbe is null && !_options.DirectRuntime;
    private PluginManager Manager => _runtime?.Manager ?? throw new InvalidOperationException("工作台尚未就绪。");
    private PluginPackageStore Packages => _runtime?.Packages ?? throw new InvalidOperationException("工作台尚未就绪。");
    private bool _refreshing;
    private bool _initialized;
    private Exception? _startupFailure;
    private bool _busy;
    private bool _initializing;
    private bool _closing;
    private bool _allowClose;
    private bool _stopped;
    private bool _shutdownPending;
    private bool _workspaceCloseRequested;
    private TaskCompletionSource? _shellOperationSettled;
    private readonly TaskCompletionSource _initializationSettled = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private TaskCompletionSource<bool> _closeCompleted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly int _uiThread = Environment.CurrentManagedThreadId;

    public MainWindow(RunOptions options)
    {
        _options = options;
        options.TraceStartup("window-constructor-entered");
        _root = new Grid();
        options.TraceStartup("creating-navigation-view");
        _navigation = Ui.Named(new NavigationView
        {
            PaneDisplayMode = NavigationViewPaneDisplayMode.Auto,
            IsSettingsVisible = false,
            IsEnabled = false,
            IsBackButtonVisible = NavigationViewBackButtonVisible.Collapsed,
            PaneTitle = "Project Workbench"
        }, "工作台导航");
        options.TraceStartup("creating-content-controls");
        _content = new ContentControl { HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
        _body = new ContentControl { HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
        options.TraceStartup("creating-info-bar");
        _notice = Ui.Named(new InfoBar { IsOpen = false }, "操作结果与错误");
        Title = options.IsIsolated ? "Project Workbench · isolated native probe" : "Project Workbench · 原生插件工作台";
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _root.RowDefinitions.Add(new RowDefinition());
        _root.Children.Add(_notice);
        if (UsesWorkspace) { var header = CreateWorkspaceHeader(); Grid.SetRow(header, 1); _root.Children.Add(header); }
        Grid.SetRow(_body, 2);
        _root.Children.Add(_body);
        _body.Content = _navigation;
        _navigation.IsEnabled = false;
        _navigation.IsSettingsVisible = false;
        _navigation.Content = _content;
        Content = _root;
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1180, 800));
        _navigation.SelectionChanged += (_, args) =>
        {
            if (!_initialized || _refreshing || _busy || _closing) return;
            if (args.IsSettingsSelected) Navigate(new("settings"));
            else if (args.SelectedItemContainer?.Tag is NavigationTarget target) Navigate(target);
        };
        AppWindow.Closing += OnClosing;
        ShowPage(Ui.Page("正在启动", "读取插件清单与本地设置…"));
        options.TraceStartup("window-content-ready");
    }

    public async Task InitializeAsync()
    {
        if (UsesWorkspace && _workspaceCloseRequested) { _initializationSettled.TrySetResult(); return; }
        _initializing = true;
        try
        {
            _options.TraceStartup("opening-host-runtime");
            if (UsesWorkspace)
            {
                if (_options.WorkspaceProbe is not null && !_options.SkipAutomaticProbe) await NativeWorkspaceProbe.PrepareAsync(_options);
                if (_options.TestingProbe is not null && !_options.SkipAutomaticProbe) await TestingNativeProbe.PrepareAsync(_options);
                if (_workspaceCloseRequested) return; // Close may have arrived before coordinator publication.
                _workspace = new(_options, (_, scope, owner) =>
                {
                    _options.TraceStartup("constructing-project-runtime");
                    return new HostRuntime(HostRuntimeOptions.Create(_options, scope), owner);
                });
                var result = await _workspace.InitializeAsync();
                RefreshWorkspace();
                _initializing = false;
                if (_workspaceCloseRequested) return; // The admitted close owns initialization cleanup.
                if (!result.Succeeded)
                {
                    ShowWorkspaceRecovery();
                    if (_options.WorkspaceProbe is not null || _options.TestingProbe is not null) throw new InvalidOperationException("Isolated workspace probe startup failed.");
                    return;
                }
                _options.TraceStartup("host-initialization-completed");
                if (_options.WorkspaceProbe is not null && !_options.SkipAutomaticProbe) await NativeWorkspaceProbe.RunAsync(this, _options);
                if (_options.TestingProbe is not null && !_options.SkipAutomaticProbe) await TestingNativeProbe.RunAsync(this, _options);
                return;
            }
            _runtime = new HostRuntime(HostRuntimeOptions.Create(_options));
            if (_options.RuntimeProbe is not null) await NativeRuntimeProbe.PrepareAsync(_runtime, _options);
            if (_options.BusinessProbe is not null) await BusinessPluginSmoke.PrepareAsync(_runtime, _options);
            _options.TraceStartup("loading-host-theme");
            LoadTheme();
            await _runtime.InitializeAsync(_options.TraceStartup);
            RefreshNavigation();
            _initialized = true;
            _navigation.IsSettingsVisible = true;
            _navigation.IsEnabled = true;
            _options.TraceStartup("opening-home-page");
            Navigate(new("home"));
            if (_runtime.MaintenanceResults.Any(result => !result.Succeeded) || Packages.Diagnostics.Count != 0)
                Notice("本地包维护需要处理", "已保留维护诊断；未受影响的插件仍可使用。请查看“包维护”。", InfoBarSeverity.Warning);
            _options.TraceStartup("host-initialization-completed");
            _initializing = false;
            if (_shutdownPending) Navigate(new("background"));
            else if (_options.BusinessProbe is not null) await BusinessPluginSmoke.RunAsync(this, _options);
            else if (_options.RuntimeProbe is not null) await NativeRuntimeProbe.RunAsync(this, _options);
            else if (_options.SmokeTest) await NativeSmoke.RunAsync(this, _options);
        }
        catch (Exception error)
        {
            _startupFailure = error;
            _initializing = false;
            _initialized = false;
            _navigation.IsEnabled = false;
            _navigation.IsSettingsVisible = false;
            DetachPluginViews();
            var message = HostFailure.Describe("HostStartupFailed", error);
            var failure = Ui.Page("工作台未能启动", message);
            failure.Children.Add(Ui.Text("请关闭此窗口后重试。若用户目录已被占用，请先关闭另一个工作台实例。"));
            var close = Ui.Named(new Button { Content = "关闭窗口" }, "关闭未能启动的工作台窗口");
            close.Click += async (_, _) => await CloseAsync();
            failure.Children.Add(close);
            _body.Content = ScrollPage(failure); // Independent of Manager, even after partial initialization.
            Notice("启动或原生验证失败", message, InfoBarSeverity.Error);
            if (_options.IsIsolated)
            {
                Environment.ExitCode = 1;
                _options.WriteFailure("initialize-or-native-smoke", error);
                if (!(UsesWorkspace && _workspaceCloseRequested)) await FinishSmokeAsync();
            }
        }
        finally { _initializing = false; _initializationSettled.TrySetResult(); }
    }

    private void RefreshNavigation()
    {
        _refreshing = true;
        try
        {
            _navigation.MenuItems.Clear();
            AddNavigation("首页", Symbol.Home, new("home"));
            if (_workspace is not null) AddNavigation("项目上下文", Symbol.Document, new("context"));
            AddNavigation("插件", Symbol.AllApps, new("plugins"));
            AddNavigation("命令", Symbol.Play, new("commands"));
            AddNavigation("后台任务", Symbol.Sync, new("background"));
            AddNavigation("凭据", Symbol.Permissions, new("credentials"));
            AddNavigation("包维护", Symbol.Download, new("packages"));
            foreach (var item in Manager.Contributions.Views)
            {
                if (item.View is not WinUiView view) continue;
                var label = view.Kind == WinUiViewKind.Settings ? $"{view.Title} · 设置" : view.Title;
                AddNavigation(label, view.Kind == WinUiViewKind.Settings ? Symbol.Setting : Symbol.Document,
                    new("view", item.PluginId, view.Id));
            }
            if (_navigation.SettingsItem is NavigationViewItem settings) Ui.Named(settings, "工作台设置");
        }
        finally { _refreshing = false; }
    }

    private void AddNavigation(string title, Symbol icon, NavigationTarget target)
    {
        var item = Ui.Named(new NavigationViewItem
        {
            Content = title,
            Icon = new SymbolIcon(icon),
            Tag = target
        }, title);
        _navigation.MenuItems.Add(item);
    }

    private void Navigate(NavigationTarget target)
    {
        if (!_initialized) return;
        if (_workspace is not null && !_workspace.CanUseCurrentRuntime) return;
        try
        {
            switch (target.Page)
            {
                case "plugins": ShowPage(PluginsPage()); break;
                case "commands": ShowPage(CommandsPage()); break;
                case "background": ShowPage(BackgroundPage()); break;
                case "credentials": ShowPage(CredentialPage()); break;
                case "packages": ShowPage(PackageMaintenancePage()); break;
                case "settings": ShowPage(SettingsPage()); break;
                case "context": ShowPage(ProjectContextPage()); break;
                case "view":
                    var registration = Manager.Contributions.Views.SingleOrDefault(v => v.PluginId == target.PluginId && v.View.Id == target.ViewId);
                    if (registration?.View is not WinUiView view)
                    {
                        ShowNavigationFailure("插件页面已停用或未提供原生视图。"); // Host-authored validation result.
                        break;
                    }
                    AssertUiThread();
                    var created = HostFailure.CreateView(view.CreateView);
                    if (created.Error is { } failure) ShowNavigationFailure(failure);
                    else _content.Content = created.Value;
                    break;
                default: ShowPage(HomePage()); break;
            }
        }
        catch (Exception error)
        {
            ShowNavigationFailure(HostFailure.Describe("NavigationFailed", error));
        }
    }

    private void ShowNavigationFailure(string message)
    {
        ShowPage(Ui.Page("无法打开页面", message));
        Notice("无法打开页面", message, InfoBarSeverity.Error);
    }

    private void ShowPage(FrameworkElement page) => _content.Content = ScrollPage(page);

    private static ScrollViewer ScrollPage(FrameworkElement page) => new()
    {
        Content = page,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        HorizontalContentAlignment = HorizontalAlignment.Stretch
    };

    private void Notice(string title, string message, InfoBarSeverity severity = InfoBarSeverity.Informational)
    {
        _notice.Title = title;
        _notice.Message = message;
        _notice.Severity = severity;
        _notice.IsOpen = true;
    }

    private async Task RunAsync(Func<HostRuntime, Task> action)
    {
        if (!_initialized || _busy || _closing || _shutdownPending || _workspace is { CanUseCurrentRuntime: false }) return;
        _busy = true;
        var settled = BeginShellOperation();
        _navigation.IsEnabled = false;
        _content.IsEnabled = false;
        if (_workspace is not null) RefreshWorkspace(reopenPage: false);
        try
        {
            var current = _runtime!;
            Task OwnedAction() => _workspace is null ? action(current)
                : _workspace.RunCurrentAsync((runtime, scope, token) => OnUiAsync(() => action(runtime)));
            if (await HostFailure.RunAsync(OwnedAction) is { } failure) Notice("操作失败", failure, InfoBarSeverity.Error);
        }
        finally
        {
            try
            {
                _busy = false;
                if (_workspace is not null) RefreshWorkspace(reopenPage: false);
                if (_initialized && !_closing)
                {
                    _navigation.IsEnabled = _workspace?.CanUseCurrentRuntime ?? true;
                    _content.IsEnabled = _workspace?.CanUseCurrentRuntime ?? true;
                    if (_shutdownPending) { RefreshNavigation(); Navigate(new("background")); }
                }
            }
            finally { settled.TrySetResult(); }
        }
    }

    private TaskCompletionSource BeginShellOperation()
        => _shellOperationSettled = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private void CompleteShellOperation(TaskCompletionSource settled)
    {
        try { _busy = false; RefreshWorkspace(reopenPage: false); }
        finally { settled.TrySetResult(); }
    }

    private Func<Task> BindCurrentAction(Func<HostRuntime, Task> action)
    {
        var runtime = _runtime;
        var generation = _runtimeGeneration;
        return () => ReferenceEquals(runtime, _runtime) && generation == _runtimeGeneration
            ? RunAsync(action) : Task.CompletedTask;
    }

    private void BindClick(Button button, Func<HostRuntime, Task> action)
    {
        var bound = BindCurrentAction(action);
        button.Click += async (_, _) => await bound();
    }

    private async Task EnableAsync(HostRuntime runtime, PluginSnapshot package)
    {
        var manifest = package.Manifest!;
        var approve = false;
        if (!package.IsTrusted)
        {
            var dialog = new ContentDialog
            {
                XamlRoot = _root.XamlRoot,
                Title = $"信任并启用 {manifest.Name}？",
                Content = $"{manifest.Id}\n{package.PackagePath}\n\n插件是与工作台同进程运行的可信代码，可访问当前用户的文件和系统权限。能力说明并非权限限制；程序集加载上下文不是安全沙箱。仅信任您审查过的本地代码。",
                PrimaryButtonText = "信任并启用",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Close
            };
            approve = await dialog.ShowAsync() == ContentDialogResult.Primary;
            if (!approve) return;
        }
        var result = await runtime.Manager.EnableAsync(manifest.Id, confirmTrust: approve);
        RefreshNavigation();
        Navigate(new("plugins"));
        Notice(result.Succeeded ? "插件已启用" : "启用失败", result.Message,
            result.Succeeded ? InfoBarSeverity.Success : InfoBarSeverity.Error);
    }

    private async Task DisableAsync(HostRuntime runtime, string pluginId)
    {
        if (_workspace is not null)
        {
            var preparation = await runtime.Manager.PrepareProjectLeaveAsync(pluginId);
            if (!preparation.Succeeded || runtime.Manager.HasPendingProjectWork)
            {
                Notice("插件尚未完成保存", WorkspaceGuidance(preparation.ErrorCode), InfoBarSeverity.Warning);
                return;
            }
        }
        // No page cache: release content and navigation entries before plugin StopAsync runs.
        DetachPluginViews();
        var result = await runtime.Manager.DisableAsync(pluginId);
        RefreshNavigation();
        Navigate(new("plugins"));
        Notice(result.Succeeded ? "插件已停用" : "插件停止发生错误", result.Message,
            result.Succeeded ? InfoBarSeverity.Success : InfoBarSeverity.Error);
    }

    private void DetachPluginViews()
    {
        _content.Content = null;
        _refreshing = true;
        _navigation.MenuItems.Clear();
        _navigation.SelectedItem = null;
        _refreshing = false;
    }

    private void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_allowClose) return;
        args.Cancel = true;
        if (_closing) return;
        _ = CloseAsync();
    }

    private async Task StopAsync()
    {
        if (_stopped) return;
        _shutdownPending = true;
        DetachPluginViews();
        _body.Content = ScrollPage(Ui.Page("正在关闭", "正在等待插件停止。协作式取消无法强制中断不响应的插件。"));
        _navigation.IsEnabled = false;
        if (_workspace is not null) await _workspace.DisposeAsync();
        else if (_runtime is not null)
        {
            // Dispose uses HostShutdown and preserves enabled/resume intent. A failure retains ownership.
            await _runtime.DisposeAsync();
        }
        _initialized = false;
        _stopped = true;
    }

    private async Task<bool> CloseAsync()
    {
        if (_closing) return await _closeCompleted.Task;
        if (!UsesWorkspace && (_busy || _initializing))
        {
            // A package write or startup recovery is known work too. Decline this close until it settles.
            _shutdownPending = true;
            ShowCloseRetry("正在等待当前操作完成", "保留用户目录租约。当前操作完成后请重试关闭。不会在写入仍进行时释放存储所有权。");
            return false;
        }
        var shellSettled = _shellOperationSettled?.Task ?? Task.CompletedTask;
        var initializationSettled = _initializing ? _initializationSettled.Task : Task.CompletedTask;
        _closing = true;
        if (UsesWorkspace) { _workspaceCloseRequested = true; _shutdownPending = true; }
        _body.IsEnabled = false;
        _closeCompleted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            if (UsesWorkspace)
            {
                Task FlushOwnedTheme(HostRuntime runtime, Workbench.Core.Projects.ProjectRuntimeScope scope, CancellationToken token)
                    => OnUiAsync(() => _themePreferences?.IsDirty == true ? FlushThemeAsync(runtime) : Task.CompletedTask);
                // Seal immediately, even during an action/switch. This callback runs inside the
                // queued close after accepted work, so it must never recursively call RunCurrent.
                var preparation = _workspace?.PrepareCloseAsync(FlushOwnedTheme);
                if (_workspace is not null) RefreshWorkspace(reopenPage: false);
                _projectDialog?.Hide();
                if (preparation is null)
                {
                    await initializationSettled;
                    preparation = _workspace?.PrepareCloseAsync(FlushOwnedTheme);
                }
                var prepared = preparation is null ? new ProjectSwitchResult(true, false, null) : await preparation;
                // Retain this window/dispatcher until the actual shell continuations have settled.
                await shellSettled;
                await initializationSettled;
                if (!prepared.Succeeded && prepared.ErrorCode != "ProjectCloseCleanupRequired")
                {
                    _shutdownPending = prepared.Pending;
                    _workspaceCloseRequested = prepared.Pending;
                    _closing = false;
                    _body.IsEnabled = true;
                    RefreshWorkspace(reopenPage: false);
                    if (prepared.Pending) ShowWorkspaceRecovery();
                    ShowCloseRetry(prepared.Pending ? "正在等待离开前准备" : "请修正后重试关闭", _themePreferences?.IsDirty == true
                        ? "主题选择尚未保存，当前选择和目录仍保留。请检查目录；如原文件已更改，请使用设置中的条件恢复，再重试关闭。"
                        : WorkspaceGuidance(prepared.ErrorCode));
                    _closeCompleted.TrySetResult(false);
                    return false;
                }
            }
            await StopAsync();
            _allowClose = true;
            Close();
            _closeCompleted.TrySetResult(true);
            return true;
        }
        catch (Exception error)
        {
            _options.WriteFailure("shutdown", error);
            _body.IsEnabled = true;
            // The manager remains queryable. Keep this same host and lease until a safe retry succeeds.
            _body.Content = _navigation;
            if (_workspace is not null)
            {
                RefreshWorkspace(reopenPage: false);
                // A completed failed close may permit document correction under retained leases.
                // Runtime admission stays closed. A pending Stop requires another Dispose pass.
                ShowWorkspaceRecovery();
            }
            else if (_initialized)
            {
                RefreshNavigation();
                _navigation.IsEnabled = true;
                _content.IsEnabled = true;
                Navigate(new("background"));
            }
            ShowCloseRetry("仍在等待插件停止", "已保留当前进程和用户目录租约。可查看状态并重试关闭；不会强制结束插件。" + HostFailure.Describe("HostStopPending", error));
            _closing = false;
            _closeCompleted.TrySetResult(false);
            return false;
        }
    }

    private void ShowCloseRetry(string title, string message)
    {
        var retry = Ui.Named(new Button { Content = "重试关闭" }, "重试安全关闭工作台");
        retry.Click += async (_, _) => await CloseAsync();
        _notice.ActionButton = retry;
        Notice(title, message, InfoBarSeverity.Warning);
    }

    private Task OnUiAsync(Func<Task> action)
    {
        if (DispatcherQueue.HasThreadAccess) return action();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!DispatcherQueue.TryEnqueue(async () =>
        {
            try { await action(); completion.TrySetResult(); }
            catch (Exception error) { completion.TrySetException(error); }
        })) completion.TrySetException(new InvalidOperationException("The window dispatcher is unavailable."));
        return completion.Task;
    }

    internal void AssertUiThread()
    {
        if (!DispatcherQueue.HasThreadAccess || Environment.CurrentManagedThreadId != _uiThread)
            throw new InvalidOperationException("Native factory did not run on the WinUI UI thread.");
    }

    internal PluginManager SmokeManager => Manager;
    internal Exception? SmokeStartupFailure => _startupFailure;
    internal bool SmokeNavigationAvailable => _navigation.IsEnabled || _navigation.IsSettingsVisible;
    internal bool SmokeShutdownPending => _shutdownPending && !_stopped;
    internal Task<bool> SmokeCloseAttemptAsync() => CloseAsync();
    internal void SmokeTryStartupNavigation()
    {
        _navigation.SelectedItem = _navigation.SettingsItem;
        Navigate(new("settings"));
        Navigate(new("home"));
    }
    internal void SmokeDetach() => DetachPluginViews();
    internal void SmokeSetTheme(ElementTheme theme) { AssertUiThread(); _root.RequestedTheme = theme; }
    internal async Task SmokeAttachAsync(WinUiView view)
    {
        AssertUiThread();
        var element = view.CreateView() ?? throw new InvalidOperationException("Native factory returned null.");
        await AttachSmokeElementAsync(element);
    }

    private async Task AttachSmokeElementAsync(FrameworkElement element)
    {
        var loaded = new TaskCompletionSource();
        void OnLoaded(object sender, RoutedEventArgs args) => loaded.TrySetResult();
        element.Loaded += OnLoaded;
        try
        {
            _content.Content = element;
            await loaded.Task.WaitAsync(TimeSpan.FromSeconds(15));
            if (element.XamlRoot is null) throw new InvalidOperationException("Native view was not attached to the host XamlRoot.");
        }
        finally { element.Loaded -= OnLoaded; }
    }

    internal async Task SmokeVerifyLiveCardThemesAsync()
    {
        AssertUiThread();
        var card = Ui.Card(Ui.Text("Live host card theme verification"));
        await AttachSmokeElementAsync(card);
        async Task<(Windows.UI.Color Fill, Windows.UI.Color Stroke)> ReadThemeAsync(ElementTheme theme)
        {
            _root.RequestedTheme = theme;
            var settled = new TaskCompletionSource();
            if (!DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => settled.TrySetResult()))
                throw new InvalidOperationException("Could not queue live theme verification.");
            await settled.Task.WaitAsync(TimeSpan.FromSeconds(15));
            card.UpdateLayout();
            if (card.ActualTheme != theme)
                throw new InvalidOperationException($"Card actual theme did not become {theme}.");
            if (card.Background is not Microsoft.UI.Xaml.Media.SolidColorBrush fill
                || card.BorderBrush is not Microsoft.UI.Xaml.Media.SolidColorBrush stroke)
                throw new InvalidOperationException("Card theme brushes were not resolved.");
            return (fill.Color, stroke.Color);
        }
        var light = await ReadThemeAsync(ElementTheme.Light);
        var dark = await ReadThemeAsync(ElementTheme.Dark);
        var lightAgain = await ReadThemeAsync(ElementTheme.Light);
        if (light.Fill.Equals(dark.Fill) || light.Stroke.Equals(dark.Stroke) || !light.Equals(lightAgain))
            throw new InvalidOperationException("The same host card did not re-resolve fill/stroke across Light/Dark/Light.");
        _root.RequestedTheme = ElementTheme.Default;
    }

    internal Task SmokeStopAsync() => StopAsync();
    internal async Task FinishSmokeAsync()
    {
        if (!await CloseAsync()) throw new PluginShutdownException();
    }

    private sealed record NavigationTarget(string Page, string? PluginId = null, string? ViewId = null);
}
