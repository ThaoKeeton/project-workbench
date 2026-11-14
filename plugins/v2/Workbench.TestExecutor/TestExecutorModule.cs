using System.Text.Json;
using Workbench.Service.Abstractions;
using Workbench.TestExecutor;

namespace Workbench.TestExecutor.Plugin;

public sealed class TestExecutorModule : IWorkbenchPluginModule, IWorkbenchRunEventSource
{
    public const string PluginId = "com.projectworkbench.test-executor";
    private const int RunLimit = 8;
    private static readonly AsyncLocal<ExecutionPolicy?> Ambient = new();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly ExecutionPolicy Denied = new(true, false, ExportMode.Local, null, null, ExecutorSchemas.DefaultComputerUseModel);
    private readonly SuiteCatalog _catalog;
    private readonly ExecutorSettingsStore _settings;
    private readonly IFailureExporter _exporter;
    private readonly ILocatorRepairProvider _repair;
    private AiBudgetLedger _budget = AiBudgetLedger.InMemory();
    private readonly object _gate = new();
    private readonly Dictionary<string, RunBuffer> _runs = new(StringComparer.Ordinal);
    private SuiteRunner? _runner;

    public TestExecutorModule()
        : this(new SuiteCatalog(new SuiteLoader()), new ExecutorSettingsStore(), new WindowsCredentialStore(), null, null, null)
    {
    }

    internal TestExecutorModule(
        SuiteCatalog catalog,
        ExecutorSettingsStore settings,
        IApiKeyStore keys,
        IComputerUseProvider? computer,
        IFailureExporter? exporter,
        IUiAutomationSessionFactory? automation,
        ILocatorRepairProvider? repair = null)
    {
        _catalog = catalog;
        _settings = settings;
        _exporter = exporter ?? new LinearFailureExporter(new LinearHttpIssueClient(keys));
        _repair = repair ?? new OpenAiLocatorRepairProvider(keys, () => Ambient.Value ?? Denied);
        Computer = computer ?? new OpenAiComputerUseProvider(keys, () => Ambient.Value ?? Denied);
        Automation = automation ?? UiAutomationFactory.Create();
    }

    public string Id => PluginId;

    internal IComputerUseProvider Computer { get; }

    internal IUiAutomationSessionFactory Automation { get; }

    public Task StartAsync(PluginModuleStart start, CancellationToken cancellationToken)
    {
        Require(start.GrantedPermissions, "project.read");
        Require(start.GrantedPermissions, "plugin.data.read");
        Require(start.GrantedPermissions, "plugin.data.write");
        Require(start.GrantedPermissions, "local.file.export");
        var root = string.IsNullOrWhiteSpace(start.PreviewUserRoot)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ProjectWorkbench-v2-preview")
            : start.PreviewUserRoot;
        _budget = new AiBudgetLedger(Path.Combine(Path.GetFullPath(root), "test-executor", "ai-budget"));
        _runner = BuildRunner(Path.Combine(Path.GetFullPath(root), "test-executor", "originals"));
        return Task.CompletedTask;
    }

