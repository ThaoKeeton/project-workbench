using System.Net.Http.Headers;
using System.Text.Json;
using Workbench.Service;

namespace Workbench.Shell.Web;

public sealed record ShellEndpoint(string BaseUrl, string SessionToken, IAsyncDisposable? OwnedService);

/// <summary>Starts a loopback service, or attaches when a healthy preview session is already listening.</summary>
public static class ShellLaunch
{
    public static ShellArguments FromArguments(string[] args) => new(
        Argument(args, "--user-root") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ProjectWorkbench-v2-preview"),
        Argument(args, "--v1-plugins"),
        Argument(args, "--v2-plugins"),
        Argument(args, "--ui-dist"),
        args.Contains("--attach", StringComparer.Ordinal));

    public static async Task<ShellEndpoint> StartOrAttachAsync(ShellArguments arguments, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(arguments.UserRoot);
        var sessionPath = Path.Combine(arguments.UserRoot, "v2-session.json");
        if (arguments.Attach && File.Exists(sessionPath))
        {
            var attached = await TryAttachAsync(sessionPath, cancellationToken);
            if (attached is not null) return attached;
        }

        var host = await WorkbenchServiceHost.StartAsync(new WorkbenchServiceOptions
        {
            UserRoot = arguments.UserRoot,
            V1PluginsRoot = arguments.V1PluginsRoot ?? Path.Combine(arguments.UserRoot, "plugins-v1"),
            V2PluginsRoot = arguments.V2PluginsRoot ?? Path.Combine(AppContext.BaseDirectory, "preview-plugins"),
            UiRoot = arguments.UiRoot ?? FindUiRoot()
        }, cancellationToken);
        var document = JsonSerializer.Serialize(new
        {
            schema = "workbench.v2-session.preview",
            schemaVersion = 2,
            baseUrl = host.BaseUrl,
            token = host.SessionToken
        });
        await File.WriteAllTextAsync(sessionPath, document, cancellationToken);
        return new ShellEndpoint(host.BaseUrl, host.SessionToken, host);
    }

    private static async Task<ShellEndpoint?> TryAttachAsync(string sessionPath, CancellationToken cancellationToken)
    {
        try
        {
            using var document = JsonDocument.Parse(await File.ReadAllBytesAsync(sessionPath, cancellationToken));
            var root = document.RootElement;
            if (root.GetProperty("schemaVersion").GetInt32() != 2) return null;
            var baseUrl = root.GetProperty("baseUrl").GetString();
            var token = root.GetProperty("token").GetString();
            if (baseUrl is null || token is null || !baseUrl.StartsWith("http://127.0.0.1:", StringComparison.Ordinal) || !SessionTokens.IsAcceptable(token))
                return null;
            using var client = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(2) };
            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v2/health");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode) return null;
            return new ShellEndpoint(baseUrl, token, OwnedService: null);
        }
        catch (Exception)
        {
            return null;
        }
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

    private static string? Argument(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}

public sealed record ShellArguments(string UserRoot, string? V1PluginsRoot, string? V2PluginsRoot, string? UiRoot, bool Attach);
