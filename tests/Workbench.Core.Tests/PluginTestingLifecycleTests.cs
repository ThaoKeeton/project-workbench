using Workbench.App;
using Workbench.Core.Projects;
using Workbench.Core.Testing;
using Workbench.Sdk;
using Xunit;
using Xunit.Abstractions;
using static Workbench.Core.Tests.PluginTestingContextTests;
using static Workbench.Core.Tests.ProjectTestStoreTests;

namespace Workbench.Core.Tests;

public sealed class PluginTestingLifecycleTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Package_loaded_optional_context_awaits_testing_before_commit_and_prepares_while_open()
    {
        using var disk = Package();
        var scope = new ProjectRuntimeScope("33333333333333333333333333333333", "First", disk.User);
        await using var runtime = Runtime(disk, scope);
        await runtime.InitializeAsync();
        Assert.True((await runtime.Manager.EnableAsync(Owner, confirmTrust: true)).Succeeded);
        var testing = Testing(runtime.Manager);
        Assert.False(Directory.Exists(StoreRoot(disk.User)));
        Assert.True((await runtime.Manager.ExecuteCommandAsync(Owner, "publish")).Succeeded);
        scope.Rename("Renamed first");
        var prepared = await runtime.Manager.PrepareProjectLeaveAsync(Owner);
        Assert.True(prepared.Succeeded);
        Assert.False(prepared.HasPendingWork);
        Assert.False(runtime.Manager.HasPendingProjectWork);
        Assert.Equal(2, (await testing.ListSourcesAsync()).Count);
        Assert.All(await testing.ListSourcesAsync(), source => { Assert.Equal(scope.Id, source.ProjectId); Assert.Equal(Owner, source.ProducerPluginId); });
        Assert.True((await runtime.Manager.DisableAsync(Owner)).Succeeded);
        Assert.Equal("TestingContextClosed", await runtime.Manager.GetSettings(Owner).GetAsync("stopTestingCode", ""));
        await Code("TestingContextClosed", () => testing.ListSourcesAsync());
        Assert.Equal(2, (await new ProjectTestStore(disk.User, scope.Id).ListSourcesAsync()).Count);
        Assert.Equal(1, await runtime.Manager.GetSettings(Owner).GetAsync("stopCalls", 0));
        output.WriteLine("Actual package Initialize awaited Testing before contributions; before-leave wrote while open; terminal Stop rejected fresh write, hook was not rerun.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Actual_manager_root_lease_outlives_held_capture_including_failed_initialization(bool failInit)
    {
        using var workers = new CallbackWorkerHeadroom();
        using var disk = Package();
        var runtime = Runtime(disk);
        var manager = runtime.Manager;
        Task<TestSourceSnapshot>? call = null;
        using var prefix = new HeldList<TestSourceItem>([new("x", "Item", "", ["Criterion"], null)]);
        try
        {
            await runtime.InitializeAsync();
            await manager.GetSettings(Owner).SetAsync("testingMode", "lazy");
            Assert.True((await manager.EnableAsync(Owner, confirmTrust: true)).Succeeded);
            var testing = Testing(manager);
            if (failInit)
            {
                Directory.CreateDirectory(StoreRoot(disk.User));
                await File.WriteAllTextAsync(Path.Combine(StoreRoot(disk.User), "store.json"), "invalid synthetic store");
            }
            call = Task.Run(() => testing.PublishSourceAsync(Source(items: prefix)));
            await prefix.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (failInit) await Code("TestStoreCorrupt", () => testing.ListRunsAsync());
            else Assert.Empty(await testing.ListRunsAsync());
            Assert.True(manager.HasPendingProjectWork);
            await Assert.ThrowsAsync<PluginShutdownException>(() => runtime.DisposeAsync().AsTask());
            Assert.True(manager.HasPendingStops);
            Assert.True(manager.HasPendingProjectWork);
            Assert.True(Assert.Single(manager.Plugins).HasRuntime);
            await Assert.ThrowsAnyAsync<IOException>(async () => { await using var otherOwner = new HostRuntime(new() { PluginsRoot = disk.Plugins, UserRoot = disk.User }); });
            Assert.False(call.IsCompleted);
            await Code("TestingContextClosed", () => testing.ListRunsAsync());
            prefix.Release.TrySetResult();
            if (failInit) await Code("TestStoreCorrupt", () => call);
            else Assert.Equal(Owner, (await call).ProducerPluginId);
            await RuntimeTest.WaitAsync(() => Task.FromResult(!manager.HasPendingStops && !manager.HasPendingProjectWork));
            Assert.Equal(1, await manager.GetSettings(Owner).GetAsync("stopCalls", 0));
            await runtime.DisposeAsync();
            await using var next = new HostRuntime(new() { PluginsRoot = disk.Plugins, UserRoot = disk.User });
            output.WriteLine($"Actual manager/root: initFailed={failInit}, held Count prefix retained original stop/root; released and joined, new HostRuntime acquired root.");
        }
        finally
        {
            prefix.Release.TrySetResult();
            if (call is not null) _ = await Record.ExceptionAsync(() => call);
            await RuntimeTest.DisposeAsync(manager);
            await runtime.DisposeAsync();
        }
    }

    [Fact]
    public async Task Actual_store_IO_is_joined_by_preparation_and_terminal_stop_before_root_release()
    {
        using var disk = Package();
        var runtime = Runtime(disk);
        var manager = runtime.Manager;
        FileStream? lease = null;
        Task<TestSourceSnapshot>? call = null;
        try
        {
            await runtime.InitializeAsync();
            Assert.True((await manager.EnableAsync(Owner, confirmTrust: true)).Succeeded);
            var testing = Testing(manager);
            await testing.PublishSourceAsync(Source());
            lease = HoldLease(disk.User);
            call = testing.PublishSourceAsync(Source() with { Title = "admitted IO" });
            var prepared = await manager.PrepareProjectLeaveAsync(Owner);
            Assert.False(prepared.Succeeded);
            Assert.True(prepared.HasPendingWork);
            Assert.True(manager.HasPendingProjectWork);
            await Assert.ThrowsAsync<PluginShutdownException>(() => runtime.DisposeAsync().AsTask());
            Assert.False(call.IsCompleted);
            Assert.True(manager.HasPendingStops);
            Assert.ThrowsAny<IOException>(() => { using var duplicate = HostRuntime.AcquireLease(disk.User); });
            lease.Dispose();
            lease = null;
            await call;
            await RuntimeTest.WaitAsync(() => Task.FromResult(!manager.HasPendingStops && !manager.HasPendingProjectWork));
            await runtime.DisposeAsync();
            using var nextOwner = HostRuntime.AcquireLease(disk.User);
            Assert.Contains(await new ProjectTestStore(disk.User, "default").ListSourcesAsync(), source => source.Title == "admitted IO");
            output.WriteLine("Actual testing.lock gate retained admitted store IO through preparation timeout and terminal stop; original call joined before lease reacquisition.");
        }
        finally
        {
            lease?.Dispose();
            if (call is not null) _ = await Record.ExceptionAsync(() => call);
            await RuntimeTest.DisposeAsync(manager);
            await runtime.DisposeAsync();
        }
    }

    [Fact]
    public async Task Canceled_first_caller_cannot_detach_actual_initialization_or_release_manager_root()
    {
        using var disk = Package();
        await Seed(new(disk.User, "default"));
        var runtime = Runtime(disk);
        var manager = runtime.Manager;
        using var cancel = new CancellationTokenSource();
        FileStream? lease = null;
        Task<IReadOnlyList<TestSourceSummary>>? first = null;
        Task<IReadOnlyList<TestRunSummary>>? second = null;
        try
        {
            await runtime.InitializeAsync();
            await manager.GetSettings(Owner).SetAsync("testingMode", "lazy");
            Assert.True((await manager.EnableAsync(Owner, confirmTrust: true)).Succeeded);
            var testing = Testing(manager);
            lease = HoldLease(disk.User);
            first = testing.ListSourcesAsync(cancel.Token);
            second = testing.ListRunsAsync();
            cancel.Cancel();
            Assert.False(first.IsCompleted);
            await Assert.ThrowsAsync<PluginShutdownException>(() => runtime.DisposeAsync().AsTask());
            Assert.True(manager.HasPendingStops);
            Assert.True(manager.HasPendingProjectWork);
            Assert.False(second.IsCompleted);
            Assert.ThrowsAny<IOException>(() => { using var duplicate = HostRuntime.AcquireLease(disk.User); });
            lease.Dispose(); lease = null;
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
            Assert.Empty(await second);
            await RuntimeTest.WaitAsync(() => Task.FromResult(!manager.HasPendingStops));
            await runtime.DisposeAsync();
            using var nextOwner = HostRuntime.AcquireLease(disk.User);
            output.WriteLine("Actual canceled caller + shared initialization/testing.lock: both calls and original stop retained root until filesystem gate released.");
        }
        finally
        {
            lease?.Dispose();
            if (first is not null) _ = await Record.ExceptionAsync(() => first);
            if (second is not null) _ = await Record.ExceptionAsync(() => second);
            await RuntimeTest.DisposeAsync(manager);
            await runtime.DisposeAsync();
        }
    }

    [Fact]
    public async Task Package_initialization_waits_for_actual_filesystem_barrier_and_preserves_foreign_live_run()
    {
        using var disk = Package();
        var store = new ProjectTestStore(disk.User, "default");
        var set = await Seed(store);
        var old = await store.CreateRunAsync(Owner, set.Id, set.Revision, [set.Cases[0].Id]);
        var foreign = await store.CreateRunAsync(Other, set.Id, set.Revision, [set.Cases[0].Id]);
        await using var runtime = Runtime(disk);
        await runtime.InitializeAsync();
        using var lease = HoldLease(disk.User);
        var enable = runtime.Manager.EnableAsync(Owner, confirmTrust: true);
        try
        {
            await RuntimeTest.WaitAsync(() => runtime.Manager.GetSettings(Owner).GetAsync("testingInitializeEntered", false));
            Assert.False(enable.IsCompleted);
            Assert.True(runtime.Manager.HasPendingProjectWork);
            Assert.Empty(runtime.Manager.Contributions.Views);
            lease.Dispose();
            Assert.True((await enable).Succeeded);
            var testing = Testing(runtime.Manager);
            Assert.Equal(TestRunState.Interrupted, (await testing.ReadRunAsync(old.Plan.Id)).State);
            Assert.Equal(TestRunState.Queued, (await testing.ReadRunAsync(foreign.Plan.Id)).State);
            Assert.Equal(1, (await testing.ReadRunAsync(old.Plan.Id)).Sequence);
        }
        finally { lease.Dispose(); _ = await enable; }
    }

    [Theory]
    [InlineData("prepare-error", "StoreLimitExceeded")]
    [InlineData("prepare-spoof", "TestingFailed")]
    public async Task Testing_errors_use_fixed_safe_codes_in_preparation_commands_and_host_boundary(string mode, string expected)
    {
        using var disk = Package();
        await using var runtime = Runtime(disk);
        await runtime.InitializeAsync();
        await runtime.Manager.GetSettings(Owner).SetAsync("testingMode", mode);
        Assert.True((await runtime.Manager.EnableAsync(Owner, confirmTrust: true)).Succeeded);
        Assert.Equal(expected, (await runtime.Manager.PrepareProjectLeaveAsync(Owner)).ErrorCode);
        Assert.Contains("TestStoreCorrupt", (await runtime.Manager.ExecuteCommandAsync(Owner, "testing-error")).Message);
        Assert.Contains("TestingFailed", (await runtime.Manager.ExecuteCommandAsync(Owner, "testing-spoof")).Message);
        Assert.Equal("TestStoreCorrupt", HostFailure.ProjectCode(new PluginTestingException("TestStoreCorrupt")));
        Assert.Equal("ProjectOperationFailed", HostFailure.ProjectCode(new PluginTestingException("synthetic-private-provider-text")));
        Assert.Equal("ProjectOperationFailed", HostFailure.ProjectCode(new PluginTestingException("InvalidContext")));
        Assert.Equal("ProjectOperationFailed", HostFailure.ProjectCode(new PluginProjectContextException("TestStoreCorrupt")));
        Assert.DoesNotContain(runtime.Manager.Plugins.SelectMany(p => p.Diagnostics), d => d.Message.Contains("synthetic-private-provider-text", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Initialization_testing_failure_is_readable_and_never_logs_forged_codes(bool spoof)
    {
        using var disk = Package();
        await using var runtime = Runtime(disk);
        await runtime.InitializeAsync();
        if (spoof) await runtime.Manager.GetSettings(Owner).SetAsync("testingMode", "initialize-spoof");
        else
        {
            Directory.CreateDirectory(StoreRoot(disk.User));
            await File.WriteAllTextAsync(Path.Combine(StoreRoot(disk.User), "store.json"), "invalid synthetic-private-provider-text");
        }
        var enabled = await runtime.Manager.EnableAsync(Owner, confirmTrust: true);
        Assert.False(enabled.Succeeded);
        Assert.Contains(spoof ? "TestingFailed" : "TestStoreCorrupt", enabled.Message);
        Assert.DoesNotContain("synthetic-private-provider-text", enabled.Message);
        Assert.DoesNotContain(runtime.Manager.Plugins.SelectMany(p => p.Diagnostics), d => d.Message.Contains("synthetic-private-provider-text", StringComparison.Ordinal));
    }

    internal static TestDirectory Package()
    {
        var disk = new TestDirectory();
        disk.Package(Owner, fields: new() { ["minimumHostVersion"] = "0.4.0", ["entryType"] = "Workbench.TestPlugin.TestingFixturePlugin" });
        return disk;
    }
    internal static HostRuntime Runtime(TestDirectory disk, ProjectRuntimeScope? scope = null)
        => new(new() { PluginsRoot = disk.Plugins, UserRoot = scope?.UserRoot ?? disk.User, ProjectScope = scope,
            HostVersion = new(0, 4, 0), StopTimeout = TimeSpan.FromMilliseconds(75) });
    internal static IProjectTestWorkspace Testing(PluginManager manager)
    {
        var view = Assert.Single(manager.Contributions.Views).View;
        return Assert.IsAssignableFrom<IProjectTestWorkspace>(view.GetType().GetProperty("Testing")!.GetValue(view));
    }
}
