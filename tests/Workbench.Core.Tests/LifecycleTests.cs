using System.Runtime.Loader;
using Workbench.Core;
using Workbench.Sdk;
using Workbench.TestDependency;
using Xunit;

namespace Workbench.Core.Tests;

public sealed class LifecycleTests
{
    private const string Id = "com.example.fixture";

    [Fact]
    public async Task First_enable_requires_explicit_trust_before_any_plugin_code_runs()
    {
        using var disk = new TestDirectory();
        disk.Package(Id);
        using var marker = disk.WatchActivation();
        await using var manager = disk.Manager();
        await manager.DiscoverAsync();

        Assert.False((await manager.EnableAsync(Id)).Succeeded);
        Assert.False(File.Exists(marker.Path));
        Assert.False(Assert.Single(manager.Plugins).IsLoaded);
        Assert.Contains(Assert.Single(manager.Plugins).Diagnostics, d => d.Code == PluginDiagnosticCode.Untrusted);

        var enabled = await manager.EnableAsync(Id, confirmTrust: true);

        Assert.True(enabled.Succeeded, enabled.Message);
        Assert.True(File.Exists(marker.Path));
        var status = Assert.Single(manager.Plugins);
        Assert.Equal(PluginState.Enabled, status.State);
        Assert.True(status.IsTrusted);
        Assert.True(status.DesiredEnabled);
        Assert.True(status.IsLoaded);
    }

    [Fact]
    public async Task Dynamically_loaded_plugin_shares_sdk_types_and_resolves_its_real_dependency()
    {
        using var disk = new TestDirectory();
        disk.Package(Id);
        await using var manager = disk.Manager();
        await manager.DiscoverAsync();
        Assert.True((await manager.EnableAsync(Id, confirmTrust: true)).Succeeded);

        var view = Assert.Single(manager.Contributions.Views).View;
        Assert.IsAssignableFrom<IPluginView>(view);
        Assert.Equal("overview", view.Id);
        var context = AssemblyLoadContext.GetLoadContext(view.GetType().Assembly)!;
        Assert.NotSame(AssemblyLoadContext.Default, context);
        Assert.False(context.IsCollectible);
        Assert.Single(manager.Contributions.Settings);
        Assert.Contains(manager.Contributions.Commands, c => c.PluginId == Id && c.Id == "ping");
        var result = await manager.ExecuteCommandAsync(Id, "ping");
        Assert.True(result.Succeeded, result.Message);
        Assert.Equal("com.example.fixture:dependency-ok:demo draft", result.Message);
        Assert.NotEqual("Default", (await manager.ExecuteCommandAsync(Id, "dependency-context")).Message);
    }

