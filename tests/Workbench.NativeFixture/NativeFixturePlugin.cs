using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Workbench.Sdk;
using Workbench.WinUI.Sdk;

namespace Workbench.NativeFixture;

public sealed class NativeFixturePlugin : IWorkbenchPlugin
{
    private IPluginRuntimeContext _context = null!;
    private string _mode = "normal";
    private string _workspaceDraft = "";

    public async Task InitializeAsync(IPluginContext context, CancellationToken cancellationToken)
    {
        _context = (IPluginRuntimeContext)context;
        _mode = await context.Settings.GetAsync("mode", "normal", cancellationToken);
        await context.Settings.SetAsync("instances", await context.Settings.GetAsync("instances", 0) + 1);
        _context.Secrets.Register(new("synthetic.credential", "Synthetic native credential"));
        _context.BackgroundTasks.Register(new("worker", "Synthetic worker", execution => RunWorkerAsync("worker", execution)));
        _context.BackgroundTasks.Register(new("confirmed-worker", "Synthetic one-use confirmation worker", execution => RunWorkerAsync("confirmed-worker", execution))
            { RequiresPluginStart = true });
        _context.BackgroundTasks.Register(new("fault", "Synthetic fault", _ => throw new InvalidOperationException("synthetic-private-exception")));
        context.RegisterView(new WinUiView("fixture-page", "Synthetic runtime page", CreatePage));
        context.RegisterView(new WinUiView("fixture-settings", "Synthetic runtime settings", CreateSettings, WinUiViewKind.Settings));
        context.RegisterCommand(new("confirm-start", "Synthetic one-use confirmation", async token =>
        {
            var result = await _context.BackgroundTasks.StartAsync("confirmed-worker", token);
            return new(result.Succeeded, result.Message);
        }));
        if (_mode.StartsWith("workspace-", StringComparison.Ordinal) && context is IPluginProjectContext project)
            project.RegisterBeforeProjectLeave(async _ =>
            {
                await context.Settings.SetAsync("prepare-calls", await context.Settings.GetAsync("prepare-calls", 0) + 1);
                await File.WriteAllTextAsync(Path.Combine(context.DataDirectory, "prepare-started"), "synthetic");
                if (_mode == "workspace-hold-prepare")
                    while (!File.Exists(Path.Combine(context.DataDirectory, "release-prepare"))) await Task.Delay(10);
                if (_mode == "workspace-fail-prepare" && !File.Exists(Path.Combine(context.DataDirectory, "corrected-prepare")))
                    throw new InvalidOperationException("synthetic-private-workspace-failure");
                await File.WriteAllTextAsync(Path.Combine(context.DataDirectory, "prepared-draft.txt"), _workspaceDraft);
            });
        if (_mode == "hold-callback") context.Lifetime.Register(() =>
        {
            File.WriteAllText(Path.Combine(context.DataDirectory, "callback-started"), "synthetic");
            while (!File.Exists(Path.Combine(context.DataDirectory, "release-callback"))) Thread.Sleep(10);
        });
        if (_mode is "init-fail" or "workspace-init-fail-hold-stop") throw new InvalidOperationException("synthetic-private-exception");
    }

    private async Task RunWorkerAsync(string id, IPluginBackgroundExecution execution)
    {
        if (_mode == "synchronous" && id == "worker")
        {
            File.WriteAllText(Path.Combine(_context.DataDirectory, "sync-started"), "synthetic");
            while (!File.Exists(Path.Combine(_context.DataDirectory, "release-sync"))) Thread.Sleep(10);
        }
        var settings = _context.Settings;
        await settings.SetAsync(id + "-runs", await settings.GetAsync(id + "-runs", 0) + 1);
        await settings.SetAsync(id + "-start", execution.StartReason.ToString());
        try { await Task.Delay(Timeout.Infinite, execution.CancellationToken); }
        catch (OperationCanceledException) when (execution.CancellationToken.IsCancellationRequested)
        {
            await settings.SetAsync(id + "-stop", execution.StopReason.ToString());
            if (_mode == "hold-task" && id == "worker")
                while (!File.Exists(Path.Combine(_context.DataDirectory, "release-work"))) await Task.Delay(10);
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _context.Settings.SetAsync("stop-calls", await _context.Settings.GetAsync("stop-calls", 0) + 1);
        if (_mode is "hold-stop" or "workspace-init-fail-hold-stop")
            while (!File.Exists(Path.Combine(_context.DataDirectory, "release-stop"))) await Task.Delay(10);
    }

    private FrameworkElement CreatePage()
    {
        if (_mode is "page-fail" or "hostile-page") throw FactoryFailure();
        var panel = new StackPanel { Spacing = 12, Padding = new Thickness(28) };
        panel.Children.Add(new TextBlock { Text = "Synthetic local-only runtime fixture" });
        if (_mode.StartsWith("workspace-", StringComparison.Ordinal))
        {
            var draft = new TextBox { Header = "Synthetic private workspace draft", Text = _workspaceDraft };
            AutomationProperties.SetName(draft, "合成项目私有草稿");
            draft.TextChanged += (_, _) => _workspaceDraft = draft.Text;
            panel.Children.Add(draft);
        }
        var confirm = new Button { Content = "Confirm synthetic one-use start" };
        AutomationProperties.SetName(confirm, "确认本次合成任务");
        confirm.Click += async (_, _) => await _context.BackgroundTasks.StartAsync("confirmed-worker");
        panel.Children.Add(confirm);
        return panel;
    }

    private FrameworkElement CreateSettings()
    {
        if (_mode is "settings-fail" or "hostile-settings") throw FactoryFailure();
        var text = new TextBlock { Text = "Synthetic settings persist in the host-owned store." };
        AutomationProperties.SetName(text, "合成插件设置页");
        return text;
    }

    private Exception FactoryFailure() => _mode.StartsWith("hostile", StringComparison.Ordinal)
        ? new HostileFactoryException(_context.DataDirectory) : new InvalidOperationException("synthetic-private-native-provider-response");

    private sealed class HostileFactoryException(string dataDirectory) : Exception
    {
        public override string Message => Fail("Message");
        public override string ToString() => Fail("ToString");
        private string Fail(string member)
        {
            File.WriteAllText(Path.Combine(dataDirectory, "unsafe-exception-access"), member);
            throw new InvalidOperationException("synthetic-private-native-provider-response");
        }
    }
}
