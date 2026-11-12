using Workbench.TestExecutor;

namespace Workbench.TestExecutor.Tests;

internal sealed class ManualClock : TimeProvider
{
    public DateTimeOffset Now { get; set; } = DateTimeOffset.Parse("2026-11-11T00:00:00Z");

    public override DateTimeOffset GetUtcNow() => Now;
}

internal sealed class RecordingDeadline : IDeadlineFactory
{
    public List<TimeSpan> Timeouts { get; } = [];

    public bool CancelImmediately { get; set; }

    public CancellationTokenSource Start(TimeSpan timeout, CancellationToken outer)
    {
        Timeouts.Add(timeout);
        var source = CancellationTokenSource.CreateLinkedTokenSource(outer);
        if (CancelImmediately) source.Cancel();
        return source;
    }
}

internal sealed class ScriptedSession : IUiAutomationSession
{
    public Queue<UiActionResult> Results { get; } = new();

    public List<UiAction> Actions { get; } = [];

    public RawFrame Frame { get; set; } = SyntheticFrames.PasswordField();

    public Task<UiActionResult> ExecuteAsync(UiAction action, CancellationToken cancellationToken)
    {
        Actions.Add(action);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Results.Count == 0 ? UiActionResult.Ok(action.Expected ?? "ok") : Results.Dequeue());
    }

    public Task<RawFrame> CaptureAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Frame);
    }

    public PixelRect? Locate(ElementSelector selector) => new PixelRect(8, 8, 16, 8);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal sealed class ScriptedFactory : IUiAutomationSessionFactory
{
    public ScriptedSession Session { get; } = new();

    public Task<IUiAutomationSession> OpenAsync(ApplicationTarget? application, CancellationToken cancellationToken)
        => Task.FromResult<IUiAutomationSession>(Session);
}

internal sealed class RecordingExporter : IFailureExporter
{
    public List<(FailureReport Report, byte[]? Png)> Calls { get; } = [];

    public Task<ExportResult> ExportAsync(FailureReport report, byte[]? maskedPng, ExecutionPolicy policy, CancellationToken cancellationToken)
    {
        Calls.Add((report, maskedPng));
        return Task.FromResult(new ExportResult("created", "SYN-1"));
    }
}

internal sealed class StubHandler : HttpMessageHandler
{
    public int Calls { get; private set; }

    public List<string> Bodies { get; } = [];

    public List<byte[]> ByteBodies { get; } = [];

    public List<string> Authorizations { get; } = [];

    public List<Uri?> Uris { get; } = [];

    public Func<HttpRequestMessage, string, HttpResponseMessage>? Responder { get; set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Calls++;
        Uris.Add(request.RequestUri);
        if (request.Headers.Authorization is { } header) Authorizations.Add(header.ToString());
        else if (request.Headers.TryGetValues("Authorization", out var values)) Authorizations.Add(string.Join(' ', values));
        else Authorizations.Add("");
        var bytes = request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(cancellationToken);
        ByteBodies.Add(bytes);
        var text = System.Text.Encoding.UTF8.GetString(bytes);
        Bodies.Add(text);
        if (Responder is null) throw new InvalidOperationException("unexpected outbound call");
        return Responder(request, text);
    }
}

internal sealed class ListSink : IRunSink
{
    public List<StepStatus> Steps { get; } = [];

    public RunSummary? Summary { get; private set; }

    public void Publish(StepStatus status) => Steps.Add(status);

    public void Complete(RunSummary summary) => Summary = summary;
}

internal static class Fixtures
{
    public static ExecutionPolicy Policy(bool localOnly = false, bool computer = false, ExportMode export = ExportMode.Ask, string? team = null, bool manual = false, bool repair = false)
        => new(localOnly, computer, export, team, "project-1", "computer-use-preview", manual, repair);

    public static TestStep FlaUi(string id, string action, int timeout = 15, string? input = null, string? expected = null, bool sensitive = false, bool allowFallback = true)
        => new(id, StepKind.FlaUi, action, new ElementSelector("确定", "sample-name"), expected, input, timeout, allowFallback, sensitive);

    public static TestStep Ai(string id, int timeout = 60, string? input = null, bool sensitive = false)
        => new(id, StepKind.Ai, "read", null, "布局完整", input, timeout, true, sensitive);

    public static TestSuite Suite(bool stop = false, params TestCase[] cases)
        => new("suite-1", "合成套件", stop, true, new ApplicationTarget("TestExecutor.SampleApp", "合成被测窗口"),
            [new RectangleMask(0, 0, 2, 2), new SelectorMask(new ElementSelector(null, "sample-secret"))], cases);

    public static TestCase Case(string id, bool allowFallback, params TestStep[] steps)
        => new(id, "用例", allowFallback, steps);

    public static SuiteRunner Runner(ScriptedFactory automation, IComputerUseProvider computer, RecordingExporter exporter, RecordingDeadline deadlines, string root, string reports, ILocatorRepairProvider? repair = null)
    {
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(reports);
        return new SuiteRunner(new RunnerDependencies
        {
            Automation = automation,
            ComputerUse = computer,
            Masker = new ScreenshotMasker(),
            Originals = new OriginalScreenshotStore(Path.Combine(root, "originals")),
            Artifacts = new RunArtifactStore(),
            Exporter = exporter,
            Deadlines = deadlines,
            Repair = repair ?? new DisabledLocatorRepair()
        });
    }

    public static string Temp() 
    {
        var path = Path.Combine(Path.GetTempPath(), "executor-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
