using Workbench.Core.Projects;
using Workbench.Core.Testing;
using Workbench.Sdk;
using Workbench.Testing.Domain;
using Xunit;
using static Workbench.Core.Tests.PluginTestingContextTests;

namespace Workbench.Core.Tests;

public sealed class TestingControllerAdmissionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Initialization_awaits_real_service_barrier_before_registering_and_close_prevents_late_contribution(bool close)
    {
        using var disk=new TestDirectory();var store=new ProjectTestStore(disk.User,"default");await Seed(store);
        using var context=Registration(new ProjectRuntimeScope("default","Default",disk.User));
        var controller=new TestingController(context);using var lease=HoldLease(disk.User);
        var initializing=controller.InitializeAsync();
        Assert.Empty(context.BackgroundTasks.GetSnapshots());Assert.False(initializing.IsCompleted);
        try
        {
            Task? stopping=null;
            if(close) { context.Close(PluginStopReason.PluginDisabled);stopping=controller.StopAsync();Assert.False(stopping.IsCompleted); }
            lease.Dispose();
            if(close)
            {
                Assert.Equal("TestingContextClosed",(await Assert.ThrowsAsync<PluginTestingException>(()=>initializing)).ErrorCode);
                await stopping!;Assert.Empty(context.BackgroundTasks.GetSnapshots());
            }
            else
            {
                await initializing;Assert.Single(context.BackgroundTasks.GetSnapshots());
                context.RegisterBeforeProjectLeave(controller.PrepareLeaveAsync);context.Commit();
                Assert.True((await context.PrepareProjectLeaveAsync(CancellationToken.None)).Succeeded);
            }
        }
        finally { lease.Dispose();context.Close(PluginStopReason.PluginDisabled);await Task.WhenAll(controller.StopAsync(),context.DrainProjectWorkAsync(),context.BackgroundTasks.StopAllAsync()); }
    }

    [Fact]
    public async Task Held_real_caller_capture_is_published_before_prefix_and_terminal_stop_joins_it_without_enumerating_fresh_closed_calls()
    {
        using var workers=new CallbackWorkerHeadroom();await using var fixture=await TestingDomainFixture.CreateAsync();var set=await fixture.ImportAndGenerateAsync();
        var source=Assert.Single(await fixture.Context.Testing.ListSourcesAsync());
        using var held=new HeldList<string>([source.Id]);
        Task<TestCaseSetSnapshot>? generating=null;
        try
        {
            generating=Task.Run(()=>fixture.Controller.GenerateAsync(held));await held.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            fixture.Context.Close(PluginStopReason.PluginDisabled);var stopping=fixture.Controller.StopAsync();
            Assert.False(stopping.IsCompleted);Assert.False(generating.IsCompleted);
            held.Release.TrySetResult();
            Assert.Equal("TestingContextClosed",(await Assert.ThrowsAsync<PluginTestingException>(()=>generating)).ErrorCode);
            await stopping;Assert.Equal(set.Id,fixture.Controller.Snapshot.Draft!.Id);
        }
        finally { held.Release.TrySetResult();if(generating is not null) _=await Record.ExceptionAsync(()=>generating); }
    }

    [Fact]
    public async Task Terminal_close_joins_retained_plain_notification_callback_and_never_invokes_it_under_controller_lock()
    {
        using var workers=new CallbackWorkerHeadroom();await using var fixture=await TestingDomainFixture.CreateAsync();await fixture.ImportAndGenerateAsync();
        using var released=new ManualResetEventSlim();var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Controller.Changed+=snapshot=>
        {
            if(snapshot.Draft?.Content.Name!="Notification held" || !entered.TrySetResult()) return;
            _=fixture.Controller.Snapshot; // Reentrant plain read cannot require the publishing controller lock.
            released.Wait();
        };
        try
        {
            fixture.Controller.CaptureSetName("Notification held");await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var stop=fixture.Controller.StopAsync();Assert.False(stop.IsCompleted);
            released.Set();await stop;
        }
        finally { released.Set(); }
    }
    [Fact]
    public async Task Pending_plain_notifications_coalesce_while_a_callback_is_held_and_retain_the_final_accepted_model()
    {
        using var workers=new CallbackWorkerHeadroom();await using var fixture=await TestingDomainFixture.CreateAsync();await fixture.ImportAndGenerateAsync();
        using var released=new ManualResetEventSlim();var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var names=new System.Collections.Concurrent.ConcurrentQueue<string>();
        fixture.Controller.Changed+=snapshot=>
        {
            var name=snapshot.Draft?.Content.Name;if(name is null) return;names.Enqueue(name);
            if(name=="Held A" && entered.TrySetResult()) released.Wait();
        };
        try
        {
            fixture.Controller.CaptureSetName("Held A");await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            fixture.Controller.CaptureSetName("Intermediate B");fixture.Controller.CaptureSetName("Final C");
            var preparation=fixture.Controller.PrepareLeaveAsync();released.Set();await preparation;
            Assert.DoesNotContain("Intermediate B",names);Assert.Contains("Final C",names);Assert.Equal("Final C",fixture.Controller.Snapshot.Draft!.Content.Name);
        }
        finally { released.Set(); }
    }

    [Fact]
    public async Task Preparation_releases_private_admission_only_after_its_final_plain_notification_has_settled()
    {
        await using var fixture=await TestingDomainFixture.CreateAsync();await fixture.ImportAndGenerateAsync();
        var seenPreparing=false;var attempted=false;PluginTestingException? refusal=null;
        var preparing=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Controller.Changed+=snapshot=>
        {
            if(snapshot.IsPreparing) { seenPreparing=true;preparing.TrySetResult(); }
            else if(seenPreparing && !attempted)
            {
                attempted=true;
                try { fixture.Controller.CaptureSetName("Too early from completion notification"); }
                catch(PluginTestingException error) { refusal=error; }
            }
        };
        using var lease=HoldLease(fixture.Disk.User);
        fixture.Controller.CaptureSetName("Final input before held preparation");
        var preparation=fixture.Controller.PrepareLeaveAsync();
        try { await preparing.Task.WaitAsync(TimeSpan.FromSeconds(5)); }
        finally { lease.Dispose(); }
        await preparation;
        Assert.True(attempted);Assert.NotNull(refusal);Assert.Equal("StoreBusy",refusal.ErrorCode);
        fixture.Controller.CaptureSetName("Accepted after settled hook");await fixture.Controller.FlushAsync();
    }

}