    public async Task<PluginModuleResponse> HandleAsync(PluginModuleRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return request.Action switch
            {
                "suites.list" => List(request),
                "runs.start" => StartRun(request),
                "runs.export" => await ExportAsync(request, cancellationToken),
                _ => Error(404, "NotFound")
            };
        }
        catch (PermissionDeniedException)
        {
            return Error(403, "PermissionDenied");
        }
        catch (ExecutorDataException error)
        {
            return Error(StatusFor(error.Code), error.Code);
        }
        catch (JsonException)
        {
            return Error(400, "InvalidPluginData");
        }
    }

    public bool TryOpenRun(string runId)
    {
        lock (_gate) return _runs.ContainsKey(runId);
    }

    public IAsyncEnumerable<string> ReadEventsAsync(string runId, CancellationToken cancellationToken)
    {
        RunBuffer buffer;
        lock (_gate)
        {
            if (!_runs.TryGetValue(runId, out buffer!)) throw new ExecutorDataException(ExecutorError.RunNotFound);
        }
        return buffer.ReadAsync(cancellationToken);
    }

    private PluginModuleResponse List(PluginModuleRequest request)
    {
        Require(request.GrantedPermissions, "project.read");
        Require(request.GrantedPermissions, "plugin.data.read");
        var items = _catalog.List(request.DataDirectory);
        return JsonResponse(200, new
        {
            schema = ExecutorSchemas.SuiteList,
            schemaVersion = ExecutorSchemas.Version,
            suites = items.Select(item => new
            {
                id = item.Id,
                name = item.Name,
                cases = item.Cases,
                format = item.Format,
                loadable = item.Loadable,
                error = item.Error
            })
        });
    }

    private PluginModuleResponse StartRun(PluginModuleRequest request)
    {
        Require(request.GrantedPermissions, "project.read");
        Require(request.GrantedPermissions, "plugin.data.read");
        Require(request.GrantedPermissions, "plugin.data.write");
        Require(request.GrantedPermissions, "local.file.export");
        var body = ParseObject(request.JsonBody, ["suiteId", "localOnly", "manualComputerUse"]);
        if (!body.TryGetProperty("suiteId", out var suiteIdNode) || suiteIdNode.ValueKind != JsonValueKind.String)
            throw new JsonException();
        var suiteId = suiteIdNode.GetString();
        var requestLocalOnly = body.TryGetProperty("localOnly", out var localNode) && localNode.ValueKind == JsonValueKind.True;
        if (body.TryGetProperty("localOnly", out var present) && present.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new JsonException();
        if (body.TryGetProperty("manualComputerUse", out var manualNode) && manualNode.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new JsonException();
        var manualComputerUse = body.TryGetProperty("manualComputerUse", out var manualFlag) && manualFlag.ValueKind == JsonValueKind.True;
        var settings = ExecutorSettingsStore.Load(request.DataDirectory);
        var policy = settings.ToPolicy(requestLocalOnly, manualComputerUse);
        var suite = _catalog.Require(request.DataDirectory, suiteId ?? "");
        var runner = _runner ?? throw new ExecutorDataException(ExecutorError.InvalidRun);
        var runId = Guid.NewGuid().ToString("N");
        var buffer = new RunBuffer(runId);
        lock (_gate)
        {
            if (_runs.Values.Count(item => !item.IsCompleted) >= RunLimit) throw new ExecutorDataException(ExecutorError.RunLimit);
            _runs[runId] = buffer;
        }
        var reportDirectory = ContainedPath.Combine(ContainedPath.Combine(request.DataDirectory, "runs"), runId);
        Directory.CreateDirectory(reportDirectory);
        var suiteFile = _catalog.TryGetFile(request.DataDirectory, suite.Id);
        var baselines = Path.Combine(request.DataDirectory, "baselines");
        _ = Task.Run(async () =>
        {
            Ambient.Value = policy;
            try
            {
                await runner.RunAsync(suite, new RunRequest(runId, policy, reportDirectory, suiteFile, baselines), buffer, CancellationToken.None);
            }
            catch (Exception)
            {
                if (!buffer.IsCompleted) buffer.Complete(new RunSummary(runId, "failed", 0, 0));
            }
            finally
            {
                Ambient.Value = null;
            }
        });
        return JsonResponse(202, new
        {
            schema = ExecutorSchemas.Run,
            schemaVersion = ExecutorSchemas.Version,
            runId,
            suiteId = suite.Id,
            status = "started",
            localOnly = policy.LocalOnly,
            computerUseEnabled = policy.CanUseComputer
        });
    }

    private async Task<PluginModuleResponse> ExportAsync(PluginModuleRequest request, CancellationToken cancellationToken)
    {
        Require(request.GrantedPermissions, "project.read");
        Require(request.GrantedPermissions, "plugin.data.read");
        var body = ParseObject(request.JsonBody, ["runId", "fingerprint"]);
        if (!body.TryGetProperty("runId", out var runNode) || !body.TryGetProperty("fingerprint", out var fingerprintNode))
            throw new JsonException();
        var runId = runNode.GetString() ?? "";
        var fingerprint = fingerprintNode.GetString() ?? "";
        if (runId.Length != 32 || runId.Any(character => character is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
            throw new ExecutorDataException(ExecutorError.RunNotFound);
        if (!TryOpenRun(runId)) throw new ExecutorDataException(ExecutorError.RunNotFound);
        var directory = ContainedPath.Combine(ContainedPath.Combine(request.DataDirectory, "runs"), runId);
        var (report, png) = RunArtifactStore.ReadFailure(directory, fingerprint);
        var policy = ExecutorSettingsStore.Load(request.DataDirectory).ToPolicy(false).ForConfirmedExport();
        var result = await _exporter.ExportAsync(report, png, policy, cancellationToken);
        return JsonResponse(200, new
        {
            schema = ExecutorSchemas.FailureReport,
            schemaVersion = ExecutorSchemas.Version,
            status = result.Status,
            issueId = result.IssueId
        });
    }

    private SuiteRunner BuildRunner(string originals)
        => new(new RunnerDependencies
        {
            Automation = Automation,
            ComputerUse = Computer,
            Masker = new ScreenshotMasker(),
            Originals = new OriginalScreenshotStore(originals),
            Artifacts = new RunArtifactStore(),
            Exporter = _exporter,
            Deadlines = new DeadlineFactory(),
            Repair = _repair,
            Budget = _budget
        });

    private static JsonElement ParseObject(string? json, string[] allowed)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > 8192) throw new JsonException();
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException();
        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (!allowed.Contains(property.Name, StringComparer.Ordinal)) throw new JsonException();
        }
        return document.RootElement.Clone();
    }

    private static int StatusFor(string code) => code switch
    {
        ExecutorError.SuiteNotFound or ExecutorError.RunNotFound or ExecutorError.ReportNotFound => 404,
        ExecutorError.RunLimit => 409,
        _ => 400
    };

    private static void Require(IReadOnlySet<string> granted, string permission)
    {
        if (!granted.Contains(permission)) throw new PermissionDeniedException();
    }

    private static PluginModuleResponse JsonResponse(int status, object body)
        => new(status, "application/json; charset=utf-8", JsonSerializer.SerializeToUtf8Bytes(body, Json));

    private static PluginModuleResponse Error(int status, string code)
        => JsonResponse(status, new { error = code });

    internal sealed class PermissionDeniedException : Exception;
}
