using Workbench.Sdk;
using Workbench.Core.Projects;

namespace Workbench.Core;

internal sealed class PluginRegistrationContext : IPluginProjectContext, IDisposable
{
    private readonly object _sync = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Dictionary<string, IPluginView> _views = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PluginCommand> _commands = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PluginSetting> _settings = new(StringComparer.Ordinal);
    private bool _open = true;
    private bool _active;
    private int _stopReason;
    private Func<CancellationToken, Task>? _beforeProjectLeave;
    private Task<ProjectPreparationResult>? _preparation;
    private readonly PluginProjectContext _project;

    public PluginRegistrationContext(string pluginId, string dataDirectory, IPluginSettings settings,
        PluginBackgroundTasks backgroundTasks, PluginSecretStore secrets, ProjectRuntimeScope projectScope)
    {
        PluginId = pluginId;
        DataDirectory = dataDirectory;
        Settings = settings;
        Lifetime = _lifetime.Token;
        BackgroundTasks = backgroundTasks;
        Secrets = secrets;
        _project = new(projectScope);
    }

    internal bool IsCommitted { get; private set; }
    public string PluginId { get; }
    public string DataDirectory { get; }
    public CancellationToken Lifetime { get; }
    public IPluginSettings Settings { get; }
    public PluginBackgroundTasks BackgroundTasks { get; }
    public PluginSecretStore Secrets { get; }
    public IProjectContext Project => _project;

    public void RegisterBeforeProjectLeave(Func<CancellationToken, Task> prepare)
    {
        ArgumentNullException.ThrowIfNull(prepare);
        lock (_sync)
        {
            EnsureOpen();
            if (_beforeProjectLeave is not null) throw new InvalidOperationException("Only one project preparation hook may be registered.");
            _beforeProjectLeave = prepare;
        }
    }

    internal bool HasPendingProjectWork
    {
        get { lock (_sync) return _preparation is { IsCompleted: false } || _project.HasPendingWork; }
    }

    internal Task<ProjectPreparationResult> PrepareProjectLeaveAsync(CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            if (_preparation is { IsCompleted: false }) return _preparation;
            if (!_active) return Task.FromResult(new ProjectPreparationResult(false, false, "ProjectContextClosed"));
            return _preparation = Task.Run(async () =>
            {
                Func<CancellationToken, Task>? prepare;
                lock (_sync)
                {
                    // A stop may close this context before the queued callback
                    // starts. Its retained task still must settle before disposal.
                    if (!_active) return new ProjectPreparationResult(false, false, "ProjectContextClosed");
                    prepare = _beforeProjectLeave;
                }
                string? failure = null;
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, Lifetime);
                try
                {
                    linked.Token.ThrowIfCancellationRequested();
                    if (prepare is not null) await prepare(linked.Token).ConfigureAwait(false);
                }
                catch (Exception error) { failure = ProjectFailure.Code(error, "ProjectPreparationFailed"); }
                await _project.DrainAsync().ConfigureAwait(false);
                return new ProjectPreparationResult(failure is null, false, failure);
            });
        }
    }

    internal async Task DrainProjectWorkAsync()
    {
        Task? preparation;
        lock (_sync) preparation = _preparation;
        if (preparation is not null) await preparation.ConfigureAwait(false);
        await _project.DrainAsync().ConfigureAwait(false);
    }
    IPluginBackgroundTasks IPluginRuntimeContext.BackgroundTasks => BackgroundTasks;
    IPluginSecretStore IPluginRuntimeContext.Secrets => Secrets;
    public PluginStopReason StopReason => (PluginStopReason)Volatile.Read(ref _stopReason);

    public void RegisterView(IPluginView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        ValidateMetadata(view.Id, view.Title);
        lock (_sync)
        {
            EnsureOpen();
            if (!_views.TryAdd(view.Id, view))
                throw new InvalidOperationException($"Duplicate view ID '{view.Id}'.");
        }
    }

    public void RegisterCommand(PluginCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateMetadata(command.Id, command.Title);
        ArgumentNullException.ThrowIfNull(command.ExecuteAsync);
        lock (_sync)
        {
            EnsureOpen();
            if (!_commands.TryAdd(command.Id, command))
                throw new InvalidOperationException($"Duplicate command ID '{command.Id}'.");
        }
    }

    public void RegisterSetting(PluginSetting setting)
    {
        ArgumentNullException.ThrowIfNull(setting);
        ValidateMetadata(setting.Id, setting.Title);
        ArgumentNullException.ThrowIfNull(setting.DefaultValue);
        lock (_sync)
        {
            EnsureOpen();
            if (!_settings.TryAdd(setting.Id, setting))
                throw new InvalidOperationException($"Duplicate setting ID '{setting.Id}'.");
        }
    }

    internal static void ValidateMetadata(string id, string title)
    {
        JsonPluginSettings.ValidateKey(id);
        if (string.IsNullOrWhiteSpace(title) || title.Length > 200)
            throw new ArgumentException("A contribution title of at most 200 characters is required.", nameof(title));
    }

    private void EnsureOpen()
    {
        if (!_open)
            throw new InvalidOperationException("Contributions can only be registered during InitializeAsync.");
    }

    internal void Commit()
    {
        lock (_sync)
        {
            EnsureOpen();
            _open = false;
            _active = true;
            IsCommitted = true;
            Secrets.CloseRegistration();
            BackgroundTasks.Commit();
        }
    }

    internal PluginContributions Snapshot()
    {
        lock (_sync)
        {
            return new(
                Array.AsReadOnly(_views.Values.Select(v => new PluginViewRegistration(PluginId, v)).ToArray()),
                Array.AsReadOnly(_commands.Values.Select(c => new PluginCommandRegistration(PluginId, c.Id, c.Title, c.Description)).ToArray()),
                Array.AsReadOnly(_settings.Values.Select(s => new PluginSettingRegistration(PluginId, s)).ToArray()));
        }
    }

    internal PluginCommand? FindCommand(string id)
    {
        lock (_sync)
            return _active ? _commands.GetValueOrDefault(id) : null;
    }

    internal void Close(PluginStopReason reason)
    {
        lock (_sync)
        {
            _open = false;
            _active = false;
            _beforeProjectLeave = null;
            _project.Close();
            var current = StopReason;
            if (current == PluginStopReason.None || current == PluginStopReason.HostShutdown && reason != PluginStopReason.HostShutdown)
                Volatile.Write(ref _stopReason, (int)reason);
            BackgroundTasks.Close(StopReason);
            Secrets.CloseRegistration();
            _views.Clear();
            _commands.Clear();
            _settings.Clear();
        }
    }

    internal Task CancelLifetimeAsync() => _lifetime.CancelAsync();

    public void Dispose()
    {
        BackgroundTasks.Dispose();
        Secrets.Dispose();
        _lifetime.Dispose();
    }
}
