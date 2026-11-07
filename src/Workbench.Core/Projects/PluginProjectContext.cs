using Workbench.Sdk;

namespace Workbench.Core.Projects;

internal sealed class PluginProjectContext(ProjectRuntimeScope scope) : IProjectContext
{
    private readonly object _sync = new();
    private readonly HashSet<Task> _operations = [];
    private bool _closed;

    public ProjectIdentity Identity
    {
        get { lock (_sync) { EnsureOpen(); return scope.Identity; } }
    }

    internal bool HasPendingWork { get { lock (_sync) return _operations.Any(task => !task.IsCompleted); } }
    internal void Close() { lock (_sync) _closed = true; }

    internal async Task DrainAsync()
    {
        // Close revokes admission before terminal drain. During a nonterminal
        // preparation, include calls accepted while earlier calls were settling.
        while (true)
        {
            Task[] pending;
            lock (_sync) pending = _operations.ToArray();
            if (pending.Length == 0) return;
            try { await Task.WhenAll(pending).ConfigureAwait(false); }
            catch (Exception) { /* Each caller receives its safe failure; drain still joins every task. */ }
            lock (_sync) _operations.RemoveWhere(task => task.IsCompleted);
        }
    }

    public Task<ProjectContextSnapshot> ReadAsync(CancellationToken cancellationToken = default)
        => RunAsync(() => scope.Documents.ReadAsync(cancellationToken));

    public Task<ProjectContextSnapshot> UpdateAsync(string instructions, string expectedRevision, CancellationToken cancellationToken = default)
        => RunAsync(() => scope.Documents.WriteAsync(instructions, expectedRevision, cancellationToken));

    private Task<ProjectContextSnapshot> RunAsync(Func<Task<ProjectContextDocument>> action)
    {
        lock (_sync)
        {
            if (_closed) return Task.FromException<ProjectContextSnapshot>(new PluginProjectContextException("ProjectContextClosed"));
            // Publish ownership before any synchronous prefix/IO can run. Caller
            // cancellation never detaches the actual store task from this set.
            _operations.RemoveWhere(task => task.IsCompleted);
            var operation = Task.Run(async () =>
            {
                try
                {
                    var document = await action().ConfigureAwait(false);
                    return new ProjectContextSnapshot(document.Revision, document.Text);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception error) { throw new PluginProjectContextException(ProjectFailure.Code(error, "ProjectContextFailed")); }
            });
            _operations.Add(operation);
            _ = operation.ContinueWith(static task => { _ = task.Exception; }, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            return operation;
        }
    }

    private void EnsureOpen()
    {
        if (_closed) throw new PluginProjectContextException("ProjectContextClosed");
    }
}

// A public plugin-created exception cannot introduce arbitrary text into host UI.
internal static class ProjectFailure
{
    internal static string Code(Exception error, string fallback) => error switch
    {
        OperationCanceledException => "ProjectPreparationCanceled",
        ProjectWorkspaceStoreException storage when Known(storage.ErrorCode) => storage.ErrorCode,
        PluginProjectContextException project when Known(project.ErrorCode) => project.ErrorCode,
        _ => fallback
    };

    private static bool Known(string code) => code is "InvalidId" or "InvalidName" or "InvalidCatalog" or "InvalidContext"
        or "RevisionConflict" or "CatalogLimitExceeded" or "ContextLimitExceeded" or "RecoveryRequired"
        or "RecoveryUnavailable" or "ActiveProjectArchived" or "ProjectNotFound" or "UnsafePath" or "StoreBusy"
        or "StoreIoFailure" or "ProjectContextClosed" or "ProjectContextFailed";
}
