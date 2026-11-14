using System.Text.Json;

namespace Workbench.App;

internal sealed record RunOptions(bool SmokeTest, string UserRoot, string? ReportPath)
{
    public string? RuntimeProbe { get; init; }
    public string? BusinessProbe { get; init; }
    public string? WorkspaceProbe { get; init; }
    public string? TestingProbe { get; init; }
    public string? SourceAcceptanceProbe { get; init; }
    // Existing in-process runtime probe windows keep their direct-host semantics.
    internal bool DirectRuntime { get; init; }
    internal bool SkipAutomaticProbe { get; init; }
    public bool IsIsolated => SmokeTest || RuntimeProbe is not null || BusinessProbe is not null || WorkspaceProbe is not null || TestingProbe is not null || SourceAcceptanceProbe is not null;
    public static readonly string[] BusinessProbePhases = ["seed", "restart", "update", "fault", "rollback", "remove",
        "acceptance-seed", "acceptance-restore", "acceptance-stopped", "acceptance-config", "acceptance-missing", "acceptance-disabled"];
    public static readonly string[] RuntimeProbePhases = ["management", "restart-seed", "restart-verify", "restart-disabled", "update-verify", "rollback-verify", "remove-verify"];
    public static readonly string[] SampleIds = ["com.projectworkbench.summary", "com.projectworkbench.test-cases"];
    private static readonly JsonSerializerOptions ReportJson = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private readonly List<object> _startupStages = [];
    public IReadOnlyList<object> StartupStages => _startupStages.ToArray();
    internal HostStartupDiagnostics StartupDiagnostics { get; } = new();

    public void TraceStartup(string stage)
    {
        try
        {
            StartupDiagnostics.ObserveStartup(stage);
            if (ReportPath is null) return;
            _startupStages.Add(new { stage, threadId = Environment.CurrentManagedThreadId, utc = DateTimeOffset.UtcNow });
            WriteReport(new { succeeded = false, stage, userRoot = UserRoot, startupStages = StartupStages });
        }
        catch (Exception) when (SmokeTest && stage.StartsWith("native-smoke-", StringComparison.Ordinal))
        {
            // These isolated-smoke checkpoints are best effort, including during window cleanup.
        }
    }

