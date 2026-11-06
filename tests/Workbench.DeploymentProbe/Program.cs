using System.Text.Json;
using Workbench.Core;
using Workbench.TestDependency;

if (args.Length is not (3 or 4))
    throw new ArgumentException("Expected plugins root, user root, plugin ID and optional probe operation.");

if (args.Length == 4 && args[3].StartsWith("host-runtime-", StringComparison.Ordinal))
{
    await HostRuntimeRestartProbe.RunAsync(args);
    return;
}

if (args.Length == 4 && args[3] == "package-apply")
{
    var options = new PluginManagerOptions { PluginsRoot = args[0], UserRoot = args[1] };
    var packages = new PluginPackageStore(options);
    var results = await packages.ApplyPendingAsync();
    await using var restarted = new PluginManager(options);
    var snapshot = (await restarted.DiscoverAsync()).Single(plugin => plugin.Manifest?.Id == args[2]);
    await restarted.RestoreEnabledAsync();
    var restored = restarted.Plugins.Single(plugin => plugin.Manifest?.Id == args[2]);
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        Applied = results.Count == 1 && results[0].Succeeded,
        Trusted = snapshot.IsTrusted,
        snapshot.DesiredEnabled,
        Loaded = restored.IsLoaded,
        Version = snapshot.Manifest!.Version
    }));
    return;
}

var hostMessage = DependencyProbe.GetMessage();
await using var manager = new PluginManager(new PluginManagerOptions
{
    PluginsRoot = args[0],
    UserRoot = args[1]
});
await manager.DiscoverAsync();
var enabled = await manager.EnableAsync(args[2], confirmTrust: true);
if (args.Length == 4)
{
    if (!enabled.Succeeded) throw new InvalidOperationException("SyntheticSecretActivationFailed");
    const string synthetic = "synthetic-dpapi-restart-value-only";
    var supported = manager.IsSecretStorageSupported(args[2]);
    var saved = false;
    var verified = false;
    var missingAfterDelete = false;
    switch (args[3])
    {
        case "secret-write":
            await manager.WriteSecretAsync(args[2], "openai.api-key", synthetic);
            saved = true;
            break;
        case "secret-read-delete":
            verified = await manager.ReadSecretAsync(args[2], "openai.api-key") == synthetic;
            await manager.DeleteSecretAsync(args[2], "openai.api-key");
            missingAfterDelete = await manager.ReadSecretAsync(args[2], "openai.api-key") is null;
            break;
        default: throw new ArgumentException("Unknown synthetic secret probe operation.");
    }
    await manager.ShutdownAsync();
    Console.WriteLine(JsonSerializer.Serialize(new { Supported = supported, Saved = saved, Verified = verified, MissingAfterDelete = missingAfterDelete }));
    return;
}
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
