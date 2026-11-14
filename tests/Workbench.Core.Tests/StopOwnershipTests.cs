using System.Diagnostics;
using System.Runtime.ExceptionServices;
using Workbench.App;
using Workbench.Core;
using Workbench.Sdk;
using Xunit;
using Xunit.Abstractions;

namespace Workbench.Core.Tests;

[Collection(BlockingCallbackCollection.Name)]
public sealed class StopOwnershipTests(ITestOutputHelper output)
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
        using var workerHeadroom = new CallbackWorkerHeadroom();
        using var disk = new TestDirectory();
        disk.Package(Id, fields: new() { ["entryType"] = "Workbench.TestPlugin.ControlledStopPlugin" });
        var manager = new PluginManager(new() { PluginsRoot = disk.Plugins, UserRoot = disk.User, StopTimeout = TimeSpan.FromMilliseconds(75) });
        await manager.DiscoverAsync();
        var settings = manager.GetSettings(Id);
        await settings.SetAsync("lifetimeCallback", true);
        await settings.SetAsync("releaseStop", true);
        Assert.True((await manager.EnableAsync(Id, confirmTrust: true)).Succeeded);
        Task CloseAsync() => dispose ? manager.DisposeAsync().AsTask() : manager.ShutdownAsync();
        async Task CloseWithinBudgetAsync()
        {
            var elapsed = Stopwatch.StartNew();
            var operation = CloseAsync();
            try { await operation.WaitAsync(TimeSpan.FromSeconds(1)); }
            finally
            {
                ThreadPool.GetAvailableThreads(out var workers, out var io);
                ThreadPool.GetMinThreads(out var minimumWorkers, out _);
                output.WriteLine($"Close: elapsedMs={elapsed.ElapsedMilliseconds}, operation={operation.Status}, pending={manager.HasPendingStops}, context={SynchronizationContext.Current?.GetType().FullName}, processors={Environment.ProcessorCount}, threads={ThreadPool.ThreadCount}, minimumWorkers={minimumWorkers}, availableWorkers={workers}, availableIO={io}, queued={ThreadPool.PendingWorkItemCount}");
            }
        }
        try
        {
            await Assert.ThrowsAsync<PluginShutdownException>(CloseWithinBudgetAsync);
            await RuntimeTest.WaitAsync(async () => await settings.GetAsync("callbackStarted", false));
            await RuntimeTest.WaitAsync(async () => await settings.GetAsync("stopCalls", 0) == 1);
            Assert.True(manager.HasPendingStops);
            var pending = Assert.Single(manager.Plugins);
            Assert.Equal(PluginState.Stopping, pending.State);
            Assert.True(pending.HasRuntime);
            Assert.Empty(manager.Contributions.Views);
            Assert.False((await manager.EnableAsync(Id)).Succeeded);
            await Assert.ThrowsAsync<PluginShutdownException>(CloseWithinBudgetAsync);
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
        using var workerHeadroom = new CallbackWorkerHeadroom();
        using var disk = BackgroundTaskTests.Package();
        const string runtimeId = "com.example.runtime";
        var manager = BackgroundTaskTests.Manager(disk, TimeSpan.FromMilliseconds(100));
        await manager.DiscoverAsync();
        var settings = manager.GetSettings(runtimeId);
        await settings.SetAsync("runtimeMode", "callback-held");
        Assert.True((await manager.EnableAsync(runtimeId, confirmTrust: true)).Succeeded);
        await manager.StartBackgroundTaskAsync(runtimeId, "monitor");
        try
        {
            await RuntimeTest.WaitAsync(async () => await settings.GetAsync("callbackRegistered", false));
            await Assert.ThrowsAsync<PluginShutdownException>(() => manager.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(1)));
            await RuntimeTest.WaitAsync(async () => await settings.GetAsync("jobCallbackStarted", false));
            await settings.SetAsync("releaseCallbackDelegate", true);
            await RuntimeTest.WaitAsync(() => Task.FromResult(Assert.Single(manager.GetBackgroundTaskSnapshots(runtimeId)).FinishedAt is not null));
            Assert.True(await settings.GetAsync("jobFinished", false));
            Assert.False(await settings.GetAsync("jobCallbackFinished", false));
            var pending = Assert.Single(manager.Plugins);
            Assert.True(pending.HasRuntime);
            Assert.True(pending.HasPendingStop);
            Assert.Equal(PluginBackgroundTaskState.Stopping, Assert.Single(manager.GetBackgroundTaskSnapshots(runtimeId)).State);
            Assert.False((await manager.DisableAsync(runtimeId)).Succeeded);
            await settings.SetAsync("releaseJobCallback", true);
            await RuntimeTest.WaitAsync(() => Task.FromResult(!manager.HasPendingStops));
            Assert.True(await settings.GetAsync("jobCallbackFinished", false));
            Assert.Equal("PluginDisabled", await settings.GetAsync("callbackFinalReason", ""));
            Assert.Equal(1, await settings.GetAsync("stopCalls", 0));
        }
        finally
        {
            await settings.SetAsync("releaseCallbackDelegate", true);
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

    [Fact]
    public async Task Cancellation_before_callback_registration_keeps_owner_and_reuses_the_bounded_Stop()
    {
        using var workerHeadroom = new CallbackWorkerHeadroom();
        using var disk = BackgroundTaskTests.Package();
        const string runtimeId = "com.example.runtime";
        var manager = BackgroundTaskTests.Manager(disk, TimeSpan.FromMilliseconds(100));
        await manager.DiscoverAsync();
        var settings = manager.GetSettings(runtimeId);
        await settings.SetAsync("runtimeMode", "callback-held");
        await settings.SetAsync("pauseCallbackRegistration", true);
        Assert.True((await manager.EnableAsync(runtimeId, confirmTrust: true)).Succeeded);
        await manager.StartBackgroundTaskAsync(runtimeId, "monitor");
        try
        {
            await RuntimeTest.WaitAsync(async () => await settings.GetAsync("callbackRegistrationPaused", false));
            await Assert.ThrowsAsync<PluginShutdownException>(() => manager.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(1)));
            await RuntimeTest.WaitAsync(async () => await settings.GetAsync("callbackCancellationObserved", false));
            await settings.SetAsync("releaseCallbackRegistration", true);
            await RuntimeTest.WaitAsync(async () => await settings.GetAsync("jobCallbackStarted", false));
            // The callback is inline in Register, so registration/delegate completion
            // cannot happen until the callback is released. Ownership must remain.
            Assert.False(await settings.GetAsync("callbackRegistered", false));
            Assert.False(await settings.GetAsync("jobFinished", false));
            Assert.Null(Assert.Single(manager.GetBackgroundTaskSnapshots(runtimeId)).FinishedAt);
            Assert.True(Assert.Single(manager.Plugins).HasRuntime);
            Assert.True(manager.HasPendingStops);
            Assert.False((await manager.DisableAsync(runtimeId).WaitAsync(TimeSpan.FromSeconds(1))).Succeeded);
            Assert.False((await manager.EnableAsync(runtimeId)).Succeeded);
            await settings.SetAsync("releaseJobCallback", true);
            await settings.SetAsync("releaseCallbackDelegate", true);
            await RuntimeTest.WaitAsync(() => Task.FromResult(!manager.HasPendingStops));
            Assert.True(await settings.GetAsync("callbackRegistered", false));
            Assert.True(await settings.GetAsync("jobFinished", false));
            Assert.Equal("PluginDisabled", await settings.GetAsync("callbackFinalReason", ""));
            Assert.Equal(1, await settings.GetAsync("stopCalls", 0));
        }
        finally
        {
            await settings.SetAsync("releaseCallbackRegistration", true);
            await settings.SetAsync("releaseCallbackDelegate", true);
            await settings.SetAsync("releaseJobCallback", true);
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
    internal static Task WithCleanupAsync(IReadOnlyList<PluginManager> managers, Func<Task> body,
        TestDirectory? ownedDirectory = null, Func<Task>? beforeCleanup = null)
        => WithCleanupCoreAsync(managers, body, ownedDirectory, beforeCleanup, null);

    internal static Task WithCleanupAsync(HostRuntime runtime, Func<Task> body,
        TestDirectory? ownedDirectory = null, Func<Task>? beforeCleanup = null)
        => WithCleanupCoreAsync([runtime.Manager], body, ownedDirectory, beforeCleanup, () => runtime.DisposeAsync().AsTask());

    private static async Task WithCleanupCoreAsync(IReadOnlyList<PluginManager> managers, Func<Task> body,
        TestDirectory? ownedDirectory, Func<Task>? beforeCleanup, Func<Task>? retireRoot)
    {
        List<Exception> failures = [];
        if (await Record.ExceptionAsync(body) is { } bodyFailure) failures.Add(bodyFailure);
        if (beforeCleanup is not null && await Record.ExceptionAsync(beforeCleanup) is { } releaseFailure) failures.Add(releaseFailure);
        // A finite Dispose may report a retained Stop. Join that same actor before touching its
        // directory, and preserve body/release/retirement/IO failures instead of masking them.
        var retired = true;
        foreach (var manager in managers.Reverse().Distinct())
            if (await Record.ExceptionAsync(() => DisposeAsync(manager)) is { } cleanupFailure)
            {
                failures.Add(cleanupFailure);
                retired = false;
            }
        // Manager retirement alone does not release a HostRuntime's user-root lease.
        if (retired && retireRoot is not null && await Record.ExceptionAsync(retireRoot) is { } rootFailure)
        {
            failures.Add(rootFailure);
            retired = false;
        }
        if (ownedDirectory is not null)
        {
            if (!retired || managers.Any(manager => manager.HasPendingStops))
                failures.Add(new InvalidOperationException($"RuntimeCleanupPending: retirement did not complete for '{ownedDirectory.Root}'; the directory was retained."));
            else if (Record.Exception(ownedDirectory.Dispose) is { } directoryFailure) failures.Add(directoryFailure);
        }
        if (failures.Count > 1) throw new AggregateException(failures);
        if (failures.Count == 1) ExceptionDispatchInfo.Capture(failures[0]).Throw();
    }

    internal static async Task<ProjectPreparationResult> CompletePreparationAsync(PluginManager manager,
        string? pluginId = null, ITestOutputHelper? output = null)
        => await CompletePreparationAsync(manager, await manager.PrepareProjectLeaveAsync(pluginId), pluginId, output);

    internal static async Task<ProjectPreparationResult> CompletePreparationAsync(PluginManager manager,
        ProjectPreparationResult result, string? pluginId = null, ITestOutputHelper? output = null)
    {
        Trace("Initial", result);
        if (result.ErrorCode == "ProjectPreparationPending")
        {
            Assert.False(result.Succeeded);
            await WaitAsync(() => Task.FromResult(!manager.HasPendingProjectWork));
            // Consume the SAME retained filter/participants once; polling Prepare would start
            // another cycle after acknowledgement and could invoke the hook again.
            result = await manager.PrepareProjectLeaveAsync(pluginId);
            Trace("Acknowledged", result);
        }
        return result;

        void Trace(string stage, ProjectPreparationResult value) => output?.WriteLine(
            $"{stage} preparation {pluginId ?? "(all)"}: Succeeded={value.Succeeded}, HasPendingWork={value.HasPendingWork}, ErrorCode={value.ErrorCode ?? "(none)"}, ManagerPending={manager.HasPendingProjectWork}");
    }

    internal static async Task<PluginOperationResult> CompleteDisableAsync(PluginManager manager, string pluginId,
        ITestOutputHelper? output = null)
    {
        var result = await manager.DisableAsync(pluginId);
        output?.WriteLine($"Initial disable {pluginId}: Succeeded={result.Succeeded}, PendingStops={manager.HasPendingStops}");
        if (!result.Succeeded && manager.HasPendingStops)
        {
            await WaitAsync(() => Task.FromResult(!manager.HasPendingStops));
            result = await manager.DisableAsync(pluginId);
        }
        return result;
    }

    internal static async Task DisposeAsync(PluginManager manager)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while (true)
        {
            try { await manager.DisposeAsync(); return; }
            catch (PluginShutdownException) { await Task.Delay(10, deadline.Token); }
        }
    }

    internal static async Task WaitAsync(Func<Task<bool>> condition)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while (!await condition()) await Task.Delay(10, deadline.Token);
    }
}
