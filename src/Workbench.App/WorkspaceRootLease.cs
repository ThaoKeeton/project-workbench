namespace Workbench.App;

internal sealed class WorkspaceRootLease : IDisposable
{
    private readonly object _gate = new();
    private readonly FileStream _stream;
    private int _borrowers;
    private bool _disposed;
    public string Root { get; }
    private WorkspaceRootLease(string root)
    {
        Root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        _stream = HostRuntime.AcquireLease(Root);
    }
    public static WorkspaceRootLease Acquire(string baseRoot) => new(baseRoot);
    internal bool Matches(string root) => string.Equals(Root, Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)),
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    public IDisposable Borrow(string root)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            HostRuntime.RejectLinks(root);
            if (!Matches(root)) throw new ArgumentException("A borrow must match the owner's root.", nameof(root));
            _borrowers++;
            return new BorrowedLease(this, null);
        }
    }
    internal IDisposable AcquireRuntimeLease(string root)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            HostRuntime.RejectLinks(root);
            // Secondary runtimes own their lock and also pin the normal app's base owner.
            var secondary = Matches(root) ? null : HostRuntime.AcquireLease(root);
            _borrowers++;
            return new BorrowedLease(this, secondary);
        }
    }
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            if (_borrowers != 0) throw new InvalidOperationException("Runtime leases must retire before the base owner.");
            _stream.Dispose();
            _disposed = true;
        }
    }
    private void Release() { lock (_gate) _borrowers--; }
    private sealed class BorrowedLease(WorkspaceRootLease owner, FileStream? secondary) : IDisposable
    {
        private WorkspaceRootLease? _owner = owner;
        public void Dispose()
        {
            var retained = Interlocked.Exchange(ref _owner, null);
            if (retained is null) return;
            secondary?.Dispose();
            retained.Release();
        }
    }
}
