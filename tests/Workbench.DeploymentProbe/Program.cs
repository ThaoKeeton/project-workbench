using System.Text.Json;
using Workbench.Core;
using Workbench.TestDependency;

if (args.Length != 3)
    throw new ArgumentException("Expected plugins root, user root and plugin ID.");

var hostMessage = DependencyProbe.GetMessage();
await using var manager = new PluginManager(new PluginManagerOptions
{
    PluginsRoot = args[0],
    UserRoot = args[1]
});
await manager.DiscoverAsync();
var enabled = await manager.EnableAsync(args[2], confirmTrust: true);
var ping = enabled.Succeeded ? await manager.ExecuteCommandAsync(args[2], "ping") : null;
var dependencyContext = enabled.Succeeded ? await manager.ExecuteCommandAsync(args[2], "dependency-context") : null;
await manager.ShutdownAsync();
Console.WriteLine(JsonSerializer.Serialize(new
{
    HostMessage = hostMessage,
    HostDependencyVersion = typeof(DependencyProbe).Assembly.GetName().Version!.ToString(),
    CoreLibraryDirectory = Path.GetDirectoryName(typeof(object).Assembly.Location),
    HostDirectory = AppContext.BaseDirectory,
    Enabled = enabled.Succeeded,
    enabled.Message,
    Ping = ping?.Message,
    DependencyContext = dependencyContext?.Message
}));
