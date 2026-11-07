using System.Collections.Concurrent;

namespace Workbench.TestDependency;

// Shared only by the test host and the dynamically loaded lifecycle fixture.
public sealed class WorkspaceLifecycleGate : IDisposable
{
    private static readonly ConcurrentDictionary<string, WorkspaceLifecycleGate> Gates = new(StringComparer.Ordinal);
    private readonly string _path;
    private int _live;
    public TaskCompletionSource Prepared { get; } = NewSignal();
    public TaskCompletionSource StopStarted { get; } = NewSignal();
    public TaskCompletionSource Initialized { get; } = NewSignal();
    public TaskCompletionSource PrepareRelease { get; } = NewSignal();
    public TaskCompletionSource StopRelease { get; } = NewSignal();
    public TaskCompletionSource InitializeRelease { get; } = NewSignal();
    public bool FailInitialization { get; set; }
    public bool FailPreparation { get; set; }
    public ConcurrentQueue<string> Events { get; } = new();
    public int Live => Volatile.Read(ref _live);
    public WorkspaceLifecycleGate(string dataPath, bool holdPrepare = false, bool holdStop = false, bool holdInitialize = false)
    {
        _path = dataPath;
        if (!Gates.TryAdd(dataPath, this)) throw new InvalidOperationException("Duplicate test gate.");
        if (!holdPrepare) PrepareRelease.SetResult();
        if (!holdStop) StopRelease.SetResult();
        if (!holdInitialize) InitializeRelease.SetResult();
    }
    public static WorkspaceLifecycleGate For(string dataPath) => Gates[dataPath];
    public void Start() { Interlocked.Increment(ref _live); Events.Enqueue("start"); Initialized.TrySetResult(); }
    public void Retire() { Interlocked.Decrement(ref _live); Events.Enqueue("retired"); }
    public void Dispose()
    {
        PrepareRelease.TrySetResult(); StopRelease.TrySetResult(); InitializeRelease.TrySetResult();
        Gates.TryRemove(_path, out _);
    }
    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
