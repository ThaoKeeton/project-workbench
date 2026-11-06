using Workbench.Core;
using Xunit;

namespace Workbench.Core.Tests;

public sealed class DataDirectoryTests
{
    [Fact]
    public async Task Activated_plugins_write_to_distinct_directories_inside_the_supplied_user_root()
    {
        using var disk = new TestDirectory();
        disk.Package("com.example.first");
        disk.Package("com.example.second");
        await using var manager = disk.Manager();
        await manager.DiscoverAsync();
        Assert.False(Directory.Exists(Path.Combine(disk.User, "data")));

        foreach (var id in new[] { "com.example.first", "com.example.second" })
        {
            Assert.True((await manager.EnableAsync(id, confirmTrust: true)).Succeeded);
            var result = await manager.ExecuteCommandAsync(id, "write-data");
            Assert.True(result.Succeeded, result.Message);
            Assert.Equal(Path.Combine(disk.User, "data", id), result.Message);
            Assert.Equal(id, await File.ReadAllTextAsync(Path.Combine(result.Message, "fixture.txt")));
        }
        Assert.Equal(2, Directory.GetDirectories(Path.Combine(disk.User, "data")).Length);
    }

    [Fact]
    public async Task Activation_rejects_a_linked_data_directory_without_writing_outside_the_user_root()
    {
        using var disk = new TestDirectory();
        disk.Package("com.example.fixture");
        var outside = Path.Combine(disk.Root, "outside");
        Directory.CreateDirectory(outside);
        Directory.CreateDirectory(Path.Combine(disk.User, "data"));
        Directory.CreateSymbolicLink(Path.Combine(disk.User, "data", "com.example.fixture"), outside);
        await using var manager = disk.Manager();
        await manager.DiscoverAsync();

        var result = await manager.EnableAsync("com.example.fixture", confirmTrust: true);

        Assert.False(result.Succeeded);
        Assert.Empty(manager.Contributions.Commands);
        Assert.Empty(Directory.EnumerateFileSystemEntries(outside));
        Assert.Contains(Assert.Single(manager.Plugins).Diagnostics,
            d => d.Code is PluginDiagnosticCode.LoadFailed or PluginDiagnosticCode.InitializationFailed);
    }
}
