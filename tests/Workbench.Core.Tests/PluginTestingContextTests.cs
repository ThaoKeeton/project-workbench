using System.Collections;
using System.Buffers.Binary;
using Workbench.Core.Projects;
using Workbench.Core.Testing;
using Workbench.Sdk;
using Xunit;
using Xunit.Abstractions;
using static Workbench.Core.Tests.ProjectTestStoreTests;

namespace Workbench.Core.Tests;

public sealed class PluginTestingContextTests(ITestOutputHelper output)
{
    internal const string Owner = "com.projectworkbench.testing";
    internal const string Other = "com.projectworkbench.executor";
    private const string Id = "11111111111111111111111111111111";

    [Fact]
    public async Task Registration_exposes_callable_testing_before_commit_and_missing_reads_create_nothing()
    {
        using var disk = new TestDirectory();
        using var context = Registration(new("default", "Default", disk.User));
        var testing = Workspace(context);
        Assert.False(Directory.Exists(disk.User));
        Assert.Empty(await testing.ListSourcesAsync());
        Assert.Empty(await testing.ListCaseSetsAsync());
        Assert.Empty(await testing.ListRunsAsync());
        Assert.False(Directory.Exists(disk.User));
        context.Close(PluginStopReason.PluginDisabled);
        await context.DrainProjectWorkAsync();
    }

    [Fact]
    public async Task Closed_unused_context_never_starts_initialization_or_touches_caller_collections()
    {
        using var disk = new TestDirectory();
        using var context = Registration(new("default", "Default", disk.User));
        var testing = Workspace(context);
        context.Close(PluginStopReason.PluginDisabled);
        await Code("TestingContextClosed", () => testing.PublishSourceAsync(Source(items: new ThrowingList<TestSourceItem>())));
        await Code("TestingContextClosed", () => testing.CreateRunAsync(Id, new string('0', 64), new ThrowingList<string>()));
        await context.DrainProjectWorkAsync();
        Assert.False(context.HasPendingProjectWork);
        Assert.False(Directory.Exists(disk.User));
    }

    [Theory]
    [InlineData("source")]
    [InlineData("cases")]
    [InlineData("update")]
    [InlineData("selection")]
    [InlineData("event")]
    [InlineData("evidence")]
    public async Task Mutable_inputs_are_frozen_before_return_while_real_initialization_is_filesystem_blocked(string kind)
    {
        using var disk = new TestDirectory();
        var store = new ProjectTestStore(disk.User, "default");
        var set = await Seed(store);
        var source = await store.ReadSourceAsync(Assert.Single(await store.ListSourcesAsync()).Id);
        using var context = Registration(new("default", "Default", disk.User));
        var testing = Workspace(context);
        using var lease = HoldLease(disk.User);
        Task actual;
        Func<Task> verify;
        switch (kind)
        {
            case "source":
            {
                var bytes = new byte[] { 123, 125 };
                string[] criteria = ["Criterion"];
                TestSourceItem[] items = [new("../../x", "Item", "", criteria, null)];
                var call = testing.PublishSourceAsync(Source(bytes, items));
                actual = call;
                bytes[0] = 0; criteria[0] = ""; items[0] = items[0] with { Title = "" };
                verify = async () =>
                {
                    var saved = await call;
                    Assert.Equal(new byte[] { 123, 125 }, saved.OriginalBytes);
                    Assert.Equal("Criterion", saved.Items[0].AcceptanceCriteria[0]);
                    Assert.Equal("Item", saved.Items[0].Title);
                };
                break;
            }
            case "cases":
            {
                var draft = Draft(source);
                var cases = draft.Cases.ToArray();
                var steps = cases[0].Steps.ToArray();
                cases[0] = cases[0] with { Steps = steps };
                var call = testing.CreateCaseSetAsync(draft with { Cases = cases });
                actual = call;
                cases[0] = cases[0] with { Title = "" }; steps[0] = steps[0] with { Instruction = "" };
                verify = async () => { var saved = await call; Assert.Equal("Case", saved.Cases[0].Title); Assert.Equal("Set title", saved.Cases[0].Steps[0].Instruction); };
                break;
            }
            case "update":
            {
                var cases = set.Cases.ToArray();
                var call = testing.UpdateCaseSetAsync(set.Id, new("Updated", cases), set.Revision);
                actual = call;
                cases[0] = cases[0] with { Title = "" };
                verify = async () => Assert.Equal("Case", (await call).Cases[0].Title);
                break;
            }
            case "selection":
            {
                var selected = new[] { set.Cases[0].Id };
                var call = testing.CreateRunAsync(set.Id, set.Revision, selected);
                actual = call;
                selected[0] = "bad";
                verify = async () => Assert.Equal(set.Cases[0].Id, (await call).Plan.Cases[0].Id);
                break;
            }
            case "event":
            {
                string[] ids = [Id];
                var call = testing.AppendRunEventAsync(Id, 0, new string('0', 64),
                    new(TestRunEventKind.StepFinished, Id, Id, TestStepOutcome.Passed, EvidenceIds: ids) { NativePlainTextState = " \t\r\n" });
                actual = call;
                ids[0] = "bad";
                verify = () => Code("TestRecordNotFound", () => call);
                break;
            }
            default:
            {
                var bytes = Frame();
                var call = testing.AddRunEvidenceAsync(Id, new("image/bmp", bytes, true));
                actual = call;
                bytes[0] = 0;
                verify = () => Code("TestRecordNotFound", () => call);
                break;
            }
        }
        try
        {
            Assert.False(actual.IsCompleted);
            Assert.True(context.HasPendingProjectWork);
            context.Close(PluginStopReason.PluginDisabled);
            var drain = context.DrainProjectWorkAsync();
            Assert.False(drain.IsCompleted);
            lease.Dispose();
            await verify();
            await drain.WaitAsync(TimeSpan.FromSeconds(5));
            output.WriteLine($"{kind}: mutable caller input changed after API returned, before real initialization lease released; original capture retained.");
        }
        finally { lease.Dispose(); context.Close(PluginStopReason.PluginDisabled); await context.DrainProjectWorkAsync(); }
    }

