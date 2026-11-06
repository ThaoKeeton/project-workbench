using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Workbench.Core;
using Workbench.WinUI.Sdk;

namespace Workbench.App;

internal sealed partial class MainWindow : Window
{
    private readonly RunOptions _options;
    private readonly Grid _root = new();
    private readonly NavigationView _navigation = Ui.Named(new NavigationView
    {
        PaneDisplayMode = NavigationViewPaneDisplayMode.Auto,
        IsSettingsVisible = true,
        IsBackButtonVisible = NavigationViewBackButtonVisible.Collapsed,
        PaneTitle = "Project Workbench"
    }, "工作台导航");
    private readonly ContentControl _content = new()
    {
        HorizontalContentAlignment = HorizontalAlignment.Stretch,
        VerticalContentAlignment = VerticalAlignment.Stretch
    };
    private readonly InfoBar _notice = Ui.Named(new InfoBar { IsOpen = false }, "操作结果与错误");
    private HostRuntime? _runtime;
    private PluginManager Manager => _runtime?.Manager ?? throw new InvalidOperationException("工作台尚未就绪。");
    private bool _refreshing;
    private bool _busy;
    private bool _closing;
    private bool _allowClose;
    private bool _stopped;
    private readonly TaskCompletionSource _closeCompleted = new();
    private readonly int _uiThread = Environment.CurrentManagedThreadId;

    public MainWindow(RunOptions options)
    {
        _options = options;
        Title = options.SmokeTest ? "Project Workbench · isolated native smoke" : "Project Workbench · 原生插件工作台";
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _root.RowDefinitions.Add(new RowDefinition());
        _root.Children.Add(_notice);
        Grid.SetRow(_navigation, 1);
        _root.Children.Add(_navigation);
        _navigation.Content = _content;
        Content = _root;
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1180, 800));
        _navigation.SelectionChanged += (_, args) =>
        {
            if (_refreshing || _busy || _closing) return;
            if (args.IsSettingsSelected) Navigate(new("settings"));
            else if (args.SelectedItemContainer?.Tag is NavigationTarget target) Navigate(target);
        };
        AppWindow.Closing += OnClosing;
        ShowPage(Ui.Page("正在启动", "读取插件清单与本地设置…"));
    }

    public async Task InitializeAsync()
    {
        try
        {
            _runtime = new HostRuntime(_options);
            LoadTheme();
            await Manager.DiscoverAsync();
            await Manager.RestoreEnabledAsync();
            RefreshNavigation();
            Navigate(new("home"));
            if (_options.SmokeTest) await NativeSmoke.RunAsync(this, _options);
        }
        catch (Exception error)
        {
            Notice("启动或原生验证失败", error.Message, InfoBarSeverity.Error);
            if (_options.SmokeTest)
            {
                Environment.ExitCode = 1;
                _options.WriteFailure("initialize-or-native-smoke", error);
                await FinishSmokeAsync();
            }
        }
    }

    private void RefreshNavigation()
    {
        _refreshing = true;
        try
        {
            _navigation.MenuItems.Clear();
            AddNavigation("首页", Symbol.Home, new("home"));
            AddNavigation("插件", Symbol.AllApps, new("plugins"));
            AddNavigation("命令", Symbol.Play, new("commands"));
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
        try
        {
            switch (target.Page)
            {
                case "plugins": ShowPage(PluginsPage()); break;
                case "commands": ShowPage(CommandsPage()); break;
                case "settings": ShowPage(SettingsPage()); break;
                case "view":
                    var registration = Manager.Contributions.Views.SingleOrDefault(v => v.PluginId == target.PluginId && v.View.Id == target.ViewId);
                    if (registration?.View is not WinUiView view) throw new InvalidOperationException("插件页面已停用或未提供原生视图。");
                    AssertUiThread();
                    _content.Content = view.CreateView() ?? throw new InvalidOperationException("插件返回了空视图。");
                    break;
                default: ShowPage(HomePage()); break;
            }
        }
        catch (Exception error)
        {
            ShowPage(HomePage());
            Notice("无法打开页面", error.Message, InfoBarSeverity.Error);
        }
    }

    private void ShowPage(FrameworkElement page) => _content.Content = new ScrollViewer
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

    private async Task RunAsync(Func<Task> action)
    {
        if (_busy || _closing) return;
        _busy = true;
        _navigation.IsEnabled = false;
        _content.IsEnabled = false;
        try { await action(); }
        catch (Exception error) { Notice("操作失败", error.Message, InfoBarSeverity.Error); }
        finally
        {
            _busy = false;
            if (!_closing) { _navigation.IsEnabled = true; _content.IsEnabled = true; }
        }
    }

    private async Task EnableAsync(PluginSnapshot package)
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
        var result = await Manager.EnableAsync(manifest.Id, confirmTrust: approve);
        RefreshNavigation();
        Navigate(new("plugins"));
        Notice(result.Succeeded ? "插件已启用" : "启用失败", result.Message,
            result.Succeeded ? InfoBarSeverity.Success : InfoBarSeverity.Error);
    }

    private async Task DisableAsync(string pluginId)
    {
        // No page cache: release content and navigation entries before plugin StopAsync runs.
        DetachPluginViews();
        var result = await Manager.DisableAsync(pluginId);
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
        DetachPluginViews();
        ShowPage(Ui.Page("正在关闭", "正在等待插件停止。协作式取消无法强制中断不响应的插件。"));
        _navigation.IsEnabled = false;
        if (_runtime is not null)
        {
            await Manager.ShutdownAsync(); // Preserve desired-enabled preferences on application exit.
            await _runtime.DisposeAsync();
        }
        _stopped = true;
    }

    private async Task CloseAsync()
    {
        _closing = true;
        try
        {
            await StopAsync();
            _allowClose = true;
            Close();
            _closeCompleted.TrySetResult();
        }
        catch (Exception error)
        {
            Environment.ExitCode = 1;
            _options.WriteFailure("shutdown", error);
            Notice("关闭失败", error.Message, InfoBarSeverity.Error);
            _closing = false;
            _closeCompleted.TrySetException(error);
        }
    }

    internal void AssertUiThread()
    {
        if (!DispatcherQueue.HasThreadAccess || Environment.CurrentManagedThreadId != _uiThread)
            throw new InvalidOperationException("Native factory did not run on the WinUI UI thread.");
    }

    internal PluginManager SmokeManager => Manager;
    internal void SmokeDetach() => DetachPluginViews();
    internal void SmokeSetTheme(ElementTheme theme) { AssertUiThread(); _root.RequestedTheme = theme; }
    internal async Task SmokeAttachAsync(WinUiView view)
    {
        AssertUiThread();
        var element = view.CreateView() ?? throw new InvalidOperationException("Native factory returned null.");
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

    internal Task SmokeStopAsync() => StopAsync();
    internal async Task FinishSmokeAsync()
    {
        if (!_closing) await CloseAsync();
        else await _closeCompleted.Task;
    }

    private sealed record NavigationTarget(string Page, string? PluginId = null, string? ViewId = null);
}
