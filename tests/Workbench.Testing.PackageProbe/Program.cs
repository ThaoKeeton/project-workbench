using System.IO.Compression;
using System.Security.Cryptography;
using Workbench.Core;

if (args.Length != 1) throw new ArgumentException("Pass the actual testing delivery ZIP.");
var zipPath = Path.GetFullPath(args[0]);
var root = Path.Combine(Path.GetTempPath(), "ProjectWorkbench-testing-package-install", Guid.NewGuid().ToString("N"));
try
{
    var packages = new PluginPackageStore(new() { UserRoot = root, PluginsRoot = Path.Combine(root, "Plugins") });
    var installed = await packages.InstallAsync(zipPath);
    if (!installed.Succeeded) throw new InvalidOperationException("Actual testing ZIP installation failed.");
    var destination = Path.Combine(root, "Plugins", "com.projectworkbench.testing");
    using var archive = ZipFile.OpenRead(zipPath);
    foreach (var entry in archive.Entries)
    {
        using var source = entry.Open();
        var expected = SHA256.HashData(source);
        var actual = await File.ReadAllBytesAsync(Path.Combine(destination, entry.FullName));
        if (!expected.SequenceEqual(SHA256.HashData(actual))) throw new InvalidDataException("Installed payload differs from actual ZIP.");
    }
    await using var manager = new PluginManager(new() { UserRoot = root, PluginsRoot = Path.Combine(root, "Plugins") });
    await manager.DiscoverAsync();
    var plugin = manager.Plugins.Single();
    if (plugin.Manifest is not { Id: "com.projectworkbench.testing", Version: "0.1.0", MinimumHostVersion: "0.4.0" }
        || plugin.State != PluginState.Disabled || plugin.IsLoaded || plugin.IsTrusted || plugin.DesiredEnabled)
        throw new InvalidOperationException("Discovery executed/trusted the plugin or changed manifest identity.");
    if ((await packages.InstallAsync(zipPath)).Succeeded) throw new InvalidOperationException("Duplicate install overwrote the real package.");
    Console.WriteLine($"Actual six-file testing ZIP installed byte-identically; disabled/untrusted discovery and duplicate refusal passed. SHA256={Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(zipPath))).ToLowerInvariant()}");
}
finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
