namespace Workbench.Sdk;

/// <summary>Optional project services supplied by host 0.3.0 or later; API/runtime services 1 are unchanged.</summary>
public static class WorkbenchProjectServices
{
    public const int Version = 1;
}

public sealed record ProjectIdentity(string Id, string Name);
public sealed record ProjectContextSnapshot(string Revision, string Instructions);

public interface IProjectContext
{
    /// <summary>Current display identity; calls are revoked when the owning plugin context closes.</summary>
    ProjectIdentity Identity { get; }
    Task<ProjectContextSnapshot> ReadAsync(CancellationToken cancellationToken = default);
    /// <summary>Requires the last read revision. Cancellation does not establish rollback; reread once work settles.</summary>
    Task<ProjectContextSnapshot> UpdateAsync(string instructions, string expectedRevision, CancellationToken cancellationToken = default);
}

public interface IPluginProjectContext : IPluginRuntimeContext
{
    IProjectContext Project { get; }
    /// <summary>Register once during InitializeAsync. Runs off the UI thread; capture document models, never UI controls.</summary>
    void RegisterBeforeProjectLeave(Func<CancellationToken, Task> prepare);
}

/// <summary>A safe code only. Hosts must allow-list codes even when supplied by this public exception type.</summary>
public sealed class PluginProjectContextException(string errorCode) : Exception("Project context operation failed.")
{
    public string ErrorCode { get; } = errorCode;
}
