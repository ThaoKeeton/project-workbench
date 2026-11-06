using Workbench.Core;
using Xunit;

namespace Workbench.Core.Tests;

public sealed class InstallationTests
{
    // These fail if installation executes code, replaces an ID, follows links,
    // or leaves a partially copied package visible after cancellation.
    [Fact]
    public async Task Installs_a_contained_copy_without_trusting_or_executing_it()
    {
        using var disk = new TestDirectory();
        var source = disk.Package("com.example.install", external: true);
        Directory.CreateDirectory(Path.Combine(source, "assets"));
        await File.WriteAllTextAsync(Path.Combine(source, "assets", "sample.txt"), "sample data");
        using var marker = disk.WatchActivation();
        await using var manager = disk.Manager();

        var result = await manager.InstallAsync(source);

        Assert.True(result.Succeeded, result.Message);
        var package = Assert.Single(await manager.DiscoverAsync());
        Assert.Equal(Path.Combine(disk.Plugins, "com.example.install"), package.PackagePath);
        Assert.Equal("sample data", await File.ReadAllTextAsync(Path.Combine(package.PackagePath, "assets", "sample.txt")));
        Assert.Equal(PluginState.Disabled, package.State);
        Assert.False(package.IsTrusted);
        Assert.False(File.Exists(marker.Path));
    }

    [Fact]
    public async Task Rejects_an_existing_id_even_if_it_uses_a_different_directory_name()
    {
        using var disk = new TestDirectory();
        disk.Package("com.example.duplicate", folder: "unrelated-folder");
        var source = disk.Package("com.example.duplicate", external: true);
        await using var manager = disk.Manager();

        var result = await manager.InstallAsync(source);

        Assert.False(result.Succeeded);
        Assert.Single(Directory.EnumerateDirectories(disk.Plugins));
        Assert.False(Directory.Exists(Path.Combine(disk.Plugins, "com.example.duplicate")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Rejects_linked_files_and_directories_without_creating_a_target(bool directoryLink)
    {
        using var disk = new TestDirectory();
        var source = disk.Package("com.example.install", external: true);
        var outside = Path.Combine(disk.Root, "outside");
        Directory.CreateDirectory(outside);
        await File.WriteAllTextAsync(Path.Combine(outside, "private.txt"), "outside package");
        if (directoryLink)
            Directory.CreateSymbolicLink(Path.Combine(source, "linked"), outside);
        else
            File.CreateSymbolicLink(Path.Combine(source, "linked.txt"), Path.Combine(outside, "private.txt"));
        await using var manager = disk.Manager();

        var result = await manager.InstallAsync(source);

        Assert.False(result.Succeeded);
        Assert.Empty(Directory.EnumerateFileSystemEntries(disk.Plugins));
        Assert.Equal("outside package", await File.ReadAllTextAsync(Path.Combine(outside, "private.txt")));
    }

    [Fact]
    public async Task Rejects_traversal_in_an_install_manifest()
    {
        using var disk = new TestDirectory();
        var source = disk.Package("com.example.install", external: true,
            fields: new() { ["entryAssembly"] = "../outside.dll" });
        await using var manager = disk.Manager();

        Assert.False((await manager.InstallAsync(source)).Succeeded);
        Assert.Empty(Directory.EnumerateFileSystemEntries(disk.Plugins));
    }

    [Fact]
    public async Task Cancellation_after_copy_starts_cleans_the_staging_directory()
    {
        using var disk = new TestDirectory();
        var source = disk.Package("com.example.install", external: true);
        await using (var large = File.Create(Path.Combine(source, "large.bin")))
            large.SetLength(128 * 1024 * 1024);
        await using var manager = disk.Manager();
        using var cancel = new CancellationTokenSource();

        var installation = manager.InstallAsync(source, cancel.Token);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while (!Directory.EnumerateDirectories(disk.Plugins, ".install-*").Any() && !installation.IsCompleted)
            await Task.Delay(1, deadline.Token);
        Assert.False(installation.IsCompleted, "The test must cancel a real copy after staging begins.");
        cancel.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => installation);
        Assert.Empty(Directory.EnumerateFileSystemEntries(disk.Plugins));
    }

    [Fact]
    public async Task Loaded_ids_stay_reserved_if_their_manifest_disappears_from_disk()
    {
        using var disk = new TestDirectory();
        var installed = disk.Package("com.example.fixture", folder: "old-folder");
        var source = disk.Package("com.example.fixture", external: true);
        await using var manager = disk.Manager();
        await manager.DiscoverAsync();
        Assert.True((await manager.EnableAsync("com.example.fixture", confirmTrust: true)).Succeeded);
        File.Move(Path.Combine(installed, "plugin.json"), Path.Combine(installed, "manifest.removed"));

        var result = await manager.InstallAsync(source);

        Assert.False(result.Succeeded);
        Assert.False(Directory.Exists(Path.Combine(disk.Plugins, "com.example.fixture")));
    }
}
