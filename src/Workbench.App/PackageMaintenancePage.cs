using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.Storage.Pickers;
using Workbench.Core;

namespace Workbench.App;

internal sealed partial class MainWindow
{
    private StackPanel PackageMaintenancePage()
    {
        var page = Ui.Page("包维护", "导入不会执行代码或批准信任。更新、回滚和移除先暂存，安全退出后由下次启动在发现插件前执行。");
        page.Children.Add(Ui.Text("代码回滚不恢复、删除或降级当前数据、设置、凭据。备份仅覆盖 UserRoot 管理的数据/设置/密文与后台意图；明确连接的独立版规范状态、外部 DWS profile 和其他外部存储不在自动备份或恢复范围。"));
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        AddAction(actions, "导入 ZIP", "导入本地插件 ZIP", () => ImportZipAsync());
        AddAction(actions, "导入文件夹", "导入本地插件文件夹", InstallAsync);
        AddAction(actions, "刷新维护状态", "刷新包维护状态", async () => { await Packages.RefreshAsync(); Navigate(new("packages")); });
        page.Children.Add(actions);
        if (_runtime!.MaintenanceResults.Count != 0)
        {
            page.Children.Add(Ui.Text("本次启动的维护结果", 20));
            foreach (var result in _runtime.MaintenanceResults)
            {
                var outcome = PackagePresentation.ForResult(PackageAction.Startup, result, Packages.PendingOperations.Count != 0);
                page.Children.Add(Ui.Text($"{outcome.Title}：{outcome.Body}"));
            }
        }
        page.Children.Add(Ui.Text("待重启操作", 20));
        if (Packages.PendingOperations.Count == 0) page.Children.Add(Ui.Text("没有待执行的操作。"));
        foreach (var pending in Packages.PendingOperations)
        {
            var body = new StackPanel { Spacing = 10 };
            var phase = PackagePresentation.ForPending(pending.Phase);
            body.Children.Add(Ui.Text($"{pending.PluginId} · {PackagePresentation.KindLabel(pending.Kind)}", 20));
            body.Children.Add(Ui.Text($"{pending.OldVersion ?? "—"} → {pending.NewVersion ?? "移除"} · 阶段：{phase.Label}\n{phase.Body}"));
            if (phase.CanCancel)
                AddAction(body, "取消暂存", $"取消包维护 {pending.PluginId}", async () => PackageNotice(PackageAction.Cancel, await Packages.CancelPendingAsync(pending.PluginId)));
            page.Children.Add(Ui.Card(body));
        }
        page.Children.Add(Ui.Text("当前包", 20));
        foreach (var package in Manager.Plugins.Where(package => package.Manifest is not null))
        {
            var body = new StackPanel { Spacing = 10 };
            body.Children.Add(Ui.Text($"{package.Manifest!.Name} · {package.Manifest.Version}", 20));
            body.Children.Add(Ui.Text(package.Manifest.Id));
            AddPackageActions(body, package);
            page.Children.Add(Ui.Card(body));
        }
        page.Children.Add(Ui.Text("代码历史", 20));
        if (Packages.History.Count == 0) page.Children.Add(Ui.Text("尚无已提交且指纹有效的代码快照。"));
        foreach (var history in Packages.History)
        {
            var body = new StackPanel { Spacing = 10 };
            body.Children.Add(Ui.Text($"{history.PluginId} · {history.Version}", 20));
            body.Children.Add(Ui.Text($"{history.HistoryId} · {history.CreatedAt:u}\n只回退代码。当前数据保持原样；仍需重新确认信任。"));
            if (history.DataBackupPath is not null) body.Children.Add(Ui.Text($"宿主管理状态的历史记录（不会自动回写）：{history.DataBackupPath}"));
            AddAction(body, "暂存代码回滚", $"回滚代码 {history.PluginId}/{history.HistoryId}", async () =>
            {
                if (await ConfirmMaintenanceAsync("暂存代码回滚？", $"{history.PluginId} → {history.Version}\n这不会恢复或降级业务数据、凭据或外部存储。下次启动应用后需要重新确认信任。"))
                    PackageNotice(PackageAction.Rollback, await Packages.StageRollbackAsync(history.PluginId, history.HistoryId));
            }, !Packages.PendingOperations.Any(operation => operation.PluginId == history.PluginId));
            page.Children.Add(Ui.Card(body));
        }
        AddDiagnostics(page);
        return page;
    }

