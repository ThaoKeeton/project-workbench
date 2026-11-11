namespace Workbench.Service;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            var userRoot = Argument(args, "--user-root") ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ProjectWorkbench-v2-preview");
            var printToken = args.Contains("--print-token", StringComparer.Ordinal);
            var host = await WorkbenchServiceHost.StartAsync(new WorkbenchServiceOptions
            {
                UserRoot = userRoot,
                V1PluginsRoot = Argument(args, "--v1-plugins") ?? Path.Combine(userRoot, "plugins-v1"),
                V2PluginsRoot = Argument(args, "--v2-plugins") ?? Path.Combine(AppContext.BaseDirectory, "preview-plugins"),
                UiRoot = Argument(args, "--ui-dist") ?? FindUiRoot(),
                AllowLan = args.Contains("--allow-lan", StringComparer.Ordinal)
            });
            Console.WriteLine("listening: " + host.BaseUrl);
            if (printToken) Console.WriteLine("session-token: " + host.SessionToken);
            Console.Out.Flush();
            using var cancellation = new CancellationTokenSource();
            Console.CancelKeyPress += (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                cancellation.Cancel();
            };
            try { await host.WaitForShutdownAsync(cancellation.Token); }
            catch (OperationCanceledException) { }
            await host.DisposeAsync();
            return 0;
        }
        catch (Exception)
        {
            Console.Error.WriteLine("service-start-failed");
            return 1;
        }
    }

    private static string? Argument(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static string? FindUiRoot()
    {
        var beside = Path.Combine(AppContext.BaseDirectory, "ui");
        if (File.Exists(Path.Combine(beside, "index.html"))) return beside;
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        for (var depth = 0; depth < 8 && current is not null; depth++, current = current.Parent)
        {
            var dist = Path.Combine(current.FullName, "web", "host", "dist");
            if (File.Exists(Path.Combine(dist, "index.html"))) return dist;
        }
        return null;
    }
}
