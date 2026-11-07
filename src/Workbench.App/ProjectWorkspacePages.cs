using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Workbench.Core.Projects;

namespace Workbench.App;

internal sealed partial class MainWindow
{
    private MenuBar? _workspaceMenu;
    private TextBlock? _workspaceTitle;
    private ComboBox? _workspaceSelector;
    private bool _selectingWorkspace;
    private ContentDialog? _projectDialog;

    private FrameworkElement CreateWorkspaceHeader()
    {
        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16, Padding = new Thickness(12, 4, 12, 4) };
        _workspaceMenu = Ui.Named(new MenuBar(), "文件菜单");
        var file = new MenuBarItem { Title = "文件" };
        var create = Ui.Named(new MenuFlyoutItem { Text = "新建项目…" }, "新建项目");
        var open = Ui.Named(new MenuFlyoutItem { Text = "打开项目…" }, "打开项目");
        var rename = Ui.Named(new MenuFlyoutItem { Text = "重命名当前项目…" }, "重命名当前项目");
        create.Click += async (_, _) => await ShowProjectNameDialogAsync(create: true);
        open.Click += async (_, _) => await ShowOpenProjectDialogAsync();
        rename.Click += async (_, _) => await ShowProjectNameDialogAsync(create: false);
        file.Items.Add(create); file.Items.Add(open); file.Items.Add(rename);
        _workspaceMenu.Items.Add(file);
        header.Children.Add(_workspaceMenu);
        _workspaceTitle = Ui.Named(Ui.Text("Project Workbench", 18), "当前项目标题");
        _workspaceTitle.VerticalAlignment = VerticalAlignment.Center;
        header.Children.Add(_workspaceTitle);
        _workspaceSelector = Ui.Named(new ComboBox { Visibility = Visibility.Collapsed, MinWidth = 180,
            PlaceholderText = "切换项目", VerticalAlignment = VerticalAlignment.Center }, "切换项目");
        _workspaceSelector.SelectionChanged += async (_, _) =>
        {
            if (_selectingWorkspace || _workspace is null || _workspaceSelector.SelectedItem is not ComboBoxItem { Tag: string id }
                || id == _workspace.ActiveProject?.Id) return;
            await RunWorkspaceAsync(() => _workspace.SwitchAsync(id));
        };
        header.Children.Add(_workspaceSelector);
        return header;
    }

    private void RefreshWorkspace(bool reopenPage = true)
    {
        if (_workspace is null) return;
        AssertUiThread();
        var changed = _runtimeGeneration != _workspace.RuntimeGeneration;
        if (changed)
        {
            // Includes a failed switch that restored a fresh manager for the same project ID.
            DetachPluginViews();
            _runtime = _workspace.Runtime;
            _runtimeGeneration = _workspace.RuntimeGeneration;
            _notice.ActionButton = null;
            _notice.IsOpen = false;
            LoadTheme();
        }
        _initialized = _workspace.Runtime is not null;
        var usable = _workspace.CanUseCurrentRuntime && !_closing && !_shutdownPending;
        if (_workspaceTitle is not null) _workspaceTitle.Text = _workspace.ActiveProject?.Name ?? "Project Workbench";
        Title = _workspace.ActiveProject is { } active ? $"{active.Name} · Project Workbench" : "Project Workbench";
        if (_workspaceMenu is not null) _workspaceMenu.IsEnabled = usable && !_busy;
        if (_workspaceSelector is not null)
        {
            _selectingWorkspace = true;
            try
            {
                _workspaceSelector.Items.Clear();
                foreach (var project in _workspace.Catalog?.Projects.Where(p => !p.IsArchived) ?? [])
                    _workspaceSelector.Items.Add(new ComboBoxItem { Content = project.Name, Tag = project.Id });
                _workspaceSelector.Visibility = _workspaceSelector.Items.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
                _workspaceSelector.SelectedItem = _workspaceSelector.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == _workspace.ActiveProject?.Id);
                _workspaceSelector.IsEnabled = usable && !_busy;
            }
            finally { _selectingWorkspace = false; }
        }
        _navigation.IsEnabled = usable && !_busy;
        _navigation.IsSettingsVisible = usable;
        _content.IsEnabled = usable && !_busy;
        if (usable)
        {
            _body.Content = _navigation;
            if (changed || reopenPage)
            {
                RefreshNavigation();
                Navigate(new("home"));
            }
        }
    }

    private async Task RunWorkspaceAsync(Func<Task<ProjectSwitchResult>> operation)
    {
        if (_workspace is null || _busy || _closing || _shutdownPending) return;
        _busy = true;
        var settled = BeginShellOperation();
        _content.IsEnabled = false;
        _navigation.IsEnabled = false;
        if (_workspaceMenu is not null) _workspaceMenu.IsEnabled = false;
        if (_workspaceSelector is not null) _workspaceSelector.IsEnabled = false;
        _body.Content = ScrollPage(Ui.Page("正在打开项目", "正在保存上下文并等待已知插件工作安全结束…"));
        try
        {
            if (_themePreferences?.IsDirty == true)
                await _workspace.RunCurrentAsync((runtime, _, _) => OnUiAsync(() => FlushThemeAsync(runtime)));
            DetachPluginViews(); // Only after accepted preferences are durable; clears credential drafts.
            _body.Content = ScrollPage(Ui.Page("正在打开项目", "正在保存上下文并等待已知插件工作安全结束…"));
            var pending = operation(); // Admission closes synchronously before this first await.
            var result = await pending;
            RefreshWorkspace();
            if (!result.Succeeded)
            {
                if (!_workspace.CanUseCurrentRuntime) ShowWorkspaceRecovery();
                Notice("项目操作尚未完成", WorkspaceGuidance(result.ErrorCode), InfoBarSeverity.Warning);
            }
        }
        catch (Exception error)
        {
            RefreshWorkspace();
            if (!_workspace.CanUseCurrentRuntime) ShowWorkspaceRecovery();
            Notice("项目操作未完成", _themePreferences?.IsDirty == true
                ? "主题偏好尚未保存。已保留当前选择和项目，请检查目录，或改选主题自动重试。"
                : HostFailure.Describe("ProjectOperationFailed", error), InfoBarSeverity.Warning);
        }
        finally
        {
            CompleteShellOperation(settled);
        }
    }

    private async Task ShowProjectNameDialogAsync(bool create)
    {
        if (_workspace is not { CanUseCurrentRuntime: true } || _busy || _closing || _shutdownPending) return;
        // Input collection is separate from the coordinator queue: transitions must never nest in RunCurrentAsync.
        _busy = true;
        var settled = BeginShellOperation();
        RefreshWorkspace(reopenPage: false);
        var name = Ui.Named(new TextBox { Header = "项目名称", Text = create ? "" : _workspace.ActiveProject!.Name, MaxLength = 80 }, "项目名称");
        var dialog = new ContentDialog { XamlRoot = _root.XamlRoot, Title = create ? "新建项目" : "重命名当前项目", Content = name,
            PrimaryButtonText = create ? "创建并打开" : "重命名", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Primary };
        ContentDialogResult result;
        try { _projectDialog = dialog; result = await dialog.ShowAsync(); }
        catch (Exception error) { Notice("无法打开项目对话框", HostFailure.Describe("ProjectDialogFailed", error), InfoBarSeverity.Warning); return; }
        finally { _projectDialog = null; CompleteShellOperation(settled); }
        if (result == ContentDialogResult.Primary)
            await RunWorkspaceAsync(() => create ? _workspace.CreateAndSwitchAsync(name.Text) : _workspace.RenameAsync(name.Text));
    }

    private async Task ShowOpenProjectDialogAsync()
    {
        if (_workspace is not { CanUseCurrentRuntime: true } || _busy || _closing || _shutdownPending) return;
        _busy = true;
        var settled = BeginShellOperation();
        RefreshWorkspace(reopenPage: false);
        var list = Ui.Named(new ListView { SelectionMode = ListViewSelectionMode.Single, MaxHeight = 400 }, "打开项目列表");
        foreach (var project in _workspace.Catalog!.Projects)
            list.Items.Add(new ListViewItem { Content = project.Name + (project.IsArchived ? "（已归档）" : ""), Tag = project });
        IReadOnlyList<ProjectWorkspaceInfo> recoverable;
        try { recoverable = await _workspace.GetRecoverableAsync(); }
        catch (Exception error)
        {
            CompleteShellOperation(settled);
            Notice("项目列表暂不能读取", HostFailure.Describe("ProjectCatalogReadFailed", error), InfoBarSeverity.Warning);
            return;
        }
        var orphanIds = recoverable.Select(project => project.Id).ToHashSet(StringComparer.Ordinal);
        if (_workspaceCloseRequested) { CompleteShellOperation(settled); return; }
        foreach (var project in recoverable)
            list.Items.Add(new ListViewItem { Content = project.Name + "（待恢复）", Tag = project });
        var body = new StackPanel { Spacing = 12 };
        body.Children.Add(Ui.Text("归档只隐藏项目入口，保留原目录中的上下文、设置和数据。打开归档项目会恢复其入口。"));
        body.Children.Add(list);
        var dialog = new ContentDialog { XamlRoot = _root.XamlRoot, Title = "打开项目", Content = body,
            PrimaryButtonText = "打开", SecondaryButtonText = "归档选中项", CloseButtonText = "取消", IsPrimaryButtonEnabled = false,
            IsSecondaryButtonEnabled = false, DefaultButton = ContentDialogButton.Close };
        list.SelectionChanged += (_, _) =>
        {
            var project = (list.SelectedItem as ListViewItem)?.Tag as ProjectWorkspaceInfo;
            dialog.IsPrimaryButtonEnabled = project is not null;
            dialog.PrimaryButtonText = project is not null && (project.IsArchived || orphanIds.Contains(project.Id)) ? "恢复并打开" : "打开";
            dialog.IsSecondaryButtonEnabled = project is { IsArchived: false } && !orphanIds.Contains(project.Id) && project.Id != _workspace.ActiveProject?.Id;
        };
        ContentDialogResult result;
        try { _projectDialog = dialog; result = await dialog.ShowAsync(); }
        catch (Exception error) { Notice("无法打开项目对话框", HostFailure.Describe("ProjectDialogFailed", error), InfoBarSeverity.Warning); return; }
        finally { _projectDialog = null; CompleteShellOperation(settled); }
        if ((list.SelectedItem as ListViewItem)?.Tag is not ProjectWorkspaceInfo selected) return;
        if (result == ContentDialogResult.Secondary) await RunWorkspaceAsync(() => _workspace.SetArchivedAsync(selected.Id, true));
        else if (result == ContentDialogResult.Primary) await RunWorkspaceAsync(async () =>
        {
            if (orphanIds.Contains(selected.Id)) return await _workspace.RecoverProjectAsync(selected.Id);
            if (selected.IsArchived)
            {
                var restored = await _workspace.SetArchivedAsync(selected.Id, false);
                if (!restored.Succeeded) return restored;
            }
            return await _workspace.SwitchAsync(selected.Id);
        });
    }

    private StackPanel ProjectContextPage()
    {
        var page = Ui.Page("项目上下文", "为当前项目记录目标、约定和背景。编辑会自动保存到当前项目目录。");
        if (_workspace?.Document is not { } document)
        {
            page.Children.Add(Ui.Text("项目上下文尚未就绪。请先重试打开项目。"));
            return page;
        }
        var editor = Ui.Named(new TextBox { Text = document.Text, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
            MinHeight = 300, HorizontalAlignment = HorizontalAlignment.Stretch, IsEnabled = document.IsEditable }, "项目上下文编辑器");
        var status = Ui.Named(Ui.Text(""), "项目上下文保存状态");
        var recover = Ui.Named(new Button { Content = "保留本地副本并重新读取", Visibility = Visibility.Collapsed }, "保留上下文冲突副本并重新读取");
        var replacing = false;
        void Refresh()
        {
            editor.IsEnabled = document.IsEditable && !_closing;
            status.Text = document.ErrorCode is { } code ? WorkspaceGuidance(code)
                : document.IsDirty ? "正在自动保存…" : "已自动保存";
            recover.Visibility = document.ErrorCode is null ? Visibility.Collapsed : Visibility.Visible;
            recover.IsEnabled = (document.IsEditable || _workspace?.CanUseCurrentRuntime == true) && !_closing;
        }
        editor.TextChanged += (_, _) =>
        {
            if (replacing || !document.IsEditable || _closing) return;
            try { document.Edit(editor.Text); }
            catch (Exception) { Notice("上下文暂不能编辑", "正在等待当前操作完成；原内容仍保留在当前项目目录。", InfoBarSeverity.Warning); }
            Refresh();
        };
        recover.Click += async (_, _) =>
        {
            if (!(document.IsEditable || _workspace?.CanUseCurrentRuntime == true) || _closing) return;
            try
            {
                await document.PreserveConflictAndReloadAsync();
                replacing = true;
                editor.Text = document.Text;
                replacing = false;
                Notice("上下文已重新读取", "如有未保存内容，已先保留到当前项目的 context-conflicts 目录。", InfoBarSeverity.Success);
            }
            catch (Exception) { Notice("上下文恢复未完成", "已保留当前内容。请检查项目目录可写性后重试。", InfoBarSeverity.Warning); }
            finally { replacing = false; Refresh(); }
        };
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        timer.Tick += (_, _) => Refresh();
        page.Loaded += (_, _) => timer.Start();
        page.Unloaded += (_, _) => timer.Stop();
        page.Children.Add(editor); page.Children.Add(status); page.Children.Add(recover);
        Refresh();
        return page;
    }

    private void ShowWorkspaceRecovery()
    {
        var page = Ui.Page(_shutdownPending ? "关闭尚未完成" : "项目尚未就绪",
            _shutdownPending ? "当前目录仍由此窗口持有。可以在清理确认完成后修正上下文，再重试安全关闭。"
                : WorkspaceGuidance(_workspace?.Transition.ErrorCode));
        if (_workspace?.Document is not null) page.Children.Add(ProjectContextPage());
        if (!_shutdownPending && _workspace is not null)
        {
            var retry = Ui.Named(new Button { Content = "重试打开项目" }, "重试打开项目");
            retry.Click += async (_, _) => await RunWorkspaceAsync(() => _workspace.RetryAsync());
            page.Children.Add(retry);
            if (_workspace.Transition.ErrorCode is "RecoveryRequired" or "InvalidCatalog")
            {
                var recover = Ui.Named(new Button { Content = "从保留记录恢复项目列表" }, "恢复项目列表");
                recover.Click += async (_, _) => await RunWorkspaceAsync(() => _workspace.RecoverCatalogAsync());
                page.Children.Add(recover);
            }
        }
        _body.Content = ScrollPage(page);
    }

    private static string WorkspaceGuidance(string? code) => code switch
    {
        "InvalidName" => "请使用有效的项目名称后重试。",
        "RevisionConflict" => "上下文或项目列表已在别处更新。上下文可先保留本地副本并重新读取；项目操作请重试。",
        "ContextLimitExceeded" => "上下文过长。请缩短当前文本，系统会自动重试保存。",
        "InvalidContext" => "上下文文件无法读取。已保留原文件，请修复本地文件后重新读取。",
        "InvalidCatalog" or "RecoveryRequired" => "项目列表需要恢复。原文件与项目目录仍保留，可重试或从保留记录恢复。",
        "ProjectPreparationPending" or "ProjectStopPending" or "ProjectStartupPending" or "ProjectClosePending" or "ProjectRestorePending"
            => "仍在等待已知插件工作结束。目录租约继续保留，请等待后重试。",
        "ProjectPreparationFailed" => "插件尚未完成离开前准备。请修正可编辑的内容后重试。",
        "StoreBusy" or "StoreIoFailure" => "本地目录暂不可写或被占用。请检查目录后重试；原内容已保留。",
        _ => "项目操作未完成。已保留目录和当前内容，请检查本地文件并重试。"
    };
}
