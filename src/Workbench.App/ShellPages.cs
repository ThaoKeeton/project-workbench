using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.Storage.Pickers;
using Workbench.Core;

namespace Workbench.App;

internal sealed partial class MainWindow
{
    private bool _themeSettingsNeedRepair;
    private HostThemeSession? _themePreferences;

    private StackPanel HomePage()
    {
        if (_workspace?.Document is not null) return ProjectContextPage();
        var page = Ui.Page("项目工作台", "一个原生窗口，承载独立插件的页面、命令与设置。");
        var enabled = Manager.Plugins.Count(p => p.State == PluginState.Enabled);
        page.Children.Add(Ui.Card(Ui.Text($"已发现 {Manager.Plugins.Count} 个包 · 已启用 {enabled} 个插件 · {Manager.Contributions.Commands.Count} 个命令", 20)));
        page.Children.Add(Ui.Text("开始使用", 20));
        page.Children.Add(Ui.Text("打开“插件”，审查本地包并明确确认信任后启用。启用后，原生页面和插件设置会出现在左侧导航。"));
        page.Children.Add(Ui.Text("默认随附工作摘要和测试用例两个本地示例，不读取真实业务数据。钉钉洞察与 UPCAM 可从配套生产插件包按需导入；首次试用请使用演示或本地 sample/preview，真实业务连接和外部操作需要各插件自己的明确确认。安装步骤见配套下载中的 README.zh-CN.md。"));
        page.Children.Add(Ui.Text("后台任务与凭据可在左侧管理。更新、回滚和移除会暂存到下次启动；当前进程不会卸载或替换已加载程序集。"));
        if (_shutdownPending) page.Children.Add(Ui.Text("关闭仍在等待已知插件工作结束。管理页面仅供查询，请使用窗口上方“重试关闭”。"));
        AddDiagnostics(page);
        return page;
    }

    private StackPanel PluginsPage()
    {
        var page = Ui.Page("插件", "清单发现与本地安装不会运行插件代码。首次启用需要明确的信任确认。");
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        var install = Ui.Named(new Button { Content = "从本地文件夹安装", IsEnabled = !_shutdownPending }, "从本地文件夹安装插件包");
        BindClick(install, InstallAsync);
        var refresh = Ui.Named(new Button { Content = "重新发现", IsEnabled = !_shutdownPending }, "重新发现本地插件包");
        BindClick(refresh, async runtime =>
        {
            DetachPluginViews();
            await runtime.Manager.DiscoverAsync();
            RefreshNavigation();
            Navigate(new("plugins"));
        });
        actions.Children.Add(install);
        actions.Children.Add(refresh);
        page.Children.Add(actions);
        page.Children.Add(Ui.Text($"可写安装目录：{Path.Combine(EffectiveRoot, "Plugins")}\n随应用分发的包目录：{Path.Combine(AppContext.BaseDirectory, "Plugins")}"));
        AddDiagnostics(page, includePackages: false);
        foreach (var package in Manager.Plugins)
        {
            var manifest = package.Manifest;
            var body = new StackPanel { Spacing = 10 };
            body.Children.Add(Ui.Text(manifest?.Name ?? Path.GetFileName(package.PackagePath), 20));
            body.Children.Add(Ui.Text(manifest is null ? "清单无效" : $"{manifest.Id} · v{manifest.Version} · API {manifest.ApiVersion}\n{manifest.Description}"));
            body.Children.Add(Ui.Text($"状态：{package.State} · 已信任：{package.IsTrusted} · 下次启动启用：{package.DesiredEnabled}\n{package.PackagePath}"));
            if (manifest is not null) body.Children.Add(Ui.Text($"声明能力：{string.Join("、", manifest.Capabilities)}"));
            foreach (var diagnostic in package.Diagnostics) body.Children.Add(Ui.Text($"[{diagnostic.Code}] {diagnostic.Message}"));
            if (manifest is not null)
            {
                var id = manifest.Id;
                var ownsRuntime = package.HasRuntime || package.HasPendingStop;
                var button = Ui.Named(new Button
                {
                    Content = package.HasPendingStop ? "重试停用" : ownsRuntime ? "停用" : "启用",
                    IsEnabled = !_shutdownPending && (ownsRuntime || package.State != PluginState.Invalid)
                }, $"{(ownsRuntime ? "停用" : "启用")} {manifest.Name}");
                BindClick(button, runtime => ownsRuntime ? DisableAsync(runtime, id) : EnableAsync(runtime, package));
                body.Children.Add(button);
                AddPackageActions(body, package);
            }
            page.Children.Add(Ui.Card(body));
        }
        return page;
    }

    private async Task InstallAsync(HostRuntime runtime)
    {
        var picker = new FolderPicker(AppWindow.Id) { CommitButtonText = "安装此插件包" };
        var folder = await picker.PickSingleFolderAsync();
        if (folder is null) return;
        var result = await runtime.Packages.InstallAsync(folder.Path);
        if (result.Succeeded)
        {
            DetachPluginViews();
            await runtime.Manager.DiscoverAsync();
            RefreshNavigation();
            Navigate(new("plugins"));
        }
        var outcome = PackagePresentation.ForResult(PackageAction.Import, result, runtime.Packages.PendingOperations.Count != 0);
        Notice(outcome.Title, outcome.Body, outcome.IsSuccess ? InfoBarSeverity.Success : InfoBarSeverity.Warning);
    }

