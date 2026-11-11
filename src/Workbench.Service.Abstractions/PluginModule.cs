namespace Workbench.Service.Abstractions;

/// <summary>
/// Preview plugin contract, version 2.0.0-preview.1. This is not the stable
/// <c>IWorkbenchPlugin</c> API 1 surface and does not change it.
/// </summary>
public static class PluginContractV2
{
    public const string Name = "workbench.plugin.v2";
    public const string Version = "2.0.0-preview.1";
}

/// <summary>
/// In-process backend module. The host grants only the permissions declared by
/// the package, then the module must check them again before touching data.
/// </summary>
public interface IWorkbenchPluginModule
{
    string Id { get; }

    Task StartAsync(PluginModuleStart start, CancellationToken cancellationToken);

    Task<PluginModuleResponse> HandleAsync(PluginModuleRequest request, CancellationToken cancellationToken);
}

public sealed record PluginModuleStart(IReadOnlySet<string> GrantedPermissions);

public sealed record PluginModuleRequest(
    string Action,
    string ProjectId,
    string DataDirectory,
    IReadOnlySet<string> GrantedPermissions,
    string? JsonBody);

public sealed record PluginModuleResponse(int StatusCode, string ContentType, byte[] Body);
