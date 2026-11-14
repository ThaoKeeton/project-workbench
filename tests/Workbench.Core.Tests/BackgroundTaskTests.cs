using System.Text.Json;
using Workbench.Core;
using Workbench.Sdk;
using Xunit;

namespace Workbench.Core.Tests;

public sealed class BackgroundTaskTests
{
    private const string Id = "com.example.runtime";

    [Fact]
    public async Task Duplicate_start_and_Start_Stop_race_are_single_flight_until_true_completion()
    {
        using var disk = Package();
        await using var manager = Manager(disk, TimeSpan.FromMilliseconds(100));
        await EnableAsync(manager, "held");
        var settings = manager.GetSettings(Id);
        try
        {
            var starts = await Task.WhenAll(Enumerable.Range(0, 30).Select(_ => manager.StartBackgroundTaskAsync(Id, "monitor")))
                .WaitAsync(TimeSpan.FromSeconds(5));
            Assert.All(starts, result => Assert.True(result.Succeeded, result.Message));
            await RuntimeTest.WaitAsync(async () => await settings.GetAsync("jobRuns", 0) == 1);
            var stop = manager.StopBackgroundTaskAsync(Id, "monitor");
            await RuntimeTest.WaitAsync(async () => await settings.GetAsync("jobStopReason", "") == "UserRequest");
            Assert.False((await manager.StartBackgroundTaskAsync(Id, "monitor")).Succeeded);
            Assert.False((await stop).Succeeded);
            var snapshot = Assert.Single(manager.GetBackgroundTaskSnapshots(Id));
            Assert.Equal(PluginBackgroundTaskState.Stopping, snapshot.State);
            Assert.False(snapshot.DesiredRunning);
            Assert.Equal(1, await settings.GetAsync("jobRuns", 0));
            Assert.Single(Directory.EnumerateFiles(Path.Combine(disk.User, "data", Id), "run-*.marker"));
            await settings.SetAsync("releaseJob", true);
            await RuntimeTest.WaitAsync(() => Task.FromResult(Assert.Single(manager.GetBackgroundTaskSnapshots(Id)).State == PluginBackgroundTaskState.Stopped));
            Assert.True((await manager.StartBackgroundTaskAsync(Id, "monitor")).Succeeded);
            await RuntimeTest.WaitAsync(async () => await settings.GetAsync("jobRuns", 0) == 2);
        }
        finally
        {
            await settings.SetAsync("releaseJob", true);
            await RuntimeTest.DisposeAsync(manager);
        }
    }