    private StackPanel CommandsPage()
    {
        var page = Ui.Page("命令", "只执行当前启用插件的命令。运行结果与错误显示在窗口上方。");
        if (Manager.Contributions.Commands.Count == 0) page.Children.Add(Ui.Text("暂无命令。请先启用本地示例插件。"));
        foreach (var command in Manager.Contributions.Commands)
        {
            var body = new StackPanel { Spacing = 10 };
            body.Children.Add(Ui.Text(command.Title, 20));
            body.Children.Add(Ui.Text($"{command.PluginId} / {command.Id}\n{command.Description}"));
            var button = Ui.Named(new Button { Content = "运行", IsEnabled = !_shutdownPending }, $"运行 {command.Title}");
            BindClick(button, async runtime =>
            {
                var result = await runtime.Manager.ExecuteCommandAsync(command.PluginId, command.Id);
                Notice(result.Succeeded ? "命令完成" : "命令失败", result.Message,
                    result.Succeeded ? InfoBarSeverity.Success : InfoBarSeverity.Error);
            });
            body.Children.Add(button);
            page.Children.Add(Ui.Card(body));
        }
        AddDiagnostics(page);
        return page;
    }

    private StackPanel SettingsPage()
    {
        var page = Ui.Page("工作台设置", "主题作用于同一个窗口内的主程序和原生插件控件。");
        page.Children.Add(Ui.Text("Project Workbench 0.4.0 · API 1 · Runtime services 1 · Project services 1 · Testing contracts 1"));
        var theme = Ui.Named(new ComboBox { Header = "应用主题", HorizontalAlignment = HorizontalAlignment.Stretch, IsEnabled = !_shutdownPending }, "应用主题");
        theme.Items.Add("跟随系统"); theme.Items.Add("浅色"); theme.Items.Add("深色");
        theme.SelectedIndex = (int)_root.RequestedTheme;
        Button? repair = null;
        async Task SaveCurrentThemeAsync(HostRuntime runtime)
        {
            _root.RequestedTheme = (ElementTheme)theme.SelectedIndex;
            var recovering = _themeSettingsNeedRepair;
            _themePreferences!.Select(theme.SelectedIndex);
            await FlushThemeAsync(runtime);
            _themeSettingsNeedRepair = false;
            if (repair is not null) page.Children.Remove(repair);
            Notice("设置已保存", "主题偏好将在下次启动时恢复。" + (recovering ? "原主题文件已保留为同目录的 .corrupt-*.bak 备份。" : ""), InfoBarSeverity.Success);
        }
        var saveTheme = BindCurrentAction(SaveCurrentThemeAsync);
        theme.SelectionChanged += async (_, _) => await saveTheme();
        page.Children.Add(theme);
        if (_themeSettingsNeedRepair)
        {
            page.Children.Add(Ui.Text("恢复会先保留当前文件的完整副本。如果文件在失败后已更改，普通自动保存或关闭重试不会覆盖它；请使用此条件恢复入口重新确认当前文件。"));
            repair = Ui.Named(new Button { Content = "恢复主题设置", IsEnabled = !_shutdownPending }, "恢复主题设置");
            BindClick(repair, async runtime =>
            {
                await _themePreferences!.ObserveRecoveryAsync();
                await SaveCurrentThemeAsync(runtime);
            });
            page.Children.Add(repair);
        }
        page.Children.Add(Ui.Text($"本地用户目录：{EffectiveRoot}"));
        page.Children.Add(Ui.Text("插件设置通过左侧各插件的“设置”入口打开。JSON 设置与数据文件不是凭据保险箱。"));
        AddDiagnostics(page);
        return page;
    }

    private void LoadTheme()
    {
        _root.RequestedTheme = ElementTheme.Default;
        _themeSettingsNeedRepair = false;
        _themePreferences = new(EffectiveRoot);
        _themePreferences.Load();
        _root.RequestedTheme = (ElementTheme)_themePreferences.SelectedTheme;
        _themeSettingsNeedRepair = _themePreferences.NeedsRepair;
        if (_themePreferences.ErrorCode is { } failure)
            Notice("主题设置无法读取", $"已使用系统主题，原文件尚未改写。请在“工作台设置”保持“跟随系统”并点击“恢复主题设置”，或改选主题自动保存；保存前会保留原文件备份。若仍失败，请检查用户目录可写性。{failure}", InfoBarSeverity.Warning);
    }

    private async Task FlushThemeAsync(HostRuntime runtime)
    {
        if (_themePreferences is null) return;
        if (!string.Equals(runtime.UserRoot, _themePreferences.UserRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The preference session belongs to another root.");
        await _themePreferences.FlushAsync();
        _themeSettingsNeedRepair = _themePreferences.NeedsRepair;
    }

    private void AddDiagnostics(StackPanel page, bool includePackages = true)
    {
        var messages = Manager.Diagnostics.Select(d => $"[主程序/{d.Code}] {d.Message}")
            .Concat(Packages.Diagnostics.Select(d => $"[包维护/{d.Code}] {d.Message}"));
        if (includePackages) messages = messages.Concat(Manager.Plugins.SelectMany(p => p.Diagnostics.Select(d => $"[{p.Manifest?.Id ?? p.PackagePath}/{d.Code}] {d.Message}")));
        var lines = messages.Distinct().ToArray();
        if (lines.Length == 0) return;
        page.Children.Add(Ui.Text("诊断", 20));
        page.Children.Add(Ui.Text(string.Join("\n\n", lines)));
    }

}
