using System.Runtime.InteropServices;
using Workbench.Core;
using Xunit;

namespace Workbench.Core.Tests;

public sealed class DiscoveryTests
{
    // These fail if scanning activates code, ignores a good package after a bad one,
    // or silently accepts ambiguous IDs/paths/compatibility.
    [Fact]
    public async Task Discovers_metadata_without_executing_the_real_plugin_assembly()
    {
        using var disk = new TestDirectory();
        disk.Package("com.example.fixture");
        using var marker = disk.WatchActivation();
        await using var manager = disk.Manager();

        var packages = await manager.DiscoverAsync();

        var package = Assert.Single(packages);
        Assert.Equal("com.example.fixture", package.Manifest?.Id);
        Assert.Equal(PluginState.Disabled, package.State);
        Assert.False(package.IsTrusted);
        Assert.False(package.IsLoaded);
        Assert.False(File.Exists(marker.Path));
        Assert.Empty(manager.Contributions.Commands);
    }

    [Fact]
    public async Task Bad_packages_have_diagnostics_without_blocking_a_valid_package()
    {
        using var disk = new TestDirectory();
        var malformed = disk.Package("com.example.malformed", folder: "a-malformed");
        await File.WriteAllTextAsync(Path.Combine(malformed, "plugin.json"), "{ nope");
        disk.Package("com.example.api", folder: "b-api", fields: new() { ["apiVersion"] = 99 });
        disk.Package("com.example.host", folder: "c-host", fields: new() { ["minimumHostVersion"] = "9.0.0" });
        var missing = disk.Package("com.example.missing", folder: "d-missing");
        File.Delete(Path.Combine(missing, "Workbench.TestPlugin.dll"));
        disk.Package("com.example.valid", folder: "z-valid");
        await using var manager = disk.Manager();

        var packages = await manager.DiscoverAsync();

        Assert.Equal(new[] { "a-malformed", "b-api", "c-host", "d-missing", "z-valid" },
            packages.Select(p => Path.GetFileName(p.PackagePath)));
        Assert.Contains(packages[0].Diagnostics, d => d.Code == PluginDiagnosticCode.InvalidManifest);
        Assert.Contains(packages[1].Diagnostics, d => d.Code == PluginDiagnosticCode.IncompatibleApi);
        Assert.Contains(packages[2].Diagnostics, d => d.Code == PluginDiagnosticCode.IncompatibleHost);
        Assert.Contains(packages[3].Diagnostics, d => d.Code == PluginDiagnosticCode.MissingAssembly);
        Assert.Equal(PluginState.Disabled, packages[4].State);
    }

    [Fact]
    public async Task Every_duplicate_id_is_rejected_instead_of_selecting_a_winner()
    {
        using var disk = new TestDirectory();
        disk.Package("com.example.duplicate", folder: "first");
        disk.Package("com.example.duplicate", folder: "second");
        disk.Package("com.example.valid");
        await using var manager = disk.Manager();

        var packages = await manager.DiscoverAsync();

        var duplicates = packages.Where(p => p.Manifest?.Id == "com.example.duplicate").ToArray();
        Assert.Equal(2, duplicates.Length);
        Assert.All(duplicates, p => Assert.Equal(PluginState.Invalid, p.State));
        Assert.All(duplicates, p => Assert.Contains(p.Diagnostics, d => d.Code == PluginDiagnosticCode.DuplicatePluginId));
        Assert.Equal(PluginState.Disabled, Assert.Single(packages, p => p.Manifest?.Id == "com.example.valid").State);
    }

