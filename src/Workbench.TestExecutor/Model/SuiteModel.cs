namespace Workbench.TestExecutor;

public enum StepKind
{
    Web,
    FlaUi,
    Ai
}

public enum ExportMode
{
    Ask,
    Local,
    Push
}

public sealed record ElementSelector(
    string? Name,
    string? AutomationId,
    string? Selector = null,
    string? Text = null,
    string? Role = null,
    string? Title = null,
    string? Url = null)
{
    public string Display =>
        Selector is { Length: > 0 } ? "selector=" + Selector :
        Text is { Length: > 0 } ? "text=" + Text :
        Role is { Length: > 0 } ? "role=" + Role + (Name is { Length: > 0 } ? "/" + Name : "") :
        Title is { Length: > 0 } ? "title=" + Title :
        Url is { Length: > 0 } ? "url=" + Url :
        AutomationId is { Length: > 0 } ? "automationId=" + AutomationId :
        "name=" + Name;

    public bool HasUiTarget => Name is { Length: > 0 } || AutomationId is { Length: > 0 };

    public bool HasDomLocator => Selector is { Length: > 0 } || Text is { Length: > 0 } || Role is { Length: > 0 };
}

public sealed record ApplicationTarget(
    string? ProcessName,
    string? WindowTitle,
    int? DebugPort = null,
    string? Executable = null,
    IReadOnlyList<string>? Arguments = null,
    IReadOnlyDictionary<string, string>? Environment = null,
    bool Attach = false,
    string? Url = null);

public sealed record VisualSettings(double MaxMismatchRatio, int ChannelTolerance, IReadOnlyList<PixelRect> Ignore)
{
    public static VisualSettings Default { get; } = new(0.01, 8, []);
}

public abstract record MaskSpec;

public sealed record RectangleMask(int X, int Y, int Width, int Height) : MaskSpec;

public sealed record SelectorMask(ElementSelector Selector) : MaskSpec;

public sealed record TestStep(
    string Id,
    StepKind Kind,
    string Action,
    ElementSelector? Target,
    string? Expected,
    string? Input,
    int TimeoutSeconds,
    bool AllowFallback,
    bool Sensitive);

public sealed record TestCase(string Id, string Name, bool AllowFallback, IReadOnlyList<TestStep> Steps);

public sealed record TestSuite(
    string Id,
    string Name,
    bool StopOnFirstFailure,
    bool MaskPasswordControls,
    ApplicationTarget? Application,
    IReadOnlyList<MaskSpec> Masks,
    IReadOnlyList<TestCase> Cases,
    VisualSettings? Visual = null);

public readonly record struct PixelRect(int X, int Y, int Width, int Height);

public sealed record RawFrame(byte[] Png, IReadOnlyList<PixelRect> SensitiveRegions);

public sealed class MaskedImage
{
    internal MaskedImage(byte[] png) => Png = png;

    public byte[] Png { get; }
}
