using System.Security;
using Workbench.Core.Projects;
using Workbench.Sdk;

namespace Workbench.Core.Testing;

/// <summary>Guarded source, case and durable run records for a captured project.</summary>
public sealed partial class ProjectTestStore
{
    private readonly string _directory;
    private readonly string _projectId;
    private readonly string _leasePath;

    public ProjectTestStore(string userRoot, string projectId)
    {
        try
        {
            ProjectWorkspaceStorage.ValidateId(projectId);
            var root = ProjectWorkspaceStorage.NormalizeRoot(userRoot);
            _projectId = projectId;
            _directory = ProjectWorkspaceStorage.PathIn(root, ".workbench-project", "testing");
            _leasePath = ProjectWorkspaceStorage.PathIn(root, ".workbench-project", "testing.lock");
        }
        catch (ProjectWorkspaceStoreException error) { throw TestValidation.Error(error.ErrorCode); }
    }

    // Capture runs before scheduling. A facade must first retain its admission,
    // then use the same TestValidation.Copy* helpers outside coordinator locks
    // before returning or scheduling its later call to this store.
    private static Task<T> CaptureAndSchedule<TInput, T>(Func<TInput> capture, Func<TInput, Task<T>> action, string invalidCode)
    {
        TInput input;
        try { input = capture(); }
        catch (PluginTestingException error) { return Task.FromException<T>(TestValidation.Error(error.ErrorCode)); }
        catch (Exception) { return Task.FromException<T>(TestValidation.Error(invalidCode)); }
        return Schedule(() => action(input));
    }

    private static Task<T> Schedule<T>(Func<Task<T>> action) => Task.Run(async () =>
    {
        try { return await action().ConfigureAwait(false); }
        catch (ProjectWorkspaceStoreException error) { throw TestValidation.Error(error.ErrorCode); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or SecurityException)
        {
            throw TestValidation.Error("StoreIoFailure");
        }
    });

    private async Task<T> WithLeaseAsync<T>(bool create, Func<StoreLease?, Task<T>> action, CancellationToken cancellationToken)
    {
        await using var lease = await AcquireStoreLeaseAsync(create, cancellationToken).ConfigureAwait(false);
        return await action(lease).ConfigureAwait(false);
    }

    private string ManagedPath(string area, string id) => ProjectWorkspaceStorage.PathIn(_directory, area, id + ".json");
    private static string NewId() => Guid.NewGuid().ToString("N");

    /// <summary>A validated same-store exclusive lease. Dispose after all locked helper work settles.</summary>
    internal sealed class StoreLease : IAsyncDisposable
    {
        internal readonly ProjectTestStore Owner;
        internal readonly Dictionary<string, TestSourceSnapshot> Sources = new(StringComparer.Ordinal);
        internal readonly Dictionary<string, TestCaseSetSnapshot> Cases = new(StringComparer.Ordinal);
        internal readonly Dictionary<string, byte[]> CaseBytes = new(StringComparer.Ordinal);
        internal readonly Dictionary<string, TestCaseSetSnapshot> Archives = new(StringComparer.Ordinal);
        internal readonly Dictionary<string, TestRunSnapshot> Runs = new(StringComparer.Ordinal);
        internal readonly Dictionary<string, Dictionary<string, TestEvidenceReceipt>> Evidence = new(StringComparer.Ordinal);
        private readonly FileStream _stream;
        internal bool IsDisposed { get; private set; }

        internal StoreLease(ProjectTestStore owner, FileStream stream) { Owner = owner; _stream = stream; }
        public async ValueTask DisposeAsync()
        {
            IsDisposed = true;
            await _stream.DisposeAsync().ConfigureAwait(false);
        }
    }

    private void RequireLease(StoreLease lease)
    {
        if (lease.Owner != this || lease.IsDisposed) throw TestValidation.Error("StoreBusy");
    }
}
