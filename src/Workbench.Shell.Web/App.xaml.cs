using Microsoft.UI.Xaml;

namespace Workbench.Shell.Web;

public partial class ShellApplication : Application
{
    private ShellWindow? _window;

    public ShellApplication() => InitializeComponent();

    protected override void OnLaunched(LaunchActivatedEventArgs args) => _ = LaunchAsync();

    private async Task LaunchAsync()
    {
        try
        {
            var endpoint = await ShellLaunch.StartOrAttachAsync(ShellLaunch.FromArguments(Environment.GetCommandLineArgs().Skip(1).ToArray()));
            _window = new ShellWindow(endpoint.OwnedService);
            _window.Activate();
            await _window.AttachAsync(endpoint.BaseUrl, endpoint.SessionToken);
        }
        catch (Exception)
        {
            Environment.ExitCode = 1;
        }
    }
}
