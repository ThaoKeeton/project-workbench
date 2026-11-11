namespace Workbench.Service;

/// <summary>Preview service settings. <see cref="AllowLan"/> is rejected at startup.</summary>
public sealed record WorkbenchServiceOptions
{
    public required string UserRoot { get; init; }
    public required string V1PluginsRoot { get; init; }
    public required string V2PluginsRoot { get; init; }
    public string? UiRoot { get; init; }
    public string? SessionToken { get; init; }
    /// <summary>Reserved for a future opt-in. The preview always refuses this flag.</summary>
    public bool AllowLan { get; init; }
}
