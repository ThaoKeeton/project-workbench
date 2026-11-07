using Workbench.Core;
using Workbench.Core.Projects;

namespace Workbench.App;

/// <summary>Serializes host actions and project transactions; failed work keeps its roots owned.</summary>
internal sealed class WorkspaceRuntimeCoordinator : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly RunOptions _options;
    private readonly Func<string, ProjectRuntimeScope, WorkspaceRootLease, HostRuntime> _factory;
    private readonly ProjectWorkspaceCatalog _store;
    private Task _tail = Task.CompletedTask;
    private Task? _disposal;
    private WorkspaceRootLease? _owner;
    private OwnedProject? _current, _retiring;
    private SwitchAttempt? _attempt;
    private string? _retryTarget;
    private int _transitionRequests;
    private volatile bool _closing;
    private CloseAttempt? _closeAttempt;
    private bool _disposed;
    private long _runtimeGeneration;
    private bool _preparingClose;
    private volatile bool _normalCloseRequested;
    private Task<ProjectSwitchResult>? _closePreparation;

    public WorkspaceRuntimeCoordinator(RunOptions options, Func<string, ProjectRuntimeScope, WorkspaceRootLease, HostRuntime> factory)
    {
        _options = options;
        _factory = factory;
        _store = new(options.UserRoot);
    }

    public ProjectWorkspaceSnapshot? Catalog { get; private set; }
    public ProjectWorkspaceInfo? ActiveProject => _current?.Project;
    public HostRuntime? Runtime => _current?.Runtime;
    public ProjectRuntimeScope? Scope => _current?.Scope;
    public ProjectDocumentSession? Document => _current?.Document;
    public ProjectTransitionSnapshot Transition { get; private set; } = new(ProjectTransitionState.Idle, null, null, null);
    public long RuntimeGeneration => Interlocked.Read(ref _runtimeGeneration);
    public bool CanUseCurrentRuntime
    {
        get { lock (_gate) return CanAdmitCurrent; }
    }

    /// <summary>Normal native close first joins edits/actions and committed leave hooks. A settled
    /// correctable failure retains the same live manager; success/pending work seals admission.</summary>
    public Task<ProjectSwitchResult> PrepareCloseAsync(CancellationToken ct = default)
        => PrepareCloseCoreAsync(null, ct);

    public Task<ProjectSwitchResult> PrepareCloseAsync(
        Func<HostRuntime, ProjectRuntimeScope, CancellationToken, Task> flushHost, CancellationToken ct = default)
        => PrepareCloseCoreAsync(flushHost ?? throw new ArgumentNullException(nameof(flushHost)), ct);

    private Task<ProjectSwitchResult> PrepareCloseCoreAsync(
        Func<HostRuntime, ProjectRuntimeScope, CancellationToken, Task>? flushHost, CancellationToken ct)
    {
        lock (_gate)
        {
            if (_disposed) return Task.FromResult(new ProjectSwitchResult(true, false, null));
            if (_closing)
                return Task.FromResult(new ProjectSwitchResult(false, true, "ProjectCloseCleanupRequired"));
            if (_closePreparation is { IsCompleted: false }) return _closePreparation;
            if (_closePreparation is { IsCompletedSuccessfully: true } done && done.Result.Succeeded) return done;
            _preparingClose = true; // Published synchronously before a queued callback can run.
            _normalCloseRequested = true; // Also cancels target admission in an already queued switch.
            return _closePreparation = QueueLocked(async () =>
            {
                if (_attempt is { } attempt)
                {
                    if (attempt.Stage == SwitchStage.Prepare && _current is { Stopped: false } && attempt.Candidate is null)
                    {
                        // The old live context still owns any failed/pending hook. Supersede only
                        // the target request, then run/join its preparation for this normal close.
                        _attempt = null;
                        _retryTarget = null;
                    }
                    else return new ProjectSwitchResult(false, true, "ProjectCloseCleanupRequired");
                }
                if (_current is { Stopped: true }) return new ProjectSwitchResult(false, true, "ProjectCloseCleanupRequired");
                if (_current is null) return new ProjectSwitchResult(true, false, null);
                ProjectSwitchResult result;
                try
                {
                    // This callback is part of the sealed operation, after accepted shell work.
                    // The native caller returns its awaited dispatcher continuation, never RunCurrent.
                    if (flushHost is not null) await flushHost(_current.Runtime!, _current.Scope, ct).ConfigureAwait(false);
                    await _current.Document.QuiesceAsync(ct).ConfigureAwait(false);
                    var preparation = await _current.Runtime!.Manager.PrepareProjectLeaveAsync(cancellationToken: ct).ConfigureAwait(false);
                    var pending = preparation.HasPendingWork || _current.Runtime.Manager.HasPendingProjectWork;
                    result = preparation.Succeeded && !pending ? new(true, false, null)
                        : new(false, pending, PreparationCode(preparation.ErrorCode));
                }
                catch (Exception error)
                {
                    result = new(false, _current.Document.HasPendingWork || _current.Runtime!.Manager.HasPendingProjectWork,
                        HostFailure.ProjectCode(error));
                }
                if (!result.Succeeded)
                {
                    if (!result.Pending)
                    {
                        _current.Document.SetPaused(_closing);
                        lock (_gate) { _preparingClose = false; _normalCloseRequested = false; }
                    }
                    Block(result.ErrorCode ?? "ProjectPreparationFailed", result.Pending);
                }
                return result;
            });
        }
    }

    private bool CanAdmitCurrent => !_closing && !_preparingClose && _current is { Stopped: false, Runtime: not null }
        && _attempt is null && _retiring is null && _transitionRequests == 0;

    public Task<ProjectSwitchResult> InitializeAsync(CancellationToken ct = default) => TransitionAsync(async () =>
    {
        if (_attempt is not null) return await ContinueAsync(ct).ConfigureAwait(false);
        await OpenCatalogAsync(ct).ConfigureAwait(false);
        return await BeginAsync(Catalog!.ActiveProjectId, ct).ConfigureAwait(false);
    });

    public Task<ProjectSwitchResult> SwitchAsync(string id, CancellationToken ct = default)
        => TransitionAsync(() => BeginAsync(id, ct));

    public Task<ProjectSwitchResult> CreateAndSwitchAsync(string name, CancellationToken ct = default) => TransitionAsync(async () =>
    {
        if (_attempt is not null) return Block("ProjectTransitionBusy", true);
        await EnsureCatalogAsync(ct).ConfigureAwait(false);
        var created = await _store.CreateAsync(name, Catalog!.Revision, ct).ConfigureAwait(false);
        Catalog = created.Snapshot;
        return await BeginAsync(created.Project.Id, ct).ConfigureAwait(false);
    });

    public Task<ProjectSwitchResult> RenameAsync(string name, CancellationToken ct = default) => TransitionAsync(async () =>
    {
        if (_attempt is not null) return Block("ProjectTransitionBusy", true);
        if (_current is null) return Block("ProjectNotReady", false);
        var saved = await _store.RenameAsync(_current.Project.Id, name, Catalog!.Revision, ct).ConfigureAwait(false);
        Catalog = saved;
        _current.Project = saved.Projects.Single(p => p.Id == _current.Project.Id);
        _current.Scope.Rename(_current.Project.Name);
        return Ready();
    });

    public Task<ProjectSwitchResult> SetArchivedAsync(string id, bool archived, CancellationToken ct = default) => TransitionAsync(async () =>
    {
        if (_attempt is not null) return Block("ProjectTransitionBusy", true);
        await EnsureCatalogAsync(ct).ConfigureAwait(false);
        Catalog = await _store.SetArchivedAsync(id, archived, Catalog!.Revision, ct).ConfigureAwait(false);
        return Ready();
    });

    public Task<IReadOnlyList<ProjectWorkspaceInfo>> GetRecoverableAsync(CancellationToken ct = default)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_closing, this);
            return QueueLocked(async () =>
            {
                try
                {
                    await EnsureCatalogAsync(ct).ConfigureAwait(false);
                    return await _store.GetRecoverableAsync(ct).ConfigureAwait(false);
                }
                catch (Exception error) { throw new ProjectWorkspaceStoreException(HostFailure.ProjectCode(error)); }
            });
        }
    }

    public Task<ProjectSwitchResult> RecoverProjectAsync(string id, CancellationToken ct = default) => TransitionAsync(async () =>
    {
        if (_attempt is not null) return Block("ProjectTransitionBusy", true);
        await EnsureCatalogAsync(ct).ConfigureAwait(false);
        Catalog = await _store.RecoverProjectAsync(id, Catalog!.Revision, ct).ConfigureAwait(false);
        return await BeginAsync(id, ct).ConfigureAwait(false);
    });

    public Task<ProjectSwitchResult> RecoverCatalogAsync(CancellationToken ct = default) => TransitionAsync(async () =>
    {
        if (_attempt is not null) return Block("ProjectTransitionBusy", true);
        EnsureOwner();
        SetState(ProjectTransitionState.Recovering);
        Catalog = await _store.RecoverCatalogAsync(ct).ConfigureAwait(false);
        return await BeginAsync(Catalog.ActiveProjectId, ct).ConfigureAwait(false);
    });

    public Task<ProjectSwitchResult> RetryAsync(CancellationToken ct = default) => TransitionAsync(async () =>
    {
        if (_attempt is not null) return await ContinueAsync(ct).ConfigureAwait(false);
        await OpenCatalogAsync(ct).ConfigureAwait(false);
        return await BeginAsync(_retryTarget ?? Catalog!.ActiveProjectId, ct).ConfigureAwait(false);
    });

    /// <summary>The shell admits and awaits all commands, settings/theme IO and other host actions here.
    /// A transition immediately closes admission and joins already admitted callbacks before stopping.</summary>
    public Task RunCurrentAsync(Func<HostRuntime, ProjectRuntimeScope, CancellationToken, Task> action, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (!CanAdmitCurrent) throw new ProjectWorkspaceStoreException("ProjectTransitionBusy");
            return QueueLocked(async () =>
            {
                var current = _current!;
                try { await action(current.Runtime!, current.Scope, ct).ConfigureAwait(false); }
                catch (Exception error) { throw new ProjectWorkspaceStoreException(HostFailure.ProjectCode(error)); }
                return true;
            });
        }
    }

    private async Task<ProjectSwitchResult> BeginAsync(string id, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (_attempt is { } existing)
            return existing.Target.Id == id ? await ContinueAsync(ct).ConfigureAwait(false) : Block("ProjectTransitionBusy", true);
        await EnsureCatalogAsync(ct).ConfigureAwait(false);
        _ = _store.GetProjectRoot(id); // Validate before any transition snapshot can contain a caller value.
        var target = Catalog!.Projects.SingleOrDefault(p => p.Id == id);
        if (target is null) return Block("ProjectNotFound", false);
        if (target.IsArchived) return Block("ActiveProjectArchived", false);
        if (_current?.Project.Id == id) return Ready();
        _retryTarget = id;
        _attempt = new(target, _current is null ? SwitchStage.Start : SwitchStage.Prepare);
        return await ContinueAsync(ct).ConfigureAwait(false);
    }

    private async Task<ProjectSwitchResult> ContinueAsync(CancellationToken ct)
    {
        var attempt = _attempt!;
        while (true)
        {
            if (_normalCloseRequested && !_closing && attempt.Stage == SwitchStage.Prepare)
            {
                // Never Stop a live current manager merely because close canceled a queued target.
                // PrepareCloseAsync owns its document/hooks next, with the same private buffers.
                _attempt = null;
                _retryTarget = null;
                return Block("ProjectClosing", HasPendingOwnedWork);
            }
            if ((_closing || _normalCloseRequested) && attempt.Stage != SwitchStage.Rollback)
            {
                attempt.FailureCode = "ProjectClosing";
                attempt.Stage = SwitchStage.Rollback;
            }
            switch (attempt.Stage)
            {
                case SwitchStage.Prepare:
                    SetState(ProjectTransitionState.Preparing);
                    var leaving = _current!;
                    try { await leaving.Document.QuiesceAsync(ct).ConfigureAwait(false); }
                    catch (Exception error)
                    {
                        var pending = leaving.Document.HasPendingWork;
                        if (!pending) leaving.Document.SetPaused(_closing);
                        return Block(HostFailure.ProjectCode(error), pending);
                    }
                    var preparation = await leaving.Runtime!.Manager.PrepareProjectLeaveAsync(cancellationToken: ct).ConfigureAwait(false);
                    if (!preparation.Succeeded || leaving.Runtime.Manager.HasPendingProjectWork)
                    {
                        var pending = preparation.HasPendingWork || leaving.Runtime.Manager.HasPendingProjectWork;
                        if (!pending) leaving.Document.SetPaused(_closing);
                        return Block(PreparationCode(preparation.ErrorCode), pending);
                    }
                    attempt.Stage = SwitchStage.Stop;
                    break;
                case SwitchStage.Stop:
                    SetState(ProjectTransitionState.StoppingCurrent);
                    try { await _current!.Runtime!.StopForProjectLeaveAsync().ConfigureAwait(false); }
                    catch (PluginShutdownException) { return Block("ProjectStopPending", true); }
                    _current!.Stopped = true;
                    attempt.Stage = SwitchStage.Start;
                    break;
                case SwitchStage.Start:
                    SetState(ProjectTransitionState.StartingTarget);
                    try
                    {
                        if (attempt.Candidate is null)
                        {
                            var scope = new ProjectRuntimeScope(attempt.Target.Id, attempt.Target.Name, _store.GetProjectRoot(attempt.Target.Id));
                            var candidate = attempt.Candidate = new(attempt.Target, scope, CanRecoverDocumentAfterFailedClose);
                            candidate.Document.SetPaused(true);
                            await candidate.Document.InitializeAsync(ct).ConfigureAwait(false);
                            ct.ThrowIfCancellationRequested();
                            candidate.Runtime = _factory(scope.UserRoot, scope, _owner!);
                            if (!ReferenceEquals(candidate.Runtime.Scope, scope)
                                || !string.Equals(candidate.Runtime.UserRoot, scope.UserRoot, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                                throw new ProjectWorkspaceStoreException("ProjectRuntimeMismatch");
                            candidate.Startup = candidate.Runtime.InitializeAsync();
                        }
                        if (attempt.Candidate.Startup is { } startup) await startup.WaitAsync(ct).ConfigureAwait(false);
                        if (attempt.Candidate.Runtime!.Manager.HasPendingStops || attempt.Candidate.Runtime.Manager.HasPendingProjectWork)
                        {
                            attempt.FailureCode = "ProjectStartupPending";
                            attempt.Stage = SwitchStage.Rollback;
                        }
                        else attempt.Stage = SwitchStage.Commit;
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested && attempt.Candidate?.Startup is { IsCompleted: false })
                    {
                        return Block("Canceled", true);
                    }
                    catch (Exception error)
                    {
                        attempt.FailureCode = HostFailure.ProjectCode(error);
                        attempt.Stage = SwitchStage.Rollback;
                    }
                    break;
                case SwitchStage.Commit:
                    SetState(ProjectTransitionState.PersistingSelection);
                    try
                    {
                        Catalog = await _store.SetActiveAsync(attempt.Target.Id, Catalog!.Revision, ct).ConfigureAwait(false);
                    }
                    catch (Exception error)
                    {
                        attempt.FailureCode = HostFailure.ProjectCode(error);
                        attempt.Stage = SwitchStage.Rollback;
                        break;
                    }
                    _retiring = _current;
                    _current = attempt.Candidate!;
                    attempt.Candidate = null;
                    _attempt = null;
                    _retryTarget = null;
                    Interlocked.Increment(ref _runtimeGeneration);
                    if (!await DrainRetiringAsync().ConfigureAwait(false)) return Block("ProjectRetirementPending", true);
                    _current.Document.SetPaused(_closing);
                    return Ready();
                case SwitchStage.Rollback:
                    return await RollbackAsync(attempt, ct).ConfigureAwait(false);
            }
        }
    }

    private async Task<ProjectSwitchResult> RollbackAsync(SwitchAttempt attempt, CancellationToken ct)
    {
        SetState(ProjectTransitionState.Recovering, attempt.FailureCode);
        if (attempt.Candidate is { } candidate)
        {
            if (candidate.Startup is { } startup)
            {
                try { await startup.WaitAsync(ct).ConfigureAwait(false); }
                catch (OperationCanceledException) when (!startup.IsCompleted) { return Block("ProjectStartupPending", true); }
                catch (Exception) { /* Its fixed failure is retained; retirement is still mandatory. */ }
            }
            try
            {
                await candidate.Document.DisposeAsync().ConfigureAwait(false);
                if (candidate.Runtime is not null) await candidate.Runtime.DisposeAsync().ConfigureAwait(false);
            }
            catch (PluginShutdownException) { return Block("ProjectStopPending", true); }
            attempt.Candidate = null;
        }
        if (!_closing && !_normalCloseRequested && _current is { Stopped: true } current)
        {
            if (attempt.RestoreNeedsRetirement)
            {
                try { await current.Runtime!.StopForProjectLeaveAsync().ConfigureAwait(false); }
                catch (PluginShutdownException) { return Block("ProjectRestorePending", true); }
                attempt.Restoration = null;
                attempt.RestoreNeedsRetirement = false;
            }
            try
            {
                if (attempt.Restoration is null)
                {
                    attempt.Restoration = current.Runtime!.RestoreAfterProjectLeaveAsync();
                    Interlocked.Increment(ref _runtimeGeneration);
                }
                await attempt.Restoration.WaitAsync(ct).ConfigureAwait(false);
                if (current.Runtime!.Manager.HasPendingStops || current.Runtime.Manager.HasPendingProjectWork)
                {
                    attempt.RestoreNeedsRetirement = true;
                    return Block("ProjectRestorePending", true);
                }
                current.Stopped = false;
            }
            catch (OperationCanceledException) when (attempt.Restoration is { IsCompleted: false }) { return Block("ProjectRestorePending", true); }
            catch (Exception)
            {
                attempt.RestoreNeedsRetirement = true;
                return Block("ProjectRestoreFailed", current.Runtime!.Manager.HasPendingStops || current.Runtime.Manager.HasPendingProjectWork);
            }
        }
        _current?.Document.SetPaused(_closing);
        _attempt = null;
        return Block(attempt.FailureCode ?? "ProjectOperationFailed", false, attempt.Target.Id);
    }

    private void EnsureOwner() => _owner ??= WorkspaceRootLease.Acquire(_options.UserRoot);
    private async Task EnsureCatalogAsync(CancellationToken ct)
    {
        EnsureOwner();
        if (Catalog is null) await OpenCatalogAsync(ct).ConfigureAwait(false);
    }
    private async Task OpenCatalogAsync(CancellationToken ct)
    {
        EnsureOwner();
        Catalog = await _store.OpenAsync(ct).ConfigureAwait(false);
        if (_current is { } current)
        {
            var identity = Catalog.Projects.SingleOrDefault(p => p.Id == current.Project.Id);
            if (identity is not null) { current.Project = identity; current.Scope.Rename(identity.Name); }
        }
    }

    private Task<ProjectSwitchResult> TransitionAsync(Func<Task<ProjectSwitchResult>> operation)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_closing, this);
            if (_preparingClose) return Task.FromResult(Block("ProjectClosePending", true));
            _transitionRequests++;
            return QueueLocked(async () =>
            {
                try
                {
                    if (_normalCloseRequested) return Block("ProjectClosing", HasPendingOwnedWork);
                    if (!await DrainRetiringAsync().ConfigureAwait(false)) return Block("ProjectRetirementPending", true);
                    return await operation().ConfigureAwait(false);
                }
                catch (Exception error) { return Block(HostFailure.ProjectCode(error), HasPendingOwnedWork); }
                finally { lock (_gate) _transitionRequests--; }
            });
        }
    }

    private bool HasPendingOwnedWork => _attempt?.Candidate?.Startup is { IsCompleted: false }
        || _attempt?.Restoration is { IsCompleted: false }
        || new[] { _current, _attempt?.Candidate, _retiring }.Any(p => p is not null
            && (p.Document.HasPendingWork || p.Runtime?.Manager.HasPendingStops == true || p.Runtime?.Manager.HasPendingProjectWork == true));

    // Called under an editor gate. Never take the coordinator/core gates or invoke plugin code.
    // A pending runtime cleanup remains sealed until a later owned close pass joins it successfully.
    private bool CanRecoverDocumentAfterFailedClose()
    {
        var close = Volatile.Read(ref _closeAttempt);
        return close is { RuntimeCleanupSettled: true, Completion.IsFaulted: true };
    }

    private async Task<bool> DrainRetiringAsync()
    {
        if (_retiring is not { } retired) return true;
        try
        {
            await retired.Document.DisposeAsync().ConfigureAwait(false);
            if (retired.Runtime is not null) await retired.Runtime.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception) { return false; }
        _retiring = null;
        _current?.Document.SetPaused(_closing);
        return true;
    }

    private ProjectSwitchResult Ready()
    {
        SetState(ProjectTransitionState.Idle);
        return new(true, false, null);
    }
    private ProjectSwitchResult Block(string code, bool pending, string? target = null)
    {
        Transition = new(ProjectTransitionState.Blocked, ActiveProject?.Id, target ?? _attempt?.Target.Id, code);
        return new(false, pending, code);
    }
    private void SetState(ProjectTransitionState state, string? error = null)
        => Transition = new(state, ActiveProject?.Id, _attempt?.Target.Id, error);
    private static string PreparationCode(string? code) => code is "ProjectPreparationPending" or "ProjectPreparationCanceled"
        or "ProjectPreparationBusy" or "ProjectPreparationFailed" or "ProjectPluginUnavailable" or "ProjectContextClosed" ? code : "ProjectPreparationFailed";

    private Task<T> QueueLocked<T>(Func<Task<T>> operation)
    {
        var previous = _tail;
        var task = Task.Run(async () =>
        {
            try { await previous.ConfigureAwait(false); } catch (Exception) { }
            return await operation().ConfigureAwait(false);
        });
        _tail = task;
        return task;
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_disposed) return ValueTask.CompletedTask;
            _closing = true;
            if (_disposal is null || _disposal.IsCompleted)
            {
                // Seal failed-close correction before taking any editor gate or capturing its tail.
                var close = new CloseAttempt();
                Volatile.Write(ref _closeAttempt, close);
                _current?.Document.SetPaused(true);
                _attempt?.Candidate?.Document.SetPaused(true);
                _disposal = QueueLocked(async () =>
                {
                    string? failure = null;
                    var runtimeCleanupSettled = true;
                    // Joining the operation tail also joins every admitted shell callback. No caller
                    // timeout/cancellation detaches candidate initialization, restoration, or editor IO.
                    foreach (var project in new[] { _attempt?.Candidate, _current, _retiring }.OfType<OwnedProject>().Distinct())
                    {
                        var editorRetired = false;
                        try { await project.Document.DisposeAsync().ConfigureAwait(false); editorRetired = true; }
                        catch (Exception error) { failure ??= HostFailure.ProjectCode(error); }
                        try
                        {
                            if (project.Runtime is not null)
                            {
                                if (editorRetired) await project.Runtime.DisposeAsync().ConfigureAwait(false);
                                // A failed dirty editor still owns this root. Stop known plugin work,
                                // but retain its runtime lease until preserve/reload and close succeed.
                                else await project.Runtime.StopForProjectLeaveAsync().ConfigureAwait(false);
                            }
                        }
                        catch (Exception) { runtimeCleanupSettled = false; failure ??= "ProjectClosePending"; }
                    }
                    close.RuntimeCleanupSettled = runtimeCleanupSettled;
                    if (failure is not null)
                    {
                        Block(failure, true);
                        throw new ProjectWorkspaceStoreException(failure);
                    }
                    _owner?.Dispose();
                    _current = _retiring = null;
                    _attempt = null;
                    _disposed = true;
                    return true;
                });
                // Until published, this attempt's null completion also refuses recovery. A fast
                // failed worker cannot combine its new readiness with the previous close's task.
                close.Completion = _disposal;
            }
            return new(_disposal);
        }
    }

    private enum SwitchStage { Prepare, Stop, Start, Commit, Rollback }
    private sealed class CloseAttempt
    {
        internal volatile Task? Completion;
        internal volatile bool RuntimeCleanupSettled;
    }
    private sealed class SwitchAttempt(ProjectWorkspaceInfo target, SwitchStage stage)
    {
        internal ProjectWorkspaceInfo Target { get; } = target;
        internal SwitchStage Stage { get; set; } = stage;
        internal OwnedProject? Candidate { get; set; }
        internal string? FailureCode { get; set; }
        internal Task? Restoration { get; set; }
        internal bool RestoreNeedsRetirement { get; set; }
    }
    private sealed class OwnedProject(ProjectWorkspaceInfo project, ProjectRuntimeScope scope, Func<bool> canRecoverAfterOwnerClose)
    {
        internal ProjectWorkspaceInfo Project { get; set; } = project;
        internal ProjectRuntimeScope Scope { get; } = scope;
        internal ProjectDocumentSession Document { get; } = new(scope.Documents, canRecoverAfterOwnerClose);
        internal HostRuntime? Runtime { get; set; }
        internal Task? Startup { get; set; }
        internal bool Stopped { get; set; }
    }
}
