using System.Buffers.Binary;
using System.Collections;
using Workbench.App;
using Workbench.Core.Testing;
using Workbench.Sdk;
using Xunit;
using static Workbench.Core.Tests.ProjectTestStoreTests;

namespace Workbench.Core.Tests;

public sealed class NativeStoreObservationTests
{
    private const string Owner = "com.projectworkbench.testing";

    [Theory]
    [InlineData("sources")]
    [InlineData("source")]
    [InlineData("sets")]
    [InlineData("set")]
    [InlineData("runs")]
    [InlineData("run")]
    [InlineData("evidence")]
    public async Task Real_lease_refusal_is_unavailable_then_release_reads_the_existing_record_without_writes(string read)
    {
        using var disk = new TestDirectory();
        var store = new ProjectTestStore(disk.User, "default");
        var source = await store.PublishSourceAsync(Owner, Source());
        var set = await store.CreateCaseSetAsync(Owner, Draft(source));
        var run = await store.CreateRunAsync(Owner, set.Id, set.Revision, [set.Cases[0].Id]);
        var receipt = await store.AddRunEvidenceAsync(Owner, run.Plan.Id, new("image/bmp", Bmp(), true));
        var before = Snapshot(StoreRoot(disk.User));
        var admissions = 0;

        async Task<object> Read()
        {
            admissions++;
            return read switch
            {
                "sources" => await store.ListSourcesAsync(),
                "source" => await store.ReadSourceAsync(source.Id),
                "sets" => await store.ListCaseSetsAsync(),
                "set" => await store.ReadCaseSetAsync(set.Id),
                "runs" => await store.ListRunsAsync(),
                "run" => await store.ReadRunAsync(run.Plan.Id),
                "evidence" => await store.ReadRunEvidenceAsync(run.Plan.Id, receipt.Id),
                _ => throw new InvalidOperationException()
            };
        }

        using (var held = HoldLease(disk.User))
        {
            Assert.False((await NativeStoreObservation.ReadAsync(Read())).IsReady);
            Assert.Equal(1, admissions);
            AssertBytes(before, StoreRoot(disk.User));
            Assert.False((await NativeStoreObservation.ReadAsync(Read())).IsReady);
            Assert.Equal(2, admissions);
            AssertBytes(before, StoreRoot(disk.User));
        }

        var actualRead = Read();
        var available = await NativeStoreObservation.ReadAsync(actualRead);
        Assert.True(available.IsReady);
        Assert.Same(await actualRead, available.Value);
        Assert.Equal(3, admissions);
        switch (available.Value)
        {
            case TestSourceSnapshot value: Assert.Equal(source.Id, value.Id); Assert.Equal(source.Revision, value.Revision); Assert.Equal(source.OriginalBytes, value.OriginalBytes); break;
            case TestCaseSetSnapshot value: Assert.Equal(set.Revision, value.Revision); Assert.Equal(set.Id, value.Id); break;
            case TestRunSnapshot value: Assert.Equal(run.Plan.Id, value.Plan.Id); Assert.Equal(run.HeadHash, value.HeadHash); break;
            case TestEvidence value: Assert.Equal(receipt, value.Receipt); Assert.Equal(Bmp(), value.Bytes); break;
            case IReadOnlyList<TestSourceSummary> value: Assert.Equal(source.Id, Assert.Single(value).Id); break;
            case IReadOnlyList<TestCaseSetSummary> value: Assert.Equal(set.Id, Assert.Single(value).Id); break;
            case IReadOnlyList<TestRunSummary> value: Assert.Equal(run.Plan.Id, Assert.Single(value).Id); break;
            default: throw new InvalidOperationException();
        }
        AssertBytes(before, StoreRoot(disk.User));
    }

