using Workbench.Core.Projects;
using Workbench.Sdk;
using Xunit;
using Xunit.Abstractions;

namespace Workbench.Core.Tests;

public sealed class PluginProjectTests(ITestOutputHelper output)
{
    private const string Id = "com.example.project";
    private const string Other = "com.example.second";

    [Fact]
    public async Task Project_service_reads_and_updates_only_its_bound_store_with_revision_conflicts()
    {
        using var disk = new TestDirectory();
        var scope = new ProjectRuntimeScope("c3c3c3c3c3c3c3c3c3c3c3c3c3c3c3c3", "  Project C  ", disk.User);
        var project = new PluginProjectContext(scope);
        var empty = await project.ReadAsync();
        Assert.Equal("missing", empty.Revision);
        Assert.Empty(empty.Instructions);
        Assert.False(Directory.Exists(disk.User));
        var written = await project.UpdateAsync("local project instructions", empty.Revision);
        Assert.Equal("local project instructions", (await scope.Documents.ReadAsync()).Text);
        Assert.Equal("local project instructions", (await project.ReadAsync()).Instructions);
        var conflict = await Assert.ThrowsAsync<PluginProjectContextException>(() => project.UpdateAsync("stale edit", empty.Revision));
        Assert.Equal("RevisionConflict", conflict.ErrorCode);
        Assert.Null(conflict.InnerException);
        Assert.Equal(written.Revision, (await project.ReadAsync()).Revision);
        scope.Rename("Renamed C");
        Assert.Equal(new ProjectIdentity(scope.Id, "Renamed C"), project.Identity);
        Assert.False(Directory.Exists(Path.Combine(disk.Root, "other-project")));
    }

    [Fact]
    public async Task Closed_project_service_is_immediately_revoked_even_when_a_store_call_is_pending()
    {
        using var disk = new TestDirectory();
        var scope = new ProjectRuntimeScope("default", "Default", disk.User);
        var initial = await scope.Documents.WriteAsync("before", "missing");
        using var held = new FileStream(Path.Combine(disk.User, ".workbench-project", "context.lock"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var project = new PluginProjectContext(scope);
        var pending = project.UpdateAsync("already owned", initial.Revision);
        Assert.True(project.HasPendingWork);
        project.Close();
        var error = await Assert.ThrowsAsync<PluginProjectContextException>(() => project.UpdateAsync("late", initial.Revision));
        Assert.Equal("ProjectContextClosed", error.ErrorCode);
        Assert.Throws<PluginProjectContextException>(() => project.Identity);
        var drain = project.DrainAsync();
        Assert.False(drain.IsCompleted);
        held.Dispose();
        await pending;
        await drain.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(project.HasPendingWork);
        Assert.Equal("already owned", (await scope.Documents.ReadAsync()).Text);
    }

    [Fact]
    public async Task Invalid_context_is_a_safe_sdk_error_and_never_an_editable_blank()
    {
        using var disk = new TestDirectory();
        var directory = Path.Combine(disk.User, ".workbench-project");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "context.json"), "{ invalid synthetic-secret-never-log }");
        var project = new PluginProjectContext(new("default", "Default", disk.User));
        var error = await Assert.ThrowsAsync<PluginProjectContextException>(() => project.ReadAsync());
        Assert.Equal("InvalidContext", error.ErrorCode);
        Assert.Null(error.InnerException);
        Assert.DoesNotContain("synthetic-secret-never-log", error.ToString());
        Assert.Contains("invalid synthetic-secret-never-log", await File.ReadAllTextAsync(Path.Combine(directory, "context.json")));
    }

    [Fact]
    public void Manager_refuses_a_scope_bound_to_a_different_user_root()
    {
        using var disk = new TestDirectory();
        Assert.Throws<ArgumentException>(() => new PluginManager(new()
        {
            PluginsRoot = disk.Plugins, UserRoot = disk.User,
            ProjectScope = new("default", "Wrong", Path.Combine(disk.Root, "other"))
        }));
    }

