using Microsoft.UI.Xaml;
using Workbench.Sdk;
using Workbench.Testing.Domain;
using Workbench.WinUI.Sdk;

namespace Workbench.Testing;

public sealed class TestingPlugin : IWorkbenchPlugin
{
    private readonly NativeActionOwner _owner = new();
    private readonly object _pagesSync = new();
    private readonly HashSet<TestingPage> _pages = [];
    private readonly SemaphoreSlim _preparation = new(1, 1);
    private readonly NativeRemoteOperation _remote = new();
    internal NativeSourceDraft SourceDraft { get; } = new();
    internal bool SourceBusy => _remote.IsBusy;
    internal bool SourceSecretsSupported => _secrets.IsSupported;
    private TestingController _controller = null!;
    private IPluginTestingContext _context = null!;
    private IPluginSecretStore _secrets = null!;
    private CancellationTokenRegistration _lifetimeRegistration;
    internal TestingController Controller => _controller;
    internal NativeActionOwner Owner => _owner;
    internal CancellationToken Lifetime => _context.Lifetime;
    private string? _ownedRunId;
    internal bool CanStart(TestingControllerSnapshot snapshot) => NativeRunAdmission.CanStart(snapshot, Volatile.Read(ref _ownedRunId));
    internal async Task RunAsync(string[] ids)
    { var run = await _controller.RunAsync(ids); await RecordOwnedAsync(run); }
    internal async Task RetryAsync(string parentId, CancellationToken token = default)
    { var run = await _controller.RetryAsync(parentId, token); await RecordOwnedAsync(run); }
    private async Task RecordOwnedAsync(TestRunSnapshot run)
    {
        Volatile.Write(ref _ownedRunId, run.Plan.Id);
        foreach (var page in Pages()) await page.NotifyAsync().ConfigureAwait(false);
    }

    public async Task InitializeAsync(IPluginContext context, CancellationToken cancellationToken)
    {
        _context = context as IPluginTestingContext ?? throw new PluginTestingException("TestingContextClosed");
        _secrets = _context.Secrets;
        _secrets.Register(new("figma-token", "Figma 访问令牌", "仅在原生页面明确读取时使用；需 file_content:read 和目标文件访问权限。"));
        _secrets.Register(new("openai-api-key", "OpenAI API 密钥", "仅在原生页面明确发送所选来源时使用；可能产生费用。"));
        _lifetimeRegistration = _context.Lifetime.Register(() => { _owner.Terminal(); _remote.Terminal(); });
        _controller = new(_context);
        await _controller.InitializeAsync(cancellationToken).ConfigureAwait(false);
        _context.RegisterBeforeProjectLeave(PrepareAsync);
        context.RegisterView(new WinUiView("testing", "测试设计", CreatePage));
        Register("import-example-requirements", "导入打包的需求示例（含故意失败）", token => ImportBundledAsync(TestSourceKind.Requirements, token));
        Register("import-example-figma", "导入打包的 Figma 本地示例", token => ImportBundledAsync(TestSourceKind.Figma, token));
        Register("generate-all", "从所有本地来源生成用例", async token => { await _controller.GenerateAsync(_controller.Snapshot.Sources.Select(s => s.Id).ToArray(), token); });
        Register("export-local-csv", "导出当前用例 CSV 到插件本地目录", async token =>
        { await NativeTestingFiles.WriteAsync(Path.Combine(context.DataDirectory, "testing-cases.csv"), await _controller.ExportCsvAsync(token), token); });
        Register("queue-mapped", "排队当前用例集的全部映射用例", async token => { await RecordOwnedAsync(await _controller.QueueRunAsync(MappedIds(), token)); });
        Register("start-queued", "启动已排队的虚拟执行", async token => { await _controller.StartQueuedRunAsync(token); });
        Register("cancel-run", "取消并等待虚拟执行实际结束", async token => { await _controller.CancelAsync(token); });
        Register("retry-selected", "从第一步重试选中的历史执行", async token =>
        { await RetryAsync(_controller.Snapshot.SelectedRun?.Plan.Id ?? throw new PluginTestingException("InvalidRun"), token); });
        Register("refresh", "刷新已提交的测试记录", async token =>
        {
            await _controller.RefreshAsync(token);
            foreach (var page in Pages()) await page.NotifyAsync(retryEvidence: true).ConfigureAwait(false);
        });
    }
    private string[] MappedIds() => (_controller.Snapshot.Draft?.Content.Cases ?? []).Where(c => c.Steps.All(s => s.Action is not null)).Select(c => c.Id).ToArray();
    private void Register(string id, string title, Func<CancellationToken, Task> action) => _context.RegisterCommand(new(id, title,
        token => _owner.UserAsync(async () => { await action(token).ConfigureAwait(false); return new PluginCommandResult(true, "本地操作已完成；执行状态以已提交日志为准。SIMULATED。"); })));

