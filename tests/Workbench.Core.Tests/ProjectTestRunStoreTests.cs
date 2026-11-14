using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Workbench.Core.Testing;
using Workbench.Sdk;
using Xunit;
using static Workbench.Core.Tests.ProjectTestStoreTests;

namespace Workbench.Core.Tests;

public sealed class ProjectTestRunStoreTests
{
    private const string Owner = "com.projectworkbench.testing";
    private const string Other = "com.projectworkbench.executor";
    private const string Id = "11111111111111111111111111111111";
    private const string Hash = "0000000000000000000000000000000000000000000000000000000000000000";

    [Fact]
    public async Task MissingRunReadsAndOwnerInitializationCreateNothing()
    {
        using var disk = new TestDirectory();
        var store = new ProjectTestStore(disk.User, "default");
        Assert.Empty(await store.ListRunsAsync());
        await Code("TestRecordNotFound", () => store.ReadRunAsync(Id));
        await Code("TestRecordNotFound", () => store.ReadRunEvidenceAsync(Id, Id));
        await store.InitializeOwnerAsync(Owner);
        Assert.False(Directory.Exists(disk.User));
    }

    [Fact]
    public async Task RunAndRetryFreezeExactRevisionUnderOneLeaseAndPreserveOriginalAuthor()
    {
        using var disk = new TestDirectory();
        var store = new ProjectTestStore(disk.User, "default");
        var set = await Set(store);
        var original = await File.ReadAllBytesAsync(CasePath(disk.User, set.Id));
        var selected = new[] { set.Cases[0].Id };
        Task<TestRunSnapshot> creating;
        using (var held = HoldLease(disk.User))
        {
            creating = store.CreateRunAsync(Other, set.Id, set.Revision, selected);
            selected[0] = Id;
            Assert.False(creating.IsCompleted);
        }
        var run = await creating;
        Assert.Equal(Other, run.Plan.ProducerPluginId);
        Assert.Equal("virtual.task-form.v1", run.Plan.TargetId);
        Assert.Equal(set.Cases[0].Id, Assert.Single(run.Plan.Cases).Id);
        Assert.Equal(set.Revision, run.Plan.CaseRevision);
        Assert.Null(run.Plan.ParentRunId);
        Assert.Equal(TestRunState.Queued, run.State);
        Assert.Equal(0, run.Sequence);
        var queued = Assert.Single(run.Events);
        Assert.Equal(TestRunEventKind.Queued, queued.Kind);
        Assert.Equal(Hash, queued.PriorHash);
        Assert.Equal(run.Plan.PlanHash, queued.PlanHash);
        Assert.Equal(original, await File.ReadAllBytesAsync(Path.Combine(StoreRoot(disk.User), "case-revisions", set.Id, set.Revision + ".json")));
        Assert.True(File.Exists(EventPath(disk.User, run.Plan.Id, 0)));
        var changed = await store.UpdateCaseSetAsync(Owner, set.Id, new("B", set.Cases.Select(c => c with { Title = "B" }).ToArray()), set.Revision);
        Assert.Equal(Owner, changed.ProducerPluginId);
        await Code("RevisionConflict", () => store.CreateRunAsync(Other, set.Id, set.Revision, [set.Cases[0].Id]));
        await Code("InvalidRun", () => store.CreateRetryRunAsync(Other, run.Plan.Id));
        run = await Append(store, run, new(TestRunEventKind.Failed), Other);
        var retry = await store.CreateRetryRunAsync(Other, run.Plan.Id);
        Assert.NotEqual(run.Plan.Id, retry.Plan.Id);
        Assert.NotEqual(run.Plan.SessionId, retry.Plan.SessionId);
        Assert.Equal(run.Plan.Id, retry.Plan.ParentRunId);
        Assert.Equal(set.Revision, retry.Plan.CaseRevision);
        Assert.Equal("Case", retry.Plan.Cases[0].Title);
        ((TestCase[])run.Plan.Cases)[0] = run.Plan.Cases[0] with { Title = "caller mutation" };
        var reopened = await new ProjectTestStore(disk.User, "default").ReadRunAsync(run.Plan.Id);
        Assert.Equal("Case", reopened.Plan.Cases[0].Title);
        Assert.Equal(2, (await store.ListRunsAsync()).Count);
        await Code("InvalidProducer", () => store.CreateRetryRunAsync(Owner, run.Plan.Id));
        await Code("InvalidProducer", () => Append(store, retry, new(TestRunEventKind.RunStarted), Owner));
    }

