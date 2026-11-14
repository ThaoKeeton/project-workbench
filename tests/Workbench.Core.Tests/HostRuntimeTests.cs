using Workbench.App;
using Xunit;

namespace Workbench.Core.Tests;

public sealed class HostRuntimeTests
{
    private const string Id = "com.example.fixture";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Metadata_discovery_reconfigures_an_inactive_plugin_without_restoring_its_runtime(bool desiredEnabled)
    {
        using var disk = new TestDirectory();
        disk.Package(Id, fields: new() { ["entryType"] = "Workbench.TestPlugin.RuntimeFixturePlugin" });
        var options = new PluginManagerOptions { PluginsRoot = disk.Plugins, UserRoot = disk.User };
        await using (var seed = new HostRuntime(options))
        {
            await seed.InitializeAsync();
            Assert.True((await seed.Manager.EnableAsync(Id, confirmTrust: true)).Succeeded);
            Assert.Equal(1, await seed.Manager.GetSettings(Id).GetAsync("instanceCount", 0));
            await seed.Manager.GetSettings(Id).SetAsync("runtimeMode", "initialize-fail");
        }
        if (!desiredEnabled)
        {
            await using var failed = new HostRuntime(options);
            await failed.InitializeAsync();
            var revoked = Assert.Single(failed.Manager.Plugins);
            Assert.False(revoked.DesiredEnabled);
            Assert.False(revoked.HasRuntime);
            Assert.Empty(failed.Manager.GetSecretDefinitions(Id));
        }
        var instancesBeforeOffline = desiredEnabled ? 1 : 2;

        await using (var offline = new HostRuntime(options))
        {
            Assert.Empty(offline.Manager.Plugins);
            var undiscovered = Assert.Throws<KeyNotFoundException>(() => offline.Manager.GetSettings(Id));
            Assert.Equal(unchecked((int)0x80131577), undiscovered.HResult);
            await offline.Manager.DiscoverAsync();
            var settings = offline.Manager.GetSettings(Id);
            Assert.Equal("initialize-fail", await settings.GetAsync("runtimeMode", ""));
            Assert.Equal(instancesBeforeOffline, await settings.GetAsync("instanceCount", 0));
            var discovered = Assert.Single(offline.Manager.Plugins);
            Assert.Equal(desiredEnabled, discovered.DesiredEnabled);
            Assert.False(discovered.IsLoaded);
            Assert.False(discovered.HasRuntime);
            Assert.Empty(offline.Manager.Contributions.Views);
            Assert.ThrowsAny<IOException>(() => { using var duplicate = HostRuntime.AcquireLease(disk.User); });
            await settings.SetAsync("runtimeMode", "cooperative");
            Assert.Equal(instancesBeforeOffline, await settings.GetAsync("instanceCount", 0));
            Assert.Equal(instancesBeforeOffline, await settings.GetAsync("stopCalls", 0));
        }
        using (var nextOwner = HostRuntime.AcquireLease(disk.User)) { }

        await using (var reopened = new HostRuntime(options))
        {
            await reopened.InitializeAsync();
            var restored = Assert.Single(reopened.Manager.Plugins);
            Assert.Equal(desiredEnabled, restored.DesiredEnabled);
            Assert.Equal(desiredEnabled, restored.IsLoaded);
            Assert.Equal(desiredEnabled, restored.HasRuntime);
            Assert.Equal("cooperative", await reopened.Manager.GetSettings(Id).GetAsync("runtimeMode", ""));
            if (!desiredEnabled)
            {
                Assert.Equal(instancesBeforeOffline, await reopened.Manager.GetSettings(Id).GetAsync("instanceCount", 0));
                Assert.Empty(reopened.Manager.GetSecretDefinitions(Id));
                Assert.True((await reopened.Manager.EnableAsync(Id, confirmTrust: true)).Succeeded);
            }
            Assert.NotEmpty(reopened.Manager.GetSecretDefinitions(Id));
            Assert.Equal(instancesBeforeOffline + 1, await reopened.Manager.GetSettings(Id).GetAsync("instanceCount", 0));
            Assert.Equal(instancesBeforeOffline, await reopened.Manager.GetSettings(Id).GetAsync("stopCalls", 0));
        }
        using var finalOwner = HostRuntime.AcquireLease(disk.User);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failed_disposal_keeps_store_lease_and_same_stop_until_known_work_finishes(bool heldCallback)
    {
        using var disk = new TestDirectory();
        disk.Package(Id, fields: new() { ["entryType"] = "Workbench.TestPlugin.ControlledStopPlugin" });
        var runtime = new HostRuntime(new() { PluginsRoot = disk.Plugins, UserRoot = disk.User, StopTimeout = TimeSpan.FromMilliseconds(75) });
        await runtime.InitializeAsync();
        var manager = runtime.Manager;
        var settings = manager.GetSettings(Id);
        await settings.SetAsync("lifetimeCallback", heldCallback);
        await settings.SetAsync("releaseStop", heldCallback);
        Assert.True((await manager.EnableAsync(Id, confirmTrust: true)).Succeeded);
        try
        {
            await Assert.ThrowsAsync<PluginShutdownException>(() => runtime.DisposeAsync().AsTask());
            await RuntimeTest.WaitAsync(async () => await settings.GetAsync("stopCalls", 0) == 1);
            Assert.True(manager.HasPendingStops);
            Assert.True(Assert.Single(manager.Plugins).HasRuntime);
            Assert.ThrowsAny<IOException>(() => { using var duplicate = HostRuntime.AcquireLease(disk.User); });
            Assert.False((await manager.EnableAsync(Id)).Succeeded);
            await Assert.ThrowsAsync<PluginShutdownException>(() => runtime.DisposeAsync().AsTask());
            Assert.Equal(1, await settings.GetAsync("stopCalls", 0));
            Assert.Equal(1, await settings.GetAsync("instanceCount", 0));
        }
        finally
        {
            await settings.SetAsync("releaseCancellation", true);
            await settings.SetAsync("releaseStop", true);
            await RuntimeTest.WaitAsync(() => Task.FromResult(!manager.HasPendingStops));
            await runtime.DisposeAsync();
        }
        using var nextOwner = HostRuntime.AcquireLease(disk.User);
    }

    [Fact]
    public async Task Startup_applies_prepared_update_before_any_discovery_seals_the_root()
    {
        using var disk = new TestDirectory();
        disk.Package(Id);
        var update = disk.Package(Id, folder: "update", fields: new() { ["version"] = "2.0.0" }, external: true);
        var options = new PluginManagerOptions { PluginsRoot = disk.Plugins, UserRoot = disk.User };
        Assert.True((await new PluginPackageStore(options).StageUpdateAsync(Id, update)).Succeeded);
        await using var runtime = new HostRuntime(options);
        List<string> stages = [];
        await runtime.InitializeAsync(stage =>
        {
            stages.Add(stage);
            Assert.ThrowsAny<IOException>(() => { using var duplicate = HostRuntime.AcquireLease(disk.User); });
        });
        var package = Assert.Single(runtime.Manager.Plugins);
        Assert.Equal("2.0.0", package.Manifest!.Version);
        Assert.False(package.IsLoaded);
        Assert.False(package.IsTrusted);
        Assert.False(package.DesiredEnabled);
        Assert.Equal(new[] { "applying-pending-package-maintenance", "discovering-packages", "restoring-enabled-packages" }, stages);
    }

    [Fact]
    public async Task Failed_managed_maintenance_is_reported_without_blocking_unrelated_bundled_root()
    {
        using var disk = new TestDirectory();
        disk.Package(Id);
        const string bundledId = "com.example.bundled";
        var bundled = disk.Package(bundledId, external: true);
        var metadata = Path.Combine(disk.Plugins, ".workbench-packages");
        Directory.CreateDirectory(metadata);
        await File.WriteAllTextAsync(Path.Combine(metadata, "pending"), "not a directory");
        await using var runtime = new HostRuntime(new() { PluginsRoot = disk.Plugins, UserRoot = disk.User, AdditionalDiscoveryRoots = [Path.GetDirectoryName(bundled)!] });
        await runtime.InitializeAsync();
        Assert.Contains(runtime.MaintenanceResults, result => !result.Succeeded);
        Assert.NotEmpty(runtime.Packages.Diagnostics);
        Assert.True((await runtime.Manager.EnableAsync(bundledId, confirmTrust: true)).Succeeded);
        Assert.False((await runtime.Manager.EnableAsync(Id, confirmTrust: true)).Succeeded);
    }
}
