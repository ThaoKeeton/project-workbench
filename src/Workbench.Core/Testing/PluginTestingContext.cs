using System.Security;
using Workbench.Core.Projects;
using Workbench.Sdk;

namespace Workbench.Core.Testing;

/// <summary>One plugin generation's captured authority and retained testing work.</summary>
internal sealed class PluginTestingContext(ProjectRuntimeScope scope, string producerPluginId) : IProjectTestWorkspace
{
    private readonly object _sync = new();
    private readonly HashSet<Task> _operations = [];
    private Task? _initialization;
    private bool _closed;

    internal bool HasPendingWork { get { lock (_sync) return _operations.Any(task => !task.IsCompleted); } }
    internal void Close() { lock (_sync) _closed = true; }

    internal async Task DrainAsync()
    {
        while (true)
        {
            Task[] pending;
            lock (_sync) pending = _operations.ToArray();
            if (pending.Length == 0) return;
            try { await Task.WhenAll(pending).ConfigureAwait(false); }
            catch (Exception) { /* Callers receive safe errors; drain joins all actual work. */ }
            lock (_sync) _operations.RemoveWhere(task => task.IsCompleted);
        }
    }

    public Task<TestSourceSnapshot> PublishSourceAsync(TestSourceDraft draft, CancellationToken cancellationToken = default)
        => CaptureAsync(() => TestValidation.CopySourceDraft(draft), input => scope.Testing.PublishSourceAsync(producerPluginId, input, cancellationToken), "InvalidTestSource", cancellationToken);
    public Task<IReadOnlyList<TestSourceSummary>> ListSourcesAsync(CancellationToken cancellationToken = default)
        => RunAsync(() => scope.Testing.ListSourcesAsync(cancellationToken), cancellationToken);
    public Task<TestSourceSnapshot> ReadSourceAsync(string sourceId, CancellationToken cancellationToken = default)
        => RunAsync(() => scope.Testing.ReadSourceAsync(sourceId, cancellationToken), cancellationToken);
    public Task<TestCaseSetSnapshot> CreateCaseSetAsync(TestCaseSetDraft draft, CancellationToken cancellationToken = default)
        => CaptureAsync(() => TestValidation.CopyCaseSetDraft(draft), input => scope.Testing.CreateCaseSetAsync(producerPluginId, input, cancellationToken), "InvalidCaseSet", cancellationToken);
    public Task<IReadOnlyList<TestCaseSetSummary>> ListCaseSetsAsync(CancellationToken cancellationToken = default)
        => RunAsync(() => scope.Testing.ListCaseSetsAsync(cancellationToken), cancellationToken);
    public Task<TestCaseSetSnapshot> ReadCaseSetAsync(string setId, CancellationToken cancellationToken = default)
        => RunAsync(() => scope.Testing.ReadCaseSetAsync(setId, cancellationToken), cancellationToken);
    public Task<TestCaseSetSnapshot> UpdateCaseSetAsync(string setId, TestCaseSetContent content, string expectedRevision, CancellationToken cancellationToken = default)
        => CaptureAsync(() => TestValidation.CopyCaseSetContent(content), input => scope.Testing.UpdateCaseSetAsync(producerPluginId, setId, input, expectedRevision, cancellationToken), "InvalidCaseSet", cancellationToken);
    public Task<TestCaseSetSnapshot> ReadCaseRevisionAsync(string setId, string revision, CancellationToken cancellationToken = default)
        => RunAsync(() => scope.Testing.ReadCaseRevisionAsync(setId, revision, cancellationToken), cancellationToken);
    public Task<TestRunSnapshot> CreateRunAsync(string setId, string expectedCaseRevision, IReadOnlyList<string> selectedCaseIds, CancellationToken cancellationToken = default)
        => CaptureAsync(() => TestValidation.CopySelectedCaseIds(selectedCaseIds), input => scope.Testing.CreateRunAsync(producerPluginId, setId, expectedCaseRevision, input, cancellationToken), "InvalidRun", cancellationToken);
    public Task<TestRunSnapshot> CreateRetryRunAsync(string parentRunId, CancellationToken cancellationToken = default)
        => RunAsync(() => scope.Testing.CreateRetryRunAsync(producerPluginId, parentRunId, cancellationToken), cancellationToken);
    public Task<IReadOnlyList<TestRunSummary>> ListRunsAsync(CancellationToken cancellationToken = default)
        => RunAsync(() => scope.Testing.ListRunsAsync(cancellationToken), cancellationToken);
    public Task<TestRunSnapshot> ReadRunAsync(string runId, CancellationToken cancellationToken = default)
        => RunAsync(() => scope.Testing.ReadRunAsync(runId, cancellationToken), cancellationToken);
    public Task<TestRunSnapshot> AppendRunEventAsync(string runId, long expectedSequence, string expectedHeadHash, TestRunEventDraft draft, CancellationToken cancellationToken = default)
        => CaptureAsync(() => TestValidation.CopyRunEventDraft(draft), input => scope.Testing.AppendRunEventAsync(producerPluginId, runId, expectedSequence, expectedHeadHash, input, cancellationToken), "InvalidRun", cancellationToken);
    public Task<TestEvidenceReceipt> AddRunEvidenceAsync(string runId, TestEvidenceDraft draft, CancellationToken cancellationToken = default)
        => CaptureAsync(() => TestValidation.CopyEvidenceDraft(draft), input => scope.Testing.AddRunEvidenceAsync(producerPluginId, runId, input, cancellationToken), "InvalidEvidence", cancellationToken);
    public Task<TestEvidence> ReadRunEvidenceAsync(string runId, string evidenceId, CancellationToken cancellationToken = default)
        => RunAsync(() => scope.Testing.ReadRunEvidenceAsync(runId, evidenceId, cancellationToken), cancellationToken);