    [Fact]
    public async Task SelectionMustBeUniqueInFrozenOrderAndMappedWithinExecutableCap()
    {
        using var disk = new TestDirectory();
        var store = new ProjectTestStore(disk.User, "default");
        var source = await store.PublishSourceAsync(Owner, Source());
        var sample = Draft(source).Cases[0];
        var set = await store.CreateCaseSetAsync(Owner, new("Two", [sample, sample]));
        foreach (var selection in new[] { Array.Empty<string>(), new[] { Id }, new[] { set.Cases[0].Id, set.Cases[0].Id }, new[] { set.Cases[1].Id, set.Cases[0].Id } })
            await Code("InvalidRun", () => store.CreateRunAsync(Owner, set.Id, set.Revision, selection));
        var manual = await store.CreateCaseSetAsync(Owner, new("Manual", [sample with { Steps = [sample.Steps[0] with { Action = null }] }]));
        await Code("ManualStepUnsupported", () => store.CreateRunAsync(Owner, manual.Id, manual.Revision, [manual.Cases[0].Id]));
        var large = await store.CreateCaseSetAsync(Owner, new("Large", Enumerable.Repeat(sample with { Steps = Enumerable.Repeat(sample.Steps[0], 20).ToArray() }, 11).ToArray()));
        await Code("StoreLimitExceeded", () => store.CreateRunAsync(Owner, large.Id, large.Revision, large.Cases.Select(c => c.Id).ToArray()));
        Assert.Empty(await store.ListRunsAsync());
        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(StoreRoot(disk.User), "case-revisions")));
        var valid = await store.CreateRunAsync(Owner, large.Id, large.Revision, large.Cases.Take(10).Select(c => c.Id).ToArray());
        Assert.Equal(200, valid.Plan.Cases.Sum(c => c.Steps.Count));
    }

    [Fact]
    public async Task JournalCasAndStartedStepOrderAreCheckedBeforeAnyWrite()
    {
        using var disk = new TestDirectory();
        var store = new ProjectTestStore(disk.User, "default");
        var run = await Run(store);
        var item = run.Plan.Cases[0];
        var step = item.Steps[0];
        await Code("InvalidRun", () => Append(store, run, new(TestRunEventKind.Queued)));
        await Code("InvalidRun", () => Append(store, run, new(TestRunEventKind.Completed)));
        await Code("InvalidRun", () => Append(store, run, new(TestRunEventKind.StepStarted, item.Id, step.Id)));
        var started = await Append(store, run, new(TestRunEventKind.RunStarted));
        await Code("RevisionConflict", () => Append(store, run, new(TestRunEventKind.RunStarted)));
        await Code("RevisionConflict", () => store.AppendRunEventAsync(Owner, started.Plan.Id, started.Sequence, Hash, new(TestRunEventKind.Failed)));
        await Code("InvalidRun", () => Append(store, started, new(TestRunEventKind.RunStarted)));
        await Code("InvalidRun", () => Append(store, started, new(TestRunEventKind.StepStarted, item.Id, item.Steps[1].Id)));
        started = await Append(store, started, new(TestRunEventKind.StepStarted, item.Id, step.Id));
        await Code("InvalidRun", () => Append(store, started, new(TestRunEventKind.StepStarted, item.Id, step.Id)));
        await Code("InvalidRun", () => Append(store, started, new(TestRunEventKind.Completed)));
        var failed = await Append(store, started, new(TestRunEventKind.Failed, ErrorCode: "raw provider message"));
        Assert.Equal("TestingFailed", failed.Events[^1].ErrorCode);
        Assert.Null(failed.Events[^1].NativePlainTextState);
        await Code("InvalidRun", () => Append(store, failed, new(TestRunEventKind.Interrupted)));
        Assert.Equal(TestRunState.Failed, (await new ProjectTestStore(disk.User, "default").ReadRunAsync(run.Plan.Id)).State);
        Assert.Equal(4, Directory.EnumerateFiles(Path.Combine(RunRoot(disk.User, run.Plan.Id), "events")).Count());
    }

    [Fact]
    public async Task CancelRequestPreventsNewStepAndCanSettleUncertainActionWithoutInventedOutcome()
    {
        using var disk = new TestDirectory();
        var store = new ProjectTestStore(disk.User, "default");
        var run = await Run(store);
        run = await Append(store, run, new(TestRunEventKind.RunStarted));
        run = await Append(store, run, new(TestRunEventKind.CancelRequested));
        Assert.Equal(TestRunState.CancelRequested, run.State);
        await Code("InvalidRun", () => Append(store, run, new(TestRunEventKind.StepStarted, run.Plan.Cases[0].Id, run.Plan.Cases[0].Steps[0].Id)));
        await Code("InvalidRun", () => Append(store, run, new(TestRunEventKind.CancelRequested)));
        run = await Append(store, run, new(TestRunEventKind.Cancelled));
        Assert.Equal(TestRunState.Cancelled, run.State);
        Assert.DoesNotContain(run.Events, e => e.Outcome is not null);
        Assert.Equal(TestRunState.Queued, (await store.CreateRetryRunAsync(Owner, run.Plan.Id)).State);
    }

    [Fact]
    public async Task EditedLastEventIsRefusedEvenWithoutAFollowingHashLink()
    {
        using var disk = new TestDirectory();
        var store = new ProjectTestStore(disk.User, "default");
        var run = await Run(store);
        run = await Append(store, run, new(TestRunEventKind.Failed, ErrorCode: "AdapterFailed"));
        var path = EventPath(disk.User, run.Plan.Id, run.Sequence);
        var bytes = Encoding.UTF8.GetBytes((await File.ReadAllTextAsync(path)).Replace("AdapterFailed", "TestingFailed", StringComparison.Ordinal));
        await File.WriteAllBytesAsync(path, bytes);
        await Code("TestStoreCorrupt", () => new ProjectTestStore(disk.User, "default").ReadRunAsync(run.Plan.Id));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
    }

    [Theory]
    [InlineData("multiline")]
    [InlineData("whitespace")]
    [InlineData("maximum")]
    public async Task ActualStepStateAndDurableEvidenceReopenExactlyIncludingUnicodeAndWhitespace(string variant)
    {
        using var disk = new TestDirectory();
        var store = new ProjectTestStore(disk.User, "default");
        var run = await Run(store, 1);
        var observed = variant switch { "multiline" => "Observed 🧪\r\nline\t二", "whitespace" => " \r\n\t ", _ => "🧪" + new string('x', 8190) };
        run = await StartStep(store, run);
        var frame = Bmp();
        var receipt = await store.AddRunEvidenceAsync(Owner, run.Plan.Id, new("image/bmp", frame, true));
        Assert.Equal(172854, receipt.Length);
        Assert.Equal(Sha(frame), receipt.Sha256);
        Assert.True(File.Exists(EvidencePath(disk.User, run.Plan.Id, receipt.Id, "receipt.json")));
        Assert.Equal(frame, await File.ReadAllBytesAsync(EvidencePath(disk.User, run.Plan.Id, receipt.Id, "frame.bmp")));
        run = await Append(store, run, Finish(run, receipt.Id, observed));
        var stateEvent = run.Events[^1];
        var raw = await File.ReadAllBytesAsync(EventPath(disk.User, run.Plan.Id, run.Sequence));
        var wire = JsonNode.Parse(raw)!.AsObject();
        Assert.Equal(observed, wire["nativePlainTextState"]!.GetValue<string>());
        Assert.Equal(stateEvent.Hash, EventDigest(raw));
        run = await Append(store, run, new(TestRunEventKind.Completed));
        await Code("InvalidRun", () => store.CreateRetryRunAsync(Owner, run.Plan.Id));
        var reopened = await new ProjectTestStore(disk.User, "default").ReadRunAsync(run.Plan.Id);
        Assert.Equal(TestRunState.Completed, reopened.State);
        Assert.Equal(observed, reopened.Events[^2].NativePlainTextState);
        Assert.Equal(stateEvent.Hash, reopened.Events[^2].Hash);
        Assert.Equal(raw, await File.ReadAllBytesAsync(EventPath(disk.User, run.Plan.Id, stateEvent.Sequence)));
        var evidence = await store.ReadRunEvidenceAsync(run.Plan.Id, receipt.Id);
        evidence.Bytes[54] ^= 1;
        Assert.Equal(frame, (await store.ReadRunEvidenceAsync(run.Plan.Id, receipt.Id)).Bytes);
        await Code("InvalidRun", () => store.AddRunEvidenceAsync(Owner, run.Plan.Id, new("image/bmp", frame, true)));
    }

    [Fact]
    public async Task InvalidOrMissingObservedStateRefusesWithoutChangingAnyBytes()
    {
        using var disk = new TestDirectory();
        var store = new ProjectTestStore(disk.User, "default");
        var run = await StartStep(store, await Run(store, 1));
        var receipt = await store.AddRunEvidenceAsync(Owner, run.Plan.Id, new("image/bmp", Bmp(), true));
        var before = SnapshotBytes(StoreRoot(disk.User));
        foreach (var state in new string?[] { null, "", "\ud800", "\udc00" })
            await Code("InvalidRun", () => Append(store, run, Finish(run, receipt.Id, state)));
        await Code("StoreLimitExceeded", () => Append(store, run, Finish(run, receipt.Id, new string('x', 8193))));
        await Code("InvalidRun", () => Append(store, run, new(TestRunEventKind.Failed) { NativePlainTextState = "unexpected" }));
        AssertBytes(before, StoreRoot(disk.User));
    }

    [Fact]
    public async Task FinishedStepRequiresMatchingStartedStepOwnedReceiptAndFullPassingPlan()
    {
        using var disk = new TestDirectory();
        var store = new ProjectTestStore(disk.User, "default");
        var run = await Run(store, 3);
        var foreign = await Run(store, 1);
        var foreignReceipt = await store.AddRunEvidenceAsync(Owner, foreign.Plan.Id, new("image/bmp", Bmp(), true));
        var receipt = await store.AddRunEvidenceAsync(Owner, run.Plan.Id, new("image/bmp", Bmp(), true));
        await Code("InvalidProducer", () => store.AddRunEvidenceAsync(Other, run.Plan.Id, new("image/bmp", Bmp(), true)));
        await Code("InvalidRun", () => Append(store, run, Finish(run, receipt.Id, "observed")));
        run = await StartStep(store, run);
        await Code("InvalidRun", () => Append(store, run, Finish(run, foreignReceipt.Id, "observed")));
        await Code("InvalidRun", () => Append(store, run, Finish(run, Id, "observed")));
        await Code("InvalidRun", () => Append(store, run, Finish(run, receipt.Id, "observed") with { EvidenceIds = [] }));
        await Code("InvalidRun", () => Append(store, run, Finish(run, receipt.Id, "observed") with { EvidenceIds = [receipt.Id, receipt.Id] }));
        await Code("InvalidRun", () => Append(store, run, Finish(run, receipt.Id, "observed") with { StepId = run.Plan.Cases[0].Steps[1].Id }));
        await Code("InvalidRun", () => Append(store, run, Finish(run, receipt.Id, "observed") with { ErrorCode = "AssertionFailed" }));
        var evidenceIds = new[] { receipt.Id };
        Task<TestRunSnapshot> finishing;
        using (var held = HoldLease(disk.User))
        {
            finishing = Append(store, run, Finish(run, receipt.Id, "actual") with { EvidenceIds = evidenceIds });
            evidenceIds[0] = foreignReceipt.Id;
        }
        run = await finishing;
        Assert.Equal(receipt.Id, Assert.Single(run.Events[^1].EvidenceIds));
        await Code("InvalidRun", () => Append(store, run, new(TestRunEventKind.Completed)));
        run = await Append(store, run, new(TestRunEventKind.StepStarted, run.Plan.Cases[0].Id, run.Plan.Cases[0].Steps[1].Id));
        run = await Append(store, run, Finish(run, receipt.Id, "failed observed", TestStepOutcome.Failed) with { ErrorCode = "provider private details" });
        Assert.Equal("TestingFailed", run.Events[^1].ErrorCode);
        await Code("InvalidRun", () => Append(store, run, new(TestRunEventKind.Completed)));
        await Code("InvalidRun", () => Append(store, run, new(TestRunEventKind.CancelRequested)));
        await Code("InvalidRun", () => Append(store, run, new(TestRunEventKind.Interrupted)));
        await Code("InvalidRun", () => Append(store, run, new(TestRunEventKind.StepStarted, run.Plan.Cases[0].Id, run.Plan.Cases[0].Steps[2].Id)));
        Assert.Equal(TestRunState.Failed, (await Append(store, run, new(TestRunEventKind.Failed))).State);
    }

    [Fact]
    public async Task CancelRequestedCanFinishOnlyItsAlreadyStartedStepBeforeTerminalSettlement()
    {
        using var disk = new TestDirectory();
        var store = new ProjectTestStore(disk.User, "default");
        var run = await StartStep(store, await Run(store));
        var receipt = await store.AddRunEvidenceAsync(Owner, run.Plan.Id, new("image/bmp", Bmp(), true));
        run = await Append(store, run, new(TestRunEventKind.CancelRequested));
        run = await Append(store, run, Finish(run, receipt.Id, "action did happen"));
        Assert.Equal(TestRunState.CancelRequested, run.State);
        await Code("InvalidRun", () => Append(store, run, new(TestRunEventKind.StepStarted, run.Plan.Cases[0].Id, run.Plan.Cases[0].Steps[1].Id)));
        await Code("InvalidRun", () => Append(store, run, new(TestRunEventKind.Completed)));
        run = await Append(store, run, new(TestRunEventKind.Cancelled));
        Assert.Equal("action did happen", run.Events[^2].NativePlainTextState);
    }

    [Fact]
    public async Task EvidenceIsCopiedBeforeSchedulingAndFormatPathFlagCapsRefuseBeforePublication()
    {
        using var disk = new TestDirectory();
        var store = new ProjectTestStore(disk.User, "default");
        var run = await Run(store);
        var frame = Bmp();
        Task<TestEvidenceReceipt> saving;
        using (var held = HoldLease(disk.User))
        {
            saving = store.AddRunEvidenceAsync(Owner, run.Plan.Id, new("image/bmp", frame, true));
            frame[54] = 255;
        }
        var receipt = await saving;
        Assert.Equal(0, (await store.ReadRunEvidenceAsync(run.Plan.Id, receipt.Id)).Bytes[54]);
        foreach (var invalid in new[] { new TestEvidenceDraft("image/png", Bmp(), true), new("../../frame.bmp", Bmp(), true), new("image/bmp", Bmp(), false), new("image/bmp", [], true) })
            await Code("InvalidEvidence", () => store.AddRunEvidenceAsync(Owner, run.Plan.Id, invalid));
        foreach (var offset in new[] { 0, 2, 6, 8, 10, 14, 18, 22, 26, 28, 30, 34, 38, 42, 46, 50 })
        {
            var invalid = Bmp();
            invalid[offset] ^= 1;
            await Code("InvalidEvidence", () => store.AddRunEvidenceAsync(Owner, run.Plan.Id, new("image/bmp", invalid, true)));
        }
        await Code("InvalidEvidence", () => store.AddRunEvidenceAsync(Owner, run.Plan.Id, new("image/bmp", Bmp()[..^1], true)));
        await Code("InvalidEvidence", () => store.AddRunEvidenceAsync(Owner, run.Plan.Id, new("image/bmp", new byte[512 * 1024], true)));
        await Code("StoreLimitExceeded", () => store.AddRunEvidenceAsync(Owner, run.Plan.Id, new("image/bmp", new byte[512 * 1024 + 1], true)));
        await Code("InvalidId", () => store.ReadRunEvidenceAsync(run.Plan.Id, "../../private"));
        Assert.Single(Directory.EnumerateDirectories(Path.Combine(RunRoot(disk.User, run.Plan.Id), "evidence")));
    }

    [Fact]
    public async Task OwnerInitializationRecoversOnceIncludingFailedObservedStepAndPreservesForeignAndTerminal()
    {
        using var disk = new TestDirectory();
        var store = new ProjectTestStore(disk.User, "default");
        var queued = await Run(store, 1);
        var failedStep = await StartStep(store, await Run(store, 1));
        var receipt = await store.AddRunEvidenceAsync(Owner, failedStep.Plan.Id, new("image/bmp", Bmp(), true));
        failedStep = await Append(store, failedStep, Finish(failedStep, receipt.Id, "failed 🧪\r\n\t", TestStepOutcome.Failed));
        var stepBytes = SnapshotBytes(RunRoot(disk.User, failedStep.Plan.Id));
        stepBytes.Remove("head.json"); // The commitment advances; immutable history/evidence must remain byte-exact.
        await Code("InvalidRun", () => Append(store, failedStep, new(TestRunEventKind.Interrupted)));
        var set = await Set(store);
        var foreign = await store.CreateRunAsync(Other, set.Id, set.Revision, [set.Cases[0].Id]);
        var terminal = await Append(store, await Run(store, 1), new(TestRunEventKind.Failed));
        var foreignBytes = SnapshotBytes(RunRoot(disk.User, foreign.Plan.Id));
        var terminalBytes = SnapshotBytes(RunRoot(disk.User, terminal.Plan.Id));
        var fresh = new ProjectTestStore(disk.User, "default");
        await fresh.InitializeOwnerAsync(Owner);
        Assert.Equal(TestRunState.Interrupted, (await fresh.ReadRunAsync(queued.Plan.Id)).State);
        var recovered = await fresh.ReadRunAsync(failedStep.Plan.Id);
        Assert.Equal(TestRunState.Interrupted, recovered.State);
        Assert.Equal(TestStepOutcome.Failed, recovered.Events[^2].Outcome);
        Assert.Equal("failed 🧪\r\n\t", recovered.Events[^2].NativePlainTextState);
        Assert.Equal("RecoveryRequired", recovered.Events[^1].ErrorCode);
        Assert.True(JsonNode.Parse(await File.ReadAllBytesAsync(EventPath(disk.User, recovered.Plan.Id, recovered.Sequence)))!["isRecovery"]!.GetValue<bool>());
        AssertBytes(stepBytes, RunRoot(disk.User, failedStep.Plan.Id), allowAdditional: true);
        AssertBytes(foreignBytes, RunRoot(disk.User, foreign.Plan.Id));
        AssertBytes(terminalBytes, RunRoot(disk.User, terminal.Plan.Id));
        var all = SnapshotBytes(StoreRoot(disk.User));
        await fresh.InitializeOwnerAsync(Owner);
        AssertBytes(all, StoreRoot(disk.User));
        Assert.Equal(TestRunState.Queued, (await fresh.CreateRetryRunAsync(Owner, recovered.Plan.Id)).State);
    }

    [Theory]
    [InlineData("truncate")]
    [InlineData("edit")]
    [InlineData("delete")]
    [InlineData("duplicate-file")]
    [InlineData("gap")]
    [InlineData("schema")]
    [InlineData("unknown")]
    [InlineData("duplicate-field")]
    [InlineData("missing-state")]
    [InlineData("missing-recovery")]
    [InlineData("unicode")]
    [InlineData("prior")]
    [InlineData("plan")]
    [InlineData("kind-number")]
    [InlineData("false-completed")]
    [InlineData("recovery-kind")]
    [InlineData("recovery-type")]
    public async Task IndependentValidFirstEventsRefuseTamperingAndInitializationPreservesEveryRecord(string mutation)
    {
        using var disk = new TestDirectory();
        var store = new ProjectTestStore(disk.User, "default");
        var untouched = await Run(store, 1);
        var run = await Run(store, 1);
        var first = IndependentEvent(run.Plan, 0, Hash, "Queued");
        await File.WriteAllBytesAsync(EventPath(disk.User, run.Plan.Id, 0), first);
        var second = IndependentEvent(run.Plan, 1, EventDigest(first), "RunStarted");
        var path = EventPath(disk.User, run.Plan.Id, 1);
        await File.WriteAllBytesAsync(path, second);
        await WriteIndependentHead(disk.User, run.Plan, 1, EventDigest(second));
        Assert.Equal(TestRunState.Running, (await store.ReadRunAsync(run.Plan.Id)).State);
        var node = JsonNode.Parse(second)!.AsObject();
        switch (mutation)
        {
            case "schema": node["schemaVersion"] = 2; break;
            case "unknown": node["future"] = true; break;
            case "missing-state": node.Remove("nativePlainTextState"); break;
            case "missing-recovery": node.Remove("isRecovery"); break;
            case "prior": node["priorHash"] = Hash; break;
            case "plan": node["planHash"] = Hash; break;
            case "kind-number": node["kind"] = 1; break;
            case "false-completed": node["kind"] = "Completed"; break;
            case "recovery-kind": node["isRecovery"] = true; node["errorCode"] = "RecoveryRequired"; break;
            case "recovery-type": node["isRecovery"] = "true"; break;
        }
        var changed = SealEvent(node);
        if (mutation == "truncate") changed = changed[..^7];
        if (mutation == "edit") changed[20] ^= 1;
        if (mutation == "duplicate-field") changed = SealEventText(node.ToJsonString().Replace("\"schemaVersion\":1", "\"schemaVersion\":1,\"schemaVersion\":1", StringComparison.Ordinal));
        if (mutation == "unicode") changed = SealEventText(node.ToJsonString().Replace("\"errorCode\":null", "\"errorCode\":\"\\uD800\"", StringComparison.Ordinal));
        await File.WriteAllBytesAsync(path, changed);
        if (mutation == "delete") File.Delete(EventPath(disk.User, run.Plan.Id, 0));
        if (mutation == "duplicate-file") File.Copy(path, Path.Combine(Path.GetDirectoryName(path)!, "0000000001-copy.json"));
        if (mutation == "gap") File.Move(path, EventPath(disk.User, run.Plan.Id, 2));
        var before = SnapshotBytes(StoreRoot(disk.User));
        var fresh = new ProjectTestStore(disk.User, "default");
        await Code("TestStoreCorrupt", () => fresh.ReadRunAsync(run.Plan.Id));
        await Code("TestStoreCorrupt", () => fresh.ListSourcesAsync());
        await Code("TestStoreCorrupt", () => fresh.InitializeOwnerAsync(Owner));
        AssertBytes(before, StoreRoot(disk.User));
        Assert.Single(Directory.EnumerateFiles(Path.Combine(RunRoot(disk.User, untouched.Plan.Id), "events")));
    }

    [Theory]
    [InlineData("schema")]
    [InlineData("target")]
    [InlineData("project")]
    [InlineData("cases")]
    [InlineData("duplicate-selection")]
    [InlineData("unknown")]
    [InlineData("bytes")]
    public async Task ModifiedPlanCannotAdoptOtherAuthorityOrDriftFromExactArchive(string mutation)
    {
        using var disk = new TestDirectory();
        var store = new ProjectTestStore(disk.User, "default");
        var run = await Run(store, 1);
        var path = Path.Combine(RunRoot(disk.User, run.Plan.Id), "plan.json");
        var node = JsonNode.Parse(await File.ReadAllBytesAsync(path))!.AsObject();
        switch (mutation)
        {
            case "schema": node["schemaVersion"] = 2; break;
            case "target": node["targetId"] = "real.desktop"; break;
            case "project": node["projectId"] = Id; break;
            case "cases": node["cases"]![0]!["steps"]![0]!["instruction"] = "changed after freeze"; break;
            case "duplicate-selection": node["cases"]!.AsArray().Add(node["cases"]![0]!.DeepClone()); break;
            case "unknown": node["callerAuthority"] = true; break;
        }
        await File.WriteAllTextAsync(path, node.ToJsonString() + (mutation == "bytes" ? " " : ""));
        if (mutation != "bytes")
        {
            var modifiedPlan = run.Plan with { PlanHash = Sha(await File.ReadAllBytesAsync(path)) };
            var genesis = IndependentEvent(modifiedPlan, 0, Hash, "Queued");
            await File.WriteAllBytesAsync(EventPath(disk.User, run.Plan.Id, 0), genesis);
            await WriteIndependentHead(disk.User, modifiedPlan, 0, EventDigest(genesis));
        }
        var before = SnapshotBytes(StoreRoot(disk.User));
        await Code("TestStoreCorrupt", () => new ProjectTestStore(disk.User, "default").ReadRunAsync(run.Plan.Id));
        AssertBytes(before, StoreRoot(disk.User));
    }

    [Theory]
    [InlineData("run")]
    [InlineData("partial-run")]
    [InlineData("partial-run-head")]
    [InlineData("run-head-temp")]
    [InlineData("evidence")]
    [InlineData("partial-evidence")]
    [InlineData("event-temp")]
    public async Task RecognizedInterruptedStagesAreRetainedAndNeverExposedAsRunnable(string stageKind)
    {
        using var disk = new TestDirectory();
        var store = new ProjectTestStore(disk.User, "default");
        var run = await Run(store, 1);
        if (stageKind.Contains("run", StringComparison.Ordinal))
        {
            var stage = RunRoot(disk.User, run.Plan.Id) + ".stage";
            Directory.Move(RunRoot(disk.User, run.Plan.Id), stage);
            if (stageKind == "partial-run") File.Delete(Path.Combine(stage, "events", "0000000000.json"));
            if (stageKind is "partial-run-head" or "run-head-temp") File.Delete(Path.Combine(stage, "head.json"));
            if (stageKind == "run-head-temp") await File.WriteAllTextAsync(Path.Combine(stage, ".head.json." + Id + ".tmp"), "partial");
        }
        else if (stageKind.Contains("evidence", StringComparison.Ordinal))
        {
            var receipt = await store.AddRunEvidenceAsync(Owner, run.Plan.Id, new("image/bmp", Bmp(), true));
            var root = Path.GetDirectoryName(EvidencePath(disk.User, run.Plan.Id, receipt.Id, "receipt.json"))!;
            Directory.Move(root, root + ".stage");
            if (stageKind == "partial-evidence") File.Delete(Path.Combine(root + ".stage", "receipt.json"));
        }
        else await File.WriteAllTextAsync(Path.Combine(RunRoot(disk.User, run.Plan.Id), "events", ".0000000001.json." + Id + ".tmp"), "partial");
        var before = SnapshotBytes(StoreRoot(disk.User));
        await Code("RecoveryRequired", () => store.ListRunsAsync());
        await Code("RecoveryRequired", () => store.InitializeOwnerAsync(Owner));
        await Code("RecoveryRequired", () => store.PublishSourceAsync(Owner, Source()));
        AssertBytes(before, StoreRoot(disk.User));
    }

    [Fact]
    public async Task RecoveryFlagCannotAppearAfterTerminalOrBeRemovedFromFailedStepRecovery()
    {
        using var disk = new TestDirectory();
        var store = new ProjectTestStore(disk.User, "default");
        var run = await StartStep(store, await Run(store, 1));
        var receipt = await store.AddRunEvidenceAsync(Owner, run.Plan.Id, new("image/bmp", Bmp(), true));
        run = await Append(store, run, Finish(run, receipt.Id, "failure preserved", TestStepOutcome.Failed));
        await store.InitializeOwnerAsync(Owner);
        var recovered = await store.ReadRunAsync(run.Plan.Id);
        var path = EventPath(disk.User, run.Plan.Id, recovered.Sequence);
        var node = JsonNode.Parse(await File.ReadAllBytesAsync(path))!.AsObject();
        node["isRecovery"] = false;
        await File.WriteAllBytesAsync(path, SealEvent(node));
        await Code("TestStoreCorrupt", () => store.ReadRunAsync(run.Plan.Id));
        node["isRecovery"] = true;
        var restored = SealEvent(node);
        await File.WriteAllBytesAsync(path, restored);
        await WriteIndependentHead(disk.User, run.Plan, recovered.Sequence, EventDigest(restored));
        recovered = await store.ReadRunAsync(run.Plan.Id);
        Assert.Equal(TestRunState.Interrupted, recovered.State);
        var after = JsonNode.Parse(IndependentEvent(run.Plan, recovered.Sequence + 1, recovered.HeadHash, "Interrupted"))!.AsObject();
        after["isRecovery"] = true;
        after["errorCode"] = "RecoveryRequired";
        var afterBytes = SealEvent(after);
        await File.WriteAllBytesAsync(EventPath(disk.User, run.Plan.Id, recovered.Sequence + 1), afterBytes);
        await WriteIndependentHead(disk.User, run.Plan, recovered.Sequence + 1, EventDigest(afterBytes));
        await Code("TestStoreCorrupt", () => store.ReadRunAsync(run.Plan.Id));
    }

    [Fact]
    public async Task EventEncodedByteCapIsExactAndReplayUsesSequenceInsteadOfMtime()
    {
        using var disk = new TestDirectory();
        var store = new ProjectTestStore(disk.User, "default");
        var run = await Run(store, 1);
        var first = IndependentEvent(run.Plan, 0, Hash, "Queued");
        var node = JsonNode.Parse(first)!.AsObject();
        node["hash"] = Hash;
        var text = node.ToJsonString();
        var exact = SealEventText(text + new string(' ', 256 * 1024 - Encoding.UTF8.GetByteCount(text)));
        var path = EventPath(disk.User, run.Plan.Id, 0);
        await File.WriteAllBytesAsync(path, exact);
        await WriteIndependentHead(disk.User, run.Plan, 0, EventDigest(exact));
        Assert.Equal(EventDigest(exact), (await store.ReadRunAsync(run.Plan.Id)).HeadHash);
        var second = IndependentEvent(run.Plan, 1, EventDigest(exact), "RunStarted");
        await File.WriteAllBytesAsync(EventPath(disk.User, run.Plan.Id, 1), second);
        await WriteIndependentHead(disk.User, run.Plan, 1, EventDigest(second));
        File.SetLastWriteTimeUtc(path, new DateTime(2030, 1, 1));
        File.SetLastWriteTimeUtc(EventPath(disk.User, run.Plan.Id, 1), new DateTime(2000, 1, 1));
        Assert.Equal(TestRunState.Running, (await store.ReadRunAsync(run.Plan.Id)).State);
        await File.WriteAllBytesAsync(path, exact.Append((byte)' ').ToArray());
        await Code("StoreLimitExceeded", () => store.ReadRunAsync(run.Plan.Id));
        Assert.Equal(256 * 1024 + 1, new FileInfo(path).Length);
    }

    [Theory]
    [InlineData("native-edit")]
    [InlineData("native-empty")]
    [InlineData("native-unicode")]
    [InlineData("native-limit")]
    public async Task ReopenedActualObservedStepRejectsChangedOrMalformedStateWithoutRepair(string mutation)
    {
        using var disk = new TestDirectory();
        var store = new ProjectTestStore(disk.User, "default");
        var run = await StartStep(store, await Run(store, 1));
        var receipt = await store.AddRunEvidenceAsync(Owner, run.Plan.Id, new("image/bmp", Bmp(), true));
        run = await Append(store, run, Finish(run, receipt.Id, "actual 🧪\r\n\t"));
        var path = EventPath(disk.User, run.Plan.Id, run.Sequence);
        var node = JsonNode.Parse(await File.ReadAllBytesAsync(path))!.AsObject();
        Assert.Equal("actual 🧪\r\n\t", (await new ProjectTestStore(disk.User, "default").ReadRunAsync(run.Plan.Id)).Events[^1].NativePlainTextState);
        node["nativePlainTextState"] = mutation == "native-empty" ? "" : mutation == "native-limit" ? new string('x', 8193) : "changed";
        var bytes = mutation == "native-edit" ? Encoding.UTF8.GetBytes(node.ToJsonString()) : SealEvent(node);
        if (mutation == "native-unicode") bytes = SealEventText(Encoding.UTF8.GetString(bytes).Replace("\"nativePlainTextState\":\"changed\"", "\"nativePlainTextState\":\"\\uDC00\"", StringComparison.Ordinal));
        await File.WriteAllBytesAsync(path, bytes);
        await Code(mutation == "native-limit" ? "StoreLimitExceeded" : "TestStoreCorrupt", () => store.ReadRunAsync(run.Plan.Id));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
    }

    [Theory]
    [InlineData("schema")]
    [InlineData("unknown")]
    [InlineData("duplicate")]
    [InlineData("missing")]
    [InlineData("run")]
    [InlineData("project")]
    [InlineData("owner")]
    [InlineData("length")]
    [InlineData("sha")]
    [InlineData("flag")]
    [InlineData("mime")]
    [InlineData("truncate")]
    [InlineData("pixels")]
    [InlineData("header")]
    [InlineData("delete")]
    [InlineData("extra")]
    public async Task IndependentValidFirstReceiptAndActualFrameRejectTamperingWithoutChangingBytes(string mutation)
    {
        using var disk = new TestDirectory();
        var store = new ProjectTestStore(disk.User, "default");
        var run = await Run(store, 1);
        var receipt = await store.AddRunEvidenceAsync(Owner, run.Plan.Id, new("image/bmp", Bmp(), true));
        var path = EvidencePath(disk.User, run.Plan.Id, receipt.Id, "receipt.json");
        var framePath = EvidencePath(disk.User, run.Plan.Id, receipt.Id, "frame.bmp");
        await File.WriteAllBytesAsync(path, IndependentReceipt(run.Plan, receipt.Id, Bmp()));
        Assert.Equal(Bmp(), (await store.ReadRunEvidenceAsync(run.Plan.Id, receipt.Id)).Bytes);
        var node = JsonNode.Parse(await File.ReadAllBytesAsync(path))!.AsObject();
        switch (mutation)
        {
            case "schema": node["schemaVersion"] = 2; break;
            case "unknown": node["future"] = true; break;
            case "missing": node.Remove("mimeType"); break;
            case "run": node["runId"] = Id; break;
            case "project": node["projectId"] = Id; break;
            case "owner": node["producerPluginId"] = Other; break;
            case "length": node["length"] = 172855; break;
            case "sha": node["sha256"] = Hash; break;
            case "flag": node["isSimulated"] = false; break;
            case "mime": node["mimeType"] = "text/html"; break;
        }
        var text = node.ToJsonString();
        if (mutation == "duplicate") text = text.Replace("\"schemaVersion\":1", "\"schemaVersion\":1,\"schemaVersion\":1", StringComparison.Ordinal);
        await File.WriteAllTextAsync(path, text);
        var frame = Bmp();
        if (mutation == "truncate") await File.WriteAllBytesAsync(framePath, frame[..^1]);
        if (mutation == "pixels") { frame[54] = 1; await File.WriteAllBytesAsync(framePath, frame); }
        if (mutation == "header")
        {
            frame[28] = 32;
            await File.WriteAllBytesAsync(framePath, frame);
            node["sha256"] = Sha(frame);
            await File.WriteAllTextAsync(path, node.ToJsonString());
        }
        if (mutation == "delete") File.Delete(framePath);
        if (mutation == "extra") await File.WriteAllTextAsync(EvidencePath(disk.User, run.Plan.Id, receipt.Id, "valuable.html"), "retained");
        var before = SnapshotBytes(StoreRoot(disk.User));
        await Code("TestStoreCorrupt", () => store.ReadRunEvidenceAsync(run.Plan.Id, receipt.Id));
        await Code("TestStoreCorrupt", () => store.InitializeOwnerAsync(Owner));
        AssertBytes(before, StoreRoot(disk.User));
    }

    [Fact]
    public async Task CompleteOrphanReceiptsCountAgainstCapAndAreNeverPruned()
    {
        using var disk = new TestDirectory();
        var store = new ProjectTestStore(disk.User, "default");
        var run = await Run(store, 1);
        var bytes = Bmp();
        for (var i = 0; i < 256; i++)
        {
            var id = i.ToString("x32", System.Globalization.CultureInfo.InvariantCulture);
            Directory.CreateDirectory(Path.GetDirectoryName(EvidencePath(disk.User, run.Plan.Id, id, "receipt.json"))!);
            await File.WriteAllBytesAsync(EvidencePath(disk.User, run.Plan.Id, id, "receipt.json"), IndependentReceipt(run.Plan, id, bytes));
            await File.WriteAllBytesAsync(EvidencePath(disk.User, run.Plan.Id, id, "frame.bmp"), bytes);
            if (i == 0) Assert.Equal(bytes, (await store.ReadRunEvidenceAsync(run.Plan.Id, id)).Bytes);
        }
        Assert.Equal(TestRunState.Queued, (await store.ReadRunAsync(run.Plan.Id)).State);
        Assert.Empty(run.Events[0].EvidenceIds);
        await Code("StoreLimitExceeded", () => store.AddRunEvidenceAsync(Owner, run.Plan.Id, new("image/bmp", bytes, true)));
        Assert.Equal(256, Directory.EnumerateDirectories(Path.Combine(RunRoot(disk.User, run.Plan.Id), "evidence")).Count());
        var extra = Id;
        Directory.CreateDirectory(Path.GetDirectoryName(EvidencePath(disk.User, run.Plan.Id, extra, "receipt.json"))!);
        await File.WriteAllBytesAsync(EvidencePath(disk.User, run.Plan.Id, extra, "receipt.json"), IndependentReceipt(run.Plan, extra, bytes));
        await File.WriteAllBytesAsync(EvidencePath(disk.User, run.Plan.Id, extra, "frame.bmp"), bytes);
        await Code("StoreLimitExceeded", () => store.ListRunsAsync());
        Assert.Equal(257, Directory.EnumerateDirectories(Path.Combine(RunRoot(disk.User, run.Plan.Id), "evidence")).Count());
    }

    [Fact]
    public async Task RunAndEventCountCapsRejectRealExtraEntriesWithoutFallback()
    {
        using var disk = new TestDirectory();
        var store = new ProjectTestStore(disk.User, "default");
        var run = await Run(store, 1);
        var planNode = JsonNode.Parse(await File.ReadAllBytesAsync(Path.Combine(RunRoot(disk.User, run.Plan.Id), "plan.json")))!.AsObject();
        for (var i = 0; i < 255; i++)
        {
            var id = i.ToString("x32", System.Globalization.CultureInfo.InvariantCulture);
            planNode["id"] = id;
            planNode["sessionId"] = Guid.NewGuid().ToString("N");
            var bytes = Encoding.UTF8.GetBytes(planNode.ToJsonString());
            Directory.CreateDirectory(Path.Combine(RunRoot(disk.User, id), "events"));
            Directory.CreateDirectory(Path.Combine(RunRoot(disk.User, id), "evidence"));
            await File.WriteAllBytesAsync(Path.Combine(RunRoot(disk.User, id), "plan.json"), bytes);
            var plan = run.Plan with { Id = id, PlanHash = Sha(bytes) };
            var genesisBytes = IndependentEvent(plan, 0, Hash, "Queued");
            await File.WriteAllBytesAsync(EventPath(disk.User, id, 0), genesisBytes);
            await WriteIndependentHead(disk.User, plan, 0, EventDigest(genesisBytes));
            if (i == 0) Assert.Equal(TestRunState.Queued, (await store.ReadRunAsync(id)).State);
        }
        Assert.Equal(256, (await store.ListRunsAsync()).Count);
        await Code("StoreLimitExceeded", () => store.CreateRunAsync(Owner, run.Plan.CaseSetId, run.Plan.CaseRevision, [run.Plan.Cases[0].Id]));
        var events = Path.Combine(RunRoot(disk.User, run.Plan.Id), "events");
        var genesis = await File.ReadAllBytesAsync(EventPath(disk.User, run.Plan.Id, 0));
        for (var sequence = 1; sequence <= 512; sequence++) await File.WriteAllBytesAsync(EventPath(disk.User, run.Plan.Id, sequence), genesis);
        await Code("StoreLimitExceeded", () => store.ReadRunAsync(run.Plan.Id));
        Assert.Equal(513, Directory.EnumerateFiles(events).Count());
    }

    [Theory]
    [InlineData("run")]
    [InlineData("event")]
    [InlineData("frame")]
    [InlineData("receipt")]
    [InlineData("head")]
    public async Task ManagedRecordLinksNeverReadOrMutateTheirTargets(string area)
    {
        using var disk = new TestDirectory();
        var store = new ProjectTestStore(disk.User, "default");
        var run = await Run(store, 1);
        var outside = Path.Combine(disk.Root, "outside");
        Directory.CreateDirectory(outside);
        var valuable = Path.Combine(outside, "valuable");
        await File.WriteAllTextAsync(valuable, "must remain");
        string path;
        if (area is "frame" or "receipt")
        {
            var receipt = await store.AddRunEvidenceAsync(Owner, run.Plan.Id, new("image/bmp", Bmp(), true));
            path = EvidencePath(disk.User, run.Plan.Id, receipt.Id, area == "frame" ? "frame.bmp" : "receipt.json");
        }
        else path = area == "event" ? EventPath(disk.User, run.Plan.Id, 0) : area == "head" ? HeadPath(disk.User, run.Plan.Id) : RunRoot(disk.User, run.Plan.Id);
        if (area == "run") { Directory.Delete(path, true); Directory.CreateSymbolicLink(path, outside); }
        else { File.Delete(path); File.CreateSymbolicLink(path, valuable); }
        await Code("UnsafePath", () => store.ReadRunAsync(run.Plan.Id));
        await Code("UnsafePath", () => store.InitializeOwnerAsync(Owner));
        Assert.Equal("must remain", await File.ReadAllTextAsync(valuable));
    }

    [Fact]
    public async Task CallerCollectionFailureUsesSafeErrorAllowlistBeforeAnyIo()
    {
        using var disk = new TestDirectory();
        var store = new ProjectTestStore(disk.User, "default");
        await Code("TestingFailed", () => store.CreateRunAsync(Owner, Id, Hash, new FaultingIds()));
        Assert.False(Directory.Exists(disk.User));
    }

    [Fact]
    public async Task TwoStoresAppendThroughSameRealLeaseAndOnlyOneHeadCasWins()
    {
        using var disk = new TestDirectory();
        var first = new ProjectTestStore(disk.User, "default");
        var second = new ProjectTestStore(disk.User, "default");
        var run = await Run(first, 1);
        var writes = new[] { Append(first, run, new(TestRunEventKind.RunStarted)), Append(second, run, new(TestRunEventKind.RunStarted)) };
        try { await Task.WhenAll(writes); } catch (PluginTestingException) { }
        var winner = await Assert.Single(writes, task => task.IsCompletedSuccessfully);
        var loser = Assert.Single(writes, task => task.IsFaulted);
        Assert.Equal("RevisionConflict", Assert.IsType<PluginTestingException>(loser.Exception!.InnerException).ErrorCode);
        var reopened = await second.ReadRunAsync(run.Plan.Id);
        Assert.Equal(winner.HeadHash, reopened.HeadHash);
        Assert.Equal(2, reopened.Events.Count);
    }

    private sealed class FaultingIds : IReadOnlyList<string>
    {
        public int Count => throw new PluginTestingException("private caller diagnostic");
        public string this[int index] => throw new InvalidOperationException();
        public IEnumerator<string> GetEnumerator() => throw new InvalidOperationException();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    [Fact]
    public async Task CanonicalFourMiBSelectedCaseBodyFitsHostPlanMetadataAndReadCapIsExact()
    {
        using var disk = new TestDirectory();
        var store = new ProjectTestStore(disk.User, "default");
        var source = await store.PublishSourceAsync(Owner, Source());
        var sample = Draft(source).Cases[0];
        var set = await store.CreateCaseSetAsync(Owner, new("Set", Enumerable.Repeat(sample with { Steps = Enumerable.Repeat(sample.Steps[0], 20).ToArray() }, 10).ToArray()));
        var cases = set.Cases.Select(c => c with { Steps = c.Steps.Select(s => s with { Instruction = new string('x', 4096), Expected = new string('x', 4096) }).ToArray() }).ToArray();
        const int caseLimit = 4 * 1024 * 1024;
        var baseline = CanonicalCaseFixture(set, cases);
        var escaped = (caseLimit - baseline.Length) / 5; // Literal '<' becomes six UTF8 JSON bytes, replacing one ASCII byte.
        Assert.InRange(escaped, 1, 200 * 2 * 4096);
        string Field()
        {
            var count = Math.Min(4096, escaped);
            escaped -= count;
            return new string('<', count) + new string('x', 4096 - count);
        }
        cases = cases.Select(c => c with { Steps = c.Steps.Select(s => s with { Instruction = Field(), Expected = Field() }).ToArray() }).ToArray();
        Assert.Equal(0, escaped);
        cases[0] = cases[0] with { Prerequisites = new string('p', (caseLimit - baseline.Length) % 5) };
        var original = CanonicalCaseFixture(set, cases);
        Assert.Equal(caseLimit, original.Length);
        Assert.Equal((byte)'}', original[^1]); // Canonical serializer output, no envelope whitespace padding.
        await File.WriteAllBytesAsync(CasePath(disk.User, set.Id), original);
        set = await store.ReadCaseSetAsync(set.Id);
        Assert.Equal(200, set.Cases.Sum(c => c.Steps.Count));
        var run = await store.CreateRunAsync(Owner, set.Id, set.Revision, set.Cases.Select(c => c.Id).ToArray());
        var planPath = Path.Combine(RunRoot(disk.User, run.Plan.Id), "plan.json");
        var planBytes = await File.ReadAllBytesAsync(planPath);
        Assert.InRange(planBytes.Length, caseLimit + 1, caseLimit + 4096);
        Assert.Equal(Sha(planBytes), run.Plan.PlanHash);
        Assert.Equal(original, await File.ReadAllBytesAsync(Path.Combine(StoreRoot(disk.User), "case-revisions", set.Id, set.Revision + ".json")));
        run = await Append(store, run, new(TestRunEventKind.Failed));
        var retry = await store.CreateRetryRunAsync(Owner, run.Plan.Id);
        Assert.Equal(set.Revision, retry.Plan.CaseRevision);
        Assert.Equal(200, (await new ProjectTestStore(disk.User, "default").ReadRunAsync(retry.Plan.Id)).Plan.Cases.Sum(c => c.Steps.Count));
        var retryPath = Path.Combine(RunRoot(disk.User, retry.Plan.Id), "plan.json");
        var retryBytes = await File.ReadAllBytesAsync(retryPath);
        Assert.InRange(retryBytes.Length, caseLimit + 1, caseLimit + 4096);
        var exact = retryBytes.Concat(Enumerable.Repeat((byte)' ', caseLimit + 4096 - retryBytes.Length)).ToArray();
        await File.WriteAllBytesAsync(retryPath, exact);
        var paddedPlan = retry.Plan with { PlanHash = Sha(exact) };
        var paddedGenesis = IndependentEvent(paddedPlan, 0, Hash, "Queued");
        await File.WriteAllBytesAsync(EventPath(disk.User, retry.Plan.Id, 0), paddedGenesis);
        await WriteIndependentHead(disk.User, paddedPlan, 0, EventDigest(paddedGenesis));
        Assert.Equal(Sha(exact), (await store.ReadRunAsync(retry.Plan.Id)).Plan.PlanHash);
        var oversized = exact.Append((byte)' ').ToArray();
        await File.WriteAllBytesAsync(retryPath, oversized);
        var before = SnapshotBytes(StoreRoot(disk.User));
        await Code("StoreLimitExceeded", () => store.ReadRunAsync(retry.Plan.Id));
        await Code("StoreLimitExceeded", () => store.CreateRunAsync(Owner, set.Id, set.Revision, set.Cases.Select(c => c.Id).ToArray()));
        await Code("StoreLimitExceeded", () => store.InitializeOwnerAsync(Owner));
        AssertBytes(before, StoreRoot(disk.User));
    }

    private static readonly JsonSerializerOptions FixtureJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    [Theory]
    [InlineData("failed")]
    [InlineData("completed")]
    [InlineData("step-started")]
    [InlineData("step-finished")]
    public async Task DeletedFinalEventCannotBecomeAnOlderHeadOrReplacementRecovery(string finalKind)
    {
        using var disk = new TestDirectory();
        var store = new ProjectTestStore(disk.User, "default");
        var run = await StartStep(store, await Run(store, 1));
        if (finalKind != "step-started")
        {
            var receipt = await store.AddRunEvidenceAsync(Owner, run.Plan.Id, new("image/bmp", Bmp(), true));
            run = await Append(store, run, Finish(run, receipt.Id, "observed 🧪\r\n\t", finalKind == "failed" ? TestStepOutcome.Failed : TestStepOutcome.Passed));
            if (finalKind is "failed" or "completed")
                run = await Append(store, run, new(finalKind == "failed" ? TestRunEventKind.Failed : TestRunEventKind.Completed));
        }
        var valid = await new ProjectTestStore(disk.User, "default").ReadRunAsync(run.Plan.Id);
        Assert.Equal(run.State, valid.State);
        Assert.Equal(run.Sequence, valid.Sequence);
        Assert.Equal(run.HeadHash, valid.HeadHash);
        var untouched = await Run(store, 1);
        var before = SnapshotBytes(StoreRoot(disk.User));
        var evidenceDirectory = Path.Combine(Path.GetTempPath(), "workbench-testing-task2-fix1-20261107", "tail-" + finalKind + "-" + Guid.NewGuid().ToString("N"));
        SaveByteMap(evidenceDirectory, "before", before);
        var tailPath = EventPath(disk.User, run.Plan.Id, run.Sequence);
        var removedBytes = await File.ReadAllBytesAsync(tailPath);
        Assert.Equal(run.HeadHash, EventDigest(removedBytes));
        File.Delete(tailPath);
        var afterDeletion = SnapshotBytes(StoreRoot(disk.User));
        Assert.Equal(before.Count - 1, afterDeletion.Count);
        SaveByteMap(evidenceDirectory, "deleted", afterDeletion);
        try
        {
            var fresh = new ProjectTestStore(disk.User, "default");
            await Code("RecoveryRequired", () => fresh.ReadRunAsync(run.Plan.Id));
            await Code("RecoveryRequired", () => fresh.ListRunsAsync());
            await Code("RecoveryRequired", () => Append(fresh, run, new(TestRunEventKind.Failed)));
            await Code("RecoveryRequired", () => fresh.InitializeOwnerAsync(Owner));
            Assert.False(File.Exists(tailPath));
            AssertBytes(afterDeletion, StoreRoot(disk.User));
            Assert.Single(Directory.EnumerateFiles(Path.Combine(RunRoot(disk.User, untouched.Plan.Id), "events")));
        }
        finally { SaveByteMap(evidenceDirectory, "after-refusal-attempts", SnapshotBytes(StoreRoot(disk.User))); }
    }

    [Fact]
    public async Task InitialAndAppendedHeadCommitTheExactPlanSequenceAndEventDigest()
    {
        using var disk = new TestDirectory();
        var store = new ProjectTestStore(disk.User, "default");
        var run = await Run(store, 1);
        var genesis = await File.ReadAllBytesAsync(EventPath(disk.User, run.Plan.Id, 0));
        var originalHead = await File.ReadAllBytesAsync(HeadPath(disk.User, run.Plan.Id));
        AssertHead(run, originalHead);
        run = await Append(store, run, new(TestRunEventKind.RunStarted));
        var newHead = await File.ReadAllBytesAsync(HeadPath(disk.User, run.Plan.Id));
        AssertHead(run, newHead);
        Assert.NotEqual(originalHead, newHead);
        Assert.Equal(genesis, await File.ReadAllBytesAsync(EventPath(disk.User, run.Plan.Id, 0)));
        var fresh = await new ProjectTestStore(disk.User, "default").ReadRunAsync(run.Plan.Id);
        Assert.Equal(run.Sequence, fresh.Sequence);
        Assert.Equal(run.HeadHash, fresh.HeadHash);
    }

    [Theory]
    [InlineData("missing", "TestStoreCorrupt")]
    [InlineData("truncate", "TestStoreCorrupt")]
    [InlineData("schema", "TestStoreCorrupt")]
    [InlineData("unknown", "TestStoreCorrupt")]
    [InlineData("duplicate", "TestStoreCorrupt")]
    [InlineData("missing-field", "TestStoreCorrupt")]
    [InlineData("run", "TestStoreCorrupt")]
    [InlineData("plan", "TestStoreCorrupt")]
    [InlineData("negative", "TestStoreCorrupt")]
    [InlineData("over-limit", "TestStoreCorrupt")]
    [InlineData("invalid-hash", "TestStoreCorrupt")]
    [InlineData("behind", "RecoveryRequired")]
    [InlineData("ahead", "RecoveryRequired")]
    [InlineData("digest", "RecoveryRequired")]
    [InlineData("temporary", "RecoveryRequired")]
    public async Task MissingMalformedOrDivergentHeadBlocksTheWholeStoreAndOwnerRecovery(string mutation, string code)
    {
        using var disk = new TestDirectory();
        var store = new ProjectTestStore(disk.User, "default");
        var run = await Run(store, 1);
        var initial = await File.ReadAllBytesAsync(HeadPath(disk.User, run.Plan.Id));
        run = await Append(store, run, new(TestRunEventKind.RunStarted));
        var untouched = await Run(store, 1);
        Assert.Equal(TestRunState.Running, (await store.ReadRunAsync(run.Plan.Id)).State);
        var path = HeadPath(disk.User, run.Plan.Id);
        var node = JsonNode.Parse(await File.ReadAllBytesAsync(path))!.AsObject();
        switch (mutation)
        {
            case "schema": node["schemaVersion"] = 2; break;
            case "unknown": node["future"] = true; break;
            case "missing-field": node.Remove("eventHash"); break;
            case "run": node["runId"] = Id; break;
            case "plan": node["planHash"] = Hash; break;
            case "negative": node["sequence"] = -1; break;
            case "over-limit": node["sequence"] = 512; break;
            case "invalid-hash": node["eventHash"] = "../bad"; break;
            case "ahead": node["sequence"] = 2; break;
            case "digest": node["eventHash"] = Hash; break;
        }
        var bytes = Encoding.UTF8.GetBytes(node.ToJsonString());
        if (mutation == "truncate") bytes = bytes[..^4];
        if (mutation == "duplicate") bytes = Encoding.UTF8.GetBytes(node.ToJsonString().Replace("\"schemaVersion\":1", "\"schemaVersion\":1,\"schemaVersion\":1", StringComparison.Ordinal));
        if (mutation == "behind") bytes = initial;
        await File.WriteAllBytesAsync(path, bytes);
        if (mutation == "missing") File.Delete(path);
        if (mutation == "temporary") await File.WriteAllTextAsync(Path.Combine(RunRoot(disk.User, run.Plan.Id), ".head.json." + Id + ".tmp"), "incomplete head update");
        var before = SnapshotBytes(StoreRoot(disk.User));
        var fresh = new ProjectTestStore(disk.User, "default");
        await Code(code, () => fresh.ReadRunAsync(run.Plan.Id));
        await Code(code, () => fresh.ListSourcesAsync());
        await Code(code, () => fresh.ListRunsAsync());
        await Code(code, () => Append(fresh, run, new(TestRunEventKind.Failed)));
        await Code(code, () => fresh.InitializeOwnerAsync(Owner));
        AssertBytes(before, StoreRoot(disk.User));
        Assert.Single(Directory.EnumerateFiles(Path.Combine(RunRoot(disk.User, untouched.Plan.Id), "events")));
    }

    [Fact]
    public async Task ActualHeadPublicationFailureRetainsTheNewEventAndOldHeadWithoutAdvancingOrRepair()
    {
        using var disk = new TestDirectory();
        var store = new ProjectTestStore(disk.User, "default");
        var run = await Run(store, 1);
        var root = RunRoot(disk.User, run.Plan.Id);
        var initial = await File.ReadAllBytesAsync(HeadPath(disk.User, run.Plan.Id));
        AssertHead(run, initial);
        if (OperatingSystem.IsWindows())
        {
            // Readers may open the old head, but replacing it is denied without FILE_SHARE_DELETE.
            using var held = new FileStream(HeadPath(disk.User, run.Plan.Id), FileMode.Open, FileAccess.Read, FileShare.Read);
            await Code("StoreIoFailure", () => Append(store, run, new(TestRunEventKind.RunStarted)));
        }
        else
        {
            // The events child stays writable; only creating the adjacent head temporary is denied.
            var mode = File.GetUnixFileMode(root);
            File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserExecute);
            try { await Code("StoreIoFailure", () => Append(store, run, new(TestRunEventKind.RunStarted))); }
            finally { File.SetUnixFileMode(root, mode); }
        }
        Assert.Equal(initial, await File.ReadAllBytesAsync(HeadPath(disk.User, run.Plan.Id)));
        var next = await File.ReadAllBytesAsync(EventPath(disk.User, run.Plan.Id, 1));
        Assert.Equal("RunStarted", JsonNode.Parse(next)!["kind"]!.GetValue<string>());
        Assert.Equal(run.HeadHash, JsonNode.Parse(next)!["priorHash"]!.GetValue<string>());
        Assert.Equal(EventDigest(next), JsonNode.Parse(next)!["hash"]!.GetValue<string>());
        Assert.Empty(Directory.EnumerateFiles(root, "*.tmp", SearchOption.AllDirectories));
        var before = SnapshotBytes(StoreRoot(disk.User));
        SaveByteMap(Path.Combine(Path.GetTempPath(), "workbench-testing-task2-fix1-20261107", "actual-head-failure-" + Guid.NewGuid().ToString("N")), "partial-publication", before);
        await Code("RecoveryRequired", () => store.ReadRunAsync(run.Plan.Id));
        await Code("RecoveryRequired", () => store.ListRunsAsync());
        await Code("RecoveryRequired", () => Append(store, run, new(TestRunEventKind.Failed)));
        await Code("RecoveryRequired", () => store.InitializeOwnerAsync(Owner));
        AssertBytes(before, StoreRoot(disk.User));
    }

    [Fact]
    public async Task IndependentOldHeadPlusNewEventCrashWindowFixtureIsPreservedWithoutGuessing()
    {
        using var disk = new TestDirectory();
        var store = new ProjectTestStore(disk.User, "default");
        var run = await Run(store, 1);
        var initial = await File.ReadAllBytesAsync(HeadPath(disk.User, run.Plan.Id));
        AssertHead(run, initial);
        var next = IndependentEvent(run.Plan, 1, run.HeadHash, "RunStarted");
        await File.WriteAllBytesAsync(EventPath(disk.User, run.Plan.Id, 1), next);
        var before = SnapshotBytes(StoreRoot(disk.User));
        Assert.Equal(initial, before[Path.GetRelativePath(StoreRoot(disk.User), HeadPath(disk.User, run.Plan.Id))]);
        await Code("RecoveryRequired", () => new ProjectTestStore(disk.User, "default").ReadRunAsync(run.Plan.Id));
        await Code("RecoveryRequired", () => store.InitializeOwnerAsync(Owner));
        AssertBytes(before, StoreRoot(disk.User));
    }

    [Fact]
    public async Task HeadEncodedCapIsExactAndOversizedHeaderIsPreserved()
    {
        using var disk = new TestDirectory();
        var store = new ProjectTestStore(disk.User, "default");
        var run = await Run(store, 1);
        var path = HeadPath(disk.User, run.Plan.Id);
        var original = await File.ReadAllBytesAsync(path);
        var exact = original.Concat(Enumerable.Repeat((byte)' ', 4096 - original.Length)).ToArray();
        await File.WriteAllBytesAsync(path, exact);
        Assert.Equal(run.HeadHash, (await store.ReadRunAsync(run.Plan.Id)).HeadHash);
        var tooLarge = exact.Append((byte)' ').ToArray();
        await File.WriteAllBytesAsync(path, tooLarge);
        await Code("StoreLimitExceeded", () => store.ReadRunAsync(run.Plan.Id));
        await Code("StoreLimitExceeded", () => store.InitializeOwnerAsync(Owner));
        Assert.Equal(tooLarge, await File.ReadAllBytesAsync(path));
    }

    private static string HeadPath(string user, string runId) => Path.Combine(RunRoot(user, runId), "head.json");
    private static void AssertHead(TestRunSnapshot run, byte[] bytes)
    {
        var node = JsonNode.Parse(bytes)!.AsObject();
        Assert.Equal(5, node.Count);
        Assert.Equal(1, node["schemaVersion"]!.GetValue<int>());
        Assert.Equal(run.Plan.Id, node["runId"]!.GetValue<string>());
        Assert.Equal(run.Plan.PlanHash, node["planHash"]!.GetValue<string>());
        Assert.Equal(run.Sequence, node["sequence"]!.GetValue<long>());
        Assert.Equal(run.HeadHash, node["eventHash"]!.GetValue<string>());
    }

    private static Task WriteIndependentHead(string user, TestRunPlan plan, long sequence, string eventHash)
        => File.WriteAllTextAsync(HeadPath(user, plan.Id), $$"""
            {"schemaVersion":1,"runId":"{{plan.Id}}","planHash":"{{plan.PlanHash}}","sequence":{{sequence}},"eventHash":"{{eventHash}}"}
            """);

    private static void SaveByteMap(string directory, string name, Dictionary<string, byte[]> files)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, name + ".json"), JsonSerializer.Serialize(files.ToDictionary(
            pair => pair.Key, pair => new { Length = pair.Value.Length, Sha256 = Sha(pair.Value), Base64 = Convert.ToBase64String(pair.Value) })));
    }
    private static byte[] CanonicalCaseFixture(TestCaseSetSnapshot set, IReadOnlyList<TestCase> cases) => JsonSerializer.SerializeToUtf8Bytes(new
    {
        SchemaVersion = 1, set.Id, ProjectId = "default", ProducerPluginId = Owner,
        UpdatedUtc = DateTimeOffset.Parse("2026-01-01T00:00:00+00:00", System.Globalization.CultureInfo.InvariantCulture), Name = "Set", Cases = cases
    }, FixtureJson);

    private static byte[] IndependentReceipt(TestRunPlan plan, string id, byte[] bytes) => Encoding.UTF8.GetBytes($$"""
        {"schemaVersion":1,"id":"{{id}}","runId":"{{plan.Id}}","projectId":"default","producerPluginId":"{{plan.ProducerPluginId}}","mimeType":"image/bmp","length":172854,"sha256":"{{Sha(bytes)}}","isSimulated":true}
        """);

    private static byte[] IndependentEvent(TestRunPlan plan, long sequence, string prior, string kind) => SealEventText($$"""
        {"schemaVersion":1,"runId":"{{plan.Id}}","sequence":{{sequence}},"priorHash":"{{prior}}","planHash":"{{plan.PlanHash}}","createdUtc":"2026-01-01T00:00:00Z","kind":"{{kind}}","caseId":null,"stepId":null,"outcome":null,"errorCode":null,"evidenceIds":[],"nativePlainTextState":null,"isRecovery":false,"hash":"{{Hash}}"}
        """);
    private static byte[] SealEvent(JsonObject node) { node["hash"] = Hash; return SealEventText(node.ToJsonString()); }
    private static byte[] SealEventText(string text)
    {
        var match = System.Text.RegularExpressions.Regex.Match(text, "\\\"hash\\\":\\\"([0-9a-f]{64})\\\"");
        Assert.True(match.Success);
        var value = match.Groups[1];
        var zero = text[..value.Index] + Hash + text[(value.Index + value.Length)..];
        var sha = Sha(Encoding.UTF8.GetBytes(zero));
        return Encoding.UTF8.GetBytes(zero.Replace("\"hash\":\"" + Hash + "\"", "\"hash\":\"" + sha + "\"", StringComparison.Ordinal));
    }

    private static async Task<TestRunSnapshot> StartStep(ProjectTestStore store, TestRunSnapshot run)
    {
        run = await Append(store, run, new(TestRunEventKind.RunStarted));
        return await Append(store, run, new(TestRunEventKind.StepStarted, run.Plan.Cases[0].Id, run.Plan.Cases[0].Steps[0].Id));
    }
    private static TestRunEventDraft Finish(TestRunSnapshot run, string receiptId, string? state, TestStepOutcome outcome = TestStepOutcome.Passed)
    {
        var started = run.Events.LastOrDefault(e => e.Kind == TestRunEventKind.StepStarted);
        return new(TestRunEventKind.StepFinished, started?.CaseId ?? run.Plan.Cases[0].Id, started?.StepId ?? run.Plan.Cases[0].Steps[0].Id, outcome, EvidenceIds: [receiptId]) { NativePlainTextState = state };
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
    private static string EvidencePath(string user, string runId, string id, string file) => Path.Combine(RunRoot(user, runId), "evidence", id, file);
    private static string EventDigest(byte[] bytes)
    {
        var text = Encoding.UTF8.GetString(bytes);
        var match = System.Text.RegularExpressions.Regex.Match(text, "\\\"hash\\\":\\\"([0-9a-f]{64})\\\"");
        Assert.True(match.Success);
        var value = match.Groups[1];
        return Sha(Encoding.UTF8.GetBytes(text[..value.Index] + Hash + text[(value.Index + value.Length)..]));
    }
    private static Dictionary<string, byte[]> SnapshotBytes(string root) => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
        .ToDictionary(p => Path.GetRelativePath(root, p), File.ReadAllBytes, StringComparer.Ordinal);
    private static void AssertBytes(Dictionary<string, byte[]> before, string root, bool allowAdditional = false)
    {
        var after = SnapshotBytes(root);
        if (!allowAdditional) Assert.Equal(before.Keys.Order(StringComparer.Ordinal), after.Keys.Order(StringComparer.Ordinal));
        foreach (var (path, bytes) in before) Assert.Equal(bytes, after[path]);
    }

    private static FileStream HoldLease(string user) => new(Path.Combine(user, ".workbench-project", "testing.lock"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    private static async Task<TestCaseSetSnapshot> Set(ProjectTestStore store, int steps = 1)
    {
        var source = await store.PublishSourceAsync(Owner, Source());
        var sample = Draft(source).Cases[0];
        return await store.CreateCaseSetAsync(Owner, new("Set", [sample with { Steps = Enumerable.Repeat(sample.Steps[0], steps).ToArray() }]));
    }
    private static async Task<TestRunSnapshot> Run(ProjectTestStore store, int steps = 2)
    {
        var set = await Set(store, steps);
        return await store.CreateRunAsync(Owner, set.Id, set.Revision, [set.Cases[0].Id]);
    }
    private static Task<TestRunSnapshot> Append(ProjectTestStore store, TestRunSnapshot run, TestRunEventDraft draft, string owner = Owner)
        => store.AppendRunEventAsync(owner, run.Plan.Id, run.Sequence, run.HeadHash, draft);
    private static string RunRoot(string user, string run) => Path.Combine(StoreRoot(user), "runs", run);
    private static string EventPath(string user, string run, long sequence) => Path.Combine(RunRoot(user, run), "events", sequence.ToString("D10", System.Globalization.CultureInfo.InvariantCulture) + ".json");
}