    [Fact]
    public async Task Legacy_manager_binds_project_services_to_its_actual_default_root()
    {
        using var disk = Package();
        await using var manager = Manager(disk);
        await manager.DiscoverAsync();
        Assert.True((await manager.EnableAsync(Id, confirmTrust: true)).Succeeded);
        Assert.Equal("default:Default", (await manager.ExecuteCommandAsync(Id, "identity")).Message);
        Assert.True((await manager.ExecuteCommandAsync(Id, "update")).Succeeded);
        Assert.Equal("fixture instructions", (await new ProjectContextDocumentStore(disk.User, "default").ReadAsync()).Text);
    }

    [Fact]
    public async Task Manager_owns_project_service_calls_that_a_plugin_did_not_await()
    {
        using var disk = Package();
        var manager = Manager(disk);
        await manager.DiscoverAsync();
        var scope = new ProjectContextDocumentStore(disk.User, "default");
        var current = await scope.WriteAsync("before", "missing");
        await manager.GetSettings(Id).SetAsync("contextRevision", current.Revision);
        Assert.True((await manager.EnableAsync(Id, confirmTrust: true)).Succeeded);
        using var lease = new FileStream(Path.Combine(disk.User, ".workbench-project", "context.lock"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        try
        {
            Assert.True((await manager.ExecuteCommandAsync(Id, "unawaited-update")).Succeeded);
            Assert.True(manager.HasPendingProjectWork);
            Assert.False((await manager.DisableAsync(Id)).Succeeded);
            Assert.True(manager.HasPendingStops);
            Assert.True(Assert.Single(manager.Plugins).HasRuntime);
            await Assert.ThrowsAsync<PluginShutdownException>(() => manager.DisposeAsync().AsTask());
            lease.Dispose();
            await RuntimeTest.WaitAsync(() => Task.FromResult(!manager.HasPendingStops && !manager.HasPendingProjectWork));
            Assert.Equal("unawaited instructions", (await scope.ReadAsync()).Text);
        }
        finally { lease.Dispose(); await RuntimeTest.DisposeAsync(manager); }
    }

    [Fact]
    public async Task Explicit_scope_identity_and_document_updates_remain_in_the_given_project_root()
    {
        using var disk = Package();
        var projectRoot = Path.Combine(disk.Root, "managed-id-root");
        var scope = new ProjectRuntimeScope("d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4", "My project", projectRoot);
        await using var manager = new PluginManager(new() { PluginsRoot = disk.Plugins, UserRoot = projectRoot, ProjectScope = scope });
        await manager.DiscoverAsync();
        Assert.True((await manager.EnableAsync(Id, confirmTrust: true)).Succeeded);
        Assert.Equal($"{scope.Id}:My project", (await manager.ExecuteCommandAsync(Id, "identity")).Message);
        Assert.True((await manager.ExecuteCommandAsync(Id, "update")).Succeeded);
        Assert.Equal("fixture instructions", (await scope.Documents.ReadAsync()).Text);
        Assert.False(Directory.Exists(disk.User));
    }

    [Fact]
    public async Task Preparation_of_a_closed_runtime_never_invokes_its_registered_hook()
    {
        using var disk = Package();
        await using var manager = Manager(disk);
        await manager.DiscoverAsync();
        var settings = manager.GetSettings(Id);
        Assert.True((await manager.EnableAsync(Id, confirmTrust: true)).Succeeded);
        Assert.True((await manager.DisableAsync(Id)).Succeeded);
        Assert.True((await manager.PrepareProjectLeaveAsync(Id)).Succeeded);
        Assert.Equal(0, await settings.GetAsync("prepareCalls", 0));
    }

    [Fact]
    public async Task Closed_but_retained_context_rejects_preparation_and_service_calls_before_disposal()
    {
        using var disk = Package();
        var manager = Manager(disk);
        await manager.DiscoverAsync();
        var settings = manager.GetSettings(Id);
        await settings.SetAsync("projectMode", "stop-held");
        Assert.True((await manager.EnableAsync(Id, confirmTrust: true)).Succeeded);
        try
        {
            Assert.False((await manager.DisableAsync(Id)).Succeeded);
            Assert.True(Assert.Single(manager.Plugins).HasRuntime);
            var prepare = await manager.PrepareProjectLeaveAsync(Id);
            Assert.False(prepare.Succeeded);
            Assert.Equal("ProjectContextClosed", prepare.ErrorCode);
            Assert.Equal(0, await settings.GetAsync("prepareCalls", 0));
            Assert.Equal("ProjectContextClosed", await settings.GetAsync("projectStopCode", ""));
        }
        finally
        {
            await settings.SetAsync("releaseStop", true);
            await RuntimeTest.DisposeAsync(manager);
        }
    }

    [Fact]
    public async Task Caller_cancellation_does_not_release_preparation_that_ignores_it()
    {
        using var disk = Package();
        var manager = Manager(disk);
        await manager.DiscoverAsync();
        await manager.GetSettings(Id).SetAsync("projectMode", "async-held");
        Assert.True((await manager.EnableAsync(Id, confirmTrust: true)).Succeeded);
        using var cancel = new CancellationTokenSource();
        var data = Path.Combine(disk.User, "data", Id);
        try
        {
            var request = manager.PrepareProjectLeaveAsync(Id, cancel.Token);
            await RuntimeTest.WaitAsync(() => Task.FromResult(File.Exists(Path.Combine(data, "prepare-started"))));
            cancel.Cancel();
            var pending = await request;
            Assert.False(pending.Succeeded);
            Assert.True(pending.HasPendingWork);
            Assert.Equal("ProjectPreparationCanceled", pending.ErrorCode);
            Assert.True(manager.HasPendingProjectWork);
            Assert.True((await manager.PrepareProjectLeaveAsync(Id)).HasPendingWork);
            Assert.Equal(1, await manager.GetSettings(Id).GetAsync("prepareCalls", 0));
        }
        finally
        {
            await File.WriteAllTextAsync(Path.Combine(data, "release-prepare"), "cleanup");
            await RuntimeTest.DisposeAsync(manager);
        }
    }

    [Fact]
    public async Task Retry_after_timed_out_preparation_settles_returns_its_result_without_reinvoking_the_hook()
    {
        using var disk = Package();
        var manager = Manager(disk);
        await manager.DiscoverAsync();
        var settings = manager.GetSettings(Id);
        await settings.SetAsync("projectMode", "async-held");
        Assert.True((await manager.EnableAsync(Id, confirmTrust: true)).Succeeded);
        var release = Path.Combine(disk.User, "data", Id, "release-prepare");
        try
        {
            Assert.True((await manager.PrepareProjectLeaveAsync(Id)).HasPendingWork);
            await File.WriteAllTextAsync(release, "settle original hook");
            await RuntimeTest.WaitAsync(() => Task.FromResult(!manager.HasPendingProjectWork));
            Assert.True((await manager.PrepareProjectLeaveAsync(Id)).Succeeded);
            Assert.Equal(1, await settings.GetAsync("prepareCalls", 0));
            // An acknowledged result permits a genuinely new leave cycle.
            Assert.True((await manager.PrepareProjectLeaveAsync(Id)).Succeeded);
            Assert.Equal(2, await settings.GetAsync("prepareCalls", 0));
        }
        finally
        {
            await File.WriteAllTextAsync(release, "cleanup");
            await RuntimeTest.DisposeAsync(manager);
        }
    }

    [Fact]
    public async Task All_plugin_retry_refuses_a_changed_participant_set_while_the_original_hook_is_pending()
    {
        var disk = Package();
        var manager = Manager(disk);
        var data = Path.Combine(disk.User, "data", Id);
        var otherData = Path.Combine(disk.User, "data", Other);
        var release = Path.Combine(data, "release-prepare");
        var releaseOther = Path.Combine(otherData, "release-prepare");
        await RuntimeTest.WithCleanupAsync([manager], async () =>
        {
            disk.Package(Other, fields: new() { ["entryType"] = "Workbench.TestPlugin.ProjectFixturePlugin" });
            await manager.DiscoverAsync();
            var settings = manager.GetSettings(Id);
            var otherSettings = manager.GetSettings(Other);
            await settings.SetAsync("projectMode", "async-held");
            await otherSettings.SetAsync("projectMode", "async-held");
            Assert.True((await manager.EnableAsync(Id, confirmTrust: true)).Succeeded);
            var original = await manager.PrepareProjectLeaveAsync();
            output.WriteLine(Facts("Original cohort", original));
            Assert.True(original.HasPendingWork, Facts("Original cohort", original));
            await RuntimeTest.WaitAsync(() => Task.FromResult(File.Exists(Path.Combine(data, "prepare-started"))));
            Assert.True((await manager.EnableAsync(Other, confirmTrust: true)).Succeeded);
            var changed = await manager.PrepareProjectLeaveAsync();
            output.WriteLine(Facts("Changed participants", changed));
            Assert.False(changed.Succeeded, Facts("Changed participants", changed));
            Assert.True(changed.HasPendingWork, Facts("Changed participants", changed));
            Assert.Equal("ProjectPreparationBusy", changed.ErrorCode);
            Assert.Equal(0, await otherSettings.GetAsync("prepareCalls", 0));
            await File.WriteAllTextAsync(release, "release original cohort");
            await RuntimeTest.WaitAsync(() => Task.FromResult(!manager.HasPendingProjectWork));
            Assert.Equal(1, await settings.GetAsync("prepareCalls", 0));

            // Other is a fresh, independently held participant in this new cohort. Normal IO
            // speed cannot establish that a newly admitted cohort finishes in one 100ms wait.
            var fresh = await manager.PrepareProjectLeaveAsync();
            output.WriteLine(Facts("New cohort", fresh));
            Assert.False(fresh.Succeeded, Facts("New cohort", fresh));
            Assert.True(fresh.HasPendingWork, Facts("New cohort", fresh));
            Assert.Equal("ProjectPreparationPending", fresh.ErrorCode);
            await RuntimeTest.WaitAsync(() => Task.FromResult(File.Exists(Path.Combine(otherData, "prepare-started"))));
            Assert.True(manager.HasPendingProjectWork);
            Assert.Equal(1, await otherSettings.GetAsync("prepareCalls", 0));
            await File.WriteAllTextAsync(releaseOther, "release new cohort");
            await RuntimeTest.WaitAsync(() => Task.FromResult(!manager.HasPendingProjectWork));

            // Acknowledge this retained cohort once, without starting/polling new preparations.
            var acknowledged = await manager.PrepareProjectLeaveAsync();
            output.WriteLine(Facts("Acknowledged cohort", acknowledged));
            Assert.True(acknowledged.Succeeded, Facts("Acknowledged cohort", acknowledged));
            Assert.False(acknowledged.HasPendingWork, Facts("Acknowledged cohort", acknowledged));
            Assert.Null(acknowledged.ErrorCode);
            Assert.Equal(2, await settings.GetAsync("prepareCalls", 0));
            Assert.Equal(1, await otherSettings.GetAsync("prepareCalls", 0));
        }, disk, beforeCleanup: async () =>
        {
            foreach (var path in new[] { release, releaseOther })
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllTextAsync(path, "cleanup");
            }
        });

        string Facts(string stage, ProjectPreparationResult result) =>
            $"{stage}: Succeeded={result.Succeeded}, HasPendingWork={result.HasPendingWork}, ErrorCode={result.ErrorCode ?? "(none)"}, ManagerPending={manager.HasPendingProjectWork}";
    }

    [Fact]
    public async Task Completed_unacknowledged_preparation_does_not_cover_a_replacement_runtime_instance()
    {
        using var disk = Package();
        var manager = Manager(disk);
        await manager.DiscoverAsync();
        var settings = manager.GetSettings(Id);
        await settings.SetAsync("projectMode", "async-held");
        Assert.True((await manager.EnableAsync(Id, confirmTrust: true)).Succeeded);
        var release = Path.Combine(disk.User, "data", Id, "release-prepare");
        try
        {
            Assert.True((await manager.PrepareProjectLeaveAsync(Id)).HasPendingWork);
            await File.WriteAllTextAsync(release, "release original instance");
            await RuntimeTest.WaitAsync(() => Task.FromResult(!manager.HasPendingProjectWork));
            Assert.True((await manager.DisableAsync(Id)).Succeeded);
            Assert.True((await manager.EnableAsync(Id)).Succeeded);
            Assert.True((await manager.PrepareProjectLeaveAsync(Id)).Succeeded);
            Assert.Equal(2, await settings.GetAsync("prepareCalls", 0));
        }
        finally
        {
            await File.WriteAllTextAsync(release, "cleanup");
            await RuntimeTest.DisposeAsync(manager);
        }
    }

    [Fact]
    public async Task Registered_hook_commits_only_on_successful_initialize_and_refuses_late_registration()
    {
        using var disk = Package();
        await using var manager = Manager(disk);
        await manager.DiscoverAsync();
        var settings = manager.GetSettings(Id);
        await settings.SetAsync("projectMode", "init-fail");
        Assert.False((await manager.EnableAsync(Id, confirmTrust: true)).Succeeded);
        Assert.True((await manager.PrepareProjectLeaveAsync()).Succeeded);
        Assert.Equal(0, await settings.GetAsync("prepareCalls", 0));
        await settings.SetAsync("projectMode", "normal");
        Assert.True((await manager.EnableAsync(Id)).Succeeded);
        Assert.False((await manager.ExecuteCommandAsync(Id, "late-project-hook")).Succeeded);
        Assert.True((await manager.PrepareProjectLeaveAsync()).Succeeded);
        Assert.Equal(1, await settings.GetAsync("prepareCalls", 0));
    }

    [Fact]
    public async Task Duplicate_hook_rolls_back_the_whole_registration()
    {
        using var disk = Package();
        await using var manager = Manager(disk);
        await manager.DiscoverAsync();
        await manager.GetSettings(Id).SetAsync("projectMode", "duplicate");
        Assert.False((await manager.EnableAsync(Id, confirmTrust: true)).Succeeded);
        Assert.Empty(manager.Contributions.Commands);
        Assert.True((await manager.PrepareProjectLeaveAsync()).Succeeded);
        Assert.Equal(0, await manager.GetSettings(Id).GetAsync("prepareCalls", 0));
    }

    [Theory]
    [InlineData("sync-held")]
    [InlineData("async-held")]
    public async Task Preparation_retains_one_owned_hook_and_terminal_stop_joins_it(string mode)
    {
        using var disk = Package();
        var manager = Manager(disk);
        await manager.DiscoverAsync();
        var settings = manager.GetSettings(Id);
        await settings.SetAsync("projectMode", mode);
        Assert.True((await manager.EnableAsync(Id, confirmTrust: true)).Succeeded);
        var data = Path.Combine(disk.User, "data", Id);
        try
        {
            var first = await manager.PrepareProjectLeaveAsync().WaitAsync(TimeSpan.FromSeconds(3));
            Assert.False(first.Succeeded);
            Assert.True(first.HasPendingWork);
            Assert.True(manager.HasPendingProjectWork);
            await RuntimeTest.WaitAsync(() => Task.FromResult(File.Exists(Path.Combine(data, "prepare-started"))));
            var second = await manager.PrepareProjectLeaveAsync();
            Assert.True(second.HasPendingWork);
            Assert.Equal(1, await settings.GetAsync("prepareCalls", 0));
            Assert.False((await manager.DisableAsync(Id)).Succeeded);
            Assert.True(manager.HasPendingStops);
            Assert.True(Assert.Single(manager.Plugins).HasRuntime);
            Assert.False((await manager.EnableAsync(Id)).Succeeded);
            await Assert.ThrowsAsync<PluginShutdownException>(() => manager.DisposeAsync().AsTask());
            Assert.Equal(1, await settings.GetAsync("stopCalls", 0));
            await File.WriteAllTextAsync(Path.Combine(data, "release-prepare"), "release synthetic preparation");
            await RuntimeTest.WaitAsync(() => Task.FromResult(!manager.HasPendingStops && !manager.HasPendingProjectWork));
            Assert.Equal(1, await settings.GetAsync("prepareCalls", 0));
            Assert.True(await settings.GetAsync("prepareFinished", false));
            Assert.True(await settings.GetAsync("prepareLifetimeUsable", false));
        }
        finally
        {
            await File.WriteAllTextAsync(Path.Combine(data, "release-prepare"), "cleanup");
            await RuntimeTest.DisposeAsync(manager);
        }
    }

    [Fact]
    public async Task Concurrent_different_preparation_filters_cannot_silently_claim_unprepared_plugins()
    {
        using var disk = Package();
        disk.Package(Other, fields: new() { ["entryType"] = "Workbench.TestPlugin.ProjectFixturePlugin" });
        var manager = Manager(disk);
        await manager.DiscoverAsync();
        await manager.GetSettings(Id).SetAsync("projectMode", "async-held");
        Assert.True((await manager.EnableAsync(Id, confirmTrust: true)).Succeeded);
        Assert.True((await manager.EnableAsync(Other, confirmTrust: true)).Succeeded);
        var release = Path.Combine(disk.User, "data", Id, "release-prepare");
        try
        {
            Assert.True((await manager.PrepareProjectLeaveAsync(Id)).HasPendingWork);
            var other = await manager.PrepareProjectLeaveAsync(Other);
            Assert.False(other.Succeeded);
            Assert.True(other.HasPendingWork);
            Assert.Equal("ProjectPreparationBusy", other.ErrorCode);
            Assert.Equal("ProjectPreparationBusy", (await manager.PrepareProjectLeaveAsync()).ErrorCode);
            Assert.Equal(0, await manager.GetSettings(Other).GetAsync("prepareCalls", 0));
            await File.WriteAllTextAsync(release, "release");
            await RuntimeTest.WaitAsync(() => Task.FromResult(!manager.HasPendingProjectWork));
            Assert.True((await manager.PrepareProjectLeaveAsync(Other)).Succeeded);
            Assert.Equal(1, await manager.GetSettings(Other).GetAsync("prepareCalls", 0));
        }
        finally
        {
            await File.WriteAllTextAsync(release, "cleanup");
            await RuntimeTest.DisposeAsync(manager);
        }
    }

    [Theory]
    [InlineData("hostile")]
    [InlineData("spoof-code")]
    public async Task Preparation_failure_is_safe_and_preserves_enabled_runtime_for_retry(string mode)
    {
        using var disk = Package();
        await using var manager = Manager(disk);
        await manager.DiscoverAsync();
        await manager.GetSettings(Id).SetAsync("projectMode", mode);
        Assert.True((await manager.EnableAsync(Id, confirmTrust: true)).Succeeded);
        var failed = await manager.PrepareProjectLeaveAsync();
        Assert.False(failed.Succeeded);
        Assert.False(failed.HasPendingWork);
        Assert.Equal("ProjectPreparationFailed", failed.ErrorCode);
        Assert.Equal(PluginState.Enabled, Assert.Single(manager.Plugins).State);
        Assert.True(Assert.Single(manager.Plugins).HasRuntime);
        await manager.GetSettings(Id).SetAsync("allowPreparation", true);
        Assert.True((await manager.PrepareProjectLeaveAsync()).Succeeded);
        Assert.Equal(2, await manager.GetSettings(Id).GetAsync("prepareCalls", 0));
    }

    internal static TestDirectory Package()
    {
        var disk = new TestDirectory();
        disk.Package(Id, fields: new() { ["entryType"] = "Workbench.TestPlugin.ProjectFixturePlugin", ["minimumHostVersion"] = "0.3.0" });
        return disk;
    }
    internal static PluginManager Manager(TestDirectory disk) => new(new()
    {
        PluginsRoot = disk.Plugins, UserRoot = disk.User, StopTimeout = TimeSpan.FromMilliseconds(100)
    });
}
