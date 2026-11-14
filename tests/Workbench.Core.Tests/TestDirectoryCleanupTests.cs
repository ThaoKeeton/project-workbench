using Xunit;

namespace Workbench.Core.Tests;

public sealed class TestDirectoryCleanupTests
{
    [Fact]
    public async Task Cleanup_unlinks_a_replaced_root_without_deleting_its_target()
    {
        var outside = Path.Combine(Path.GetTempPath(), $"workbench-cleanup-target-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outside);
        var sentinel = Path.Combine(outside, "preserved.txt");
        await File.WriteAllTextAsync(sentinel, "preserve root target");
        var disk = new TestDirectory();
        Directory.Delete(disk.Root, recursive: true);
        Directory.CreateSymbolicLink(disk.Root, outside);
        try
        {
            disk.Dispose();

            Assert.False(Directory.Exists(disk.Root));
            Assert.True(File.Exists(sentinel), "Cleanup must not recurse through a linked root.");
            Assert.Equal("preserve root target", await File.ReadAllTextAsync(sentinel));
        }
        finally
        {
            if (Directory.Exists(disk.Root)) Directory.Delete(disk.Root);
            Directory.Delete(outside, recursive: true);
        }
    }

    [Fact]
    public async Task Cleanup_removes_owned_links_without_following_them_outside_the_test_root()
    {
        var outside = Path.Combine(Path.GetTempPath(), $"workbench-cleanup-outside-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outside);
        var sentinel = Path.Combine(outside, "preserved.txt");
        await File.WriteAllTextAsync(sentinel, "outside the owned tree");
        var disk = new TestDirectory();
        try
        {
            Directory.CreateSymbolicLink(Path.Combine(disk.Plugins, "directory-link"), outside);
            File.CreateSymbolicLink(Path.Combine(disk.Root, "file-link"), sentinel);

            disk.Dispose();

            Assert.False(Directory.Exists(disk.Root));
            Assert.Equal("outside the owned tree", await File.ReadAllTextAsync(sentinel));
        }
        finally
        {
            if (Directory.Exists(disk.Root)) disk.Dispose();
            Directory.Delete(outside, recursive: true);
        }
    }

    [Fact]
    public async Task Cleanup_after_activation_retains_only_owned_mapped_fixture_files_when_required_by_Windows()
    {
        var disk = new TestDirectory();
        disk.Package("com.example.fixture");
        await using (var manager = disk.Manager())
        {
            await manager.DiscoverAsync();
            var result = await manager.EnableAsync("com.example.fixture", confirmTrust: true);
            Assert.True(result.Succeeded, result.Message);
        }

        disk.Dispose();

        if (!Directory.Exists(disk.Root)) return; // Unix permits unlinking mapped files.
        Assert.True(OperatingSystem.IsWindows());
        var remaining = Directory.EnumerateFiles(disk.Root, "*", SearchOption.AllDirectories).ToArray();
        Assert.NotEmpty(remaining);
        Assert.All(remaining, path => Assert.Contains(Path.GetFileName(path),
            new[] { "Workbench.TestPlugin.dll", "Workbench.TestDependency.dll" }));
    }
}