    [Fact]
    public async Task Actual_inflight_Core_read_stays_owned_until_lease_release_and_returns_the_same_task_result()
    {
        using var disk = new TestDirectory();
        var store = new ProjectTestStore(disk.User, "default");
        var source = await store.PublishSourceAsync(Owner, Source());
        var before = Snapshot(StoreRoot(disk.User));
        Task<TestSourceSnapshot> actualRead;
        Task<NativeStoreObservation.Result<TestSourceSnapshot>> observing;
        using (var held = HoldLease(disk.User))
        {
            actualRead = store.ReadSourceAsync(source.Id);
            observing = NativeStoreObservation.ReadAsync(actualRead);
            Assert.False(actualRead.IsCompleted);
            Assert.False(observing.IsCompleted);
        }
        var available = await observing;
        Assert.True(available.IsReady);
        Assert.True(actualRead.IsCompletedSuccessfully);
        Assert.Same(await actualRead, available.Value);
        Assert.Equal(source.Revision, available.Value.Revision);
        AssertBytes(before, StoreRoot(disk.User));
    }

    [Fact]
    public async Task Pending_read_task_is_awaited_without_an_internal_retry_or_detached_completion()
    {
        var pending = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        var observing = NativeStoreObservation.ReadAsync(pending.Task);
        Assert.False(observing.IsCompleted);
        var exact = new object();
        pending.SetResult(exact);
        var result = await observing;
        Assert.True(result.IsReady);
        Assert.Same(exact, result.Value);
    }

    [Fact]
    public async Task Successful_null_empty_and_default_values_are_available_and_preserve_identity()
    {
        var empty = Array.Empty<object>();
        var list = await NativeStoreObservation.ReadAsync(Task.FromResult<IReadOnlyList<object>>(empty));
        Assert.True(list.IsReady);
        Assert.Same(empty, list.Value);
        var missing = await NativeStoreObservation.ReadAsync(Task.FromResult<object?>(null));
        Assert.True(missing.IsReady);
        Assert.Null(missing.Value);
        var value = await NativeStoreObservation.ReadAsync(Task.FromResult(false));
        Assert.True(value.IsReady);
        Assert.False(value.Value);
        using var disk = new TestDirectory();
        var store = new ProjectTestStore(disk.User, "default");
        var absentStore = await NativeStoreObservation.ReadAsync(store.ListRunsAsync());
        Assert.True(absentStore.IsReady);
        Assert.Empty(absentStore.Value);
        Assert.False(Directory.Exists(disk.User));
    }

    [Fact]
    public async Task Each_busy_admission_awaits_its_single_read_and_does_not_reinvoke_the_factory()
    {
        var calls = 0;
        Task<object> Read() { calls++; return Task.FromException<object>(new PluginTestingException("StoreBusy")); }
        for (var admission = 1; admission <= 3; admission++)
        {
            Assert.False((await NativeStoreObservation.ReadAsync(Read())).IsReady);
            Assert.Equal(admission, calls);
        }
    }

    [Theory]
    [InlineData("StoreIoFailure")]
    [InlineData("TestStoreCorrupt")]
    [InlineData("TestingContextClosed")]
    [InlineData("TestRecordNotFound")]
    [InlineData("InvalidId")]
    [InlineData("AssertionFailed")]
    [InlineData("RevisionConflict")]
    [InlineData("storeBusy")]
    [InlineData("STOREBUSY")]
    [InlineData(" StoreBusy")]
    [InlineData("StoreBusy ")]
    [InlineData("StoreBusy\0")]
    [InlineData("StoreBusy\u200b")]
    [InlineData("StoreBusy\nprivate")]
    [InlineData("")]
    [InlineData(null)]
    public async Task Every_other_SDK_code_propagates_the_exact_failure_without_normalizing(string? code)
    {
        var failure = new PluginTestingException(code!);
        var caught = await Assert.ThrowsAsync<PluginTestingException>(() => NativeStoreObservation.ReadAsync(Task.FromException<object>(failure)));
        Assert.Same(failure, caught);
    }