    [Theory]
    [InlineData("fault", PluginBackgroundTaskState.Faulted)]
    [InlineData("complete", PluginBackgroundTaskState.Completed)]
    public async Task Completion_is_observed_and_faults_have_only_a_safe_error_code(string mode, PluginBackgroundTaskState expected)
    {
        using var disk = Package();
        await using var manager = Manager(disk);
        await EnableAsync(manager, mode);
        Assert.True((await manager.StartBackgroundTaskAsync(Id, "monitor")).Succeeded);
        await RuntimeTest.WaitAsync(() => Task.FromResult(Assert.Single(manager.GetBackgroundTaskSnapshots(Id)).State == expected));
        var snapshot = Assert.Single(manager.GetBackgroundTaskSnapshots(Id));
        Assert.NotNull(snapshot.StartedAt);
        Assert.NotNull(snapshot.FinishedAt);
        Assert.Equal(mode == "fault" ? "BackgroundTaskFailed" : null, snapshot.ErrorCode);
        Assert.DoesNotContain("synthetic-secret-never-log", snapshot.ToString());
        Assert.All(Assert.Single(manager.Plugins).Diagnostics, d => Assert.DoesNotContain("synthetic-secret-never-log", d.Message));
        Assert.Equal(1, await manager.GetSettings(Id).GetAsync("jobRuns", 0));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Explicit_Stop_reports_Stopped_when_clean_work_finishes_before_stop_admission(bool finishDuringIntentWrite)
    {
        using var disk = Package();
        var writeEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseWrite = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var holdWrite = false;
        var runtime = new PluginRuntimeState(disk.User, async (path, document, cancellation) =>
        {
            if (holdWrite)
            {
                writeEntered.TrySetResult();
                await releaseWrite.Task.WaitAsync(cancellation);
            }
            await AtomicJsonFile.WriteAsync(path, document, cancellation);
        });
        await using var manager = new PluginManager(new() { PluginsRoot = disk.Plugins, UserRoot = disk.User }, new WindowsSecretProtector(), runtime);
        await EnableAsync(manager, "synchronous");
        Assert.True((await manager.SetBackgroundTaskResumeOnStartupAsync(Id, "monitor", true)).Succeeded);
        Assert.True((await manager.StartBackgroundTaskAsync(Id, "monitor")).Succeeded);
        await RuntimeTest.WaitAsync(() => Task.FromResult(File.Exists(Path.Combine(disk.User, "data", Id, "sync-started"))));
        var releaseWork = Path.Combine(disk.User, "data", Id, "release-sync");
        Task<PluginTaskOperationResult>? stop = null;
        try
        {
            if (!finishDuringIntentWrite)
            {
                await File.WriteAllTextAsync(releaseWork, "synthetic plugin-owned loop retired before SDK Stop");
                await RuntimeTest.WaitAsync(() => Task.FromResult(Assert.Single(manager.GetBackgroundTaskSnapshots(Id)).State == PluginBackgroundTaskState.Completed));
            }
            holdWrite = true;
            stop = manager.StopBackgroundTaskAsync(Id, "monitor");
            await writeEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (finishDuringIntentWrite)
            {
                await File.WriteAllTextAsync(releaseWork, "synthetic plugin-owned loop retired during SDK intent persistence");
                await RuntimeTest.WaitAsync(() => Task.FromResult(Assert.Single(manager.GetBackgroundTaskSnapshots(Id)).State == PluginBackgroundTaskState.Completed));
            }
            var completed = Assert.Single(manager.GetBackgroundTaskSnapshots(Id));
            Assert.Equal(PluginBackgroundTaskState.Completed, completed.State);
            Assert.True(completed.DesiredRunning);
            Assert.False(stop.IsCompleted); // Actual atomic intent persistence is still held.
            releaseWrite.TrySetResult();
            Assert.True((await stop.WaitAsync(TimeSpan.FromSeconds(5))).Succeeded);
            var stopped = Assert.Single(manager.GetBackgroundTaskSnapshots(Id));
            Assert.Equal(PluginBackgroundTaskState.Stopped, stopped.State);
            Assert.False(stopped.DesiredRunning);
            Assert.True(stopped.ResumeOnStartup); // Explicit Stop revokes running intent, not the separate preference.
            Assert.Null(stopped.ErrorCode);
            Assert.Equal(completed.StartedAt, stopped.StartedAt);
            Assert.Equal(completed.FinishedAt, stopped.FinishedAt); // Retain the actual completion timestamp.
            Assert.Equal(1, await manager.GetSettings(Id).GetAsync("jobRuns", 0));
            using var saved = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(disk.User, "background-tasks.json")));
            var intent = saved.RootElement.GetProperty("plugins").GetProperty(Id).GetProperty("tasks").GetProperty("monitor");
            Assert.False(intent.GetProperty("desiredRunning").GetBoolean());
            Assert.True(intent.GetProperty("resumeOnStartup").GetBoolean());
        }
        finally
        {
            holdWrite = false;
            releaseWrite.TrySetResult();
            await File.WriteAllTextAsync(releaseWork, "release actual synthetic work during cleanup");
            if (stop is not null) await stop.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task Explicit_Stop_of_already_faulted_work_preserves_the_genuine_error_and_completion_time()
    {
        using var disk = Package();
        await using var manager = Manager(disk);
        await EnableAsync(manager, "fault");
        Assert.True((await manager.StartBackgroundTaskAsync(Id, "monitor")).Succeeded);
        await RuntimeTest.WaitAsync(() => Task.FromResult(Assert.Single(manager.GetBackgroundTaskSnapshots(Id)).State == PluginBackgroundTaskState.Faulted));
        var failed = Assert.Single(manager.GetBackgroundTaskSnapshots(Id));
        Assert.True((await manager.StopBackgroundTaskAsync(Id, "monitor")).Succeeded);
        var stopped = Assert.Single(manager.GetBackgroundTaskSnapshots(Id));
        Assert.Equal(PluginBackgroundTaskState.Faulted, stopped.State);
        Assert.Equal("BackgroundTaskFailed", stopped.ErrorCode);
        Assert.Equal(failed.FinishedAt, stopped.FinishedAt);
        Assert.False(stopped.DesiredRunning);
        Assert.Single(Assert.Single(manager.Plugins).Diagnostics, diagnostic => diagnostic.Code == PluginDiagnosticCode.BackgroundTaskFailed);
    }

    [Fact]
    public async Task Synchronous_delegate_prefix_does_not_block_the_start_caller()
    {
        using var disk = Package();
        await using var manager = Manager(disk);
        await EnableAsync(manager, "synchronous");
        var release = Path.Combine(disk.User, "data", Id, "release-sync");
        var returned = new TaskCompletionSource<PluginTaskOperationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pulse = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var start = TestDispatcher.RunAsync(async () =>
        {
            var result = await manager.StartBackgroundTaskAsync(Id, "monitor");
            returned.TrySetResult(result);
            SynchronizationContext.Current!.Post(_ => pulse.TrySetResult(), null);
            await pulse.Task;
            // Keep the controlled dispatcher alive through actual work completion,
            // including when the negative control incorrectly runs the delegate here.
            await RuntimeTest.WaitAsync(() => Task.FromResult(Assert.Single(manager.GetBackgroundTaskSnapshots(Id)).State == PluginBackgroundTaskState.Completed));
            return result;
        });
        try
        {
            await RuntimeTest.WaitAsync(() => Task.FromResult(File.Exists(Path.Combine(disk.User, "data", Id, "sync-started"))));
            Assert.True((await returned.Task.WaitAsync(TimeSpan.FromSeconds(1))).Succeeded);
            await pulse.Task.WaitAsync(TimeSpan.FromSeconds(1));
            Assert.False(File.Exists(release));
            Assert.Equal(0, await manager.GetSettings(Id).GetAsync("jobRuns", 0)); // No delegate await ran before the held gate.
        }
        finally
        {
            await File.WriteAllTextAsync(release, "synthetic release");
            await start.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Shutdown_restores_only_opted_in_expected_work_with_Restore_reason(bool optedIn)
    {
        using var disk = Package();
        await using (var manager = Manager(disk))
        {
            await EnableAsync(manager);
            Assert.False(Assert.Single(manager.GetBackgroundTaskSnapshots(Id)).ResumeOnStartup);
            Assert.True((await manager.SetBackgroundTaskResumeOnStartupAsync(Id, "monitor", optedIn)).Succeeded);
            Assert.True((await manager.StartBackgroundTaskAsync(Id, "monitor")).Succeeded);
            await RuntimeTest.WaitAsync(async () =>
                await manager.GetSettings(Id).GetAsync("jobRuns", 0) == 1
                && await manager.GetSettings(Id).GetAsync("startReason", "") == "UserRequest");
            Assert.Equal(1, await manager.GetSettings(Id).GetAsync("jobRuns", 0));
            Assert.Equal("UserRequest", await manager.GetSettings(Id).GetAsync("startReason", ""));
            await manager.ShutdownAsync();
            Assert.Equal("HostShutdown", await manager.GetSettings(Id).GetAsync("jobStopReason", ""));
            Assert.Equal("HostShutdown", await manager.GetSettings(Id).GetAsync("contextStopReason", ""));
        }
        await using var restarted = Manager(disk);
        await restarted.DiscoverAsync();
        Assert.All(await restarted.RestoreEnabledAsync(), result => Assert.True(result.Succeeded, result.Message));
        if (optedIn) await RuntimeTest.WaitAsync(async () =>
            await restarted.GetSettings(Id).GetAsync("jobRuns", 0) == 2
            && await restarted.GetSettings(Id).GetAsync("startReason", "") == "Restore");
        Assert.Equal(optedIn ? 2 : 1, await restarted.GetSettings(Id).GetAsync("jobRuns", 0));
        if (optedIn) Assert.Equal("Restore", await restarted.GetSettings(Id).GetAsync("startReason", ""));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Explicit_stop_or_disable_clears_expected_running_intent(bool disable)
    {
        using var disk = Package();
        await using (var manager = Manager(disk))
        {
            await EnableAsync(manager);
            Assert.True((await manager.SetBackgroundTaskResumeOnStartupAsync(Id, "monitor", true)).Succeeded);
            Assert.True((await manager.StartBackgroundTaskAsync(Id, "monitor")).Succeeded);
            await RuntimeTest.WaitAsync(async () => await manager.GetSettings(Id).GetAsync("jobRuns", 0) == 1);
            if (disable) Assert.True((await manager.DisableAsync(Id)).Succeeded);
            else Assert.True((await manager.StopBackgroundTaskAsync(Id, "monitor")).Succeeded);
            Assert.Equal(disable ? "PluginDisabled" : "UserRequest", await manager.GetSettings(Id).GetAsync("jobStopReason", ""));
        }
        await using var restarted = Manager(disk);
        await restarted.DiscoverAsync();
        await restarted.RestoreEnabledAsync();
        Assert.Equal(1, await restarted.GetSettings(Id).GetAsync("jobRuns", 0));
        if (!disable) Assert.False(Assert.Single(restarted.GetBackgroundTaskSnapshots(Id)).DesiredRunning);
    }

    [Fact]
    public async Task Changed_fingerprint_requires_trust_and_cannot_inherit_task_resume_consent()
    {
        using var disk = Package();
        await using (var manager = Manager(disk))
        {
            await EnableAsync(manager);
            await manager.SetBackgroundTaskResumeOnStartupAsync(Id, "monitor", true);
            await manager.StartBackgroundTaskAsync(Id, "monitor");
            await RuntimeTest.WaitAsync(async () => await manager.GetSettings(Id).GetAsync("jobRuns", 0) == 1);
        }
        await File.WriteAllTextAsync(Path.Combine(disk.Plugins, Id, "changed.txt"), "new synthetic package data");
        await using var restarted = Manager(disk);
        await restarted.DiscoverAsync();
        Assert.Empty(await restarted.RestoreEnabledAsync());
        Assert.False((await restarted.StartBackgroundTaskAsync(Id, "monitor")).Succeeded);
        Assert.False((await restarted.EnableAsync(Id)).Succeeded);
        Assert.True((await restarted.EnableAsync(Id, confirmTrust: true)).Succeeded);
        var snapshot = Assert.Single(restarted.GetBackgroundTaskSnapshots(Id));
        Assert.False(snapshot.ResumeOnStartup);
        Assert.False(snapshot.DesiredRunning);
        Assert.Equal(PluginBackgroundTaskState.Stopped, snapshot.State);
        Assert.Equal(1, await restarted.GetSettings(Id).GetAsync("jobRuns", 0));
    }

    [Theory]
    [InlineData("initialize-probe", true)]
    [InlineData("initialize-fail", false)]
    public async Task Register_and_initialize_never_start_work_before_contributions_commit(string mode, bool succeeds)
    {
        using var disk = Package();
        await using var manager = Manager(disk);
        await manager.DiscoverAsync();
        await manager.GetSettings(Id).SetAsync("runtimeMode", mode);
        Assert.Equal(succeeds, (await manager.EnableAsync(Id, confirmTrust: true)).Succeeded);
        Assert.False(await manager.GetSettings(Id).GetAsync("startedDuringInitialize", true));
        Assert.Equal(0, await manager.GetSettings(Id).GetAsync("jobRuns", 0));
        if (!succeeds)
        {
            Assert.Empty(manager.GetBackgroundTaskSnapshots(Id));
            Assert.Empty(manager.GetSecretDefinitions(Id));
            Assert.Equal("InitializationFailed", await manager.GetSettings(Id).GetAsync("contextStopReason", ""));
        }
    }

    [Fact]
    public async Task Late_registration_is_rejected_for_both_optional_services()
    {
        var disk = Package();
        var manager = Manager(disk);
        await RuntimeTest.WithCleanupAsync([manager], async () =>
        {
            await EnableAsync(manager);
            Assert.False((await manager.ExecuteCommandAsync(Id, "late-task")).Succeeded);
            Assert.False((await manager.ExecuteCommandAsync(Id, "late-secret")).Succeeded);
            Assert.Single(manager.GetBackgroundTaskSnapshots(Id));
            Assert.Equal(2, manager.GetSecretDefinitions(Id).Count);
        }, disk);
    }

    [Fact]
    public async Task Corrupt_intent_defaults_to_no_resume_and_failed_persistence_prevents_execution()
    {
        using var disk = Package();
        Directory.CreateDirectory(disk.User);
        var path = Path.Combine(disk.User, "background-tasks.json");
        await File.WriteAllTextAsync(path, "{synthetic-secret-never-log");
        await using var manager = Manager(disk);
        await EnableAsync(manager);
        var snapshot = Assert.Single(manager.GetBackgroundTaskSnapshots(Id));
        Assert.False(snapshot.ResumeOnStartup);
        Assert.False(snapshot.DesiredRunning);
        Assert.Contains(Assert.Single(manager.Plugins).Diagnostics, d => d.Code == PluginDiagnosticCode.RuntimeStateCorrupt);
        File.Delete(path);
        Directory.CreateDirectory(path);
        Assert.False((await manager.StartBackgroundTaskAsync(Id, "monitor")).Succeeded);
        Assert.Equal(0, await manager.GetSettings(Id).GetAsync("jobRuns", 0));
        Assert.False(Assert.Single(manager.GetBackgroundTaskSnapshots(Id)).DesiredRunning);
        Assert.All(Assert.Single(manager.Plugins).Diagnostics, d => Assert.DoesNotContain("synthetic-secret-never-log", d.Message));
    }

    internal static TestDirectory Package()
    {
        var disk = new TestDirectory();
        disk.Package(Id, fields: new() { ["entryType"] = "Workbench.TestPlugin.RuntimeFixturePlugin", ["minimumHostVersion"] = "0.2.0" });
        return disk;
    }

    [Fact]
    public async Task Plugin_start_policy_rejects_host_Start_but_plugin_confirmation_can_start_and_host_can_stop()
    {
        using var disk = Package();
        await using var manager = Manager(disk);
        await manager.DiscoverAsync();
        await manager.GetSettings(Id).SetAsync("requiresPluginStart", true);
        Assert.True((await manager.EnableAsync(Id, confirmTrust: true)).Succeeded);
        Assert.False((await manager.StartBackgroundTaskAsync(Id, "monitor")).Succeeded);
        Assert.Equal(0, await manager.GetSettings(Id).GetAsync("jobRuns", 0));
        Assert.True(Assert.Single(manager.GetBackgroundTaskSnapshots(Id)).RequiresPluginStart);
        Assert.True((await manager.ExecuteCommandAsync(Id, "start-monitor")).Succeeded);
        await RuntimeTest.WaitAsync(async () => await manager.GetSettings(Id).GetAsync("jobRuns", 0) == 1);
        Assert.True((await manager.StopBackgroundTaskAsync(Id, "monitor")).Succeeded);
        Assert.Equal("UserRequest", await manager.GetSettings(Id).GetAsync("jobStopReason", ""));
    }

    [Fact]
    public async Task Plugin_start_policy_clears_old_resume_intent_and_rejects_resume_from_host_and_plugin()
    {
        using var disk = Package();
        await using (var manager = Manager(disk))
        {
            await EnableAsync(manager);
            await manager.SetBackgroundTaskResumeOnStartupAsync(Id, "monitor", true);
            await manager.StartBackgroundTaskAsync(Id, "monitor");
            await RuntimeTest.WaitAsync(async () => await manager.GetSettings(Id).GetAsync("jobRuns", 0) == 1);
            await manager.GetSettings(Id).SetAsync("requiresPluginStart", true);
        }
        await using (var restarted = Manager(disk))
        {
            await restarted.DiscoverAsync();
            await restarted.RestoreEnabledAsync();
            var snapshot = Assert.Single(restarted.GetBackgroundTaskSnapshots(Id));
            Assert.False(snapshot.ResumeOnStartup);
            Assert.False(snapshot.DesiredRunning);
            Assert.Equal(1, await restarted.GetSettings(Id).GetAsync("jobRuns", 0));
            Assert.True(snapshot.RequiresPluginStart);
            Assert.False((await restarted.SetBackgroundTaskResumeOnStartupAsync(Id, "monitor", true)).Succeeded);
            Assert.False((await restarted.ExecuteCommandAsync(Id, "resume-monitor")).Succeeded);
            Assert.True((await restarted.ExecuteCommandAsync(Id, "start-monitor")).Succeeded);
            await RuntimeTest.WaitAsync(async () => await restarted.GetSettings(Id).GetAsync("jobRuns", 0) == 2);
        }
        await using var again = Manager(disk);
        await again.DiscoverAsync();
        await again.RestoreEnabledAsync();
        Assert.Equal(2, await again.GetSettings(Id).GetAsync("jobRuns", 0));
        Assert.False(Assert.Single(again.GetBackgroundTaskSnapshots(Id)).DesiredRunning);
    }

    [Fact]
    public async Task Disable_before_restoration_also_clears_old_task_intent_for_a_later_manual_enable()
    {
        using var disk = Package();
        await using (var manager = Manager(disk))
        {
            await EnableAsync(manager);
            await manager.SetBackgroundTaskResumeOnStartupAsync(Id, "monitor", true);
            await manager.StartBackgroundTaskAsync(Id, "monitor");
            await RuntimeTest.WaitAsync(async () => await manager.GetSettings(Id).GetAsync("jobRuns", 0) == 1);
        }
        await using (var manager = Manager(disk))
        {
            await manager.DiscoverAsync();
            Assert.True((await manager.DisableAsync(Id)).Succeeded);
            Assert.True((await manager.EnableAsync(Id)).Succeeded);
        }
        await using var restarted = Manager(disk);
        await restarted.DiscoverAsync();
        await restarted.RestoreEnabledAsync();
        Assert.False(Assert.Single(restarted.GetBackgroundTaskSnapshots(Id)).DesiredRunning);
        Assert.Equal(1, await restarted.GetSettings(Id).GetAsync("jobRuns", 0));
    }

    [Fact]
    public async Task Discovery_of_a_changed_package_irrevocably_clears_old_resume_consent_even_if_files_are_reverted()
    {
        using var disk = Package();
        await using (var manager = Manager(disk))
        {
            await EnableAsync(manager);
            await manager.SetBackgroundTaskResumeOnStartupAsync(Id, "monitor", true);
            await manager.StartBackgroundTaskAsync(Id, "monitor");
            await RuntimeTest.WaitAsync(async () => await manager.GetSettings(Id).GetAsync("jobRuns", 0) == 1);
        }
        var changed = Path.Combine(disk.Plugins, Id, "temporary-change.txt");
        await File.WriteAllTextAsync(changed, "synthetic change");
        await using (var manager = Manager(disk)) await manager.DiscoverAsync();
        File.Delete(changed);
        await using var restarted = Manager(disk);
        await restarted.DiscoverAsync();
        Assert.Empty(await restarted.RestoreEnabledAsync());
        Assert.False(Assert.Single(restarted.Plugins).IsTrusted);
        Assert.True((await restarted.EnableAsync(Id, confirmTrust: true)).Succeeded);
        var snapshot = Assert.Single(restarted.GetBackgroundTaskSnapshots(Id));
        Assert.False(snapshot.DesiredRunning);
        Assert.False(snapshot.ResumeOnStartup);
        Assert.Equal(1, await restarted.GetSettings(Id).GetAsync("jobRuns", 0));
    }

    [Fact]
    public async Task Unchanged_rediscovery_preserves_reapproved_package_and_new_resume_consent()
    {
        using var disk = Package();
        await using (var first = Manager(disk))
        {
            await EnableAsync(first);
            await first.StartBackgroundTaskAsync(Id, "monitor");
            await RuntimeTest.WaitAsync(async () => await first.GetSettings(Id).GetAsync("jobRuns", 0) == 1);
            Assert.True((await first.SetBackgroundTaskResumeOnStartupAsync(Id, "monitor", true)).Succeeded);
        }
        var oldApproval = await ReadApprovalAsync(disk);
        await File.WriteAllTextAsync(Path.Combine(disk.Plugins, Id, "reapproved-version.txt"), "synthetic F2 package");
        await using (var reapproved = Manager(disk))
        {
            await reapproved.DiscoverAsync();
            Assert.Contains(Assert.Single(reapproved.Plugins).Diagnostics, d => d.Code == PluginDiagnosticCode.PackageChanged);
            Assert.False(Assert.Single(reapproved.Plugins).IsTrusted);
            Assert.Empty(await reapproved.RestoreEnabledAsync());
            Assert.True((await reapproved.EnableAsync(Id, confirmTrust: true)).Succeeded);
            Assert.True((await reapproved.StartBackgroundTaskAsync(Id, "monitor")).Succeeded);
            await RuntimeTest.WaitAsync(async () => await reapproved.GetSettings(Id).GetAsync("jobRuns", 0) == 2);
            Assert.True((await reapproved.SetBackgroundTaskResumeOnStartupAsync(Id, "monitor", true)).Succeeded);
            var newApproval = await ReadApprovalAsync(disk);
            Assert.True(newApproval.Enabled);
            Assert.NotEqual(oldApproval.Fingerprint, newApproval.Fingerprint);

            await reapproved.DiscoverAsync(); // No package files changed after approval.

            // Operations must remain usable, not just an Enabled/trusted snapshot.
            Assert.True((await reapproved.StartBackgroundTaskAsync(Id, "monitor")).Succeeded);
            Assert.True((await reapproved.SetBackgroundTaskResumeOnStartupAsync(Id, "monitor", true)).Succeeded);
            var status = Assert.Single(reapproved.Plugins);
            Assert.Equal(PluginState.Enabled, status.State);
            Assert.True(status.IsTrusted);
            Assert.True(status.DesiredEnabled);
            Assert.True(status.HasRuntime);
            Assert.Contains(status.Diagnostics, d => d.Code == PluginDiagnosticCode.PackageChanged); // Keep useful history.
            Assert.Contains(reapproved.Contributions.Commands, command => command.Id == "start-monitor");
            var task = Assert.Single(reapproved.GetBackgroundTaskSnapshots(Id));
            Assert.Equal(PluginBackgroundTaskState.Running, task.State);
            Assert.True(task.ResumeOnStartup);
            Assert.True(task.DesiredRunning);
            Assert.Equal(newApproval, await ReadApprovalAsync(disk));
            using var intent = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(disk.User, "background-tasks.json")));
            var saved = intent.RootElement.GetProperty("plugins").GetProperty(Id);
            Assert.Equal(newApproval.Fingerprint, saved.GetProperty("fingerprint").GetString());
            Assert.True(saved.GetProperty("tasks").GetProperty("monitor").GetProperty("resumeOnStartup").GetBoolean());
            Assert.True(saved.GetProperty("tasks").GetProperty("monitor").GetProperty("desiredRunning").GetBoolean());
            Assert.Equal(2, await reapproved.GetSettings(Id).GetAsync("jobRuns", 0));
            await reapproved.ShutdownAsync();
            Assert.Equal(newApproval, await ReadApprovalAsync(disk));
        }

        var restarted = Manager(disk);
        await WithRuntimeCleanupAsync(restarted, async () =>
        {
            await restarted.DiscoverAsync();
            Assert.True(Assert.Single(restarted.Plugins).IsTrusted);
            Assert.True(Assert.Single(restarted.Plugins).DesiredEnabled);
            Assert.All(await restarted.RestoreEnabledAsync(), result => Assert.True(result.Succeeded, result.Message));
            await RuntimeTest.WaitAsync(async () => await restarted.GetSettings(Id).GetAsync("jobRuns", 0) == 3
                && await restarted.GetSettings(Id).GetAsync("startReason", "") == "Restore");
            var restored = Assert.Single(restarted.GetBackgroundTaskSnapshots(Id));
            Assert.True(restored.ResumeOnStartup);
            Assert.True(restored.DesiredRunning);
            Assert.Equal(PluginBackgroundTaskState.Running, restored.State);
        });
    }

    [Fact]
    public async Task Cleanup_retries_pending_cooperative_work_and_preserves_the_body_failure()
    {
        using var disk = Package();
        var manager = Manager(disk);
        try
        {
            await EnableAsync(manager, "held");
            var settings = manager.GetSettings(Id);
            Task? cleanup = null;
            var released = false;
            try
            {
                Assert.True((await manager.StartBackgroundTaskAsync(Id, "monitor")).Succeeded);
                await RuntimeTest.WaitAsync(async () => await settings.GetAsync("jobRuns", 0) == 1);
                var bodyFailure = new Xunit.Sdk.XunitException("Synthetic assertion before cooperative cleanup.");
                cleanup = WithRuntimeCleanupAsync(manager, () => throw bodyFailure);
                // Real cancellation reaches the fixture, whose existing held
                // completion stays closed through the first actual Stop timeout.
                await RuntimeTest.WaitAsync(() => Task.FromResult(Assert.Single(manager.Plugins).Diagnostics
                    .Any(diagnostic => diagnostic.Code == PluginDiagnosticCode.StopTimedOut)));
                await RuntimeTest.WaitAsync(async () => await settings.GetAsync("stopCalls", 0) == 1
                    && await settings.GetAsync("jobStopReason", "") == "HostShutdown");
                var pending = Assert.Single(manager.Plugins);
                Assert.True(pending.HasRuntime);
                Assert.True(pending.HasPendingStop);
                Assert.Equal(PluginBackgroundTaskState.Stopping, Assert.Single(manager.GetBackgroundTaskSnapshots(Id)).State);
                Assert.Equal(1, await settings.GetAsync("instanceCount", 0));
                Assert.Equal(1, await settings.GetAsync("jobRuns", 0));
                Assert.False(await settings.GetAsync("jobFinished", false));
                var completedWhileHeld = cleanup.IsCompleted;

                await settings.SetAsync("releaseJob", true);
                released = true;
                var observed = await Record.ExceptionAsync(() => cleanup);
                Assert.Same(bodyFailure, observed);
                Assert.False(completedWhileHeld, "Cleanup must retain ownership until the held cooperative completion is released.");
                Assert.False(Assert.Single(manager.Plugins).HasRuntime);
                Assert.False(manager.HasPendingStops);
                Assert.True(await settings.GetAsync("jobFinished", false));
                Assert.Equal(1, await settings.GetAsync("instanceCount", 0));
                Assert.Equal(1, await settings.GetAsync("jobRuns", 0));
                Assert.Equal(1, await settings.GetAsync("stopCalls", 0));
            }
            finally
            {
                if (!released) await settings.SetAsync("releaseJob", true);
                if (cleanup is not null) _ = await Record.ExceptionAsync(() => cleanup); // Observe even when a covering assertion failed.
            }
        }
        finally { await RuntimeTest.DisposeAsync(manager); }
    }

    private static Task WithRuntimeCleanupAsync(PluginManager manager, Func<Task> body)
        => RuntimeTest.WithCleanupAsync([manager], body);

    private static async Task<(bool Enabled, string? Fingerprint)> ReadApprovalAsync(TestDirectory disk)
    {
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(disk.User, "plugin-state.json")));
        var approval = json.RootElement.GetProperty("plugins").GetProperty(Id);
        return (approval.GetProperty("enabled").GetBoolean(), approval.GetProperty("trustedFingerprint").GetString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Incomplete_package_revocation_retries_before_fresh_approval(bool blockApprovalFile)
    {
        var disk = Package();
        List<PluginManager> managers = [];
        await RuntimeTest.WithCleanupAsync(managers, RunAsync, disk);
        async Task RunAsync()
        {
            var first = Manager(disk);
            managers.Add(first);
            await EnableAsync(first);
            await first.StartBackgroundTaskAsync(Id, "monitor");
            await RuntimeTest.WaitAsync(async () => await first.GetSettings(Id).GetAsync("jobRuns", 0) == 1);
            await first.SetBackgroundTaskResumeOnStartupAsync(Id, "monitor", true);
            await RuntimeTest.DisposeAsync(first);
            await File.WriteAllTextAsync(Path.Combine(disk.Plugins, Id, "pending-version.txt"), "synthetic changed package");
            var writer = new ControlledRuntimeStateWriter();
            writer.HoldNextWrite();
            writer.ReleaseFirstWrite.TrySetResult();
            var manager = new PluginManager(new() { PluginsRoot = disk.Plugins, UserRoot = disk.User },
                new WindowsSecretProtector(), new PluginRuntimeState(disk.User, writer.WriteAsync));
            managers.Add(manager);
            var blockedPath = Path.Combine(disk.User, blockApprovalFile ? "plugin-state.json" : "background-tasks.json");
            var backup = Path.Combine(disk.User, "revocation-backup.json");
            var failureCode = blockApprovalFile ? PluginDiagnosticCode.StatePersistenceFailed : PluginDiagnosticCode.RuntimeStatePersistenceFailed;
            var discovery = manager.DiscoverAsync();
            await RuntimeTest.WithCleanupAsync([], async () =>
            {
                await writer.ClearWriteEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                File.Move(blockedPath, backup);
                Directory.CreateDirectory(blockedPath); // Real atomic replacement must fail.
                writer.ReleaseClearWrite.TrySetResult();
                await discovery;
                Assert.Contains(Assert.Single(manager.Plugins).Diagnostics, diagnostic => diagnostic.Code == failureCode);
                await manager.DiscoverAsync(); // The files are unchanged, but revocation is still pending.
                Assert.Equal(2, Assert.Single(manager.Plugins).Diagnostics.Count(diagnostic => diagnostic.Code == failureCode));

                Assert.False((await manager.EnableAsync(Id, confirmTrust: true)).Succeeded);
                Assert.False(Assert.Single(manager.Plugins).IsTrusted);
                Assert.False(Assert.Single(manager.Plugins).IsLoaded);
                if (!blockApprovalFile)
                    Assert.Null((await ReadApprovalAsync(disk)).Fingerprint); // No F2 grant before the consent clear succeeds.

                Directory.Delete(blockedPath);
                File.Move(backup, blockedPath);
                Assert.True((await manager.EnableAsync(Id, confirmTrust: true)).Succeeded);
                var task = Assert.Single(manager.GetBackgroundTaskSnapshots(Id));
                Assert.False(task.ResumeOnStartup);
                Assert.False(task.DesiredRunning);
                Assert.Equal(1, await manager.GetSettings(Id).GetAsync("jobRuns", 0));
                var approval = await ReadApprovalAsync(disk);
                Assert.True(approval.Enabled);
                Assert.NotNull(approval.Fingerprint);
                await manager.DiscoverAsync();
                Assert.Equal(approval, await ReadApprovalAsync(disk));
                Assert.True((await manager.StartBackgroundTaskAsync(Id, "monitor")).Succeeded);
            }, beforeCleanup: async () =>
            {
                writer.ReleaseFirstWrite.TrySetResult();
                writer.ReleaseClearWrite.TrySetResult();
                await discovery;
                if (Directory.Exists(blockedPath)) Directory.Delete(blockedPath);
                if (File.Exists(backup)) File.Move(backup, blockedPath, overwrite: true);
            });
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Retained_runtime_cannot_recreate_invalidated_consent_from_queued_or_context_operations(bool disable)
    {
        using var disk = Package();
        var writer = new ControlledRuntimeStateWriter();
        var manager = new PluginManager(new() { PluginsRoot = disk.Plugins, UserRoot = disk.User },
            new WindowsSecretProtector(), new PluginRuntimeState(disk.User, writer.WriteAsync));
        var changedPath = Path.Combine(disk.Plugins, Id, "retained-change.txt");
        Task? first = null;
        Task? queued = null;
        Task? discovering = null;
        await EnableAsync(manager, "retained-context");
        var settings = manager.GetSettings(Id);
        await manager.StartBackgroundTaskAsync(Id, "monitor");
        await RuntimeTest.WaitAsync(async () => await settings.GetAsync("retainedContextReady", false));
        Assert.True((await manager.SetBackgroundTaskResumeOnStartupAsync(Id, "monitor", true)).Succeeded);
        try
        {
            writer.HoldNextWrite();
            var operationA = manager.SetBackgroundTaskResumeOnStartupAsync(Id, "monitor", false);
            first = operationA;
            await writer.FirstWriteEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var operationB = manager.SetBackgroundTaskResumeOnStartupAsync(Id, "monitor", true);
            queued = operationB;
            Assert.False(operationB.IsCompleted); // It passed ActiveContext and queued behind A.
            await File.WriteAllTextAsync(changedPath, "synthetic changed package");
            var discovery = manager.DiscoverAsync();
            discovering = discovery;
            await RuntimeTest.WaitAsync(() => Task.FromResult(!Assert.Single(manager.Plugins).IsTrusted));
            Assert.True(Assert.Single(manager.Plugins).HasRuntime);
            Assert.Equal(0, await settings.GetAsync("stopCalls", 0));
            Assert.False(await settings.GetAsync("retainedLifetimeCanceled", false));
            writer.ReleaseFirstWrite.TrySetResult();
            await writer.ClearWriteEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            writer.ReleaseClearWrite.TrySetResult();
            await discovery;
            Assert.False((await operationA).Succeeded);
            var queuedResult = await operationB;
            Assert.False(queuedResult.Succeeded);
            var snapshot = Assert.Single(manager.GetBackgroundTaskSnapshots(Id));
            Assert.False(snapshot.ResumeOnStartup);
            Assert.False(snapshot.DesiredRunning);
            Assert.Equal(PluginBackgroundTaskState.Running, snapshot.State);
            Assert.Equal(0, await settings.GetAsync("stopCalls", 0));
            Assert.False(await settings.GetAsync("retainedLifetimeCanceled", false));
            Assert.False(await settings.GetAsync("jobFinished", false));

            foreach (var (operation, request) in new[] { ("resume", 1), ("start", 2) })
            {
                await settings.SetAsync("contextOperation", operation);
                await settings.SetAsync("contextRequest", request);
                await RuntimeTest.WaitAsync(async () => await settings.GetAsync("contextHandled", 0) == request);
                Assert.False(await settings.GetAsync("contextResult", true));
            }
            await AssertNoTaskConsentAsync(disk);
            if (disable) Assert.True((await manager.DisableAsync(Id)).Succeeded);
            else await manager.ShutdownAsync();
            await AssertNoTaskConsentAsync(disk); // Cleanup must not repopulate the old fingerprint.
            Assert.Equal(1, await settings.GetAsync("stopCalls", 0));
            File.Delete(changedPath);
        }
        finally
        {
            writer.ReleaseFirstWrite.TrySetResult();
            writer.ReleaseClearWrite.TrySetResult();
            await Task.WhenAll(new[] { first, queued, discovering }.OfType<Task>()).WaitAsync(TimeSpan.FromSeconds(5));
            await RuntimeTest.DisposeAsync(manager);
        }

        await using var restarted = Manager(disk);
        await restarted.DiscoverAsync();
        var package = Assert.Single(restarted.Plugins);
        Assert.False(package.IsTrusted);
        Assert.False(package.DesiredEnabled);
        Assert.Empty(await restarted.RestoreEnabledAsync());
        Assert.Equal(1, await restarted.GetSettings(Id).GetAsync("jobRuns", 0));
        Assert.False((await restarted.EnableAsync(Id)).Succeeded);
        Assert.True((await restarted.EnableAsync(Id, confirmTrust: true)).Succeeded);
        var fresh = Assert.Single(restarted.GetBackgroundTaskSnapshots(Id));
        Assert.False(fresh.ResumeOnStartup);
        Assert.False(fresh.DesiredRunning);
        Assert.Equal(PluginBackgroundTaskState.Stopped, fresh.State);
    }

    private static async Task AssertNoTaskConsentAsync(TestDirectory disk)
    {
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(disk.User, "background-tasks.json")));
        Assert.False(json.RootElement.GetProperty("plugins").TryGetProperty(Id, out _));
    }

    // The actual store still serializes and atomically writes real files. This
    // dependency wrapper only controls when its first write and clear may finish.
    private sealed class ControlledRuntimeStateWriter
    {
        private int _holdNext;
        private bool _holdClear;
        internal TaskCompletionSource FirstWriteEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource ReleaseFirstWrite { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource ClearWriteEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource ReleaseClearWrite { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal void HoldNextWrite() { _holdClear = true; Interlocked.Exchange(ref _holdNext, 1); }

        internal async Task WriteAsync(string path, RuntimeStateDocument document, CancellationToken cancellationToken)
        {
            if (Interlocked.Exchange(ref _holdNext, 0) == 1)
            {
                FirstWriteEntered.TrySetResult();
                await ReleaseFirstWrite.Task.WaitAsync(cancellationToken);
            }
            if (_holdClear && !document.Plugins.ContainsKey(Id))
            {
                _holdClear = false;
                ClearWriteEntered.TrySetResult();
                await ReleaseClearWrite.Task.WaitAsync(cancellationToken);
            }
            await AtomicJsonFile.WriteAsync(path, document, cancellationToken);
        }
    }

    internal static PluginManager Manager(TestDirectory disk, TimeSpan? stopTimeout = null) => new(new()
    {
        PluginsRoot = disk.Plugins,
        UserRoot = disk.User,
        StopTimeout = stopTimeout ?? TimeSpan.FromSeconds(2)
    });

    private static async Task EnableAsync(PluginManager manager, string mode = "cooperative")
    {
        await manager.DiscoverAsync();
        await manager.GetSettings(Id).SetAsync("runtimeMode", mode);
        var result = await manager.EnableAsync(Id, confirmTrust: true);
        Assert.True(result.Succeeded, result.Message);
    }
}
