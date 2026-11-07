using Xunit;
using Workbench.Core.Projects;
using Workbench.Sdk;
using Workbench.Testing.Domain;
namespace Workbench.Core.Tests;

// Real Core facade + real supervisor + JSON settings, composed before registration Commit.
internal sealed class TestingDomainFixture : IAsyncDisposable
{
    internal TestDirectory Disk { get; }=new();
    internal PluginRegistrationContext Context { get; }
    internal PluginBackgroundTasks Tasks { get; }
    internal TestingController Controller { get; }
    internal string SettingsPath=>Path.Combine(Disk.User,"settings","testing","settings.json");
    private TestingDomainFixture(IControlledTestAdapter? adapter,AsyncTestGate? delegatePrefix,AsyncTestGate? startPersist)
    {
        var settings=new JsonPluginSettings(Path.Combine(Disk.User,"settings","testing"),_=>{});
        Tasks=new(new Dictionary<string,SavedTaskIntent>(),async(id,intent,ct)=>
        {
            if(startPersist is not null) await startPersist.HoldAsync();
            await settings.SetAsync("intent-"+id,intent,ct);
        },_=>{},TimeSpan.FromMilliseconds(75));
        Context=new(PluginTestingContextTests.Owner,Path.Combine(Disk.User,"data","testing"),settings,Tasks,
            new PluginSecretStore(Disk.User,PluginTestingContextTests.Owner,new WindowsSecretProtector(),_=>{}),new ProjectRuntimeScope("default","Default",Disk.User));
        IPluginTestingContext supplied=delegatePrefix is null ? Context : new PrefixContext(Context,new PrefixTasks(Tasks,delegatePrefix));
        Controller=new(supplied,adapter);
    }
    internal static async Task<TestingDomainFixture> CreateAsync(IControlledTestAdapter? adapter=null,AsyncTestGate? delegatePrefix=null,AsyncTestGate? startPersist=null)
    {
        var fixture=new TestingDomainFixture(adapter,delegatePrefix,startPersist);
        try
        {
            await fixture.Context.Settings.SetAsync("fixtureVersion",1);
            await fixture.Controller.InitializeAsync();
            fixture.Context.RegisterBeforeProjectLeave(fixture.Controller.PrepareLeaveAsync); // The domain does not register this hook.
            Assert.True(await fixture.Tasks.PrepareCommitAsync(CancellationToken.None));
            fixture.Context.Commit();return fixture;
        }
        catch { await fixture.DisposeAsync();throw; }
    }
    internal async Task<TestCaseSetSnapshot> ImportAndGenerateAsync()
    {
        var source=await Controller.ImportExampleAsync(TestSourceKind.Requirements);
        return await Controller.GenerateAsync([source.Id]);
    }
    public async ValueTask DisposeAsync()
    {
        Context.Close(PluginStopReason.PluginDisabled);
        await Task.WhenAll(Context.CancelLifetimeAsync(),Tasks.StopAllAsync(),Controller.StopAsync(),Context.DrainProjectWorkAsync());
        Context.Dispose();Disk.Dispose();
    }
    private sealed class PrefixTasks(IPluginBackgroundTasks actual,AsyncTestGate gate) : IPluginBackgroundTasks
    {
        public void Register(PluginBackgroundTask definition)=>actual.Register(definition with { RunAsync=async execution=> { await gate.HoldAsync();await definition.RunAsync(execution); } });
        public IReadOnlyList<PluginBackgroundTaskSnapshot> GetSnapshots()=>actual.GetSnapshots();
        public Task<PluginTaskOperationResult> StartAsync(string id,CancellationToken ct=default)=>actual.StartAsync(id,ct);
        public Task<PluginTaskOperationResult> StopAsync(string id,CancellationToken ct=default)=>actual.StopAsync(id,ct);
        public Task<PluginTaskOperationResult> SetResumeOnStartupAsync(string id,bool enabled,CancellationToken ct=default)=>actual.SetResumeOnStartupAsync(id,enabled,ct);
    }
    private sealed class PrefixContext(IPluginTestingContext actual,IPluginBackgroundTasks tasks) : IPluginTestingContext
    {
        public string PluginId=>actual.PluginId;public string DataDirectory=>actual.DataDirectory;public CancellationToken Lifetime=>actual.Lifetime;
        public IPluginSettings Settings=>actual.Settings;public IPluginBackgroundTasks BackgroundTasks=>tasks;public IPluginSecretStore Secrets=>actual.Secrets;
        public PluginStopReason StopReason=>actual.StopReason;public IProjectContext Project=>actual.Project;public IProjectTestWorkspace Testing=>actual.Testing;
        public void RegisterBeforeProjectLeave(Func<CancellationToken,Task> hook)=>actual.RegisterBeforeProjectLeave(hook);
        public void RegisterView(IPluginView view)=>actual.RegisterView(view);public void RegisterCommand(PluginCommand command)=>actual.RegisterCommand(command);public void RegisterSetting(PluginSetting setting)=>actual.RegisterSetting(setting);
    }
}

internal sealed class AsyncTestGate
{
    internal TaskCompletionSource Entered { get; }=new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource Release { get; }=new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal async Task HoldAsync() { Entered.TrySetResult();await Release.Task; }
}

// Typed extension of the real adapter; gates its actual action/reset/disposal, never fabricates success or IO.
internal sealed class GatedVirtualAdapter(int actionNumber=1) : IControlledTestAdapter
{
    internal AsyncTestGate ActionGate { get; }=new();internal AsyncTestGate DisposeGate { get; }=new();internal AsyncTestGate ResetGate { get; }=new();
    internal bool HoldReset { get; set; }
    internal int Actions,Disposals;
    internal string? ObservedOverride { get; set; }
    private readonly VirtualTaskFormAdapter _actual=new();
    public string TargetId=>_actual.TargetId;
    public async Task<IControlledTestSession> OpenAsync(string sessionId,CancellationToken ct=default)=>new Session(this,await _actual.OpenAsync(sessionId,ct),actionNumber);
    private sealed class Session(GatedVirtualAdapter owner,IControlledTestSession actual,int actionNumber) : IControlledTestSession
    {
        public async Task ResetCaseAsync(string id,CancellationToken ct=default) { if(owner.HoldReset) await owner.ResetGate.HoldAsync();await actual.ResetCaseAsync(id,ct); }
        public async Task<TestActionResult> ExecuteAsync(TestAction action,CancellationToken ct=default)
        {
            if(Interlocked.Increment(ref owner.Actions)==actionNumber) await owner.ActionGate.HoldAsync();
            var result=await actual.ExecuteAsync(action,CancellationToken.None);
            return owner.ObservedOverride is null ? result : result with { NativePlainTextState=owner.ObservedOverride };
        }
        public async ValueTask DisposeAsync() { await owner.DisposeGate.HoldAsync();await actual.DisposeAsync();Interlocked.Increment(ref owner.Disposals); }
    }
    internal void ReleaseAll() { ActionGate.Release.TrySetResult();ResetGate.Release.TrySetResult();DisposeGate.Release.TrySetResult(); }
}
