using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Workbench.Sdk;
using Workbench.WinUI.Sdk;

namespace Workbench.Summary.Sample;

public sealed class SummaryPlugin : IWorkbenchPlugin
{
    private string _audience = "项目团队";

    public async Task InitializeAsync(IPluginContext context, CancellationToken cancellationToken)
    {
        _audience = await context.Settings.GetAsync("audience", "项目团队", cancellationToken);
        context.RegisterSetting(new("audience", "摘要读者", "项目团队", "仅用于本地示例草稿。"));
        context.RegisterView(new WinUiView("summary", "工作摘要 · 示例", () => CreatePage(context)));
        context.RegisterView(new WinUiView("preferences", "工作摘要", () => CreateSettings(context), WinUiViewKind.Settings));
        context.RegisterCommand(new("generate-draft", "生成本地示例摘要", async token =>
        {
            context.Lifetime.ThrowIfCancellationRequested();
            var text = $"本地演示摘要\n读者：{_audience}\n\n已完成：原生插件页面原型。\n进行中：本地测试用例整理。\n待确认：下一次评审时间。\n\n以上均为预置示例，不来自真实业务系统，也未调用 AI。";
            var path = Path.Combine(context.DataDirectory, "summary-draft.txt");
            await File.WriteAllTextAsync(path, text, token);
            return new(true, $"示例草稿已写入本地：{path}。打开工作摘要页面可编辑。");
        }, "使用预置数据写入插件自己的数据目录，不发送消息。"));
    }

    private static FrameworkElement CreatePage(IPluginContext context)
    {
        context.Lifetime.ThrowIfCancellationRequested();
        var panel = Panel("工作摘要 · 本地示例", "以下是预置演示数据；钉钉、Teambition 和 AI 尚未接入。");
        panel.Children.Add(Text("已完成 1 项  ·  进行中 1 项  ·  待确认 1 项", 20));
        panel.Children.Add(Text("• 原生插件页面原型：已完成\n• 测试用例整理：进行中\n• 下一次评审时间：待确认"));
        var path = Path.Combine(context.DataDirectory, "summary-draft.txt");
        var draft = Named(new TextBox
        {
            Header = "可编辑的本地草稿",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 220,
            Text = File.Exists(path) ? File.ReadAllText(path) : "先在“命令”中运行“生成本地示例摘要”，或直接在此编辑。"
        }, "工作摘要示例草稿");
        var status = Text($"保存位置：{path}");
        var save = Named(new Button { Content = "保存本地草稿" }, "保存工作摘要示例草稿");
        save.Click += async (_, _) => await HandleAsync(context, status, async () =>
        {
            await File.WriteAllTextAsync(path, draft.Text, context.Lifetime);
            status.Text = $"已保存到 {path}";
        });
        panel.Children.Add(draft); panel.Children.Add(save); panel.Children.Add(status);
        return Scroll(panel);
    }

    private FrameworkElement CreateSettings(IPluginContext context)
    {
        context.Lifetime.ThrowIfCancellationRequested();
        var panel = Panel("工作摘要 · 示例设置", "偏好保存在该插件独立的本地 JSON 设置中。");
        var audience = Named(new TextBox { Header = "摘要读者", Text = _audience }, "示例摘要读者");
        var status = Text("此偏好只影响本地示例草稿。");
        var save = Named(new Button { Content = "保存偏好" }, "保存摘要示例偏好");
        save.Click += async (_, _) => await HandleAsync(context, status, async () =>
        {
            var value = audience.Text;
            await context.Settings.SetAsync("audience", value, context.Lifetime);
            _audience = value;
            status.Text = "偏好已保存。";
        });
        panel.Children.Add(audience); panel.Children.Add(save); panel.Children.Add(status);
        return Scroll(panel);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static async Task HandleAsync(IPluginContext context, TextBlock status, Func<Task> action)
    {
        try { context.Lifetime.ThrowIfCancellationRequested(); await action(); }
        catch (OperationCanceledException) { status.Text = "插件已停用，操作已取消。"; }
        catch (Exception error) { status.Text = $"本地操作失败：{error.Message}"; }
    }

    private static StackPanel Panel(string title, string description)
    {
        var panel = new StackPanel { Spacing = 16, Padding = new Thickness(28) };
        panel.Children.Add(Text(title, 28)); panel.Children.Add(Text(description)); return panel;
    }
    private static TextBlock Text(string value, double size = 14) => new() { Text = value, FontSize = size, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
    private static ScrollViewer Scroll(UIElement content) => new() { Content = content, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private static T Named<T>(T element, string name) where T : DependencyObject { AutomationProperties.SetName(element, name); return element; }
}