    [Theory]
    [InlineData(@"C:\packages\fixture", @"C:\packages\fixture\private.dll", @"C:\packages\fixture")]
    [InlineData(@"C:\packages\fixture", @"\\?\C:\packages\fixture\private.dll", @"\\?\C:\packages\fixture")]
    [InlineData(@"C:\packages\fixture", @"\\?\c:\PACKAGES\FIXTURE\private.dll", @"\\?\C:\packages\fixture")]
    [InlineData(@"C:\packages\fixture\", @"\\?\C:\packages\fixture\private.dll", @"\\?\C:\packages\fixture\")]
    [InlineData(@"\\?\C:\packages\fixture", @"\\?\C:\packages\fixture\private.dll", @"\\?\C:\packages\fixture")]
    [InlineData(@"\\server\share\fixture", @"\\?\UNC\server\share\fixture\private.dll", @"\\?\UNC\server\share\fixture")]
    [InlineData(@"\\?\UNC\server\share\fixture", @"\\?\UNC\server\share\fixture\private.dll", @"\\?\UNC\server\share\fixture")]
    [InlineData("/packages/fixture", "/packages/fixture/private.dll", "/packages/fixture")]
    public void Resolver_comparison_root_aligns_only_canonical_extended_paths(string root, string resolved, string expected)
        => Assert.Equal(expected, PluginLoadContext.ResolverComparisonRoot(root, resolved));

    [Theory]
    [InlineData(@"C:\packages\fixture", @"\\?\C:\packages\sibling\private.dll")]
    [InlineData(@"C:\packages\fixture", @"\\?\C:\packages\fixture-other\private.dll")]
    [InlineData(@"C:\packages\fixture", @"\\?\D:\packages\fixture\private.dll")]
    [InlineData(@"C:\packages\fixture", @"\\?\C:\packages\fixture\..\sibling\private.dll")]
    [InlineData(@"C:\packages\fixture", @"\\?\C:\packages\fixture\.\private.dll")]
    [InlineData(@"C:\packages\fixture", @"\\?\C:\packages\fixture\\private.dll")]
    [InlineData(@"C:\packages\fixture", @"\\?\C:\packages\fixture\sub/private.dll")]
    [InlineData(@"C:\packages\fixture", @"\\?\C:\packages\fixture\private.dll.")]
    [InlineData(@"C:\packages\fixture", @"\\?\C:\packages\fixture\private.dll ")]
    [InlineData(@"C:\packages\fixture", @"\\?\C:\packages\fixture\private.dll:stream")]
    [InlineData(@"C:\packages\fixture", @"\\?\C:\packages\fixture\NUL.dll")]
    [InlineData(@"C:\packages\fixture", @"\\?\C:\packages\fixture\COM1.dll")]
    [InlineData(@"C:\packages\fixture", @"\\?\C:\packages\fixture\LPT¹.dll")]
    [InlineData(@"C:\packages\fixture", @"\\?\GLOBALROOT\Device\HarddiskVolume1\private.dll")]
    [InlineData(@"C:\packages\fixture", @"\\?\Volume{123}\private.dll")]
    [InlineData(@"C:\packages\fixture", @"\\?\C:packages\fixture\private.dll")]
    [InlineData(@"C:\packages\fixture", @"\\?\UNC\server\share\fixture\private.dll")]
    [InlineData(@"\\server\share\fixture", @"\\?\UNC\other\share\fixture\private.dll")]
    [InlineData(@"\\server\share\fixture", @"\\?\UNC\server\other\fixture\private.dll")]
    [InlineData(@"\\server\share\fixture", @"\\?\UNC\server\share\fixture-other\private.dll")]
    [InlineData(@"\\.\C:\packages\fixture", @"\\?\C:\packages\fixture\private.dll")]
    [InlineData(@"\??\C:\packages\fixture", @"\\?\C:\packages\fixture\private.dll")]
    [InlineData(@"\\?\GLOBALROOT\Device\HarddiskVolume1", @"\\?\GLOBALROOT\Device\HarddiskVolume1\private.dll")]
    [InlineData(@"C:/packages/fixture", @"\\?\C:\packages\fixture\private.dll")]
    [InlineData(@"C:\packages\fixture.", @"\\?\C:\packages\fixture.\private.dll")]
    [InlineData(@"\\server", @"\\?\UNC\server\private.dll")]
    public void Resolver_comparison_root_refuses_external_device_and_alias_paths(string root, string resolved)
        => Assert.Throws<IOException>(() => PluginLoadContext.ResolverComparisonRoot(root, resolved));

    [WindowsLoaderFact]
    public async Task Long_entry_paths_initialize_real_private_dependencies_across_the_Windows_prefix_boundary()
    {
        foreach (var length in new[] { 259, 261 })
        {
            using var disk = new TestDirectory();
            const string entry = "Workbench.TestPlugin.dll";
            var folder = new string('p', length - disk.Plugins.Length - entry.Length - 2);
            var package = disk.Package(Id, folder);
            Assert.Equal(length, Path.Combine(package, entry).Length);
            var resolver = new AssemblyDependencyResolver(Path.Combine(package, entry));
            var dependency = resolver.ResolveAssemblyToPath(new("Workbench.TestDependency"));
            Assert.NotNull(dependency);
            Assert.Equal(length >= 260, dependency.StartsWith(@"\\?\", StringComparison.Ordinal));
            await using var manager = disk.Manager();
            await manager.DiscoverAsync();
            var enabled = await manager.EnableAsync(Id, confirmTrust: true);
            Assert.True(enabled.Succeeded, enabled.Message);
            Assert.Equal("com.example.fixture:dependency-ok:demo draft", (await manager.ExecuteCommandAsync(Id, "ping")).Message);
            Assert.StartsWith("Workbench:", (await manager.ExecuteCommandAsync(Id, "dependency-context")).Message);
            Assert.True((await manager.DisableAsync(Id)).Succeeded);
        }
    }

    [WindowsLoaderFact]
    public async Task Extended_resolver_paths_keep_actual_containment_regular_file_and_reparse_checks()
    {
        using var disk = new TestDirectory();
        var package = disk.Package(Id);
        var validated = await PackageValidator.ValidateAsync(package, new Version(0, 1, 0), CancellationToken.None);
        Assert.True(validated.IsValid);
        var loader = new PluginLoadContext(validated, [], []);
        var dependency = Path.Combine(package, "Workbench.TestDependency.dll");
        Assert.Equal(dependency, loader.ValidateResolvedFile(@"\\?\" + dependency));
        var sibling = Path.Combine(disk.Plugins, "sibling", "private.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(sibling)!); File.WriteAllText(sibling, "external bytes");
        foreach (var refused in new[] { sibling, @"\\?\" + sibling, @"\\.\" + dependency, @"\??\" + dependency,
            @"\\?\" + Path.Combine(package, "..", "sibling", "private.dll"), @"\\?\" + dependency + ".", @"\\?\" + dependency + ":stream" })
            Assert.Throws<IOException>(() => loader.ValidateResolvedFile(refused));
        var directory = Path.Combine(package, "directory.dll"); Directory.CreateDirectory(directory);
        Assert.Throws<IOException>(() => loader.ValidateResolvedFile(@"\\?\" + directory));
        var linked = Path.Combine(package, "linked.dll"); File.CreateSymbolicLink(linked, sibling);
        Assert.Throws<IOException>(() => loader.ValidateResolvedFile(@"\\?\" + linked));
        var linkedDirectory = Path.Combine(package, "linked-directory"); Directory.CreateSymbolicLink(linkedDirectory, Path.GetDirectoryName(sibling)!);
        Assert.Throws<IOException>(() => loader.ValidateResolvedFile(@"\\?\" + Path.Combine(linkedDirectory, "private.dll")));
        Assert.Equal("external bytes", File.ReadAllText(sibling));
    }

    [Theory]
    [InlineData("init-fail")]
    [InlineData("duplicate-view")]
    [InlineData("duplicate-command")]
    [InlineData("duplicate-setting")]
    [InlineData("invalid-view")]
    public async Task Failed_initialization_rolls_back_every_contribution_and_stops_the_instance(string mode)
    {
        using var disk = new TestDirectory();
        disk.Package(Id);
        await using var manager = disk.Manager();
        await manager.DiscoverAsync();
        await manager.GetSettings(Id).SetAsync("mode", mode);

        var result = await manager.EnableAsync(Id, confirmTrust: true);

        Assert.False(result.Succeeded);
        Assert.Equal(PluginState.Failed, Assert.Single(manager.Plugins).State);
        Assert.Contains(Assert.Single(manager.Plugins).Diagnostics, d => d.Code == PluginDiagnosticCode.InitializationFailed);
        AssertEmpty(manager);
        Assert.False((await manager.ExecuteCommandAsync(Id, "ping")).Succeeded);
        Assert.True(await manager.GetSettings(Id).GetAsync("stopped", false));
        Assert.True(await manager.GetSettings(Id).GetAsync("stopLifetimeCanceled", false));
    }

    [Fact]
    public async Task Stop_failure_still_revokes_all_contributions_and_command_execution()
    {
        using var disk = new TestDirectory();
        disk.Package(Id);
        await using var manager = disk.Manager();
        await manager.DiscoverAsync();
        await manager.GetSettings(Id).SetAsync("mode", "stop-fail");
        Assert.True((await manager.EnableAsync(Id, confirmTrust: true)).Succeeded);

        var result = await manager.DisableAsync(Id);

        Assert.False(result.Succeeded);
        AssertEmpty(manager);
        Assert.False((await manager.ExecuteCommandAsync(Id, "ping")).Succeeded);
        var status = Assert.Single(manager.Plugins);
        Assert.Equal(PluginState.Failed, status.State);
        Assert.True(status.IsTrusted);
        Assert.True(status.IsLoaded);
        Assert.False(status.DesiredEnabled);
        Assert.Contains(status.Diagnostics, d => d.Code == PluginDiagnosticCode.StopFailed);
        Assert.True(await manager.GetSettings(Id).GetAsync("stopLifetimeCanceled", false));
    }

    [Fact]
    public async Task Disable_cancels_a_running_cooperative_command_without_waiting_on_its_lifecycle_gate()
    {
        using var disk = new TestDirectory();
        disk.Package(Id);
        await using var manager = disk.Manager();
        await manager.DiscoverAsync();
        Assert.True((await manager.EnableAsync(Id, confirmTrust: true)).Succeeded);
        var command = manager.ExecuteCommandAsync(Id, "wait");
        await WaitForSettingAsync(manager, "commandStarted");

        var result = await manager.DisableAsync(Id).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(result.Succeeded, result.Message);
        Assert.False((await command.WaitAsync(TimeSpan.FromSeconds(5))).Succeeded);
        Assert.True(await manager.GetSettings(Id).GetAsync("commandCanceled", false));
        AssertEmpty(manager);
    }

    [Fact]
    public async Task Canceled_initialization_rolls_back_and_does_not_save_an_enabled_state()
    {
        using var disk = new TestDirectory();
        disk.Package(Id);
        await using var manager = disk.Manager();
        await manager.DiscoverAsync();
        await manager.GetSettings(Id).SetAsync("mode", "init-cancel");
        using var cancel = new CancellationTokenSource();
        var enabling = manager.EnableAsync(Id, confirmTrust: true, cancel.Token);
        await WaitForSettingAsync(manager, "initializing");

        cancel.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => enabling);
        AssertEmpty(manager);
        Assert.False(Assert.Single(manager.Plugins).DesiredEnabled);
        Assert.True(await manager.GetSettings(Id).GetAsync("stopped", false));
    }

    [Fact]
    public async Task A_disabled_plugin_can_be_reenabled_without_loading_another_assembly()
    {
        using var disk = new TestDirectory();
        disk.Package(Id);
        await using var manager = disk.Manager();
        await manager.DiscoverAsync();
        Assert.True((await manager.EnableAsync(Id, confirmTrust: true)).Succeeded);
        var assembly = Assert.Single(manager.Contributions.Views).View.GetType().Assembly;

        Assert.True((await manager.DisableAsync(Id)).Succeeded);
        AssertEmpty(manager);
        Assert.Equal(PluginState.Disabled, Assert.Single(manager.Plugins).State);
        Assert.True((await manager.EnableAsync(Id)).Succeeded);
        Assert.Same(assembly, Assert.Single(manager.Contributions.Views).View.GetType().Assembly);
    }

    [Fact]
    public async Task Command_exceptions_are_visible_and_late_registration_is_rejected()
    {
        using var disk = new TestDirectory();
        disk.Package(Id);
        await using var manager = disk.Manager();
        await manager.DiscoverAsync();
        Assert.True((await manager.EnableAsync(Id, confirmTrust: true)).Succeeded);

        var thrown = await manager.ExecuteCommandAsync(Id, "throw");
        var late = await manager.ExecuteCommandAsync(Id, "late");

        Assert.False(thrown.Succeeded);
        Assert.Contains("CommandFailed", thrown.Message);
        Assert.DoesNotContain("fixture command failure", thrown.Message);
        Assert.False(late.Succeeded);
        Assert.Single(manager.Contributions.Views);
        Assert.Contains(Assert.Single(manager.Plugins).Diagnostics, d => d.Code == PluginDiagnosticCode.CommandFailed);
    }

    [Fact]
    public async Task Missing_plugin_dependency_does_not_fall_back_to_an_unshared_host_assembly()
    {
        using var disk = new TestDirectory();
        var package = disk.Package(Id);
        _ = DependencyProbe.GetMessage();
        File.Delete(Path.Combine(package, "Workbench.TestDependency.dll"));
        await using var manager = disk.Manager();
        await manager.DiscoverAsync();

        var result = await manager.EnableAsync(Id, confirmTrust: true);

        Assert.False(result.Succeeded);
        Assert.Contains("DependencyLoadFailed", result.Message);
        Assert.Equal(PluginState.Failed, Assert.Single(manager.Plugins).State);
        AssertEmpty(manager);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Host_designated_shared_assemblies_keep_the_default_context_identity(bool byName)
    {
        using var disk = new TestDirectory();
        disk.Package(Id);
        var dependency = typeof(DependencyProbe).Assembly;
        await using var manager = new PluginManager(new PluginManagerOptions
        {
            PluginsRoot = disk.Plugins,
            UserRoot = disk.User,
            SharedAssemblies = byName ? [] : [dependency],
            SharedAssemblyNames = byName ? [dependency.GetName().Name!] : []
        });
        await manager.DiscoverAsync();

        var enabled = await manager.EnableAsync(Id, confirmTrust: true);

        Assert.True(enabled.Succeeded, enabled.Message);
        Assert.Equal("Default", (await manager.ExecuteCommandAsync(Id, "dependency-context")).Message);
    }

    [Fact]
    public async Task Constructor_failure_leaves_a_loaded_but_inactive_package_with_a_diagnostic()
    {
        using var disk = new TestDirectory();
        disk.Package(Id, fields: new() { ["entryType"] = "Workbench.TestPlugin.ThrowingConstructorPlugin" });
        await using var manager = disk.Manager();
        await manager.DiscoverAsync();

        var result = await manager.EnableAsync(Id, confirmTrust: true);

        Assert.False(result.Succeeded);
        Assert.Contains("LoadFailed", result.Message);
        Assert.DoesNotContain("fixture constructor failure", result.Message);
        Assert.True(Assert.Single(manager.Plugins).IsLoaded);
        Assert.Contains(Assert.Single(manager.Plugins).Diagnostics, d => d.Code == PluginDiagnosticCode.LoadFailed);
        AssertEmpty(manager);
    }

    [Fact]
    public async Task Independent_plugins_can_use_the_same_local_contribution_ids()
    {
        using var disk = new TestDirectory();
        disk.Package(Id);
        disk.Package("com.example.second");
        await using var manager = disk.Manager();
        await manager.DiscoverAsync();

        Assert.True((await manager.EnableAsync(Id, confirmTrust: true)).Succeeded);
        Assert.True((await manager.EnableAsync("com.example.second", confirmTrust: true)).Succeeded);

        Assert.Equal(2, manager.Contributions.Views.Count);
        Assert.Contains("com.example.second:", (await manager.ExecuteCommandAsync("com.example.second", "ping")).Message);
        Assert.True((await manager.DisableAsync(Id)).Succeeded);
        Assert.Equal("com.example.second", Assert.Single(manager.Contributions.Views).PluginId);
    }

    [Fact]
    public async Task Installation_never_replaces_an_already_loaded_package()
    {
        using var disk = new TestDirectory();
        var installed = disk.Package(Id);
        var source = disk.Package(Id, external: true, fields: new() { ["version"] = "2.0.0" });
        var previous = await File.ReadAllBytesAsync(Path.Combine(installed, "plugin.json"));
        await using var manager = disk.Manager();
        await manager.DiscoverAsync();
        Assert.True((await manager.EnableAsync(Id, confirmTrust: true)).Succeeded);

        Assert.False((await manager.InstallAsync(source)).Succeeded);
        Assert.Equal(previous, await File.ReadAllBytesAsync(Path.Combine(installed, "plugin.json")));
        Assert.True((await manager.ExecuteCommandAsync(Id, "ping")).Succeeded);
    }

    [Fact]
    public async Task Rescanning_unchanged_packages_preserves_the_live_context_without_running_it_again()
    {
        using var disk = new TestDirectory();
        disk.Package(Id);
        using var marker = disk.WatchActivation();
        await using var manager = disk.Manager();
        await manager.DiscoverAsync();
        Assert.True((await manager.EnableAsync(Id, confirmTrust: true)).Succeeded);
        var assembly = Assert.Single(manager.Contributions.Views).View.GetType().Assembly;
        File.Delete(marker.Path);

        var packages = await manager.DiscoverAsync();

        Assert.Equal(PluginState.Enabled, Assert.Single(packages).State);
        Assert.Same(assembly, Assert.Single(manager.Contributions.Views).View.GetType().Assembly);
        Assert.False(File.Exists(marker.Path));
    }

    [Fact]
    public async Task Temporary_duplicate_discovery_cannot_orphan_a_live_runtime_during_reenable()
    {
        using var disk = new TestDirectory();
        disk.Package(Id);
        using var marker = disk.WatchActivation();
        await using var manager = disk.Manager();
        await manager.DiscoverAsync();
        Assert.True((await manager.EnableAsync(Id, confirmTrust: true)).Succeeded);
        var settings = manager.GetSettings(Id);
        using var request = new CancellationTokenSource();
        var command = manager.ExecuteCommandAsync(Id, "wait", request.Token);
        await WaitForSettingAsync(manager, "commandStarted");
        File.Delete(marker.Path);
        try
        {
            var duplicate = disk.Package(Id, folder: "temporary-duplicate");
            var ambiguous = await manager.DiscoverAsync();
            Assert.All(ambiguous, p => Assert.Equal(PluginState.Invalid, p.State));
            Assert.False(await settings.GetAsync("stopped", false));
            Assert.False(command.IsCompleted);
            Directory.Delete(duplicate, recursive: true);
            await manager.DiscoverAsync();
            Assert.False(File.Exists(marker.Path));
            Assert.False(await settings.GetAsync("stopped", false));

            var reenable = await manager.EnableAsync(Id, confirmTrust: true);
            await manager.ShutdownAsync();

            var completed = await Task.WhenAny(command, Task.Delay(TimeSpan.FromSeconds(1)));
            Assert.Same(command, completed);
            Assert.False((await command).Succeeded);
            Assert.False(reenable.Succeeded, "An owned live runtime must be stopped before a replacement can be enabled.");
            Assert.True(await settings.GetAsync("stopped", false));
            Assert.True(await settings.GetAsync("stopLifetimeCanceled", false));
            AssertEmpty(manager);
        }
        finally
        {
            // Keep a buggy/orphaned runtime from leaving this regression's command
            // alive after the expected RED failure.
            request.Cancel();
            await command.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task An_operation_queued_behind_disposal_cannot_install_a_package_after_shutdown()
    {
        using var disk = new TestDirectory();
        disk.Package(Id);
        var source = disk.Package("com.example.new", external: true);
        var manager = disk.Manager();
        await manager.DiscoverAsync();
        await manager.GetSettings(Id).SetAsync("mode", "init-cancel");
        using var cancel = new CancellationTokenSource();
        var enabling = manager.EnableAsync(Id, confirmTrust: true, cancel.Token);
        await WaitForSettingAsync(manager, "initializing");
        var disposing = manager.DisposeAsync().AsTask();
        var installing = manager.InstallAsync(source);

        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => enabling);
        await disposing;

        await Assert.ThrowsAsync<ObjectDisposedException>(() => installing);
        Assert.False(Directory.Exists(Path.Combine(disk.Plugins, "com.example.new")));
    }

    private static void AssertEmpty(PluginManager manager)
    {
        Assert.Empty(manager.Contributions.Views);
        Assert.Empty(manager.Contributions.Commands);
        Assert.Empty(manager.Contributions.Settings);
    }

    private static async Task WaitForSettingAsync(PluginManager manager, string key)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!await manager.GetSettings(Id).GetAsync(key, false, deadline.Token))
            await Task.Delay(5, deadline.Token);
    }
}

internal sealed class WindowsLoaderFactAttribute : FactAttribute
{
    public WindowsLoaderFactAttribute()
    {
        if (!OperatingSystem.IsWindows()) Skip = "Requires Windows dependency resolver paths and actual Windows filesystem checks.";
    }
}