    public static RunOptions Parse(string[] args)
    {
        var smoke = args.Contains("--smoke-test", StringComparer.Ordinal);
        string? report = null;
        string? probe = null;
        string? business = null;
        string? workspace = null;
        string? testing = null;
        string? source = null;
        string? probeRoot = null;
        var sourceLane = args.Any(arg => arg.StartsWith("--source-acceptance", StringComparison.OrdinalIgnoreCase));
        var sourceValues = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i].StartsWith("--source-acceptance", StringComparison.OrdinalIgnoreCase) && args[i] != "--source-acceptance-probe")
                throw new ArgumentException("Unknown isolated source acceptance option.");
            if (sourceLane && args[i] is not ("--source-acceptance-probe" or "--probe-root" or "--smoke-report" or "--smoke-test"
                or "--runtime-probe" or "--business-probe" or "--workspace-probe" or "--testing-probe"))
                throw new ArgumentException("Unknown argument in isolated source acceptance mode.");
            if (sourceLane && args[i] is "--source-acceptance-probe" or "--probe-root" or "--smoke-report" && !sourceValues.Add(args[i]))
                throw new ArgumentException("Duplicate isolated source acceptance value.");
            if (args[i] is "--business-probe" or "--runtime-probe" or "--workspace-probe" or "--testing-probe" or "--source-acceptance-probe" or "--probe-root" or "--smoke-report" &&
                (i + 1 == args.Length || string.IsNullOrWhiteSpace(args[i + 1]) || args[i + 1].StartsWith("--", StringComparison.Ordinal)))
                throw new ArgumentException("An isolated probe option requires its explicit value.");
            if (args[i] == "--smoke-report" && i + 1 < args.Length) report = Path.GetFullPath(args[++i]);
            else if (args[i] == "--runtime-probe" && i + 1 < args.Length) probe = args[++i];
            else if (args[i] == "--business-probe" && i + 1 < args.Length) business = args[++i];
            else if (args[i] == "--workspace-probe" && i + 1 < args.Length) workspace = args[++i];
            else if (args[i] == "--testing-probe" && i + 1 < args.Length) testing = args[++i];
            else if (args[i] == "--source-acceptance-probe" && i + 1 < args.Length) source = args[++i];
            else if (args[i] == "--probe-root" && i + 1 < args.Length) probeRoot = Path.GetFullPath(args[++i]);
        }
        if (probe is not null && (!RuntimeProbePhases.Contains(probe, StringComparer.Ordinal) || smoke))
            throw new ArgumentException("Unknown or conflicting isolated runtime probe mode.");
        if (business is not null && (!BusinessProbePhases.Contains(business, StringComparer.Ordinal) || smoke || probe is not null))
            throw new ArgumentException("Unknown or conflicting isolated business probe mode.");
        if (workspace is not null && (workspace is not ("seed" or "verify") || smoke || probe is not null || business is not null))
            throw new ArgumentException("Unknown or conflicting isolated workspace probe mode.");
        if (testing is not null && (testing is not ("seed" or "verify") || smoke || probe is not null || business is not null || workspace is not null))
            throw new ArgumentException("Unknown or conflicting isolated testing probe mode.");
        if (source is not null && (source is not ("seed" or "verify" or "stop-figma" or "stop-ai") || smoke || probe is not null || business is not null || workspace is not null || testing is not null))
            throw new ArgumentException("Unknown or conflicting isolated source acceptance mode.");
        if (probe is null && business is null && workspace is null && testing is null && source is null && probeRoot is not null) throw new ArgumentException("A probe root requires an isolated probe.");
        if (probe is not null || business is not null || workspace is not null || testing is not null || source is not null)
        {
            var parent = Path.Combine(Path.GetTempPath(), source is not null ? "ProjectWorkbench-source-acceptance-probe" : testing is not null ? "ProjectWorkbench-testing-probe" : workspace is not null ? "ProjectWorkbench-workspace-probe"
                : business is not null ? "ProjectWorkbench-business-probe" : "ProjectWorkbench-runtime-probe");
            probeRoot ??= Path.Combine(parent, Guid.NewGuid().ToString("N"));
            if (!string.Equals(Path.GetDirectoryName(probeRoot), Path.TrimEndingDirectorySeparator(parent), StringComparison.OrdinalIgnoreCase)
                || !Guid.TryParseExact(Path.GetFileName(probeRoot), "N", out _)
                || source is not null && Path.GetFileName(probeRoot).Any(char.IsUpper))
                throw new ArgumentException("Runtime probes require an isolated GUID directory under the dedicated temporary probe root.");
        }
        var root = probeRoot ?? (smoke
            ? Path.Combine(Path.GetTempPath(), "ProjectWorkbench-smoke", Guid.NewGuid().ToString("N"))
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ProjectWorkbench"));
        return new(smoke, root, smoke || probe is not null || business is not null || workspace is not null || testing is not null || source is not null
                ? report ?? Path.Combine(root, source is not null ? "source-acceptance-result.json" : "smoke-result.json") : null)
            { RuntimeProbe = probe, BusinessProbe = business, WorkspaceProbe = workspace, TestingProbe = testing, SourceAcceptanceProbe = source };
    }

    public void WriteReport(object result)
    {
        if (ReportPath is null) return;
        Directory.CreateDirectory(Path.GetDirectoryName(ReportPath)!);
        File.WriteAllText(ReportPath, JsonSerializer.Serialize(result, ReportJson));
    }

    public void WriteFailure(string stage, Exception error, string? nativeMessage = null) => WriteReport(new
    {
        succeeded = false,
        stage,
        userRoot = UserRoot,
        error = HostFailure.Describe("HostOperationFailed", error),
        exceptionType = error.GetType().FullName,
        nativeMessage = nativeMessage is null ? null : "NativeFailure",
        errorHResult = $"0x{unchecked((uint)error.HResult):X8}",
        startupStages = StartupStages,
        completedUtc = DateTimeOffset.UtcNow
    });
}
