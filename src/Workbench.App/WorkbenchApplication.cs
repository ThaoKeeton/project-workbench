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
        Resources.MergedDictionaries.Add(new XamlControlsResources());
        UnhandledException += (_, args) =>
        {
            Environment.ExitCode = 1;
            _options.WriteFailure("unhandled-ui-exception", args.Exception);
            // Do not turn an unhandled native/UI failure into a successful smoke result.
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow(_options);
        _window.Activate();
        _ = _window.InitializeAsync();
    }
}
