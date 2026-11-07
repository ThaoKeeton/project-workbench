using Workbench.Core.Projects;

namespace Workbench.App;

/// <summary>Owns every admitted editor operation, including autosaves the view did not await.</summary>
internal sealed class ProjectDocumentSession(ProjectContextDocumentStore store) : IAsyncDisposable
{
    private readonly object _gate = new();
    private Task _tail = Task.CompletedTask;
    private Task? _initialization;
    private Task? _disposal;
    private string _text = "", _savedText = "", _revision = "missing";
    private string? _errorCode;
    private bool _loaded, _saveBlocked, _paused, _closing, _disposed;
    private int _reloads;

    public string Text { get { lock (_gate) return _text; } }
    public bool IsDirty { get { lock (_gate) return _text != _savedText; } }
    public bool IsEditable { get { lock (_gate) return Editable; } }
    public string? ErrorCode { get { lock (_gate) return _errorCode; } }
    internal bool HasPendingWork { get { lock (_gate) return !_tail.IsCompleted; } }
    private bool Editable => _loaded && !_paused && !_closing && !_disposed && _reloads == 0;

    public Task InitializeAsync(CancellationToken ct = default)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed || _closing, this);
            return _initialization ??= QueueLocked(async () =>
            {
                try { Adopt(await store.ReadAsync(ct).ConfigureAwait(false)); }
                catch (Exception error) { lock (_gate) { _errorCode = HostFailure.ProjectCode(error); _loaded = false; } }
            });
        }
    }

    public void Edit(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        lock (_gate)
        {
            if (!Editable) throw new InvalidOperationException("The project document is not editable.");
            _text = text;
            if (_errorCode != "RevisionConflict") _saveBlocked = false;
            // Capture no text here: a queued save writes the latest buffer and serializes revisions.
            _ = QueueLocked(() => SaveLatestAsync(retry: false, reportFailure: false, CancellationToken.None));
        }
    }

    public Task FlushAsync(CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (_disposed) return Task.CompletedTask;
            if (_paused && !_closing) throw new InvalidOperationException("The project document is preparing to leave.");
            return QueueLocked(() => SaveLatestAsync(retry: true, reportFailure: true, ct));
        }
    }

    public Task PreserveConflictAndReloadAsync(CancellationToken ct = default)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_paused && !_closing) throw new InvalidOperationException("The project document is preparing to leave.");
            _reloads++;
            return QueueLocked(async () =>
            {
                var reading = false;
                try
                {
                    string text, revision;
                    bool dirty;
                    lock (_gate) { text = _text; revision = _revision; dirty = _text != _savedText; }
                    // No reload, revision advance, or buffer replacement before this durable receipt.
                    if (dirty) _ = await store.PreserveConflictAsync(text, revision, ct).ConfigureAwait(false);
                    reading = true;
                    Adopt(await store.ReadAsync(ct).ConfigureAwait(false));
                }
                catch (Exception error)
                {
                    var code = HostFailure.ProjectCode(error);
                    lock (_gate) { _errorCode = code; _saveBlocked = true; if (reading) _loaded = false; }
                    throw new ProjectWorkspaceStoreException(code);
                }
                finally { lock (_gate) _reloads--; }
            });
        }
    }

    internal void SetPaused(bool paused) { lock (_gate) _paused = paused; }

    internal Task QuiesceAsync(CancellationToken ct)
    {
        lock (_gate)
        {
            _paused = true;
            return QueueLocked(() => SaveLatestAsync(retry: true, reportFailure: true, ct));
        }
    }

    private async Task SaveLatestAsync(bool retry, bool reportFailure, CancellationToken ct)
    {
        if (retry) lock (_gate) _saveBlocked = false;
        while (true)
        {
            string text, revision;
            lock (_gate)
            {
                if (_text == _savedText) return;
                if (!_loaded || _saveBlocked)
                {
                    if (reportFailure) throw new ProjectWorkspaceStoreException(_errorCode ?? "InvalidContext");
                    return;
                }
                text = _text; revision = _revision;
            }
            try
            {
                var written = await store.WriteAsync(text, revision, ct).ConfigureAwait(false);
                lock (_gate) { _revision = written.Revision; _savedText = text; _saveBlocked = false; _errorCode = null; }
            }
            catch (Exception error)
            {
                var code = HostFailure.ProjectCode(error);
                lock (_gate) { _errorCode = code; _saveBlocked = true; }
                if (reportFailure) throw new ProjectWorkspaceStoreException(code);
                return;
            }
        }
    }

    private void Adopt(ProjectContextDocument document)
    {
        lock (_gate)
        {
            _revision = document.Revision; _text = _savedText = document.Text;
            _loaded = true; _saveBlocked = false; _errorCode = null;
        }
    }

    // Even a faulted predecessor remains joined. A later explicit flush/reload can repair it.
    private Task QueueLocked(Func<Task> action)
    {
        var previous = _tail;
        return _tail = Task.Run(async () =>
        {
            try { await previous.ConfigureAwait(false); } catch (Exception) { }
            await action().ConfigureAwait(false);
        });
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_disposed) return ValueTask.CompletedTask;
            _closing = true;
            if (_disposal is null || _disposal.IsCompleted)
                _disposal = QueueLocked(async () =>
                {
                    await SaveLatestAsync(retry: true, reportFailure: true, CancellationToken.None).ConfigureAwait(false);
                    lock (_gate) _disposed = true;
                });
            return new(_disposal);
        }
    }
}
