namespace Workbench.Core;

public sealed record ProjectPreparationResult(bool Succeeded, bool HasPendingWork, string? ErrorCode);

public sealed partial class PluginManager
{
    private Task<ProjectPreparationResult>? _projectPreparation;
    private string? _projectPreparationFilter;
    private PluginRegistrationContext[] _projectPreparationContexts = [];

    public bool HasPendingProjectWork => Volatile.Read(ref _projectPreparation) is { IsCompleted: false }
        || Volatile.Read(ref _entries).Any(entry => entry.Context?.HasPendingProjectWork == true);

    /// <summary>Runs committed hooks off the dispatcher and joins project service calls.
    /// A finite wait/cancellation leaves the actual work owned and available for retry or terminal drain.</summary>
    public async Task<ProjectPreparationResult> PrepareProjectLeaveAsync(string? pluginId = null, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Task<ProjectPreparationResult> preparation;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var entries = Volatile.Read(ref _entries).Where(entry => pluginId is null || entry.Package.Manifest?.Id == pluginId).ToArray();
            var contexts = entries.Where(entry => entry.Snapshot.State == PluginState.Enabled && entry.Context is { IsCommitted: true })
                .Select(entry => entry.Context!).ToArray();
            var sameParticipants = string.Equals(_projectPreparationFilter, pluginId, StringComparison.Ordinal)
                && _projectPreparationContexts.SequenceEqual(contexts);
            if (_projectPreparation is { } current && (!current.IsCompleted || sameParticipants))
            {
                if (!sameParticipants)
                    return new(false, true, "ProjectPreparationBusy");
                // A timeout did not acknowledge the eventual result. Return it
                // once on retry instead of invoking the same hook a second time.
                preparation = current;
            }
            else
            {
                if (pluginId is not null && FindEntry(pluginId) is null)
                    return new(false, false, "ProjectPluginUnavailable");
                if (entries.Any(entry => entry.Context is not null && entry.Snapshot.State != PluginState.Enabled))
                    return new(false, HasPendingProjectWork, "ProjectContextClosed");
                var tasks = contexts.Select(context => context.PrepareProjectLeaveAsync(cancellationToken)).ToArray();
                _projectPreparationFilter = pluginId;
                _projectPreparationContexts = contexts;
                preparation = JoinPreparationAsync(tasks);
                Volatile.Write(ref _projectPreparation, preparation);
            }
        }
        finally { _gate.Release(); }

        try
        {
            var result = await preparation.WaitAsync(_options.StopTimeout, cancellationToken).ConfigureAwait(false);
            // Compare by task identity: an older waiter must not clear a newer
            // participant set admitted after the old task had actually settled.
            _ = Interlocked.CompareExchange(ref _projectPreparation, null, preparation);
            return result;
        }
        catch (TimeoutException) { return new(false, !preparation.IsCompleted, "ProjectPreparationPending"); }
        catch (OperationCanceledException) { return new(false, !preparation.IsCompleted, "ProjectPreparationCanceled"); }
    }

    private static async Task<ProjectPreparationResult> JoinPreparationAsync(Task<ProjectPreparationResult>[] tasks)
    {
        var results = await Task.WhenAll(tasks).ConfigureAwait(false);
        var failure = results.FirstOrDefault(result => !result.Succeeded);
        return failure ?? new(true, false, null);
    }
}
