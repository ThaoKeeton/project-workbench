using Workbench.Core;

namespace Workbench.App;

/// <summary>Owns the single-writer store lease for the whole manager lifetime.</summary>
internal sealed class HostRuntime : IAsyncDisposable
{
    private readonly IDisposable _lease;
    private readonly PluginManagerOptions _options;
    private Task? _initialization;
    private bool _managerRetired;
    private bool _disposed;
    public PluginManager Manager { get; private set; }
    public PluginPackageStore Packages { get; }
    public IReadOnlyList<PluginOperationResult> MaintenanceResults { get; private set; } = [];
    private bool _initializationStarted;

    internal string UserRoot => Path.TrimEndingDirectorySeparator(Path.GetFullPath(_options.UserRoot));
    internal Workbench.Core.Projects.ProjectRuntimeScope? Scope => _options.ProjectScope;

    public HostRuntime(PluginManagerOptions options) : this(options, AcquireLease(options.UserRoot)) { }

    public HostRuntime(PluginManagerOptions options, WorkspaceRootLease baseOwner)
        : this(options, baseOwner.AcquireRuntimeLease(options.UserRoot)) { }

    private HostRuntime(PluginManagerOptions options, IDisposable lease)
    {
        _lease = lease;
        _options = options;
        try
        {
            Packages = new PluginPackageStore(options);
            Manager = new PluginManager(options);
        }
        catch { _lease.Dispose(); throw; }
    }

    public Task InitializeAsync(Action<string>? trace = null)
    {
        if (_initializationStarted) throw new InvalidOperationException("Host initialization has already started.");
        _initializationStarted = true;
        return _initialization = InitializeCoreAsync(trace);
    }

    private async Task InitializeCoreAsync(Action<string>? trace)
    {
        trace?.Invoke("applying-pending-package-maintenance");
        MaintenanceResults = await Packages.ApplyPendingAsync();
        trace?.Invoke("discovering-packages");
        await Manager.DiscoverAsync();
        trace?.Invoke("restoring-enabled-packages");
        await Manager.RestoreEnabledAsync();
    }

    // Terminal manager retirement revokes old host/plugin actions, but keeps this root leased
    // until selection commits or rollback has safely retired the candidate.
    internal async Task StopForProjectLeaveAsync()
    {
        if (_initialization is { } initialization)
            try { await initialization; } catch (Exception) { /* Cleanup still owns this manager. */ }
        await Manager.DisposeAsync();
        _managerRetired = true;
    }

    internal Task RestoreAfterProjectLeaveAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_managerRetired) throw new InvalidOperationException("The previous manager must retire before restoration.");
        Manager = new(_options);
        _managerRetired = false;
        return _initialization = RestoreCoreAsync();
    }

    private async Task RestoreCoreAsync()
    {
        await Manager.DiscoverAsync();
        await Manager.RestoreEnabledAsync();
    }

    internal static FileStream AcquireLease(string userRoot)
    {
        RejectLinks(userRoot);
        Directory.CreateDirectory(userRoot);
        var lockPath = Path.Combine(userRoot, "host.lock");
        RejectLinks(lockPath);
        try { return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException error) when (OperatingSystem.IsWindows() && (error.HResult & 0xFFFF) is 32 or 33)
        {
            // Ownership, not a transient data-file scan. The wrapper's HResult is not the raw sharing code,
            // and this path must not wait for the current owner to release the lease.
            throw new UserStoreInUseException(error);
        }
    }

    internal static void RejectLinks(string path)
    {
        for (var part = Path.GetFullPath(path); part is not null; part = Path.GetDirectoryName(part))
            if ((File.Exists(part) || Directory.Exists(part)) && (File.GetAttributes(part) & FileAttributes.ReparsePoint) != 0)
                throw new IOException($"Reparse points are not allowed for the user store or smoke packages: {part}");
    }

    public async ValueTask DisposeAsync()
    {
        // A failed dispose is retryable. Known work and its user-root lease remain owned.
        if (_disposed) return;
        await StopForProjectLeaveAsync();
        _lease.Dispose();
        _disposed = true;
    }
}

internal sealed class UserStoreInUseException(IOException inner)
    : IOException("此用户目录已由另一个工作台实例占用。请关闭该实例后重试。", inner);
