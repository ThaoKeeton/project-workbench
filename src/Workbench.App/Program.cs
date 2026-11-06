using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace Workbench.App;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var options = RunOptions.Parse(args);
        try
        {
            options.TraceStartup("managed-main-entered");
            // Keep Windows App SDK's generated module initializers; this only supplies the code-only entry point.
            WinRT.ComWrappersSupport.InitializeComWrappers();
            options.TraceStartup("com-wrappers-ready");
            Application.Start(initialization =>
            {
                options.TraceStartup("application-start-callback");
                SynchronizationContext.SetSynchronizationContext(
                    new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
                options.TraceStartup("dispatcher-context-ready");
                options.TraceStartup("constructing-application");
                _ = new WorkbenchApplication(options);
            });
        }
        catch (Exception error)
        {
            Environment.ExitCode = 1;
            options.WriteFailure("native-startup", error);
        }
        return Environment.ExitCode;
    }
}
