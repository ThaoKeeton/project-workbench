using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using Workbench.Core;
using Xunit;

namespace Workbench.Core.Tests;

public sealed class BusinessPackageIntegrationTests
{
    [Theory]
    [InlineData("dingtalk", "com.projectworkbench.dingtalk-insights", 8)]
    [InlineData("upcam", "com.upcam.task-digest", 714)]
    [InlineData("upcam-acceptance", "com.upcam.task-digest.acceptance", 714)]
    public async Task Real_independent_package_installs_byte_exact_and_stays_untrusted_without_executing_code(string vendor, string id, int files)
    {
        using var disk = new TestDirectory();
        var options = new PluginManagerOptions { PluginsRoot = disk.Plugins, UserRoot = disk.User };
        var store = new PluginPackageStore(options);
        var input = Input(vendor);
        using var provenance = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(Path.GetDirectoryName(input)!, "provenance.json")));
        Assert.Equal(provenance.RootElement.GetProperty("sha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(input))));
        var installed = await store.InstallAsync(input);
        Assert.True(installed.Succeeded, installed.Message);
        await using var manager = new PluginManager(options);
        var plugin = Assert.Single(await manager.DiscoverAsync());
        Assert.Equal(id, plugin.Manifest!.Id);
        Assert.Equal(PluginState.Disabled, plugin.State);
        Assert.False(plugin.IsLoaded);
        Assert.False(plugin.IsTrusted);
        Assert.False(plugin.DesiredEnabled);
        Assert.Equal(files, Directory.EnumerateFiles(plugin.PackagePath, "*", SearchOption.AllDirectories).Count());
        foreach (var file in provenance.RootElement.GetProperty("files").EnumerateArray())
        {
            var path = Path.Combine(plugin.PackagePath, file.GetProperty("path").GetString()!);
            Assert.Equal(file.GetProperty("bytes").GetInt64(), new FileInfo(path).Length);
            Assert.Equal(file.GetProperty("sha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(path))));
        }
        Assert.False((await manager.EnableAsync(id)).Succeeded);
        Assert.Empty(await manager.RestoreEnabledAsync());
        Assert.Empty(manager.Contributions.Views);
        Assert.Empty(manager.GetBackgroundTaskSnapshots(id));
        Assert.False((await store.InstallAsync(input)).Succeeded);
        Assert.DoesNotContain(AppDomain.CurrentDomain.GetAssemblies(), assembly => assembly.GetName().Name is "DingTalkInsights.Workbench" or "UPCAM.Workbench");
    }

    private static string Input(string vendor, [CallerFilePath] string source = "") => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, "..", "integration", vendor, "package.zip"));
}
