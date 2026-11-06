using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Workbench.App;

internal sealed class WorkbenchApplication : Application
{
    private readonly RunOptions _options;
    private MainWindow? _window;

    public WorkbenchApplication(RunOptions options)
    {
        _options = options;
        _options.TraceStartup("application-constructor-entered");
        UnhandledException += (_, args) =>
        {
            Environment.ExitCode = 1;
            _options.WriteFailure("unhandled-ui-exception", args.Exception, args.Message);
            // Do not turn an unhandled native/UI failure into a successful smoke result.
        };
        _options.TraceStartup("adding-xaml-controls-resources");
        Resources.MergedDictionaries.Add(new XamlControlsResources());
        _options.TraceStartup("application-resources-ready");
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            _options.TraceStartup("launch-callback-entered");
            _options.TraceStartup("constructing-main-window");
            _window = new MainWindow(_options);
            _options.TraceStartup("activating-main-window");
            _window.Activate();
            _options.TraceStartup("main-window-activated");
            _ = _window.InitializeAsync();
        }
        catch (Exception error)
        {
            Environment.ExitCode = 1;
            _options.WriteFailure("launch-callback-failed", error);
            throw;
        }
    }
}
