using Workbench.Core;
using Workbench.Sdk;
using Xunit;

namespace Workbench.Core.Tests;

public sealed class StopOwnershipTests
{
    private const string Id = "com.example.fixture";

    [Fact]
    public async Task Timed_out_stop_returns_promptly_and_retry_does_not_call_Stop_twice()
    {
        using var disk = new TestDirectory();
        disk.Package(Id, fields: new() { ["entryType"] = "Workbench.TestPlugin.ControlledStopPlugin" });
        var manager = new PluginManager(new() { PluginsRoot = disk.Plugins, UserRoot = disk.User, StopTimeout = TimeSpan.FromMilliseconds(75) });
        await manager.DiscoverAsync();
        Assert.True((await manager.EnableAsync(Id, confirmTrust: true)).Succeeded);
        var settings = manager.GetSettings(Id);
        var stopping = manager.DisableAsync(Id);
        try
        {
            await RuntimeTest.WaitAsync(async () => await settings.GetAsync("stopCalls", 0) == 1);
            var result = await stopping.WaitAsync(TimeSpan.FromSeconds(1));
            Assert.False(result.Succeeded);
            Assert.Empty(manager.Contributions.Views);
            Assert.False((await manager.EnableAsync(Id)).Succeeded);
            Assert.False((await manager.DisableAsync(Id)).Succeeded);
            Assert.Equal(1, await settings.GetAsync("stopCalls", 0));
            Assert.Equal(1, await settings.GetAsync("instanceCount", 0));
        }
        finally
        {
            await settings.SetAsync("releaseStop", true);
            await stopping;
            await RuntimeTest.DisposeAsync(manager);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Pending_lifetime_callbacks_retain_ownership_across_shutdown_and_dispose_retries(bool dispose)
    {
        using var disk = new TestDirectory();
        disk.Package(Id, fields: new() { ["entryType"] = "Workbench.TestPlugin.ControlledStopPlugin" });
        var manager = new PluginManager(new() { PluginsRoot = disk.Plugins, UserRoot = disk.User, StopTimeout = TimeSpan.FromMilliseconds(75) });
        await manager.DiscoverAsync();
        var settings = manager.GetSettings(Id);
        await settings.SetAsync("lifetimeCallback", true);
        await settings.SetAsync("releaseStop", true);
        Assert.True((await manager.EnableAsync(Id, confirmTrust: true)).Succeeded);
        Task CloseAsync() => dispose ? manager.DisposeAsync().AsTask() : manager.ShutdownAsync();
        try
        {
            await Assert.ThrowsAsync<PluginShutdownException>(() => CloseAsync().WaitAsync(TimeSpan.FromSeconds(1)));
            await RuntimeTest.WaitAsync(async () => await settings.GetAsync("callbackStarted", false));
            await RuntimeTest.WaitAsync(async () => await settings.GetAsync("stopCalls", 0) == 1);
            Assert.True(manager.HasPendingStops);
            var pending = Assert.Single(manager.Plugins);
            Assert.Equal(PluginState.Stopping, pending.State);
            Assert.True(pending.HasRuntime);
            Assert.Empty(manager.Contributions.Views);
            Assert.False((await manager.EnableAsync(Id)).Succeeded);
            await Assert.ThrowsAsync<PluginShutdownException>(() => CloseAsync().WaitAsync(TimeSpan.FromSeconds(1)));
            Assert.Equal(1, await settings.GetAsync("stopCalls", 0));
            Assert.False(await settings.GetAsync("callbackFinished", false));
            await settings.SetAsync("releaseCancellation", true);
            await RuntimeTest.WaitAsync(() => Task.FromResult(!manager.HasPendingStops));
            Assert.False(Assert.Single(manager.Plugins).HasRuntime);
            await CloseAsync();
        }
        finally
        {
            await settings.SetAsync("releaseCancellation", true);
            await RuntimeTest.DisposeAsync(manager);
        }
    }

    [Fact]
    public async Task Background_work_keeps_runtime_owned_after_plugin_Stop_completes_and_late_completion_cleans_up()
    {
        using var disk = BackgroundTaskTests.Package();
        const string runtimeId = "com.example.runtime";
        var manager = BackgroundTaskTests.Manager(disk, TimeSpan.FromMilliseconds(100));
        await manager.DiscoverAsync();
        var settings = manager.GetSettings(runtimeId);
        await settings.SetAsync("runtimeMode", "held");
        Assert.True((await manager.EnableAsync(runtimeId, confirmTrust: true)).Succeeded);
        await manager.StartBackgroundTaskAsync(runtimeId, "monitor");
        await RuntimeTest.WaitAsync(async () => await settings.GetAsync("jobRuns", 0) == 1);
        try
        {
            Assert.False((await manager.DisableAsync(runtimeId)).Succeeded);
            await RuntimeTest.WaitAsync(async () => await settings.GetAsync("stopCalls", 0) == 1);
            Assert.Equal(1, await settings.GetAsync("stopCalls", 0));
            Assert.True(manager.HasPendingStops);
            Assert.Equal(PluginBackgroundTaskState.Stopping, Assert.Single(manager.GetBackgroundTaskSnapshots(runtimeId)).State);
            Assert.False((await manager.EnableAsync(runtimeId)).Succeeded);
            await settings.SetAsync("releaseJob", true);
            await RuntimeTest.WaitAsync(() => Task.FromResult(!manager.HasPendingStops));
            Assert.Equal(PluginState.Disabled, Assert.Single(manager.Plugins).State);
            Assert.False(Assert.Single(manager.Plugins).HasRuntime);
            Assert.Empty(manager.GetBackgroundTaskSnapshots(runtimeId));
            Assert.True((await manager.EnableAsync(runtimeId)).Succeeded);
            Assert.Equal(2, await settings.GetAsync("instanceCount", 0));
        }
        finally
        {
            await settings.SetAsync("releaseJob", true);
            await RuntimeTest.DisposeAsync(manager);
        }
    }

    [Fact]
    public async Task Explicit_disable_upgrades_a_pending_shutdown_reason_without_invoking_Stop_again()
    {
        using var disk = BackgroundTaskTests.Package();
        const string runtimeId = "com.example.runtime";
        var manager = BackgroundTaskTests.Manager(disk, TimeSpan.FromMilliseconds(100));
        await manager.DiscoverAsync();
        var settings = manager.GetSettings(runtimeId);
        await settings.SetAsync("runtimeMode", "held");
        Assert.True((await manager.EnableAsync(runtimeId, confirmTrust: true)).Succeeded);
        await manager.StartBackgroundTaskAsync(runtimeId, "monitor");
        await RuntimeTest.WaitAsync(async () => await settings.GetAsync("jobRuns", 0) == 1);
        try
        {
            await Assert.ThrowsAsync<PluginShutdownException>(() => manager.ShutdownAsync());
            Assert.False((await manager.DisableAsync(runtimeId)).Succeeded);
            await settings.SetAsync("releaseJob", true);
            await RuntimeTest.WaitAsync(() => Task.FromResult(!manager.HasPendingStops));
            Assert.Equal("PluginDisabled", await settings.GetAsync("jobFinalStopReason", ""));
            Assert.Equal("PluginDisabled", await settings.GetAsync("contextFinalStopReason", ""));
            Assert.Equal(1, await settings.GetAsync("stopCalls", 0));
        }
        finally
        {
            await settings.SetAsync("releaseJob", true);
            await RuntimeTest.DisposeAsync(manager);
        }
    }

    [Fact]
    public async Task Pending_background_cancellation_callback_observes_later_disable_after_delegate_has_completed()
    {
        using var disk = BackgroundTaskTests.Package();
        const string runtimeId = "com.example.runtime";
        var manager = BackgroundTaskTests.Manager(disk, TimeSpan.FromMilliseconds(100));
        await manager.DiscoverAsync();
        var settings = manager.GetSettings(runtimeId);
        await settings.SetAsync("runtimeMode", "callback-held");
        Assert.True((await manager.EnableAsync(runtimeId, confirmTrust: true)).Succeeded);
        await manager.StartBackgroundTaskAsync(runtimeId, "monitor");
        await RuntimeTest.WaitAsync(async () => await settings.GetAsync("jobRuns", 0) == 1);
        try
        {
            await Assert.ThrowsAsync<PluginShutdownException>(() => manager.ShutdownAsync());
            await RuntimeTest.WaitAsync(async () => await settings.GetAsync("jobCallbackStarted", false) && await settings.GetAsync("jobFinished", false));
            Assert.False((await manager.DisableAsync(runtimeId)).Succeeded);
            await settings.SetAsync("releaseJobCallback", true);
            await RuntimeTest.WaitAsync(() => Task.FromResult(!manager.HasPendingStops));
            Assert.Equal("PluginDisabled", await settings.GetAsync("callbackFinalReason", ""));
        }
        finally
        {
            await settings.SetAsync("releaseJobCallback", true);
            await RuntimeTest.DisposeAsync(manager);
        }
    }

    [Fact]
    public async Task A_late_Stop_fault_is_observed_and_releases_ownership_only_after_completion()
    {
        using var disk = new TestDirectory();
        disk.Package(Id, fields: new() { ["entryType"] = "Workbench.TestPlugin.ControlledStopPlugin" });
        var manager = new PluginManager(new() { PluginsRoot = disk.Plugins, UserRoot = disk.User, StopTimeout = TimeSpan.FromMilliseconds(75) });
        await manager.DiscoverAsync();
        var settings = manager.GetSettings(Id);
        await settings.SetAsync("throwStop", true);
        Assert.True((await manager.EnableAsync(Id, confirmTrust: true)).Succeeded);
        try
        {
            Assert.False((await manager.DisableAsync(Id)).Succeeded);
            Assert.True(manager.HasPendingStops);
            await settings.SetAsync("releaseStop", true);
            await RuntimeTest.WaitAsync(() => Task.FromResult(!manager.HasPendingStops));
            var snapshot = Assert.Single(manager.Plugins);
            Assert.Equal(PluginState.Failed, snapshot.State);
            Assert.False(snapshot.HasRuntime);
            Assert.Contains(snapshot.Diagnostics, d => d.Code == PluginDiagnosticCode.StopFailed);
            Assert.All(snapshot.Diagnostics, d => Assert.DoesNotContain("synthetic-secret-never-log", d.Message));
        }
        finally
        {
            await settings.SetAsync("releaseStop", true);
            await RuntimeTest.DisposeAsync(manager);
        }
    }

    [Theory]
    [InlineData("Workbench.TestPlugin.SensitiveFailurePlugin", "initialize")]
    [InlineData("Workbench.TestPlugin.SensitiveFailurePlugin", "command")]
    [InlineData("Workbench.TestPlugin.SensitiveFailurePlugin", "stop")]
    [InlineData("Workbench.TestPlugin.SensitiveFailurePlugin", "initialize-virtual")]
    [InlineData("Workbench.TestPlugin.SensitiveFailurePlugin", "command-virtual")]
    public async Task Host_originated_failures_never_expose_plugin_exception_messages(string type, string failure)
    {
        using var disk = new TestDirectory();
        disk.Package(Id, fields: new() { ["entryType"] = type });
        await using var manager = disk.Manager();
        await manager.DiscoverAsync();
        await manager.GetSettings(Id).SetAsync("failure", failure);
        var result = await manager.EnableAsync(Id, confirmTrust: true);
        var message = result.Message;
        if (failure.StartsWith("command", StringComparison.Ordinal)) message += (await manager.ExecuteCommandAsync(Id, "throw")).Message;
        if (failure.StartsWith("stop", StringComparison.Ordinal)) message += (await manager.DisableAsync(Id)).Message;
        Assert.DoesNotContain("synthetic-secret-never-log", message);
        Assert.All(Assert.Single(manager.Plugins).Diagnostics,
            diagnostic => Assert.DoesNotContain("synthetic-secret-never-log", diagnostic.Message));
    }
}

internal static class RuntimeTest
{
    internal static async Task DisposeAsync(PluginManager manager)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (true)
        {
            try { await manager.DisposeAsync(); return; }
            catch (PluginShutdownException) { await Task.Delay(10, deadline.Token); }
        }
    }

    internal static async Task WaitAsync(Func<Task<bool>> condition)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!await condition()) await Task.Delay(10, deadline.Token);
    }
}