    private void AddPackageActions(StackPanel body, PluginSnapshot package)
    {
        if (package.Manifest is null) return;
        var managedRoot = Path.GetFullPath(Path.Combine(_options.UserRoot, "Plugins"));
        var isManaged = string.Equals(Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(package.PackagePath))), managedRoot, StringComparison.OrdinalIgnoreCase);
        if (!isManaged) { body.Children.Add(Ui.Text("随应用分发的只读包，由其发布方更新。")); return; }
        var id = package.Manifest.Id;
        var canStage = !Packages.PendingOperations.Any(operation => operation.PluginId == id);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        AddAction(actions, "更新 ZIP", $"暂存 ZIP 更新 {id}", () => StageUpdateAsync(id, zip: true), canStage);
        AddAction(actions, "更新文件夹", $"暂存文件夹更新 {id}", () => StageUpdateAsync(id, zip: false), canStage);
        AddAction(actions, "暂存移除", $"暂存移除 {id}", async () =>
        {
            if (await ConfirmMaintenanceAsync("下次启动移除此插件？", $"{id}\n当前运行实例不受暂存影响。安全退出后执行，保留当前数据、设置、凭据和代码历史。"))
                PackageNotice(PackageAction.Remove, await Packages.StageRemoveAsync(id));
        }, canStage);
        body.Children.Add(actions);
        if (!canStage) body.Children.Add(Ui.Text("此包已有待执行操作，请到“包维护”查看；只有 Prepared 且尚未移动代码的暂存可取消。取消被拒绝时保留维护记录并安全重启。"));
    }

    private void AddAction(StackPanel parent, string label, string name, Func<Task> action, bool enabled = true)
    {
        var button = Ui.Named(new Button { Content = label, IsEnabled = enabled && !_shutdownPending }, name);
        button.Click += async (_, _) => await RunAsync(action);
        parent.Children.Add(button);
    }

    private async Task<string?> PickPackageAsync(bool zip)
    {
        if (!zip) return (await new FolderPicker(AppWindow.Id) { CommitButtonText = "选择插件包" }.PickSingleFolderAsync())?.Path;
        var picker = new FileOpenPicker(AppWindow.Id) { CommitButtonText = "选择插件 ZIP" };
        picker.FileTypeFilter.Add(".zip");
        return (await picker.PickSingleFileAsync())?.Path;
    }

    private async Task ImportZipAsync()
    {
        var path = await PickPackageAsync(zip: true);
        if (path is null) return;
        var result = await Packages.InstallAsync(path);
        if (result.Succeeded)
        {
            DetachPluginViews();
            await Manager.DiscoverAsync();
            RefreshNavigation();
        }
        PackageNotice(PackageAction.Import, result);
    }

    private async Task StageUpdateAsync(string id, bool zip)
    {
        var path = await PickPackageAsync(zip);
        if (path is not null) PackageNotice(PackageAction.Update, await Packages.StageUpdateAsync(id, path));
    }

    private async Task<bool> ConfirmMaintenanceAsync(string title, string content) => await new ContentDialog
    {
        XamlRoot = _root.XamlRoot, Title = title, Content = content,
        PrimaryButtonText = "确认暂存", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Close
    }.ShowAsync() == ContentDialogResult.Primary;

    private void PackageNotice(PackageAction action, PluginOperationResult result)
    {
        Navigate(new("packages"));
        var outcome = PackagePresentation.ForResult(action, result, Packages.PendingOperations.Count != 0);
        Notice(outcome.Title, outcome.Body, outcome.IsSuccess ? InfoBarSeverity.Success : InfoBarSeverity.Warning);
    }
}