    [Fact]
    public async Task Concurrent_first_calls_join_same_owner_initialization_despite_first_caller_cancellation()
    {
        using var disk = new TestDirectory();
        var store = new ProjectTestStore(disk.User, "default");
        var set = await Seed(store);
        var old = await store.CreateRunAsync(Owner, set.Id, set.Revision, [set.Cases[0].Id]);
        var foreign = await store.CreateRunAsync(Other, set.Id, set.Revision, [set.Cases[0].Id]);
        using var context = Registration(new("default", "Default", disk.User));
        var testing = Workspace(context);
        using var cancel = new CancellationTokenSource();
        using var held = HoldLease(disk.User);
        var first = testing.ListRunsAsync(cancel.Token);
        var second = testing.ReadRunAsync(old.Plan.Id);
        cancel.Cancel();
        try
        {
            Assert.True(context.HasPendingProjectWork);
            Assert.False(first.IsCompleted);
            Assert.False(second.IsCompleted);
            held.Dispose();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
            var recovered = await second;
            Assert.Equal(TestRunState.Interrupted, recovered.State);
            Assert.Equal(1, recovered.Sequence);
            Assert.Equal(TestRunState.Queued, (await testing.ReadRunAsync(foreign.Plan.Id)).State);
            var current = await testing.CreateRunAsync(set.Id, set.Revision, [set.Cases[0].Id]);
            await testing.ListSourcesAsync();
            await testing.ListRunsAsync();
            Assert.Equal(TestRunState.Queued, (await testing.ReadRunAsync(current.Plan.Id)).State);
            Assert.Single((await testing.ReadRunAsync(old.Plan.Id)).Events, e => e.Kind == TestRunEventKind.Interrupted);
            output.WriteLine("Real testing.lock gate: first caller canceled; same shared init recovered own old run once; foreign/current runs remained Queued.");
        }
        finally { held.Dispose(); context.Close(PluginStopReason.PluginDisabled); await context.DrainProjectWorkAsync(); }
    }