    internal async Task ImportBundledAsync(TestSourceKind kind, CancellationToken token)
    {
        var name = kind == TestSourceKind.Requirements ? "requirements.synthetic.json" : "figma.synthetic.json";
        var path = Path.Combine(Path.GetDirectoryName(typeof(TestingPlugin).Assembly.Location)!, "Fixtures", name);
        await ImportFileAsync(kind, path, token).ConfigureAwait(false);
    }
    internal async Task ImportFileAsync(TestSourceKind kind, string path, CancellationToken token)
    {
        var bytes = await NativeTestingFiles.ReadAsync(path, token).ConfigureAwait(false);
        await _controller.ImportAsync(kind, bytes, token).ConfigureAwait(false);
    }
    internal Task ImportRemoteFigmaAsync(NativeFigmaInput input) => RemoteAsync("正在读取 Figma 并导入", async token =>
    {
        var read = NativeSourceCredentials.ForAction(_secrets, "figma-token");
        SourceDraft.CompleteFigma(await _controller.ImportFigmaAsync(input.Request, input.Criteria, read, token).ConfigureAwait(false));
    });
    internal Task GenerateRemoteAiAsync(IReadOnlyList<string> selected) => RemoteAsync("正在发送所选来源到 OpenAI", async token =>
    {
        var read = NativeSourceCredentials.ForAction(_secrets, "openai-api-key");
        SourceDraft.CompleteAi(await _controller.GenerateWithAiAsync(selected, read, token).ConfigureAwait(false));
    });
    private async Task RemoteAsync(string label, Func<CancellationToken, Task> action)
    {
        var admitted = false;
        try
        {
            await _remote.RunAsync(async token =>
            {
                admitted = true;
                SourceDraft.Begin(label);
                await NotifyPagesAsync().ConfigureAwait(false);
                try { await action(token).ConfigureAwait(false); }
                catch (Exception error) { SourceDraft.Fail(error); }
                return true;
            }, async failure =>
            {
                if (failure is not null) SourceDraft.Fail(failure);
                await NotifyPagesAsync().ConfigureAwait(false);
            }).ConfigureAwait(false);
        }
        catch (Exception) when (admitted) { /* The retained settlement records safe failure before busy is released. */ }
        finally { await NotifyPagesAsync().ConfigureAwait(false); }
    }
    internal async Task CancelSourceAsync()
    {
        try { await _remote.RequestCancellationAsync().ConfigureAwait(false); }
        catch (Exception) { /* Remote settlement owns the failure message; a late cancel continuation must not overwrite a later action. */ }
        await NotifyPagesAsync().ConfigureAwait(false);
    }
    private async Task NotifyPagesAsync()
    { foreach (var page in Pages()) await page.NotifyAsync().ConfigureAwait(false); }
    private FrameworkElement CreatePage()
    {
        _context.Lifetime.ThrowIfCancellationRequested();
        return new TestingPage(this);
    }
    internal void Attach(TestingPage page) { lock (_pagesSync) _pages.Add(page); }
    internal void Detach(TestingPage page) { lock (_pagesSync) _pages.Remove(page); }
    private TestingPage[] Pages() { lock (_pagesSync) return _pages.ToArray(); }
    private async Task PrepareAsync(CancellationToken token)
    {
        await _preparation.WaitAsync(token).ConfigureAwait(false);
        using var seal = _owner.Seal();
        using var remoteSeal = _remote.Seal();
        try
        {
            Exception? cancellationFailure = null;
            try { await _remote.RequestCancellationAsync().ConfigureAwait(false); }
            catch (Exception error) { SourceDraft.Fail(error); cancellationFailure = error; }
            await _owner.DrainAsync().ConfigureAwait(false);
            await _controller.PrepareLeaveAsync(token).ConfigureAwait(false);
            // Changed is a synchronous bridge; its real dispatcher continuations are independently retained here.
            await _owner.DrainAsync().ConfigureAwait(false);
            if (cancellationFailure is not null) throw new PluginTestingException("TestingFailed");
        }
        finally
        {
            try
            {
                foreach (var page in Pages()) await page.NotifyAsync(reopening: true).ConfigureAwait(false);
                await _owner.DrainAsync().ConfigureAwait(false);
            }
            finally { remoteSeal.Dispose(); seal.Dispose(); _preparation.Release(); }
        }
    }
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _owner.Terminal();
        _remote.Terminal();
        Exception? cancellationFailure = null;
        try { await _remote.RequestCancellationAsync().ConfigureAwait(false); }
        catch (Exception error) { SourceDraft.Fail(error); cancellationFailure = error; }
        await _owner.DrainAsync().ConfigureAwait(false);
        try { if (_controller is not null) await _controller.StopAsync(cancellationToken).ConfigureAwait(false); }
        finally
        {
            foreach (var page in Pages()) await page.RetireAsync().ConfigureAwait(false);
            await _owner.DrainAsync().ConfigureAwait(false);
            _lifetimeRegistration.Dispose();
        }
        if (cancellationFailure is not null) throw new PluginTestingException("TestingFailed");
    }
}
