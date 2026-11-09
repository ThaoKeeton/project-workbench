using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.Storage.Pickers;

namespace Workbench.App;

internal sealed partial class MainWindow
{
    // Independent of Manager and project IO, so the same controls work before a runtime exists.
    private FrameworkElement CreateErrorReportPanel(StartupErrorReport report)
    {
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(Ui.Text("错误报告（默认脱敏）", 20));
        panel.Children.Add(Ui.Text($"错误码：{report.ErrorCode}\n失败阶段：{report.Stage}"));
        panel.Children.Add(Ui.Text("仅包含阶段、错误码、应用与系统版本及随机报告编号，不包含路径、机器名、凭据、配置或项目内容。导出只保存到你选择的文件夹，不会自动发送。"));
        var status = Ui.Named(Ui.Text("可导出 JSON 文件交给支持人员分析。原有数据不会改写。"), "错误报告导出状态");
        var export = Ui.Named(new Button { Content = "导出错误报告…" }, "导出脱敏错误报告");
        export.Click += async (_, _) =>
        {
            if (_busy || _closing || !export.IsEnabled) return;
            _busy = true;
            var settled = BeginShellOperation();
            export.IsEnabled = false;
            try
            {
                var picker = new FolderPicker(AppWindow.Id) { CommitButtonText = "将错误报告保存到此文件夹" };
                var folder = await picker.PickSingleFolderAsync();
                if (folder is null) { status.Text = "已取消导出，尚未写入报告。"; return; }
                if (_closing) return;
                var result = await HostErrorReportExport.TryWriteAsync(folder.Path, report);
                status.Text = result switch
                {
                    HostReportExportStatus.Succeeded => $"已导出 {report.FileName}。可将此 JSON 文件发给支持人员分析。",
                    HostReportExportStatus.AlreadyExists => "此文件夹已有同名报告，原文件未覆盖。可使用已有报告或选择其他文件夹。",
                    HostReportExportStatus.Canceled => "已取消导出，原有文件未改写。",
                    _ => "此文件夹暂不可写或不适合保存报告。请选择其他本地文件夹重试，原有文件未改写。"
                };
            }
            catch (Exception) { status.Text = "无法打开保存窗口或导出报告。请重试，或选择其他本地文件夹；原有文件未改写。"; }
            finally
            {
                if (!_closing) export.IsEnabled = true;
                CompleteShellOperation(settled);
            }
        };
        panel.Children.Add(export);
        panel.Children.Add(status);
        return Ui.Card(panel);
    }
}
