namespace Workbench.Service;

public static class Program
{
    public static Task<int> Main(string[] args) => RunAsync(args, CancellationToken.None);

    internal static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        using var shutdown = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            shutdown.Cancel();
        };
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
            }, shutdown.Token);
            await Console.Out.WriteLineAsync("listening: " + host.BaseUrl);
            if (printToken) await Console.Out.WriteLineAsync("session-token: " + host.SessionToken);
            await Console.Out.FlushAsync(shutdown.Token);
            try { await host.WaitForShutdownAsync(shutdown.Token); }
            catch (OperationCanceledException) { }
            await host.DisposeAsync();
            return 0;
        }
        catch (Exception)
        {
            await Console.Error.WriteLineAsync("service-start-failed");
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
