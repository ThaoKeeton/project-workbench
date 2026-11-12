using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;

namespace Workbench.TestExecutor;

public sealed class SuiteLoader
{
    public const int MaxDocumentBytes = 256 * 1024;
    private static readonly JsonSchema Schema = JsonSchema.FromText(ReadResource("Workbench.TestExecutor.suite.v2.schema.json"));
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static string SchemaText { get; } = ReadResource("Workbench.TestExecutor.suite.v2.schema.json");

    public TestSuite Load(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > MaxDocumentBytes)
            throw new ExecutorDataException(ExecutorError.InvalidSuite);
        JsonNode node;
        try
        {
            node = LooksLikeJson(text) ? JsonNode.Parse(text) ?? throw new ExecutorDataException(ExecutorError.InvalidSuite) : YamlJson.Parse(text);
        }
        catch (JsonException)
        {
            throw new ExecutorDataException(ExecutorError.InvalidSuite);
        }
        if (!SchemaCheck.Matches(Schema, node)) throw new ExecutorDataException(ExecutorError.InvalidSuite);
        SuiteDto? dto;
        try
        {
            dto = node.Deserialize<SuiteDto>(Json);
        }
        catch (JsonException)
        {
            throw new ExecutorDataException(ExecutorError.InvalidSuite);
        }
        return Normalize(dto);
    }

    public TestSuite LoadBuiltin() => Load(ReadResource("Workbench.TestExecutor.synthetic-suite.json"));

    private static bool LooksLikeJson(string text)
    {
        foreach (var character in text)
        {
            if (char.IsWhiteSpace(character)) continue;
            return character == '{';
        }
        return false;
    }

    private static TestSuite Normalize(SuiteDto? dto)
    {
        if (dto is null || !TextRules.IsSafeId(dto.Id) || !TextRules.IsSafeText(dto.Name, 120) || dto.Cases is not { Count: > 0 })
            throw new ExecutorDataException(ExecutorError.InvalidSuite);
        if (dto.Cases.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != dto.Cases.Count)
            throw new ExecutorDataException(ExecutorError.InvalidSuite);
        var application = NormalizeApplication(dto.Application);
        return new TestSuite(
            dto.Id!,
            dto.Name!,
            dto.StopOnFirstFailure ?? false,
            dto.MaskPasswordControls ?? true,
            application,
            (dto.Masks ?? []).Select(NormalizeMask).ToArray(),
            dto.Cases.Select(NormalizeCase).ToArray(),
            NormalizeVisual(dto.Visual),
            NormalizePopup(dto.PopupSelector));
    }

    private static ApplicationTarget? NormalizeApplication(ApplicationDto? application)
    {
        if (application is null) return null;
        if (application.ProcessName is not null && !TextRules.IsSafeId(application.ProcessName))
            throw new ExecutorDataException(ExecutorError.InvalidSuite);
        if (!TextRules.IsOptionalText(application.WindowTitle, 120) || !TextRules.IsOptionalText(application.Executable, 260) || !TextRules.IsOptionalText(application.Url, 200))
            throw new ExecutorDataException(ExecutorError.InvalidSuite);
        if (application.DebugPort is int port && port is < 1 or > 65535)
            throw new ExecutorDataException(ExecutorError.InvalidSuite);
        var arguments = application.Arguments ?? [];
        if (arguments.Count > 20 || arguments.Any(argument => !TextRules.IsOptionalText(argument, 200) || argument.Length == 0))
            throw new ExecutorDataException(ExecutorError.InvalidSuite);
        var environment = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in application.Env ?? [])
        {
            if (!TextRules.IsSafeId(pair.Key) || !TextRules.IsOptionalText(pair.Value, 200))
                throw new ExecutorDataException(ExecutorError.InvalidSuite);
            environment[pair.Key] = pair.Value;
        }
        return new ApplicationTarget(
            BlankToNull(application.ProcessName),
            BlankToNull(application.WindowTitle),
            application.DebugPort,
            BlankToNull(application.Executable),
            arguments,
            environment,
            application.Attach ?? false,
            BlankToNull(application.Url));
    }

    private static VisualSettings? NormalizeVisual(VisualDto? visual)
    {
        if (visual is null) return null;
        var ratio = visual.MaxMismatchRatio ?? VisualSettings.Default.MaxMismatchRatio;
        var tolerance = visual.ChannelTolerance ?? VisualSettings.Default.ChannelTolerance;
        if (ratio is < 0 or > 1 || tolerance is < 0 or > 255) throw new ExecutorDataException(ExecutorError.InvalidSuite);
        var ignore = (visual.Ignore ?? []).Select(rect =>
        {
            if (rect.X is null || rect.Y is null || rect.Width is null || rect.Height is null || rect.Width < 1 || rect.Height < 1)
                throw new ExecutorDataException(ExecutorError.InvalidSuite);
            return new PixelRect(rect.X.Value, rect.Y.Value, rect.Width.Value, rect.Height.Value);
        }).ToArray();
        return new VisualSettings(ratio, tolerance, ignore);
    }

    private static TestCase NormalizeCase(CaseDto item)
    {
        if (!TextRules.IsSafeId(item.Id) || !TextRules.IsSafeText(item.Name, 120) || item.Steps is not { Count: > 0 })
            throw new ExecutorDataException(ExecutorError.InvalidSuite);
        if (item.Steps.Select(step => step.Id).Distinct(StringComparer.Ordinal).Count() != item.Steps.Count)
            throw new ExecutorDataException(ExecutorError.InvalidSuite);
        return new TestCase(item.Id!, item.Name!, item.AllowFallback ?? true, item.Steps.Select(NormalizeStep).ToArray());
    }

    private static TestStep NormalizeStep(StepDto step)
    {
        if (!TextRules.IsSafeId(step.Id) || step.Type is not ("web" or "flaui" or "ai" or "scene3d") || string.IsNullOrEmpty(step.Action))
            throw new ExecutorDataException(ExecutorError.InvalidSuite);
        if (!TextRules.IsOptionalText(step.Expected, 500) || !TextRules.IsOptionalText(step.Input, 500))
            throw new ExecutorDataException(ExecutorError.InvalidSuite);
        if (step.Tolerance is < 0 or > 1000) throw new ExecutorDataException(ExecutorError.InvalidSuite);
        var kind = step.Type switch
        {
            "ai" => StepKind.Ai,
            "web" => StepKind.Web,
            "scene3d" => StepKind.Scene3d,
            _ => StepKind.FlaUi
        };
        var target = NormalizeSelector(step.Target);
        if (kind == StepKind.FlaUi && target?.HasUiTarget != true) throw new ExecutorDataException(ExecutorError.InvalidSuite);
        if (kind == StepKind.Web && step.Action is not ("screenshot" or "switch-window" or "open-file") && target?.HasDomLocator != true)
            throw new ExecutorDataException(ExecutorError.InvalidSuite);
        if (kind == StepKind.Web && step.Action == "switch-window" && target?.Title is null && target?.Url is null)
            throw new ExecutorDataException(ExecutorError.InvalidSuite);
        if (kind == StepKind.Scene3d && step.Action is not ("read-scene" or "project" or "drag" or "orbit" or "assert-scene" or "screenshot" or "probe" or "snapshot" or "diff" or "click" or "wheel" or "compare-pixels"))
            throw new ExecutorDataException(ExecutorError.InvalidSuite);
        var sceneLocator = target?.HasSceneLocator == true;
        var worldPoint = step.Input?.StartsWith("world:", StringComparison.Ordinal) == true;
        if (kind == StepKind.Scene3d && step.Action is "click" or "drag" or "project" && !sceneLocator && !worldPoint)
            throw new ExecutorDataException(ExecutorError.InvalidSuite);
        if (kind == StepKind.Scene3d && step.Action is not ("screenshot" or "probe" or "compare-pixels" or "snapshot" or "diff" or "read-scene" or "assert-scene") && target?.HasDomLocator != true && !sceneLocator && !worldPoint)
            throw new ExecutorDataException(ExecutorError.InvalidSuite);
        var timeout = step.TimeoutSeconds ?? kind switch
        {
            StepKind.Ai => ExecutorSchemas.DefaultAiTimeoutSeconds,
            StepKind.Web or StepKind.Scene3d => ExecutorSchemas.RendererBlockSeconds,
            _ => ExecutorSchemas.DefaultFlaUiTimeoutSeconds
        };
        if (timeout is < 1 or > 300) throw new ExecutorDataException(ExecutorError.InvalidSuite);
        return new TestStep(step.Id!, kind, step.Action, target, BlankToNull(step.Expected), BlankToNull(step.Input), timeout, step.AllowFallback ?? true, step.Sensitive ?? false, step.AllowPopup ?? false, step.Tolerance);
    }

    private static MaskSpec NormalizeMask(MaskDto mask)
    {
        if (mask.Kind == "rectangle")
        {
            if (mask.X is null || mask.Y is null || mask.Width is null || mask.Height is null || mask.Width < 1 || mask.Height < 1)
                throw new ExecutorDataException(ExecutorError.InvalidSuite);
            return new RectangleMask(mask.X.Value, mask.Y.Value, mask.Width.Value, mask.Height.Value);
        }
        if (mask.Kind != "selector") throw new ExecutorDataException(ExecutorError.InvalidSuite);
        return new SelectorMask(NormalizeSelector(new SelectorDto { Name = mask.Name, AutomationId = mask.AutomationId }) ?? throw new ExecutorDataException(ExecutorError.InvalidSuite));
    }

    private static ElementSelector? NormalizeSelector(SelectorDto? selector)
    {
        if (selector is null) return null;
        var name = BlankToNull(selector.Name);
        var automationId = BlankToNull(selector.AutomationId);
        var css = BlankToNull(selector.Selector);
        var text = BlankToNull(selector.Text);
        var role = BlankToNull(selector.Role);
        var title = BlankToNull(selector.Title);
        var url = BlankToNull(selector.Url);
        var objectName = BlankToNull(selector.Object);
        var uuid = BlankToNull(selector.Uuid);
        var path = BlankToNull(selector.Path);
        if (name is null && automationId is null && css is null && text is null && role is null && title is null && url is null && objectName is null && uuid is null && path is null) return null;
        if (!TextRules.IsOptionalText(name, 200) || !TextRules.IsOptionalText(automationId, 200) || !TextRules.IsOptionalText(css, 200)
            || !TextRules.IsOptionalText(text, 200) || !TextRules.IsOptionalText(role, 40) || !TextRules.IsOptionalText(title, 120) || !TextRules.IsOptionalText(url, 200)
            || !TextRules.IsOptionalText(objectName, 80) || !TextRules.IsOptionalText(uuid, 80) || !TextRules.IsOptionalText(path, 200))
            throw new ExecutorDataException(ExecutorError.InvalidSuite);
        return new ElementSelector(name, automationId, css, text, role, title, url, objectName, uuid, path);
    }

    private static string NormalizePopup(string? selector)
    {
        var popup = BlankToNull(selector) ?? ExecutorSchemas.PopupSelector;
        if (!TextRules.IsSafeText(popup, 80)) throw new ExecutorDataException(ExecutorError.InvalidSuite);
        return popup;
    }

    private static string? BlankToNull(string? value) => string.IsNullOrEmpty(value) ? null : value;

    internal static string ReadResource(string name)
    {
        using var stream = typeof(SuiteLoader).Assembly.GetManifestResourceStream(name) ?? throw new InvalidOperationException("missing resource");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private sealed class SuiteDto
    {
        public string? Id { get; init; }
        public string? Name { get; init; }
        public bool? StopOnFirstFailure { get; init; }
        public bool? MaskPasswordControls { get; init; }
        public ApplicationDto? Application { get; init; }
        public List<MaskDto>? Masks { get; init; }
        public List<CaseDto>? Cases { get; init; }
        public VisualDto? Visual { get; init; }
        public string? PopupSelector { get; init; }
    }

    private sealed class ApplicationDto
    {
        public string? ProcessName { get; init; }
        public string? WindowTitle { get; init; }
        public int? DebugPort { get; init; }
        public string? Executable { get; init; }
        public List<string>? Arguments { get; init; }
        public Dictionary<string, string>? Env { get; init; }
        public bool? Attach { get; init; }
        public string? Url { get; init; }
    }

    private sealed class VisualDto
    {
        public double? MaxMismatchRatio { get; init; }
        public int? ChannelTolerance { get; init; }
        public List<RectDto>? Ignore { get; init; }
    }

    private sealed class RectDto
    {
        public int? X { get; init; }
        public int? Y { get; init; }
        public int? Width { get; init; }
        public int? Height { get; init; }
    }

    private sealed class CaseDto
    {
        public string? Id { get; init; }
        public string? Name { get; init; }
        public bool? AllowFallback { get; init; }
        public List<StepDto>? Steps { get; init; }
    }

    private sealed class StepDto
    {
        public string? Id { get; init; }
        public string? Type { get; init; }
        public string? Action { get; init; }
        public SelectorDto? Target { get; init; }
        public string? Expected { get; init; }
        public string? Input { get; init; }
        public int? TimeoutSeconds { get; init; }
        public bool? AllowFallback { get; init; }
        public bool? Sensitive { get; init; }
        public bool? AllowPopup { get; init; }
        public double? Tolerance { get; init; }
    }

    private sealed class SelectorDto
    {
        public string? Name { get; init; }
        public string? AutomationId { get; init; }
        public string? Selector { get; init; }
        public string? Text { get; init; }
        public string? Role { get; init; }
        public string? Title { get; init; }
        public string? Url { get; init; }
        public string? Object { get; init; }
        public string? Uuid { get; init; }
        public string? Path { get; init; }
    }

    private sealed class MaskDto
    {
        public string? Kind { get; init; }
        public int? X { get; init; }
        public int? Y { get; init; }
        public int? Width { get; init; }
        public int? Height { get; init; }
        public string? Name { get; init; }
        public string? AutomationId { get; init; }
    }
}