    [Theory]
    [InlineData("corrupt")]
    [InlineData("future")]
    [InlineData("missing-tail")]
    public async Task Initialization_failure_is_memoized_and_only_a_new_corrected_context_can_retry(string damage)
    {
        using var disk = new TestDirectory();
        var scope = new ProjectRuntimeScope("default", "Default", disk.User);
        var store = new ProjectTestStore(disk.User, "default");
        var set = await Seed(store);
        var run = await store.CreateRunAsync(Owner, set.Id, set.Revision, [set.Cases[0].Id]);
        run = await store.AppendRunEventAsync(Owner, run.Plan.Id, run.Sequence, run.HeadHash, new(TestRunEventKind.RunStarted));
        var path = damage == "missing-tail" ? Path.Combine(StoreRoot(disk.User), "runs", run.Plan.Id, "events", "0000000001.json")
            : Path.Combine(StoreRoot(disk.User), "store.json");
        var original = await File.ReadAllBytesAsync(path);
        if (damage == "missing-tail") File.Delete(path);
        else await File.WriteAllTextAsync(path, damage == "future" ? "{\"schemaVersion\":2,\"projectId\":\"default\"}" : "synthetic-private-provider-text");
        using var context = Registration(scope);
        var testing = Workspace(context);
        var expected = damage == "missing-tail" ? "RecoveryRequired" : "TestStoreCorrupt";
        await Code(expected, () => testing.ListSourcesAsync());
        await File.WriteAllBytesAsync(path, original);
        await Code(expected, () => testing.ListRunsAsync());
        Assert.Equal(TestRunState.Running, (await store.ReadRunAsync(run.Plan.Id)).State);
        context.Close(PluginStopReason.PluginDisabled);
        await context.DrainProjectWorkAsync();
        using var fresh = Registration(scope);
        Assert.Equal(TestRunState.Interrupted, (await Workspace(fresh).ReadRunAsync(run.Plan.Id)).State);
        await Code("TestingContextClosed", () => testing.ListRunsAsync());
        fresh.Close(PluginStopReason.PluginDisabled);
        await fresh.DrainProjectWorkAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Close_retains_actual_synchronous_capture_until_release_even_after_initialization_failure(bool failInit)
    {
        using var workers = new CallbackWorkerHeadroom();
        using var disk = new TestDirectory();
        if (failInit)
        {
            Directory.CreateDirectory(StoreRoot(disk.User));
            await File.WriteAllTextAsync(Path.Combine(StoreRoot(disk.User), "store.json"), "bad synthetic data");
        }
        using var context = Registration(new("default", "Default", disk.User));
        var testing = Workspace(context);
        using var prefix = new HeldList<TestSourceItem>([new("x", "Item", "", ["Criterion"], null)]);
        Task<TestSourceSnapshot>? call = null;
        try
        {
            call = Task.Run(() => testing.PublishSourceAsync(Source(items: prefix)));
            await prefix.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (failInit) await Code("TestStoreCorrupt", () => testing.ListRunsAsync());
            else Assert.Empty(await testing.ListRunsAsync());
            Assert.True(context.HasPendingProjectWork);
            context.Close(PluginStopReason.PluginDisabled);
            var drain = context.DrainProjectWorkAsync();
            Assert.False(drain.IsCompleted);
            Assert.False(call.IsCompleted);
            await Code("TestingContextClosed", () => testing.PublishSourceAsync(Source(items: new ThrowingList<TestSourceItem>())));
            prefix.Release.TrySetResult();
            if (failInit) await Code("TestStoreCorrupt", () => call);
            else Assert.Equal(Owner, (await call).ProducerPluginId);
            await drain.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(context.HasPendingProjectWork);
            output.WriteLine($"Actual caller Count prefix joined after close; initFailed={failInit}; no caller getter under lock or on closed fresh call.");
        }
        finally
        {
            prefix.Release.TrySetResult();
            if (call is not null) _ = await Record.ExceptionAsync(() => call);
            context.Close(PluginStopReason.PluginDisabled);
            await context.DrainProjectWorkAsync();
        }
    }

    [Fact]
    public async Task Capture_failure_still_joins_shared_initialization_and_counterfeit_codes_are_sanitized()
    {
        using var disk = new TestDirectory();
        await Seed(new(disk.User, "default"));
        using var context = Registration(new("default", "Default", disk.User));
        var testing = Workspace(context);
        using var held = HoldLease(disk.User);
        var failed = testing.PublishSourceAsync(Source(items: new ThrowingList<TestSourceItem>()));
        context.Close(PluginStopReason.PluginDisabled);
        var drain = context.DrainProjectWorkAsync();
        try
        {
            Assert.False(failed.IsCompleted);
            Assert.False(drain.IsCompleted);
            held.Dispose();
            await Code("TestingFailed", () => failed);
            await drain.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally { held.Dispose(); await context.DrainProjectWorkAsync(); }
    }

    [Fact]
    public async Task Every_workspace_method_uses_captured_root_owner_and_defensive_copies_before_return()
    {
        using var disk = new TestDirectory();
        var scope = new ProjectRuntimeScope("default", "Original", disk.User);
        using var context = Registration(scope);
        using var foreignContext = Registration(scope, Other);
        using var secondContext = Registration(new("22222222222222222222222222222222", "Second", Path.Combine(disk.Root, "second")));
        var testing = Workspace(context);
        var foreign = Workspace(foreignContext);
        var second = Workspace(secondContext);
        await testing.ListSourcesAsync();
        await foreign.ListRunsAsync();
        var bytes = new byte[] { 123, 125 };
        string[] criteria = ["Criterion"];
        TestSourceItem[] items = [new("../../x", "Item", "", criteria, null)];
        var sourceCall = testing.PublishSourceAsync(Source(bytes, items));
        bytes[0] = 0;
        criteria[0] = "changed";
        items[0] = items[0] with { Title = "changed" };
        var source = await sourceCall;
        Assert.Equal(new byte[] { 123, 125 }, source.OriginalBytes);
        Assert.Equal("Criterion", source.Items[0].AcceptanceCriteria[0]);
        Assert.Equal("Item", source.Items[0].Title);
        Assert.Equal(Owner, source.ProducerPluginId);
        Assert.Equal("default", source.ProjectId);
        var stored = await testing.ReadSourceAsync(source.Id);
        stored.OriginalBytes[0] = 0;
        Assert.Equal((byte)123, (await testing.ReadSourceAsync(source.Id)).OriginalBytes[0]);
        var draft = Draft(source);
        var cases = draft.Cases.ToArray();
        var creating = testing.CreateCaseSetAsync(draft with { Cases = cases });
        cases[0] = cases[0] with { Title = "changed" };
        var set = await creating;
        Assert.Equal("Case", set.Cases[0].Title);
        var content = set.Cases.ToArray();
        var updating = testing.UpdateCaseSetAsync(set.Id, new("Renamed set", content), set.Revision);
        content[0] = content[0] with { Title = "changed" };
        set = await updating;
        Assert.Equal("Case", set.Cases[0].Title);
        Assert.Single(await testing.ListSourcesAsync());
        Assert.Single(await testing.ListCaseSetsAsync());
        Assert.Equal(set.Revision, (await testing.ReadCaseSetAsync(set.Id)).Revision);
        await Code("InvalidProducer", () => foreign.UpdateCaseSetAsync(set.Id, new("foreign", set.Cases), set.Revision));
        var selected = new[] { set.Cases[0].Id };
        var creatingRun = testing.CreateRunAsync(set.Id, set.Revision, selected);
        selected[0] = Id;
        var run = await creatingRun;
        Assert.Equal(set.Cases[0].Id, run.Plan.Cases[0].Id);
        Assert.Equal(set.Revision, (await testing.ReadCaseRevisionAsync(set.Id, set.Revision)).Revision);
        scope.Rename("Renamed root");
        Assert.Equal("default", run.Plan.ProjectId);
        run = await testing.AppendRunEventAsync(run.Plan.Id, run.Sequence, run.HeadHash, new(TestRunEventKind.RunStarted));
        var step = run.Plan.Cases[0].Steps[0];
        run = await testing.AppendRunEventAsync(run.Plan.Id, run.Sequence, run.HeadHash, new(TestRunEventKind.StepStarted, run.Plan.Cases[0].Id, step.Id));
        var bmp = Frame();
        var evidenceCall = testing.AddRunEvidenceAsync(run.Plan.Id, new("image/bmp", bmp, true));
        bmp[54] = 255;
        var receipt = await evidenceCall;
        Assert.Equal((byte)0, (await testing.ReadRunEvidenceAsync(run.Plan.Id, receipt.Id)).Bytes[54]);
        var evidence = await testing.ReadRunEvidenceAsync(run.Plan.Id, receipt.Id);
        evidence.Bytes[54] = 123;
        Assert.Equal((byte)0, (await testing.ReadRunEvidenceAsync(run.Plan.Id, receipt.Id)).Bytes[54]);
        string[] evidenceIds = [receipt.Id];
        var finishing = testing.AppendRunEventAsync(run.Plan.Id, run.Sequence, run.HeadHash,
            new(TestRunEventKind.StepFinished, run.Plan.Cases[0].Id, step.Id, TestStepOutcome.Passed, EvidenceIds: evidenceIds) { NativePlainTextState = " \t\r\n" });
        evidenceIds[0] = Id;
        run = await finishing;
        Assert.Equal(receipt.Id, Assert.Single(run.Events[^1].EvidenceIds));
        Assert.Equal(" \t\r\n", run.Events[^1].NativePlainTextState);
        await Code("InvalidProducer", () => foreign.AppendRunEventAsync(run.Plan.Id, run.Sequence, run.HeadHash, new(TestRunEventKind.Completed)));
        await Code("InvalidProducer", () => foreign.AddRunEvidenceAsync(run.Plan.Id, new("image/bmp", Frame(), true)));
        run = await testing.AppendRunEventAsync(run.Plan.Id, run.Sequence, run.HeadHash, new(TestRunEventKind.Failed));
        await Code("InvalidProducer", () => foreign.CreateRetryRunAsync(run.Plan.Id));
        var retry = await testing.CreateRetryRunAsync(run.Plan.Id);
        Assert.Equal(run.Plan.Id, retry.Plan.ParentRunId);
        Assert.Equal(run.Plan.CaseRevision, retry.Plan.CaseRevision);
        var foreignRun = await foreign.CreateRunAsync(set.Id, set.Revision, [set.Cases[0].Id]);
        Assert.Equal(Other, foreignRun.Plan.ProducerPluginId);
        Assert.Equal(Owner, (await foreign.ReadCaseSetAsync(set.Id)).ProducerPluginId);
        Assert.Equal(run.HeadHash, (await testing.ReadRunAsync(run.Plan.Id)).HeadHash);
        Assert.Equal(3, (await testing.ListRunsAsync()).Count);
        Assert.Empty(await second.ListSourcesAsync());
        await Code("TestRecordNotFound", () => second.ReadSourceAsync(source.Id));
        var secondSource = await second.PublishSourceAsync(Source());
        Assert.Equal("22222222222222222222222222222222", secondSource.ProjectId);
        Assert.Single(await testing.ListSourcesAsync());
        foreach (var current in new[] { context, foreignContext, secondContext }) { current.Close(PluginStopReason.PluginDisabled); await current.DrainProjectWorkAsync(); }
    }

    internal static PluginRegistrationContext Registration(ProjectRuntimeScope scope, string producer = Owner)
        => new(producer, Path.Combine(scope.UserRoot, "data", producer), new JsonPluginSettings(Path.Combine(scope.UserRoot, "settings", producer), _ => { }),
            new PluginBackgroundTasks(new Dictionary<string, SavedTaskIntent>(), (_, _, _) => Task.CompletedTask, _ => { }, TimeSpan.FromMilliseconds(75)),
            new PluginSecretStore(scope.UserRoot, producer, new WindowsSecretProtector(), _ => { }), scope);

    internal static IProjectTestWorkspace Workspace(PluginRegistrationContext context) => Assert.IsAssignableFrom<IPluginTestingContext>(context).Testing;
    internal static async Task<TestCaseSetSnapshot> Seed(ProjectTestStore store)
        => await store.CreateCaseSetAsync(Owner, Draft(await store.PublishSourceAsync(Owner, Source())));
    internal static FileStream HoldLease(string root) => new(Path.Combine(root, ".workbench-project", "testing.lock"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    internal static byte[] Frame()
    {
        var bytes = new byte[172854];
        bytes[0] = (byte)'B'; bytes[1] = (byte)'M';
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(2), 172854);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(10), 54);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(14), 40);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(18), 320);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(22), 180);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(26), 1);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(28), 24);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(34), 172800);
        return bytes;
    }

    internal sealed class HeldList<T>(T[] values) : IReadOnlyList<T>, IDisposable
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Count { get { Entered.TrySetResult(); Release.Task.GetAwaiter().GetResult(); return values.Length; } }
        public T this[int index] => values[index];
        public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)values).GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        public void Dispose() => Release.TrySetResult();
    }

    private sealed class ThrowingList<T> : IReadOnlyList<T>
    {
        public int Count => throw new PluginTestingException("synthetic-private-provider-text");
        public T this[int index] => throw new InvalidOperationException();
        public IEnumerator<T> GetEnumerator() => throw new InvalidOperationException();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