    [Theory]
    [InlineData("id", "../escape")]
    [InlineData("id", "plain")]
    [InlineData("id", "com.Example.plugin")]
    [InlineData("id", "com.example..plugin")]
    [InlineData("id", "com.example.plugin\n")]
    [InlineData("version", "1.0.0\n")]
    [InlineData("minimumHostVersion", "0.1.0\n")]
    [InlineData("entryAssembly", "../outside.dll")]
    [InlineData("entryAssembly", "..\\outside.dll")]
    [InlineData("entryAssembly", "/tmp/outside.dll")]
    [InlineData("entryAssembly", "C:\\outside.dll")]
    [InlineData("entryAssembly", "Workbench.TestPlugin.exe")]
    [InlineData("version", "nonsense")]
    [InlineData("minimumHostVersion", "nonsense")]
    [InlineData("entryType", "")]
    public async Task Rejects_unsafe_or_invalid_manifest_fields(string field, string value)
    {
        using var disk = new TestDirectory();
        disk.Package("com.example.fixture", fields: new() { [field] = value });
        await using var manager = disk.Manager();

        var package = Assert.Single(await manager.DiscoverAsync());

        Assert.Equal(PluginState.Invalid, package.State);
        Assert.NotEmpty(package.Diagnostics);
    }

    [Fact]
    public async Task Rejects_symbolic_links_even_when_the_entry_is_otherwise_valid()
    {
        using var disk = new TestDirectory();
        var package = disk.Package("com.example.fixture");
        var outside = Path.Combine(disk.Root, "outside.txt");
        await File.WriteAllTextAsync(outside, "outside");
        File.CreateSymbolicLink(Path.Combine(package, "linked.txt"), outside);
        await using var manager = disk.Manager();

        var found = Assert.Single(await manager.DiscoverAsync());

        Assert.Equal(PluginState.Invalid, found.State);
        Assert.Contains(found.Diagnostics, d => d.Code == PluginDiagnosticCode.InvalidPackage);
    }

    [Fact]
    public async Task Bundled_and_user_packages_share_one_duplicate_id_check()
    {
        using var disk = new TestDirectory();
        disk.Package("com.example.duplicate");
        var bundled = disk.Package("com.example.duplicate", external: true);
        await using var manager = disk.Manager(Path.GetDirectoryName(bundled)!);

        var packages = await manager.DiscoverAsync();

        Assert.Equal(2, packages.Count);
        Assert.All(packages, p => Assert.Equal(PluginState.Invalid, p.State));
        Assert.All(packages, p => Assert.Contains(p.Diagnostics, d => d.Code == PluginDiagnosticCode.DuplicatePluginId));
    }

    [Fact]
    public async Task Rejects_a_named_pipe_without_opening_it_for_reading()
    {
        if (!OperatingSystem.IsLinux())
            return;
        using var disk = new TestDirectory();
        var package = disk.Package("com.example.fixture");
        var pipe = Path.Combine(package, "z-pipe");
        Assert.Equal(0, MkFifo(pipe, 384));
        await using var manager = disk.Manager();

        var discovery = Task.Run(() => manager.DiscoverAsync());
        var blocked = await Task.WhenAny(discovery, Task.Delay(TimeSpan.FromSeconds(1))) != discovery;
        if (blocked)
        {
            // Unblock a buggy reader so this regression test can fail cleanly.
            using var writer = new FileStream(pipe, FileMode.Open, FileAccess.Write);
        }
        var found = Assert.Single(await discovery.WaitAsync(TimeSpan.FromSeconds(5)));

        Assert.False(blocked, "Discovery tried to read a named pipe instead of rejecting a non-regular file.");
        Assert.Equal(PluginState.Invalid, found.State);
        Assert.Contains(found.Diagnostics, d => d.Code == PluginDiagnosticCode.InvalidPackage);
    }

    [DllImport("libc", EntryPoint = "mkfifo", SetLastError = true)]
    private static extern int MkFifo(string path, uint mode);

    [Theory]
    [InlineData("CON.dll")]
    [InlineData("lib/NUL.dll")]
    [InlineData("COM1.dll")]
    [InlineData("AUX.dll")]
    public async Task Reserved_Windows_device_names_are_rejected_before_file_access(string entry)
    {
        using var disk = new TestDirectory();
        disk.Package("com.example.fixture", fields: new() { ["entryAssembly"] = entry });
        await using var manager = disk.Manager();

        var found = Assert.Single(await manager.DiscoverAsync());

        Assert.Contains(found.Diagnostics, d => d.Code == PluginDiagnosticCode.InvalidManifest);
    }
}
