using Workbench.Sdk;

namespace Workbench.TestPlugin;

// SDK-only dynamically loaded fixture; never installed as a shipping plugin.
public sealed class TestingFixturePlugin : IWorkbenchPlugin
{
    private IPluginTestingContext _context = null!;
    private string _mode = "";

    public async Task InitializeAsync(IPluginContext context, CancellationToken cancellationToken)
    {
        _context = context as IPluginTestingContext ?? throw new PluginTestingException("TargetUnavailable");
        _mode = await context.Settings.GetAsync("testingMode", "normal", cancellationToken);
        await context.Settings.SetAsync("testingInitializeEntered", true);
        if (_mode == "initialize-spoof") throw new PluginTestingException("synthetic-private-provider-text");
        if (_mode != "lazy") await _context.Testing.ListSourcesAsync(cancellationToken);
        context.RegisterView(new TestingView(_context));
        _context.RegisterBeforeProjectLeave(async token =>
        {
            if (_mode == "prepare-spoof") throw new PluginTestingException("synthetic-private-provider-text");
            if (_mode == "prepare-error") throw new PluginTestingException("StoreLimitExceeded");
            await _context.Testing.PublishSourceAsync(Source("before leave"), token);
            await context.Settings.SetAsync("prepared", true);
        });
        context.RegisterCommand(new("publish", "Publish", async token =>
        {
            await _context.Testing.PublishSourceAsync(Source("command"), token);
            return new(true, "published");
        }));
        context.RegisterCommand(new("testing-error", "Testing error", _ => throw new PluginTestingException("TestStoreCorrupt")));
        context.RegisterCommand(new("testing-spoof", "Testing spoof", _ => throw new PluginTestingException("synthetic-private-provider-text")));
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_context is null) return;
        await _context.Settings.SetAsync("stopCalls", await _context.Settings.GetAsync("stopCalls", 0) + 1);
        try { await _context.Testing.PublishSourceAsync(Source("terminal write")); }
        catch (PluginTestingException error) { await _context.Settings.SetAsync("stopTestingCode", error.ErrorCode); }
    }

    private static TestSourceDraft Source(string title) => new(TestSourceKind.Requirements, "fixture", "1", "doc", title,
        [123, 125], [new("fixture/item", "Item", "", ["Criterion"], null)]);

    public sealed class TestingView(IPluginTestingContext context) : IPluginView
    {
        public string Id => "testing";
        public string Title => "Testing fixture";
        public IProjectTestWorkspace Testing => context.Testing;
        public IProjectContext Project => context.Project;
    }
}