    private Task<T> RunAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken)
        => CaptureAsync(static () => true, _ => action(), "TestingFailed", cancellationToken);

    private Task<T> CaptureAsync<TInput, T>(Func<TInput> capture, Func<TInput, Task<T>> action, string invalidCode, CancellationToken cancellationToken)
    {
        TaskCompletionSource? startInitialization = null;
        var captured = new TaskCompletionSource<TInput>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<T> operation;
        lock (_sync)
        {
            if (_closed)
            {
                var rejected = Task.FromException<T>(TestValidation.Error("TestingContextClosed"));
                _ = rejected.Exception;
                return rejected;
            }
            _operations.RemoveWhere(task => task.IsCompleted);
            if (_initialization is null)
            {
                startInitialization = new(TaskCreationOptions.RunContinuationsAsynchronously);
                // This actual task cannot enter store code until admission is published.
                _initialization = InitializeAsync(startInitialization.Task);
                Retain(_initialization);
            }
            // captured is still incomplete, so no action or caller prefix can run under this lock.
            operation = ExecuteAsync(captured.Task, _initialization, action, cancellationToken);
            Retain(operation);
        }
        startInitialization?.SetResult();
        // Complete the defensive copy synchronously before returning. Caller code is outside Core locks.
        try { captured.SetResult(capture()); }
        catch (Exception error) { captured.SetException(SafeFailure(error, invalidCode)); }
        return operation;
    }

    private async Task InitializeAsync(Task admitted)
    {
        await admitted.ConfigureAwait(false);
        try
        {
            // Context-owned initialization is never canceled by a caller or Close. Close denies
            // new admission and drains this same bounded store operation before releasing authority.
            await scope.Testing.InitializeOwnerAsync(producerPluginId, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception error) { throw SafeFailure(error, "TestingFailed"); }
    }

    private static async Task<T> ExecuteAsync<TInput, T>(Task<TInput> captured, Task initialization, Func<TInput, Task<T>> action, CancellationToken cancellationToken)
    {
        try
        {
            // WhenAll joins BOTH activities even when either faults first. A failed init cannot
            // release a blocked caller prefix, and a failed capture cannot detach initialization.
            await Task.WhenAll(initialization, captured).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return await action(await captured.ConfigureAwait(false)).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw new OperationCanceledException(cancellationToken); }
        catch (Exception error) { throw SafeFailure(error, "TestingFailed"); }
    }

    // Called only under _sync. Observes faults even when a plugin drops its returned task.
    private void Retain(Task operation)
    {
        _operations.Add(operation);
        _ = operation.ContinueWith(static task => { _ = task.Exception; }, CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private static PluginTestingException SafeFailure(Exception error, string fallback) => error switch
    {
        PluginTestingException testing => TestValidation.Error(testing.ErrorCode),
        ProjectWorkspaceStoreException storage => TestValidation.Error(storage.ErrorCode),
        IOException or UnauthorizedAccessException or SecurityException => TestValidation.Error("StoreIoFailure"),
        _ => TestValidation.Error(fallback)
    };
}