    [Theory]
    [InlineData("StoreBusy")]
    [InlineData("StoreIoFailure")]
    public void Synchronous_factory_prefix_failures_stay_outside_the_read_task_observation(string code)
    {
        var failure = new PluginTestingException(code);
        Task<object> Read() => throw failure;
        var caught = Assert.Throws<PluginTestingException>(() => { _ = NativeStoreObservation.ReadAsync(Read()); });
        Assert.Same(failure, caught);
    }

    [Fact]
    public void Synchronous_foreign_and_cancellation_prefix_failures_are_unchanged()
    {
        var foreign = new HostileException();
        Task<object> ForeignRead() => throw foreign;
        Assert.Same(foreign, Assert.Throws<HostileException>(() => { _ = NativeStoreObservation.ReadAsync(ForeignRead()); }));
        var cancelled = new OperationCanceledException();
        Task<object> CancelledRead() => throw cancelled;
        Assert.Same(cancelled, Assert.Throws<OperationCanceledException>(() => { _ = NativeStoreObservation.ReadAsync(CancelledRead()); }));
    }

    [Fact]
    public async Task Cancellation_and_foreign_throwing_properties_propagate_untouched()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var stopped = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => NativeStoreObservation.ReadAsync(Task.FromCanceled<object>(cancellation.Token)));
        Assert.Equal(cancellation.Token, stopped.CancellationToken);
        var cancellationFailure = new OperationCanceledException(cancellation.Token);
        Assert.Same(cancellationFailure, await Assert.ThrowsAsync<OperationCanceledException>(() => NativeStoreObservation.ReadAsync(Task.FromException<object>(cancellationFailure))));
        var foreign = new HostileException();
        Assert.Same(foreign, await Assert.ThrowsAsync<HostileException>(() => NativeStoreObservation.ReadAsync(Task.FromException<object>(foreign))));
    }

    [Fact]
    public async Task Real_missing_record_invalid_id_and_corruption_are_fatal_observations()
    {
        using var disk = new TestDirectory();
        var store = new ProjectTestStore(disk.User, "default");
        await Code("TestRecordNotFound", () => NativeStoreObservation.ReadAsync(store.ReadRunAsync("11111111111111111111111111111111")));
        await Code("InvalidId", () => NativeStoreObservation.ReadAsync(store.ReadRunAsync("../invalid")));
        var source = await store.PublishSourceAsync(Owner, Source());
        using (var obstruction = new FileStream(SourcePath(disk.User, source.Id), FileMode.Open, FileAccess.Read, FileShare.None))
            await Code("StoreIoFailure", () => NativeStoreObservation.ReadAsync(store.ReadSourceAsync(source.Id)));
        await File.WriteAllTextAsync(Path.Combine(StoreRoot(disk.User), "store.json"), "{}");
        await Code("TestStoreCorrupt", () => NativeStoreObservation.ReadAsync(store.ListRunsAsync()));
    }

    private sealed class HostileException : Exception
    {
        public string ErrorCode => throw new InvalidOperationException();
        public override string Message => throw new InvalidOperationException();
        public override string? StackTrace => throw new InvalidOperationException();
        public override IDictionary Data => throw new InvalidOperationException();
        public override string ToString() => throw new InvalidOperationException();
    }

    private static FileStream HoldLease(string user) => new(Path.Combine(user, ".workbench-project", "testing.lock"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    private static Dictionary<string, byte[]> Snapshot(string root) => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
        .ToDictionary(path => Path.GetRelativePath(root, path), File.ReadAllBytes, StringComparer.Ordinal);
    private static void AssertBytes(Dictionary<string, byte[]> before, string root)
    {
        var after = Snapshot(root);
        Assert.Equal(before.Keys.Order(StringComparer.Ordinal), after.Keys.Order(StringComparer.Ordinal));
        foreach (var (path, bytes) in before) Assert.Equal(bytes, after[path]);
    }
    private static byte[] Bmp()
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
}
