using System.Text.Json;
using Workbench.App;
using Workbench.Core.Projects;
using Workbench.TestDependency;
using Xunit;

namespace Workbench.Core.Tests;

public sealed class WorkspaceRuntimeCoordinatorTests
{
    private const string PluginId = "com.example.workspace";

    [Fact]
    public async Task Normal_close_supersedes_queued_switch_but_still_saves_live_hook_only_draft()
    {
        using var fixture = new Fixture();
        var target = await fixture.CreateTargetAsync();
        using var gate = fixture.Gate(fixture.Disk.User);
        gate.PrivateDraft = "queued-switch private draft";
        var coordinator = fixture.Coordinator();
        Assert.True((await coordinator.InitializeAsync()).Succeeded);
        Assert.True((await coordinator.Runtime!.Manager.EnableAsync(PluginId, confirmTrust: true)).Succeeded);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var action = coordinator.RunCurrentAsync(async (_, _, _) => { entered.SetResult(); await release.Task; });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var change = coordinator.SwitchAsync(target.Id);
        var close = coordinator.PrepareCloseAsync();
        try
        {
            Assert.False(coordinator.CanUseCurrentRuntime);
            Assert.False(close.IsCompleted);
            Assert.False(gate.Prepared.Task.IsCompleted);
            release.TrySetResult();
            await action;
            Assert.False((await change).Succeeded);
            Assert.True((await close).Succeeded);
            Assert.Equal(1, fixture.FactoryCalls);
            Assert.Equal("queued-switch private draft", await File.ReadAllTextAsync(Path.Combine(fixture.Disk.User, "data", PluginId, "prepared-draft.txt")));
            Assert.False(gate.StopStarted.Task.IsCompleted);
        }
        finally { release.TrySetResult(); await action; await coordinator.DisposeAsync(); }
        Assert.Equal(0, gate.Live);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Normal_close_after_blocked_switch_keeps_failed_or_pending_live_preparation_owned(bool held)
    {
        using var fixture = new Fixture();
        var target = await fixture.CreateTargetAsync();
        using var gate = fixture.Gate(fixture.Disk.User, holdPrepare: held);
        gate.FailPreparation = !held;
        gate.PrivateDraft = "blocked-switch private draft";
        var coordinator = fixture.Coordinator();
        Assert.True((await coordinator.InitializeAsync()).Succeeded);
        Assert.True((await coordinator.Runtime!.Manager.EnableAsync(PluginId, confirmTrust: true)).Succeeded);
        try
        {
            Assert.False((await coordinator.SwitchAsync(target.Id)).Succeeded);
            var close = await coordinator.PrepareCloseAsync();
            Assert.False(close.Succeeded);
            Assert.Equal(held, close.Pending);
            Assert.Equal(!held, coordinator.CanUseCurrentRuntime);
            Assert.Equal(!held, coordinator.Document!.IsEditable);
            Assert.Equal(1, gate.Live);
            Assert.False(gate.StopStarted.Task.IsCompleted);
            Assert.Equal(1, fixture.FactoryCalls);
            gate.FailPreparation = false;
            gate.PrepareRelease.TrySetResult();
            Assert.True((await coordinator.PrepareCloseAsync()).Succeeded);
            if (held) Assert.Single(gate.Events, item => item == "prepare");
            Assert.Equal("blocked-switch private draft", await File.ReadAllTextAsync(Path.Combine(fixture.Disk.User, "data", PluginId, "prepared-draft.txt")));
        }
        finally { gate.PrepareRelease.TrySetResult(); await coordinator.DisposeAsync(); }
        Assert.Equal(0, gate.Live);
    }

    [Fact]
    public async Task Normal_close_prepares_private_draft_before_stop_and_keeps_success_sealed()
    {
        using var fixture = new Fixture();
        using var gate = fixture.Gate(fixture.Disk.User, holdPrepare: true);
        var coordinator = fixture.Coordinator();
        Assert.True((await coordinator.InitializeAsync()).Succeeded);
        Assert.True((await coordinator.Runtime!.Manager.EnableAsync(PluginId, confirmTrust: true)).Succeeded);
        coordinator.Document!.Edit("owned close draft");
        gate.PrivateDraft = "private plugin draft at close";
        try
        {
            var close = coordinator.PrepareCloseAsync();
            Assert.False(coordinator.CanUseCurrentRuntime);
            var pending = await close;
            Assert.True(pending.Pending);
            Assert.True(gate.Prepared.Task.IsCompleted);
            Assert.False(gate.StopStarted.Task.IsCompleted);
            Assert.False(coordinator.Document.IsEditable);
            Assert.False((await coordinator.CreateAndSwitchAsync("Must not open")).Succeeded);
            Assert.Equal(1, fixture.FactoryCalls);
            Assert.ThrowsAny<IOException>(() => { using var duplicate = HostRuntime.AcquireLease(fixture.Disk.User); });
            gate.PrepareRelease.TrySetResult();
            Assert.True((await coordinator.PrepareCloseAsync()).Succeeded);
            Assert.False(coordinator.CanUseCurrentRuntime);
            Assert.False(gate.StopStarted.Task.IsCompleted);
            Assert.Equal("private plugin draft at close", await File.ReadAllTextAsync(Path.Combine(fixture.Disk.User, "data", PluginId, "prepared-draft.txt")));
            Assert.Equal("owned close draft", (await coordinator.Scope!.Documents.ReadAsync()).Text);
        }
        finally { gate.PrepareRelease.TrySetResult(); await coordinator.DisposeAsync(); }
        Assert.Equal(0, gate.Live);
        using var next = HostRuntime.AcquireLease(fixture.Disk.User);
    }

    [Fact]
    public async Task Settled_normal_close_hook_failure_reopens_same_live_plugin_and_editor_for_correction()
    {
        using var fixture = new Fixture();
        using var gate = fixture.Gate(fixture.Disk.User);
        gate.FailPreparation = true;
        gate.PrivateDraft = "private correction needed";
        await using var coordinator = fixture.Coordinator();
        Assert.True((await coordinator.InitializeAsync()).Succeeded);
        Assert.True((await coordinator.Runtime!.Manager.EnableAsync(PluginId, confirmTrust: true)).Succeeded);
        var manager = coordinator.Runtime.Manager;
        var result = await coordinator.PrepareCloseAsync();
        Assert.False(result.Succeeded);
        Assert.False(result.Pending);
        Assert.Equal("ProjectPreparationFailed", result.ErrorCode);
        Assert.True(coordinator.CanUseCurrentRuntime);
        Assert.True(coordinator.Document!.IsEditable);
        Assert.Same(manager, coordinator.Runtime.Manager);
        Assert.Equal(1, gate.Live);
        Assert.False(gate.StopStarted.Task.IsCompleted);
        coordinator.Document.Edit("corrected before close");
        await coordinator.RunCurrentAsync((runtime, _, _) => runtime.Manager.GetSettings(PluginId).SetAsync("correction", true));
        gate.FailPreparation = false;
        gate.PrivateDraft = "corrected private draft";
        Assert.True((await coordinator.PrepareCloseAsync()).Succeeded);
        Assert.Equal("corrected private draft", await File.ReadAllTextAsync(Path.Combine(fixture.Disk.User, "data", PluginId, "prepared-draft.txt")));
        Assert.Equal("corrected before close", (await coordinator.Scope!.Documents.ReadAsync()).Text);
    }

    [Fact]
    public async Task Canceled_normal_close_retains_ignored_hook_and_retry_joins_it_before_any_stop()
    {
        using var fixture = new Fixture();
        using var gate = fixture.Gate(fixture.Disk.User, holdPrepare: true);
        var coordinator = fixture.Coordinator();
        Assert.True((await coordinator.InitializeAsync()).Succeeded);
        Assert.True((await coordinator.Runtime!.Manager.EnableAsync(PluginId, confirmTrust: true)).Succeeded);
        using var cancel = new CancellationTokenSource();
        try
        {
            var close = coordinator.PrepareCloseAsync(cancel.Token);
            await gate.Prepared.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cancel.Cancel();
            Assert.True((await close).Pending);
            Assert.False(gate.StopStarted.Task.IsCompleted);
            Assert.False(coordinator.CanUseCurrentRuntime);
            Assert.False((await coordinator.PrepareCloseAsync()).Succeeded);
            Assert.False(gate.StopStarted.Task.IsCompleted);
            gate.PrepareRelease.TrySetResult();
            Assert.True((await coordinator.PrepareCloseAsync()).Succeeded);
        }
        finally { gate.PrepareRelease.TrySetResult(); await coordinator.DisposeAsync(); }
        Assert.Equal(0, gate.Live);
    }

    [Fact]
    public async Task Normal_close_editor_error_reopens_correction_without_retiring_manager()
    {
        using var fixture = new Fixture();
        using var gate = fixture.Gate(fixture.Disk.User);
        await using var coordinator = fixture.Coordinator();
        Assert.True((await coordinator.InitializeAsync()).Succeeded);
        Assert.True((await coordinator.Runtime!.Manager.EnableAsync(PluginId, confirmTrust: true)).Succeeded);
        coordinator.Document!.Edit(new string('x', 1024 * 1024 + 1));
        var failed = await coordinator.PrepareCloseAsync();
        Assert.False(failed.Succeeded);
        Assert.False(failed.Pending);
        Assert.True(coordinator.CanUseCurrentRuntime);
        Assert.True(coordinator.Document.IsEditable);
        Assert.False(gate.StopStarted.Task.IsCompleted);
        coordinator.Document.Edit("short corrected close draft");
        Assert.True((await coordinator.PrepareCloseAsync()).Succeeded);
        Assert.Equal("short corrected close draft", (await coordinator.Scope!.Documents.ReadAsync()).Text);
    }

    [Fact]
    public async Task Close_joins_admitted_action_and_prevents_queued_target_activation()
    {
        using var fixture = new Fixture();
        var target = await fixture.CreateTargetAsync();
        var coordinator = fixture.Coordinator();
        Assert.True((await coordinator.InitializeAsync()).Succeeded);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var action = coordinator.RunCurrentAsync(async (_, _, _) => { entered.SetResult(); await release.Task; });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var change = coordinator.SwitchAsync(target.Id);
        Assert.False(coordinator.CanUseCurrentRuntime);
        Assert.Throws<ProjectWorkspaceStoreException>(() => { _ = coordinator.RunCurrentAsync((_, _, _) => Task.CompletedTask); });
        var close = coordinator.DisposeAsync().AsTask();
        Assert.False(close.IsCompleted);
        Assert.Equal(1, fixture.FactoryCalls);
        Assert.ThrowsAny<IOException>(() => { using var duplicate = HostRuntime.AcquireLease(fixture.Disk.User); });
        release.SetResult();
        await action;
        var result = await change;
        await close;
        Assert.False(result.Succeeded);
        Assert.Equal(1, fixture.FactoryCalls);
        using var next = HostRuntime.AcquireLease(fixture.Disk.User);
    }

    [Fact]
    public async Task BaseLeaseExcludesLegacyWhileSecondaryActive()
    {
        using var fixture = new Fixture();
        await using var coordinator = fixture.Coordinator();
        Assert.True((await coordinator.InitializeAsync()).Succeeded);
        Assert.True((await coordinator.CreateAndSwitchAsync("Second")).Succeeded);
        Assert.NotEqual("default", coordinator.ActiveProject!.Id);
        Assert.ThrowsAny<IOException>(() => { using var duplicate = HostRuntime.AcquireLease(fixture.Disk.User); });
        Assert.ThrowsAny<IOException>(() => { using var duplicate = HostRuntime.AcquireLease(coordinator.Scope!.UserRoot); });
    }

    [Fact]
    public async Task SwitchFlushesLatestTextBeforeStoppingAndStartsOnlyOneRuntime()
    {
        using var fixture = new Fixture();
        var target = await fixture.CreateTargetAsync();
        using var oldGate = fixture.Gate(fixture.Disk.User, holdStop: true);
        await using var coordinator = fixture.Coordinator(root => Assert.Equal(0, oldGate.Live));
        Assert.True((await coordinator.InitializeAsync()).Succeeded);
        Assert.True((await coordinator.Runtime!.Manager.EnableAsync(PluginId, confirmTrust: true)).Succeeded);
        coordinator.Document!.Edit("latest before stop");
        var change = coordinator.SwitchAsync(target.Id);
        await Task.WhenAny(change, oldGate.StopStarted.Task).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(oldGate.StopStarted.Task.IsCompleted);
        Assert.Equal("latest before stop", (await coordinator.Scope!.Documents.ReadAsync()).Text);
        Assert.False((await change).Succeeded);
        Assert.Equal(1, fixture.FactoryCalls);
        oldGate.StopRelease.TrySetResult();
        Assert.True((await coordinator.RetryAsync()).Succeeded);
        Assert.Equal(0, oldGate.Live);
        Assert.Equal(2, fixture.FactoryCalls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PendingOldStopOrPreparationDoesNotOpenTarget(bool preparation)
    {
        using var fixture = new Fixture();
        var target = await fixture.CreateTargetAsync();
        using var gate = fixture.Gate(fixture.Disk.User, holdPrepare: preparation, holdStop: !preparation);
        await using var coordinator = fixture.Coordinator();
        Assert.True((await coordinator.InitializeAsync()).Succeeded);
        Assert.True((await coordinator.Runtime!.Manager.EnableAsync(PluginId, confirmTrust: true)).Succeeded);
        try
        {
            var pending = await coordinator.SwitchAsync(target.Id);
            Assert.True(pending.Pending);
            Assert.Equal(1, fixture.FactoryCalls);
            Assert.False((await coordinator.SwitchAsync("default")).Succeeded);
            Assert.Equal("default", coordinator.ActiveProject!.Id);
            Assert.ThrowsAny<IOException>(() => { using var duplicate = HostRuntime.AcquireLease(fixture.Disk.User); });
        }
        finally { gate.PrepareRelease.TrySetResult(); gate.StopRelease.TrySetResult(); }
        Assert.True((await coordinator.RetryAsync()).Succeeded);
        Assert.Equal(0, gate.Live);
    }

    [Fact]
    public async Task Quiesced_editor_refuses_fresh_recovery_io_while_old_stop_is_pending()
    {
        using var fixture = new Fixture();
        var target = await fixture.CreateTargetAsync();
        using var gate = fixture.Gate(fixture.Disk.User, holdStop: true);
        await using var coordinator = fixture.Coordinator();
        Assert.True((await coordinator.InitializeAsync()).Succeeded);
        Assert.True((await coordinator.Runtime!.Manager.EnableAsync(PluginId, confirmTrust: true)).Succeeded);
        try
        {
            Assert.True((await coordinator.SwitchAsync(target.Id)).Pending);
            Assert.False(coordinator.Document!.IsEditable);
            Assert.Throws<InvalidOperationException>(() => { _ = coordinator.Document.PreserveConflictAndReloadAsync(); });
        }
        finally { gate.StopRelease.TrySetResult(); }
        Assert.True((await coordinator.RetryAsync()).Succeeded);
    }

    [Fact]
    public async Task Preparation_failure_is_safe_and_keeps_document_correctable_until_retry()
    {
        using var fixture = new Fixture();
        var target = await fixture.CreateTargetAsync();
        using var gate = fixture.Gate(fixture.Disk.User);
        gate.FailPreparation = true;
        await using var coordinator = fixture.Coordinator();
        Assert.True((await coordinator.InitializeAsync()).Succeeded);
        Assert.True((await coordinator.Runtime!.Manager.EnableAsync(PluginId, confirmTrust: true)).Succeeded);
        var failure = await coordinator.SwitchAsync(target.Id);
        Assert.Equal("ProjectPreparationFailed", failure.ErrorCode);
        Assert.False(failure.Pending);
        Assert.Equal(1, fixture.FactoryCalls);
        Assert.Equal(1, gate.Live);
        Assert.True(coordinator.Document!.IsEditable);
        coordinator.Document.Edit("correction after preparation failed");
        gate.FailPreparation = false;
        Assert.True((await coordinator.RetryAsync()).Succeeded);
        Assert.Equal("correction after preparation failed", (await new ProjectContextDocumentStore(fixture.Disk.User, "default").ReadAsync()).Text);
        Assert.Equal(0, gate.Live);
    }

    [Fact]
    public async Task CandidateFailureDoesNotRestoreOldBeforeCandidateActuallyRetires()
    {
        using var fixture = new Fixture();
        var target = await fixture.CreateTargetAsync(seedEnabled: true);
        using var old = fixture.Gate(fixture.Disk.User);
        using var candidate = fixture.Gate(fixture.Root(target.Id), holdStop: true);
        candidate.FailInitialization = true;
        var coordinator = fixture.Coordinator();
        Assert.True((await coordinator.InitializeAsync()).Succeeded);
        Assert.True((await coordinator.Runtime!.Manager.EnableAsync(PluginId, confirmTrust: true)).Succeeded);
        var oldManager = coordinator.Runtime.Manager;
        var generation = coordinator.RuntimeGeneration;
        try
        {
            var result = await coordinator.SwitchAsync(target.Id);
            Assert.True(result.Pending);
            Assert.Equal("default", coordinator.ActiveProject!.Id);
            Assert.Equal(0, old.Live);
            Assert.Equal(1, candidate.Live);
            Assert.False((await coordinator.SwitchAsync("default")).Succeeded);
            Assert.ThrowsAny<IOException>(() => { using var duplicate = HostRuntime.AcquireLease(fixture.Root(target.Id)); });
        }
        finally { candidate.StopRelease.TrySetResult(); }
        var retired = await coordinator.RetryAsync();
        Assert.False(retired.Succeeded);
        Assert.False(retired.Pending);
        Assert.Equal(0, candidate.Live);
        Assert.Equal(1, old.Live);
        Assert.NotSame(oldManager, coordinator.Runtime!.Manager);
        Assert.True(coordinator.RuntimeGeneration > generation);
        Assert.True(coordinator.CanUseCurrentRuntime);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => oldManager.EnableAsync(PluginId));
        await coordinator.DisposeAsync();
    }

    [Fact]
    public async Task Admitted_host_action_finishes_before_replacement_runtime_starts()
    {
        using var fixture = new Fixture();
        var target = await fixture.CreateTargetAsync();
        var finished = false;
        await using var coordinator = fixture.Coordinator(_ => Assert.True(finished));
        Assert.True((await coordinator.InitializeAsync()).Succeeded);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var action = coordinator.RunCurrentAsync(async (runtime, _, _) =>
        {
            entered.SetResult();
            await release.Task;
            await runtime.Manager.GetSettings(PluginId).SetAsync("completed-action", "owned");
            finished = true;
        });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var change = coordinator.SwitchAsync(target.Id);
        Assert.Equal(1, fixture.FactoryCalls);
        Assert.False(change.IsCompleted);
        release.TrySetResult();
        await action;
        Assert.True((await change).Succeeded);
        Assert.Equal(2, fixture.FactoryCalls);
        Assert.True((await coordinator.SwitchAsync("default")).Succeeded);
        Assert.Equal("owned", await coordinator.Runtime!.Manager.GetSettings(PluginId).GetAsync("completed-action", "missing"));
    }

    [Fact]
    public async Task Canceled_candidate_startup_stays_owned_and_close_joins_the_actual_initializer()
    {
        using var fixture = new Fixture();
        var target = await fixture.CreateTargetAsync(seedEnabled: true);
        using var candidate = fixture.Gate(fixture.Root(target.Id), holdInitialize: true);
        var coordinator = fixture.Coordinator();
        Assert.True((await coordinator.InitializeAsync()).Succeeded);
        using var cancel = new CancellationTokenSource();
        var change = coordinator.SwitchAsync(target.Id, cancel.Token);
        await candidate.Initialized.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancel.Cancel();
        Assert.True((await change).Pending);
        Assert.False((await coordinator.SwitchAsync("default")).Succeeded);
        var close = coordinator.DisposeAsync().AsTask();
        Assert.False(close.IsCompleted);
        Assert.Equal(1, candidate.Live);
        Assert.False(candidate.StopStarted.Task.IsCompleted);
        Assert.ThrowsAny<IOException>(() => { using var duplicate = HostRuntime.AcquireLease(fixture.Disk.User); });
        candidate.InitializeRelease.TrySetResult();
        await close.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, candidate.Live);
        using var next = HostRuntime.AcquireLease(fixture.Disk.User);
    }

    [Fact]
    public async Task Catalog_rename_archive_and_explicit_recovery_preserve_scope_and_owned_roots()
    {
        using var fixture = new Fixture();
        var store = new ProjectWorkspaceCatalog(fixture.Disk.User);
        await store.OpenAsync();
        var original = await File.ReadAllBytesAsync(Path.Combine(fixture.Disk.User, ".workspaces", "catalog.json"));
        var orphan = await fixture.CreateTargetAsync();
        await File.WriteAllBytesAsync(Path.Combine(fixture.Disk.User, ".workspaces", "catalog.json"), original);
        await using var coordinator = fixture.Coordinator();
        Assert.True((await coordinator.InitializeAsync()).Succeeded);
        Assert.Equal(orphan.Id, Assert.Single(await coordinator.GetRecoverableAsync()).Id);
        Assert.True((await coordinator.RecoverProjectAsync(orphan.Id)).Succeeded);
        Assert.Equal(orphan.Id, coordinator.Scope!.Id);
        Assert.True((await coordinator.RenameAsync("  Renamed  ")).Succeeded);
        Assert.Equal("Renamed", coordinator.Scope.Identity.Name);
        var snapshot = await store.OpenAsync();
        await store.RenameAsync("default", "Outside", snapshot.Revision);
        Assert.Equal("RevisionConflict", (await coordinator.RenameAsync("Must not become identity")).ErrorCode);
        Assert.Equal("Renamed", coordinator.Scope.Identity.Name);
        Assert.True((await coordinator.RetryAsync()).Succeeded);
        Assert.Equal("ActiveProjectArchived", (await coordinator.SetArchivedAsync(orphan.Id, true)).ErrorCode);
        Assert.True((await coordinator.SwitchAsync("default")).Succeeded);
        Assert.True((await coordinator.SetArchivedAsync(orphan.Id, true)).Succeeded);
        Assert.Equal("ActiveProjectArchived", (await coordinator.SwitchAsync(orphan.Id)).ErrorCode);
        Assert.True(Directory.Exists(fixture.Root(orphan.Id)));
        Assert.True((await coordinator.SetArchivedAsync(orphan.Id, false)).Succeeded);
        Assert.True((await coordinator.SwitchAsync(orphan.Id)).Succeeded);
    }

    [Fact]
    public async Task Corrupt_catalog_stays_blocked_until_explicit_owned_history_recovery()
    {
        using var fixture = new Fixture();
        await fixture.CreateTargetAsync();
        var path = Path.Combine(fixture.Disk.User, ".workspaces", "catalog.json");
        await File.WriteAllTextAsync(path, "{broken}");
        await using var coordinator = fixture.Coordinator();
        Assert.Equal("InvalidCatalog", (await coordinator.InitializeAsync()).ErrorCode);
        Assert.Null(coordinator.Catalog);
        Assert.Null(coordinator.Runtime);
        Assert.Equal(0, fixture.FactoryCalls);
        Assert.ThrowsAny<IOException>(() => { using var duplicate = HostRuntime.AcquireLease(fixture.Disk.User); });
        Assert.True((await coordinator.RecoverCatalogAsync()).Succeeded);
        Assert.Equal("default", coordinator.ActiveProject!.Id);
        var retained = Assert.Single(Directory.EnumerateFiles(Path.Combine(fixture.Disk.User, ".workspaces", "catalog-recovery")));
        Assert.Equal("{broken}", await File.ReadAllTextAsync(retained));
    }

    [Fact]
    public async Task Failed_dirty_editor_close_keeps_secondary_root_leased_until_buffer_is_preserved()
    {
        using var fixture = new Fixture();
        var coordinator = fixture.Coordinator();
        Assert.True((await coordinator.InitializeAsync()).Succeeded);
        Assert.True((await coordinator.CreateAndSwitchAsync("Second")).Succeeded);
        var root = coordinator.Scope!.UserRoot;
        await coordinator.Scope.Documents.WriteAsync("outside", "missing");
        coordinator.Document!.Edit("dirty on close");
        await Assert.ThrowsAsync<ProjectWorkspaceStoreException>(() => coordinator.Document.FlushAsync());
        await Assert.ThrowsAsync<ProjectWorkspaceStoreException>(() => coordinator.DisposeAsync().AsTask());
        Assert.True(coordinator.Document.IsDirty);
        Assert.Equal("dirty on close", coordinator.Document.Text);
        try
        {
            Assert.ThrowsAny<IOException>(() => { using var duplicate = HostRuntime.AcquireLease(root); });
            Assert.ThrowsAny<IOException>(() => { using var duplicate = HostRuntime.AcquireLease(fixture.Disk.User); });
        }
        finally
        {
            // This recovery is admitted only after the previous close fully failed.
            // Retry must join its actual read before either retained root is released.
            using var held = new FileStream(Path.Combine(root, ".workbench-project", "context.lock"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            var recovery = coordinator.Document.PreserveConflictAndReloadAsync();
            var retry = coordinator.DisposeAsync().AsTask();
            try
            {
                Assert.False(recovery.IsCompleted);
                Assert.False(retry.IsCompleted);
                Assert.Throws<InvalidOperationException>(() => { _ = coordinator.Document.FlushAsync(); });
                Assert.Throws<InvalidOperationException>(() => { _ = coordinator.Document.PreserveConflictAndReloadAsync(); });
                Assert.ThrowsAny<IOException>(() => { using var duplicate = HostRuntime.AcquireLease(root); });
                Assert.ThrowsAny<IOException>(() => { using var duplicate = HostRuntime.AcquireLease(fixture.Disk.User); });
            }
            finally
            {
                held.Dispose();
                await recovery.WaitAsync(TimeSpan.FromSeconds(5));
                await retry.WaitAsync(TimeSpan.FromSeconds(5));
            }
        }
        using var next = HostRuntime.AcquireLease(root);
        using var receipt = JsonDocument.Parse(await File.ReadAllBytesAsync(Assert.Single(Directory.EnumerateFiles(Path.Combine(root, ".workbench-project", "context-conflicts")))));
        Assert.Equal("dirty on close", receipt.RootElement.GetProperty("text").GetString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Pending_secondary_close_refuses_late_editor_work_and_retains_both_locks(bool recovery)
    {
        using var fixture = new Fixture();
        var coordinator = fixture.Coordinator();
        Assert.True((await coordinator.InitializeAsync()).Succeeded);
        Assert.True((await coordinator.CreateAndSwitchAsync("Second")).Succeeded);
        var scope = coordinator.Scope!;
        var editor = coordinator.Document!;
        editor.Edit("before");
        await editor.FlushAsync();
        using var held = new FileStream(Path.Combine(scope.UserRoot, ".workbench-project", "context.lock"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        editor.Edit("last admitted text");
        // Establish the session's exact close barrier before the coordinator joins it.
        var editorClose = editor.DisposeAsync().AsTask();
        var close = coordinator.DisposeAsync().AsTask();
        Task? late = null;
        try
        {
            Assert.False(editorClose.IsCompleted);
            Assert.False(close.IsCompleted);
            Assert.Throws<InvalidOperationException>(() =>
            {
                late = recovery ? editor.PreserveConflictAndReloadAsync() : editor.FlushAsync();
            });
            Assert.ThrowsAny<IOException>(() => { using var duplicate = HostRuntime.AcquireLease(scope.UserRoot); });
            Assert.ThrowsAny<IOException>(() => { using var duplicate = HostRuntime.AcquireLease(fixture.Disk.User); });
        }
        finally
        {
            held.Dispose();
            await editorClose.WaitAsync(TimeSpan.FromSeconds(5));
            await close.WaitAsync(TimeSpan.FromSeconds(5));
            if (late is not null) await late.WaitAsync(TimeSpan.FromSeconds(5));
        }
        Assert.False(editor.HasPendingWork);
        Assert.Equal("last admitted text", (await scope.Documents.ReadAsync()).Text);
        using var nextSecondary = HostRuntime.AcquireLease(scope.UserRoot);
        using var nextBase = HostRuntime.AcquireLease(fixture.Disk.User);
    }

    [Fact]
    public async Task Oversized_secondary_close_allows_correction_only_after_runtime_cleanup_and_joins_retry_save()
    {
        using var fixture = new Fixture();
        var coordinator = fixture.Coordinator();
        Assert.True((await coordinator.InitializeAsync()).Succeeded);
        Assert.True((await coordinator.CreateAndSwitchAsync("Second")).Succeeded);
        var scope = coordinator.Scope!;
        var editor = coordinator.Document!;
        var runtime = coordinator.Runtime!;
        using var gate = fixture.Gate(scope.UserRoot, holdStop: true);
        Assert.True((await runtime.Manager.EnableAsync(PluginId, confirmTrust: true)).Succeeded);
        editor.Edit("before invalid text");
        await editor.FlushAsync();
        editor.Edit(new string('x', 1024 * 1024 + 1));
        var closeFailure = await Assert.ThrowsAsync<ProjectWorkspaceStoreException>(() => coordinator.DisposeAsync().AsTask());
        Assert.Equal("ContextLimitExceeded", closeFailure.ErrorCode);
        try
        {
            Assert.True(runtime.Manager.HasPendingStops);
            Assert.False(editor.IsEditable);
            Assert.Throws<InvalidOperationException>(() => editor.Edit("cleanup still pending"));
            Assert.Throws<InvalidOperationException>(() => { _ = editor.FlushAsync(); });
            Assert.Throws<InvalidOperationException>(() => { _ = editor.PreserveConflictAndReloadAsync(); });
            Assert.ThrowsAny<IOException>(() => { using var duplicate = HostRuntime.AcquireLease(scope.UserRoot); });
            Assert.ThrowsAny<IOException>(() => { using var duplicate = HostRuntime.AcquireLease(fixture.Disk.User); });
        }
        finally { gate.StopRelease.TrySetResult(); }
        // A new owned close pass joins the same actual Stop and confirms runtime cleanup.
        // The unchanged oversized buffer still fails; no root is released or manager restarted.
        Assert.Equal("ContextLimitExceeded", (await Assert.ThrowsAsync<ProjectWorkspaceStoreException>(() => coordinator.DisposeAsync().AsTask())).ErrorCode);
        Assert.False(runtime.Manager.HasPendingStops);
        Assert.Equal(0, gate.Live);
        Assert.True(editor.IsEditable);
        Assert.False(coordinator.CanUseCurrentRuntime);
        Assert.True(editor.IsDirty);
        Assert.ThrowsAny<IOException>(() => { using var duplicate = HostRuntime.AcquireLease(scope.UserRoot); });
        Assert.ThrowsAny<IOException>(() => { using var duplicate = HostRuntime.AcquireLease(fixture.Disk.User); });
        using var held = new FileStream(Path.Combine(scope.UserRoot, ".workbench-project", "context.lock"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        editor.Edit("corrected secondary text");
        var retry = coordinator.DisposeAsync().AsTask();
        Assert.False(retry.IsCompleted);
        Assert.False(editor.IsEditable);
        Assert.Throws<InvalidOperationException>(() => editor.Edit("after retry barrier"));
        Assert.Throws<InvalidOperationException>(() => { _ = editor.FlushAsync(); });
        Assert.Throws<InvalidOperationException>(() => { _ = editor.PreserveConflictAndReloadAsync(); });
        Assert.ThrowsAny<IOException>(() => { using var duplicate = HostRuntime.AcquireLease(scope.UserRoot); });
        Assert.ThrowsAny<IOException>(() => { using var duplicate = HostRuntime.AcquireLease(fixture.Disk.User); });
        held.Dispose();
        await retry.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(editor.HasPendingWork);
        Assert.Equal("corrected secondary text", (await scope.Documents.ReadAsync()).Text);
        Assert.Equal(2, fixture.FactoryCalls);
        using var nextSecondary = HostRuntime.AcquireLease(scope.UserRoot);
        using var nextBase = HostRuntime.AcquireLease(fixture.Disk.User);
    }

    [Fact]
    public async Task Failed_plugin_without_live_pending_work_remains_visible_as_a_managed_diagnostic()
    {
        using var fixture = new Fixture();
        var target = await fixture.CreateTargetAsync(seedEnabled: true);
        using var candidate = fixture.Gate(fixture.Root(target.Id));
        candidate.FailInitialization = true;
        await using var coordinator = fixture.Coordinator();
        Assert.True((await coordinator.InitializeAsync()).Succeeded);
        Assert.True((await coordinator.SwitchAsync(target.Id)).Succeeded);
        var plugin = Assert.Single(coordinator.Runtime!.Manager.Plugins);
        Assert.Equal(PluginState.Failed, plugin.State);
        Assert.False(plugin.HasRuntime);
        Assert.False(plugin.HasPendingStop);
        Assert.Equal(0, candidate.Live);
        Assert.True(coordinator.CanUseCurrentRuntime);
        Assert.Contains(plugin.Diagnostics, d => d.Code == PluginDiagnosticCode.InitializationFailed);
    }

    [Fact]
    public async Task CatalogCommitFailureKeepsOldSelectionAndBothDataRoots()
    {
        using var fixture = new Fixture();
        var target = await fixture.CreateTargetAsync(seedEnabled: true);
        using var old = fixture.Gate(fixture.Disk.User);
        using var candidate = fixture.Gate(fixture.Root(target.Id), holdInitialize: true);
        await using var coordinator = fixture.Coordinator();
        Assert.True((await coordinator.InitializeAsync()).Succeeded);
        Assert.True((await coordinator.Runtime!.Manager.EnableAsync(PluginId, confirmTrust: true)).Succeeded);
        coordinator.Document!.Edit("old context");
        var change = coordinator.SwitchAsync(target.Id);
        await Task.WhenAny(change, candidate.Initialized.Task).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(candidate.Initialized.Task.IsCompleted);
        var outside = new ProjectWorkspaceCatalog(fixture.Disk.User);
        var snapshot = await outside.OpenAsync();
        await outside.RenameAsync(target.Id, "Outside rename", snapshot.Revision);
        candidate.InitializeRelease.TrySetResult();
        var result = await change;
        Assert.False(result.Succeeded);
        Assert.Equal("RevisionConflict", result.ErrorCode);
        Assert.Equal("default", (await outside.OpenAsync()).ActiveProjectId);
        Assert.Equal("default", coordinator.ActiveProject!.Id);
        Assert.Equal(1, old.Live);
        Assert.Equal(0, candidate.Live);
        Assert.Equal("old context", (await new ProjectContextDocumentStore(fixture.Disk.User, "default").ReadAsync()).Text);
        Assert.True(Directory.Exists(fixture.Root(target.Id)));
    }

    [Fact]
    public async Task CloseJoinsFailedCandidateAndEditorTask()
    {
        // Failed candidate ownership is retained across close retries.
        using (var fixture = new Fixture())
        {
            var target = await fixture.CreateTargetAsync(seedEnabled: true);
            using var candidate = fixture.Gate(fixture.Root(target.Id), holdStop: true);
            candidate.FailInitialization = true;
            var coordinator = fixture.Coordinator();
            Assert.True((await coordinator.InitializeAsync()).Succeeded);
            Assert.True((await coordinator.SwitchAsync(target.Id)).Pending);
            try
            {
                await Assert.ThrowsAsync<ProjectWorkspaceStoreException>(() => coordinator.DisposeAsync().AsTask());
                Assert.ThrowsAny<IOException>(() => { using var duplicate = HostRuntime.AcquireLease(fixture.Disk.User); });
                Assert.ThrowsAny<IOException>(() => { using var duplicate = HostRuntime.AcquireLease(fixture.Root(target.Id)); });
            }
            finally { candidate.StopRelease.TrySetResult(); }
            await coordinator.DisposeAsync();
            using var next = HostRuntime.AcquireLease(fixture.Disk.User);
        }
        // Independently, a close must join an editor task that was admitted before quiescence.
        // Fresh editor recovery during failed-candidate cleanup is prohibited by the admission test.
        using (var fixture = new Fixture())
        {
            var store = new ProjectContextDocumentStore(fixture.Disk.User, "default");
            await store.WriteAsync("before close", "missing");
            var coordinator = fixture.Coordinator();
            Assert.True((await coordinator.InitializeAsync()).Succeeded);
            using var held = new FileStream(Path.Combine(fixture.Disk.User, ".workbench-project", "context.lock"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            coordinator.Document!.Edit("latest on close");
            var editor = coordinator.Document.FlushAsync();
            var close = coordinator.DisposeAsync().AsTask();
            Assert.False(editor.IsCompleted);
            Assert.False(close.IsCompleted);
            Assert.ThrowsAny<IOException>(() => { using var duplicate = HostRuntime.AcquireLease(fixture.Disk.User); });
            held.Dispose();
            await editor;
            await close;
            using var next = HostRuntime.AcquireLease(fixture.Disk.User);
            Assert.Equal("latest on close", (await store.ReadAsync()).Text);
        }
    }

    [Fact]
    public async Task ThemeSettingsSecretsAndContextRemainProjectBound()
    {
        using var fixture = new Fixture();
        using var gate = fixture.Gate(fixture.Disk.User);
        await using var coordinator = fixture.Coordinator();
        Assert.True((await coordinator.InitializeAsync()).Succeeded);
        Assert.True((await coordinator.Runtime!.Manager.EnableAsync(PluginId, confirmTrust: true)).Succeeded);
        await coordinator.Runtime!.Manager.GetSettings(PluginId).SetAsync("draft", "default draft");
        coordinator.Document!.Edit("default context");
        // The native host stores preferences in its effective user root. Use synthetic bytes here.
        await File.WriteAllTextAsync(Path.Combine(coordinator.Scope!.UserRoot, "host-settings.json"), "{\"theme\":1}");
        var secretPath = Path.Combine(coordinator.Scope.UserRoot, "secrets", PluginId);
        Directory.CreateDirectory(secretPath);
        await File.WriteAllBytesAsync(Path.Combine(secretPath, "synthetic.bin"), [10, 20, 30]);
        Assert.True((await coordinator.CreateAndSwitchAsync("Second")).Succeeded);
        var second = coordinator.ActiveProject!.Id;
        Assert.Equal(0, gate.Live);
        var unapproved = Assert.Single(coordinator.Runtime!.Manager.Plugins);
        Assert.False(unapproved.IsTrusted);
        Assert.False(unapproved.DesiredEnabled);
        Assert.Equal("empty", await coordinator.Runtime!.Manager.GetSettings(PluginId).GetAsync("draft", "empty"));
        Assert.Empty(coordinator.Document!.Text);
        Assert.False(File.Exists(Path.Combine(coordinator.Scope!.UserRoot, "host-settings.json")));
        Assert.False(Directory.Exists(Path.Combine(coordinator.Scope.UserRoot, "secrets")));
        await coordinator.Runtime.Manager.GetSettings(PluginId).SetAsync("draft", "second draft");
        coordinator.Document.Edit("second context");
        Assert.True((await coordinator.SwitchAsync("default")).Succeeded);
        Assert.Equal(1, gate.Live);
        Assert.Equal("default draft", await coordinator.Runtime!.Manager.GetSettings(PluginId).GetAsync("draft", "empty"));
        Assert.Equal("default context", coordinator.Document!.Text);
        Assert.Equal(new byte[] { 10, 20, 30 }, await File.ReadAllBytesAsync(Path.Combine(secretPath, "synthetic.bin")));
        Assert.True((await coordinator.SwitchAsync(second)).Succeeded);
        Assert.Equal("second context", coordinator.Document!.Text);
    }

    [Fact]
    public async Task AutosaveConflictPreservesBufferAndBlocksUnsafeLeave()
    {
        using var fixture = new Fixture();
        var target = await fixture.CreateTargetAsync();
        await using var coordinator = fixture.Coordinator();
        Assert.True((await coordinator.InitializeAsync()).Succeeded);
        await coordinator.Scope!.Documents.WriteAsync("outside", "missing");
        coordinator.Document!.Edit("dirty buffer");
        var failure = await coordinator.SwitchAsync(target.Id);
        Assert.False(failure.Succeeded);
        Assert.Equal("RevisionConflict", failure.ErrorCode);
        Assert.Equal(1, fixture.FactoryCalls);
        Assert.Equal("dirty buffer", coordinator.Document.Text);
        Assert.True(coordinator.Document.IsDirty);
        await coordinator.Document.PreserveConflictAndReloadAsync();
        Assert.True((await coordinator.RetryAsync()).Succeeded);
        Assert.Equal("outside", (await new ProjectContextDocumentStore(fixture.Disk.User, "default").ReadAsync()).Text);
        using var retained = JsonDocument.Parse(await File.ReadAllBytesAsync(Assert.Single(Directory.EnumerateFiles(Path.Combine(fixture.Disk.User, ".workbench-project", "context-conflicts")))));
        Assert.Equal("dirty buffer", retained.RootElement.GetProperty("text").GetString());
    }

    private sealed class Fixture : IDisposable
    {
        public TestDirectory Disk { get; } = new();
        public int FactoryCalls { get; private set; }
        public Fixture() => Disk.Package(PluginId, fields: new() { ["entryType"] = "Workbench.TestPlugin.WorkspaceLifecyclePlugin" });
        public string Root(string id) => new ProjectWorkspaceCatalog(Disk.User).GetProjectRoot(id);
        public WorkspaceLifecycleGate Gate(string root, bool holdPrepare = false, bool holdStop = false, bool holdInitialize = false)
            => new(Path.Combine(root, "data", PluginId), holdPrepare, holdStop, holdInitialize);
        private PluginManagerOptions Options(string root, ProjectRuntimeScope scope) => new()
        {
            UserRoot = root, PluginsRoot = Path.Combine(root, "Plugins"), ProjectScope = scope,
            AdditionalDiscoveryRoots = [Disk.Plugins], SharedAssemblies = [typeof(WorkspaceLifecycleGate).Assembly], StopTimeout = TimeSpan.FromMilliseconds(100)
        };
        public WorkspaceRuntimeCoordinator Coordinator(Action<string>? beforeTarget = null) => new(new(false, Disk.User, null), (root, scope, owner) =>
        {
            if (FactoryCalls++ > 0) beforeTarget?.Invoke(root);
            return new(Options(root, scope), owner);
        });
        public async Task<ProjectWorkspaceInfo> CreateTargetAsync(bool seedEnabled = false)
        {
            var catalog = new ProjectWorkspaceCatalog(Disk.User);
            var snapshot = await catalog.OpenAsync();
            var project = (await catalog.CreateAsync("Second", snapshot.Revision)).Project;
            if (seedEnabled)
            {
                var root = Root(project.Id);
                using var gate = Gate(root);
                await using var seed = new HostRuntime(Options(root, new(project.Id, project.Name, root)));
                await seed.InitializeAsync();
                Assert.True((await seed.Manager.EnableAsync(PluginId, confirmTrust: true)).Succeeded);
            }
            return project;
        }
        public void Dispose() => Disk.Dispose();
    }
}
