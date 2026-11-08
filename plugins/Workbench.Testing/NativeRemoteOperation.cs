using System.Runtime.ExceptionServices;
using Workbench.Sdk;

namespace Workbench.Testing;

internal sealed class NativeRemoteOperation
{
    private readonly object _sync = new();
    private Entry? _active;
    private int _seals;
    private bool _terminal;
    internal IDisposable Seal() { lock (_sync) _seals++; return new Lease(this); }
    internal void Terminal() { lock (_sync) _terminal = true; }
    private sealed class Lease(NativeRemoteOperation owner) : IDisposable
    {
        private NativeRemoteOperation? _owner = owner;
        public void Dispose()
        {
            var current = Interlocked.Exchange(ref _owner, null);
            if (current is not null) lock (current._sync) current._seals--;
        }
    }
    private sealed class Entry
    {
        internal readonly CancellationTokenSource Source = new();
        internal bool AcceptsCancellation = true;
        internal Task? Cancellation;
    }
    internal bool IsBusy { get { lock (_sync) return _active is not null; } }
    internal Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> action, Func<Exception?, Task>? settled = null)
    {
        var entry = new Entry();
        lock (_sync)
        {
            if (_terminal || _seals != 0 || _active is not null)
            { entry.Source.Dispose(); throw new PluginTestingException(_terminal ? "TestingContextClosed" : "StoreBusy"); }
            _active = entry;
        }
        return ExecuteAsync();
        async Task<T> ExecuteAsync()
        {
            T result = default!; Exception? failure = null;
            try { result = await action(entry.Source.Token).ConfigureAwait(false); }
            catch (Exception error) { failure = error; }
            Task cancellation;
            lock (_sync)
            {
                // Close cancellation admission before taking the task to join; disposal uses the same lock.
                entry.AcceptsCancellation = false;
                cancellation = entry.Cancellation ?? Task.CompletedTask;
            }
            try { await cancellation.ConfigureAwait(false); }
            catch (Exception error) { failure ??= error; }
            try { if (settled is not null) await settled(failure).ConfigureAwait(false); }
            catch (Exception error) { failure ??= error; }
            finally { lock (_sync) { entry.Source.Dispose(); _active = null; } }
            if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
            return result;
        }
    }
    internal Task RequestCancellationAsync()
    {
        lock (_sync)
        {
            if (_active is not { } entry) return Task.CompletedTask;
            // CancelAsync schedules actual callbacks off the UI thread. The operation retains that exact task.
            if (entry.AcceptsCancellation) return entry.Cancellation ??= entry.Source.CancelAsync();
            return entry.Cancellation ?? Task.CompletedTask;
        }
    }
}
