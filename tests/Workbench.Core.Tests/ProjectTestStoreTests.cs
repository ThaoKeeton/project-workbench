using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Workbench.Core.Testing;
using Workbench.Sdk;
using Xunit;

namespace Workbench.Core.Tests;

public sealed class ProjectTestStoreTests
{
    private const string Owner = "com.projectworkbench.testing";
    private const string Other = "com.example.other";
    private const string Id = "11111111111111111111111111111111";
    private const string Hash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Fact]
    public async Task DefaultHostAcceptsTestingMinimumWhileExplicitOldHostRefuses()
    {
        using var disk = new TestDirectory();
        disk.Package(Owner, fields: new() { ["minimumHostVersion"] = "0.4.0" });
        await using (var current = new PluginManager(new PluginManagerOptions { PluginsRoot = disk.Plugins, UserRoot = disk.User }))
        {
            var package = Assert.Single(await current.DiscoverAsync());
            Assert.Equal(PluginState.Disabled, package.State);
            Assert.Empty(package.Diagnostics);
        }
        await using var older = new PluginManager(new PluginManagerOptions { PluginsRoot = disk.Plugins, UserRoot = disk.User, HostVersion = new(0, 3, 0) });
        Assert.Contains(Assert.Single(await older.DiscoverAsync()).Diagnostics, d => d.Code == PluginDiagnosticCode.IncompatibleHost);
    }

    [Fact]
    public void InternalSafeErrorFactoryDoesNotExposeUnknownDiagnosticCodes()
    {
        Assert.Equal("TestingFailed", TestValidation.Error("private provider exception content").ErrorCode);
        Assert.Equal("StoreBusy", TestValidation.Error("StoreBusy").ErrorCode);
    }

    [Fact]
    public async Task MissingReadsCreateNothing()
    {
        using var disk = new TestDirectory();
        var store = new ProjectTestStore(disk.User, "default");
        Assert.Empty(await store.ListSourcesAsync());
        Assert.Empty(await store.ListCaseSetsAsync());
        await Code("TestRecordNotFound", () => store.ReadSourceAsync(Id));
        await Code("TestRecordNotFound", () => store.ReadCaseSetAsync(Id));
        await Code("TestRecordNotFound", () => store.ReadCaseRevisionAsync(Id, Hash));
        Assert.False(Directory.Exists(disk.User));
    }

    [Fact]
    public async Task SourceCopiesOriginalBomBytesBeforeSchedulingAndOnEveryReturn()
    {
        using var disk = new TestDirectory();
        var store = new ProjectTestStore(disk.User, "default");
        var bytes = new byte[] { 239, 187, 191, 123, 125 };
        var criteria = new[] { "Accept 🧪\r\n\t" };
        var items = new[] { new TestSourceItem("../../x", "Title", "Description", criteria, TestVirtualScenario.TaskFormPositive) };
        var draft = Source(bytes, items);
        var publish = store.PublishSourceAsync(Owner, draft);
        bytes[0] = 0;
        criteria[0] = "mutated";
        items[0] = items[0] with { Locator = "mutated" };
        var saved = await publish;
        Assert.Matches("^[0-9a-f]{32}$", saved.Id);
        Assert.Equal(new byte[] { 239, 187, 191, 123, 125 }, saved.OriginalBytes);
        Assert.Equal("aa25e978046d680ef8740d837e6de5bc1e2a2dc6089dbda1012544b538d53f65", saved.OriginalSha256);
        Assert.Equal("../../x", saved.Items[0].Locator);
        Assert.Equal("Accept 🧪\r\n\t", saved.Items[0].AcceptanceCriteria[0]);
        var raw = await File.ReadAllBytesAsync(SourcePath(disk.User, saved.Id));
        Assert.Equal(Sha(raw), saved.Revision);
        Assert.NotEqual(saved.OriginalSha256, saved.Revision);
        saved.OriginalBytes[0] = 0;
        var reopened = await new ProjectTestStore(disk.User, "default").ReadSourceAsync(saved.Id);
        Assert.Equal(239, reopened.OriginalBytes[0]);
        Assert.Equal("../../x", reopened.Items[0].Locator);
        Assert.Single(await store.ListSourcesAsync());
        Assert.False(File.Exists(Path.Combine(disk.User, "x")));
    }

    [Fact]
    public async Task ExactTraceReopensAndMetadataPreservesBindingWhileTextRequiresManual()
    {
        using var disk = new TestDirectory();
        var store = new ProjectTestStore(disk.User, "default");
        var source = await store.PublishSourceAsync(Owner, Source());
        var saved = await store.CreateCaseSetAsync(Owner, Draft(source));
        Assert.Matches("^[0-9a-f]{32}$", saved.Cases[0].Id);
        Assert.Matches("^[0-9a-f]{32}$", saved.Cases[0].Steps[0].Id);
        Assert.NotEqual(saved.Id, saved.Cases[0].Id);
        var reopened = await new ProjectTestStore(disk.User, "default").ReadCaseSetAsync(saved.Id);
        Assert.Equal(source.Revision, reopened.Cases[0].Traces[0].SnapshotRevision);
        Assert.Equal("../../x", reopened.Cases[0].Steps[0].Traces[0].Locator);
        var metadata = new TestCaseSetContent("Renamed", saved.Cases.Select(c => c with { Title = "Renamed case", Prerequisites = "Setup" }).ToArray());
        var edited = await store.UpdateCaseSetAsync(Owner, saved.Id, metadata, saved.Revision);
        Assert.Equal(saved.Cases[0].Steps[0].Action, edited.Cases[0].Steps[0].Action);
        var changed = new TestCaseSetContent(edited.Name, [edited.Cases[0] with { Steps = [edited.Cases[0].Steps[0] with { Instruction = "Changed" }] }]);
        await Code("InvalidCaseSet", () => store.UpdateCaseSetAsync(Owner, saved.Id, changed, edited.Revision));
        var changedExpected = new TestCaseSetContent(edited.Name, [edited.Cases[0] with { Steps = [edited.Cases[0].Steps[0] with { Expected = "Changed" }] }]);
        await Code("InvalidCaseSet", () => store.UpdateCaseSetAsync(Owner, saved.Id, changedExpected, edited.Revision));
        var manual = changed with { Cases = [changed.Cases[0] with { Steps = [changed.Cases[0].Steps[0] with { Action = null }] }] };
        var updated = await store.UpdateCaseSetAsync(Owner, saved.Id, manual, edited.Revision);
        Assert.Null(updated.Cases[0].Steps[0].Action);
        Assert.Equal(updated.Revision, (await store.ReadCaseRevisionAsync(saved.Id, updated.Revision)).Revision);
        await Code("TestRecordNotFound", () => store.ReadCaseRevisionAsync(saved.Id, saved.Revision));
        Assert.Single(await store.ListCaseSetsAsync());
    }

    [Fact]
    public async Task TwoCooperatingWritersUseRealLeaseAndExactlyOneCasWins()
    {
        using var disk = new TestDirectory();
        var first = new ProjectTestStore(disk.User, "default");
        var second = new ProjectTestStore(disk.User, "default");
        var source = await first.PublishSourceAsync(Owner, Source());
        var saved = await first.CreateCaseSetAsync(Owner, Draft(source));
        var writes = new[] { first.UpdateCaseSetAsync(Owner, saved.Id, new("One", saved.Cases), saved.Revision), second.UpdateCaseSetAsync(Owner, saved.Id, new("Two", saved.Cases), saved.Revision) };
        try { await Task.WhenAll(writes); } catch (PluginTestingException) { }
        _ = Assert.Single(writes, t => t.IsCompletedSuccessfully);
        var failure = Assert.Single(writes, t => t.IsFaulted).Exception!.InnerException;
        Assert.Equal("RevisionConflict", Assert.IsType<PluginTestingException>(failure).ErrorCode);
        Assert.Contains((await first.ReadCaseSetAsync(saved.Id)).Name, new[] { "One", "Two" });
    }

    [Fact]
    public async Task ForeignOwnerBadTraceStructuralChangesAndMappingChangesPreserveDraft()
    {
        using var disk = new TestDirectory();
        var store = new ProjectTestStore(disk.User, "default");
        var source = await store.PublishSourceAsync(Owner, Source());
        var saved = await store.CreateCaseSetAsync(Owner, Draft(source));
        var before = await File.ReadAllBytesAsync(CasePath(disk.User, saved.Id));
        await Code("InvalidProducer", () => store.UpdateCaseSetAsync(Other, saved.Id, new("Dirty", saved.Cases), saved.Revision));
        await Code("InvalidCaseSet", () => store.UpdateCaseSetAsync(Owner, saved.Id, new("Dirty", []), saved.Revision));
        await Code("InvalidCaseSet", () => store.UpdateCaseSetAsync(Owner, saved.Id, new("Dirty", [saved.Cases[0] with { Id = Id }]), saved.Revision));
        var remapped = saved.Cases[0] with { Steps = [saved.Cases[0].Steps[0] with { Action = new(TestActionKind.AssertText, "task.toast", "different") }] };
        await Code("InvalidCaseSet", () => store.UpdateCaseSetAsync(Owner, saved.Id, new("Dirty", [remapped]), saved.Revision));
        foreach (var trace in new[] { new TestTrace(source.Id, Hash, "../../x", 0), new TestTrace(source.Id, source.Revision, "missing", 0), new TestTrace(source.Id, source.Revision, "../../x", 1), new TestTrace(Id, Hash, "../../x", 0) })
        {
            var draft = Draft(source);
            await Code("InvalidCaseSet", () => store.CreateCaseSetAsync(Owner, draft with { Cases = [draft.Cases[0] with { Traces = [trace] }] }));
        }
        Assert.Equal(before, await File.ReadAllBytesAsync(CasePath(disk.User, saved.Id)));
        Assert.Equal("Set", saved.Name);
        var fork = await store.CreateCaseSetAsync(Other, Draft(source));
        Assert.Equal(Other, fork.ProducerPluginId);
        Assert.NotEqual(saved.Cases[0].Id, fork.Cases[0].Id);
    }

    [Fact]
    public async Task IndependentWireFixtureAtEncodedBoundaryIsAcceptedAndPlusOneRefused()
    {
        using var disk = new TestDirectory();
        var store = new ProjectTestStore(disk.User, "default");
        await store.PublishSourceAsync(Owner, Source());
        var path = SourcePath(disk.User, Id);
        var fixture = Encoding.UTF8.GetBytes(ValidSource);
        var boundary = new byte[4 * 1024 * 1024];
        Array.Fill(boundary, (byte)' ');
        fixture.CopyTo(boundary, 0);
        await File.WriteAllBytesAsync(path, boundary);
        Assert.Equal("Independent", (await store.ReadSourceAsync(Id)).Title);
        Assert.Equal(Sha(boundary), (await store.ReadSourceAsync(Id)).Revision);
        var tooLarge = boundary.Concat(new byte[] { 32 }).ToArray();
        await File.WriteAllBytesAsync(path, tooLarge);
        await Code("StoreLimitExceeded", () => store.ListSourcesAsync());
        Assert.Equal(tooLarge, await File.ReadAllBytesAsync(path));
    }

    [Theory]
    [InlineData("schema")]
    [InlineData("project")]
    [InlineData("unknown")]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("unicode")]
    [InlineData("utf8")]
    [InlineData("kind-name")]
    [InlineData("kind-number")]
    [InlineData("scenario")]
    [InlineData("sha")]
    [InlineData("id")]
    [InlineData("owner")]
    [InlineData("timestamp")]
    [InlineData("nested-unknown")]
    [InlineData("nested-duplicate")]
    public async Task IndependentMalformedSourcesArePreservedAndNeverListedAsDefaults(string mutation)
    {
        using var disk = new TestDirectory();
        var store = new ProjectTestStore(disk.User, "default");
        await store.PublishSourceAsync(Owner, Source());
        var path = SourcePath(disk.User, Id);
        await File.WriteAllTextAsync(path, ValidSource);
        Assert.Equal("Independent", (await store.ReadSourceAsync(Id)).Title);
        var node = JsonNode.Parse(ValidSource)!.AsObject();
        switch (mutation)
        {
            case "schema": node["schemaVersion"] = 2; break;
            case "project": node["projectId"] = Id; break;
            case "unknown": node["unexpected"] = true; break;
            case "missing": node.Remove("createdUtc"); break;
            case "kind-name": node["kind"] = "requirements"; break;
            case "kind-number": node["kind"] = 0; break;
            case "scenario": node["items"]![0]!["virtualScenario"] = "Unknown"; break;
            case "sha": node["originalSha256"] = Hash; break;
            case "id": node["id"] = "../../x"; break;
            case "owner": node["producerPluginId"] = "../../private"; break;
            case "timestamp": node["createdUtc"] = "2026-01-01T01:00:00+01:00"; break;
            case "nested-unknown": node["items"]![0]!["unexpected"] = true; break;
        }
        var text = node.ToJsonString();
        if (mutation == "duplicate") text = ValidSource.Replace("\"schemaVersion\":1", "\"schemaVersion\":1,\"schemaVersion\":1", StringComparison.Ordinal);
        if (mutation == "nested-duplicate") text = ValidSource.Replace("\"locator\":\"../../x\"", "\"locator\":\"../../x\",\"locator\":\"../../x\"", StringComparison.Ordinal);
        if (mutation == "unicode") text = ValidSource.Replace("Independent", "\\uD800", StringComparison.Ordinal);
        var bytes = mutation == "utf8" ? new byte[] { 123, 255, 125 } : Encoding.UTF8.GetBytes(text);
        await File.WriteAllBytesAsync(path, bytes);
        await Code("TestStoreCorrupt", () => store.ReadSourceAsync(Id));
        await Code("TestStoreCorrupt", () => store.PublishSourceAsync(Owner, Source()));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task ExactSourceCapsSucceedAndIndependentPlusOneInputsFailWithoutPublication()
    {
        using var disk = new TestDirectory();
        var store = new ProjectTestStore(disk.User, "default");
        var longText = new string('a', 4096);
        var items = Enumerable.Range(0, 256).Select(i => new TestSourceItem(i.ToString(), new string('t', 200), "", Enumerable.Repeat("C", 16).ToArray(), null)).ToArray();
        var valid = Source(new byte[1024 * 1024].Select(_ => (byte)'x').ToArray(), items) with { Title = new string('t', 200), DocumentId = new string('d', 200), ProviderId = new string('p', 100), ProviderVersion = new string('v', 40) };
        var saved = await store.PublishSourceAsync(Owner, valid);
        Assert.Equal(256, saved.Items.Count);
        var textBoundary = Source(items: [new(new string('l', 512), "🧪" + new string('t', 198), longText, [longText], null)]);
        Assert.Equal(longText, (await store.PublishSourceAsync(Owner, textBoundary)).Items[0].Description);
        foreach (var invalid in new[]
        {
            valid with { OriginalBytes = new byte[1024 * 1024 + 1] },
            valid with { Items = items.Append(items[0] with { Locator = "extra" }).ToArray() },
            Source() with { Title = new string('t', 201) },
            Source() with { ProviderId = new string('p', 101) },
            Source() with { ProviderVersion = new string('v', 41) },
            Source() with { DocumentId = new string('d', 201) },
            Source(items: [new("x", "T", "", Enumerable.Repeat("C", 17).ToArray(), null)]),
            Source(items: [new("x", "T", new string('a', 4097), ["C"], null)]),
            Source(items: [new(new string('l', 513), "T", "", ["C"], null)]),
            Source(items: [new("x", "T", "", [new string('a', 4097)], null)])
        }) await Code("StoreLimitExceeded", () => store.PublishSourceAsync(Owner, invalid));
        foreach (var invalid in new[]
        {
            Source() with { Kind = (TestSourceKind)99 },
            Source() with { Title = "\ud800" },
            Source() with { Title = " \t" },
            Source() with { OriginalBytes = [255] },
            Source() with { Items = [] },
            Source(items: [new("x", "T", "", [], null)]),
            Source(items: [new("x", "T", "", ["C"], (TestVirtualScenario)99)]),
            Source(items: [new("x", "T", "", ["C"], null), new("x", "T", "", ["C"], null)])
        }) await Code("InvalidTestSource", () => store.PublishSourceAsync(Owner, invalid));
        Assert.Equal(2, (await store.ListSourcesAsync()).Count);
        var encodedOverflow = Source(items: Enumerable.Range(0, 256).Select(i => new TestSourceItem(i.ToString(), "Title", longText, Enumerable.Repeat(longText, 16).ToArray(), null)).ToArray());
        await Code("StoreLimitExceeded", () => store.PublishSourceAsync(Owner, encodedOverflow));
        Assert.Equal(2, (await store.ListSourcesAsync()).Count);
    }

    [Fact]
    public async Task ExactCaseAndTraceCapsRoundTripAndPlusOneRefuses()
    {
        using var disk = new TestDirectory();
        var store = new ProjectTestStore(disk.User, "default");
        var source = await store.PublishSourceAsync(Owner, Source());
        var sample = Draft(source).Cases[0];
        var traces = Enumerable.Repeat(sample.Traces[0], 16).ToArray();
        var step = sample.Steps[0] with { Instruction = new string('i', 4096), Expected = new string('e', 4096), Traces = traces };
        var one = await store.CreateCaseSetAsync(Owner, new(new string('n', 200), [sample with { Title = new string('t', 200), Prerequisites = new string('p', 4096), Traces = traces, Steps = Enumerable.Repeat(step, 20).ToArray() }]));
        Assert.Equal(20, one.Cases[0].Steps.Count);
        var many = await store.CreateCaseSetAsync(Owner, new("Many", Enumerable.Repeat(sample, 200).ToArray()));
        Assert.Equal(200, many.Cases.Select(c => c.Id).Distinct().Count());
        foreach (var invalid in new[]
        {
            new TestCaseSetDraft("Set", Enumerable.Repeat(sample, 201).ToArray()),
            new TestCaseSetDraft("Set", [sample with { Steps = Enumerable.Repeat(step, 21).ToArray() }]),
            new TestCaseSetDraft("Set", [sample with { Traces = Enumerable.Repeat(sample.Traces[0], 17).ToArray() }]),
            new TestCaseSetDraft("Set", [sample with { Steps = [step with { Traces = Enumerable.Repeat(sample.Traces[0], 17).ToArray() }] }]),
            new TestCaseSetDraft("Set", [sample with { Steps = [step with { Instruction = new string('a', 4097) }] }]),
            new TestCaseSetDraft("Set", [sample with { Steps = [step with { Expected = new string('a', 4097) }] }]),
            new TestCaseSetDraft("Set", [sample with { Prerequisites = new string('a', 4097) }])
        }) await Code("StoreLimitExceeded", () => store.CreateCaseSetAsync(Owner, invalid));
        foreach (var action in new[] { new TestAction((TestActionKind)99, "task.title", "x"), new(TestActionKind.SetText, "arbitrary", "x"), new(TestActionKind.Click, "task.save", "x"), new(TestActionKind.AssertText, "task.title", "x") })
            await Code("InvalidCaseSet", () => store.CreateCaseSetAsync(Owner, new("Set", [sample with { Steps = [step with { Action = action }] }])));
        await Code("StoreLimitExceeded", () => store.CreateCaseSetAsync(Owner, new("Set", Enumerable.Repeat(sample with { Steps = Enumerable.Repeat(step, 20).ToArray() }, 200).ToArray())));
        Assert.Equal(2, (await store.ListCaseSetsAsync()).Count);
    }

    [Fact]
    public async Task StorageCountCapsPreserveAllPublishedRecords()
    {
        using var disk = new TestDirectory();
        var store = new ProjectTestStore(disk.User, "default");
        TestSourceSnapshot? source = null;
        for (var i = 0; i < 64; i++) source = await store.PublishSourceAsync(Owner, Source());
        Assert.Equal(64, (await store.ListSourcesAsync()).Count);
        await Code("StoreLimitExceeded", () => store.PublishSourceAsync(Owner, Source()));
        for (var i = 0; i < 128; i++) await store.CreateCaseSetAsync(Owner, Draft(source!));
        Assert.Equal(128, (await store.ListCaseSetsAsync()).Count);
        await Code("StoreLimitExceeded", () => store.CreateCaseSetAsync(Owner, Draft(source!)));
        Assert.Equal(64, (await store.ListSourcesAsync()).Count);
        Assert.Equal(128, (await store.ListCaseSetsAsync()).Count);
    }

    [Fact]
    public async Task RetainedExclusiveLeaseReturnsSafeBusyWithoutChangingExistingBytes()
    {
        using var disk = new TestDirectory();
        var store = new ProjectTestStore(disk.User, "default");
        var saved = await store.PublishSourceAsync(Owner, Source());
        var path = SourcePath(disk.User, saved.Id);
        var before = await File.ReadAllBytesAsync(path);
        using (var lease = new FileStream(Path.Combine(disk.User, ".workbench-project", "testing.lock"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            await Code("StoreBusy", () => store.PublishSourceAsync(Owner, Source()));
        Assert.Equal(before, await File.ReadAllBytesAsync(path));
        Assert.Single(await store.ListSourcesAsync());
    }

    [Fact]
    public async Task RealObstructionsLinksOrphansAndProjectMismatchArePreserved()
    {
        using var disk = new TestDirectory();
        var store = new ProjectTestStore(disk.User, "default");
        var source = await store.PublishSourceAsync(Owner, Source());
        var saved = await store.CreateCaseSetAsync(Owner, Draft(source));
        var path = CasePath(disk.User, saved.Id);
        var original = await File.ReadAllBytesAsync(path);
        var outside = Path.Combine(disk.Root, "private.json");
        await File.WriteAllTextAsync(outside, "private outside content");
        File.Delete(path);
        File.CreateSymbolicLink(path, outside);
        await Code("UnsafePath", () => store.ReadCaseSetAsync(saved.Id));
        await Code("UnsafePath", () => store.UpdateCaseSetAsync(Owner, saved.Id, new("Dirty", saved.Cases), saved.Revision));
        Assert.Equal("private outside content", await File.ReadAllTextAsync(outside));
        File.Delete(path);
        Directory.CreateDirectory(path);
        await Code("UnsafePath", () => store.ListCaseSetsAsync());
        Directory.Delete(path);
        await File.WriteAllBytesAsync(path, original);
        var orphan = Path.Combine(StoreRoot(disk.User), "sources", ".unowned.tmp");
        await File.WriteAllTextAsync(orphan, "orphan preserved");
        await Code("TestStoreCorrupt", () => store.ListSourcesAsync());
        Assert.Equal("orphan preserved", await File.ReadAllTextAsync(orphan));
        File.Delete(orphan);
        await Code("TestStoreCorrupt", () => new ProjectTestStore(disk.User, Id).ListSourcesAsync());
        var another = new ProjectTestStore(Path.Combine(disk.Root, "other"), Id);
        Assert.Empty(await another.ListSourcesAsync());
        await Code("InvalidCaseSet", () => another.CreateCaseSetAsync(Owner, Draft(source)));
        var independent = await another.PublishSourceAsync(Owner, Source() with { Title = "Other project" });
        await Code("TestRecordNotFound", () => store.ReadSourceAsync(independent.Id));
        await Code("TestRecordNotFound", () => another.ReadSourceAsync(source.Id));
        Assert.Equal("Source", (await store.ReadSourceAsync(source.Id)).Title);
        Assert.Equal("Other project", (await another.ReadSourceAsync(independent.Id)).Title);
        Assert.Equal(original, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task LeaseGateProvesCaseDraftIsCopiedBeforeSchedulingAndCancellationRetainsBytes()
    {
        using var disk = new TestDirectory();
        var store = new ProjectTestStore(disk.User, "default");
        var source = await store.PublishSourceAsync(Owner, Source());
        var draft = Draft(source);
        var steps = draft.Cases[0].Steps.ToArray();
        var cases = new[] { draft.Cases[0] with { Steps = steps } };
        var leasePath = Path.Combine(disk.User, ".workbench-project", "testing.lock");
        Task<TestCaseSetSnapshot> create;
        using (var lease = new FileStream(leasePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            create = store.CreateCaseSetAsync(Owner, new("Captured", cases));
            steps[0] = steps[0] with { Instruction = "mutated" };
            cases[0] = cases[0] with { Title = "mutated" };
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.PublishSourceAsync(Owner, Source(), canceled.Token));
            Assert.False(create.IsCompleted);
        }
        var saved = await create;
        Assert.Equal("Set title", saved.Cases[0].Steps[0].Instruction);
        Assert.Equal("Case", saved.Cases[0].Title);
        var raw = await File.ReadAllBytesAsync(CasePath(disk.User, saved.Id));
        using (var lease = new FileStream(leasePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            using var canceled = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.UpdateCaseSetAsync(Owner, saved.Id, new("Dirty", saved.Cases), saved.Revision, canceled.Token));
        }
        Assert.Equal(raw, await File.ReadAllBytesAsync(CasePath(disk.User, saved.Id)));
    }

    [Fact]
    public async Task StoreMarkerAndUnknownRootsAreNeverAdoptedOrReset()
    {
        using var disk = new TestDirectory();
        Directory.CreateDirectory(StoreRoot(disk.User));
        var orphan = Path.Combine(StoreRoot(disk.User), "valuable.json");
        await File.WriteAllTextAsync(orphan, "retained");
        var store = new ProjectTestStore(disk.User, "default");
        await Code("TestStoreCorrupt", () => store.ListSourcesAsync());
        await Code("TestStoreCorrupt", () => store.PublishSourceAsync(Owner, Source()));
        Assert.Equal("retained", await File.ReadAllTextAsync(orphan));
        Assert.False(File.Exists(Path.Combine(StoreRoot(disk.User), "store.json")));
    }

    [Fact]
    public async Task ExistingProjectRootMarkerMustMatchCapturedIdentityBeforeTestingInitialization()
    {
        using var disk = new TestDirectory();
        var metadata = Path.Combine(disk.User, ".workbench-project");
        Directory.CreateDirectory(metadata);
        var path = Path.Combine(metadata, "workspace.json");
        await File.WriteAllTextAsync(path, "{\"schemaVersion\":1,\"id\":\"11111111111111111111111111111111\",\"name\":\"Project\"}");
        var matching = new ProjectTestStore(disk.User, Id);
        Assert.Empty(await matching.ListSourcesAsync());
        var bytes = await File.ReadAllBytesAsync(path);
        var mismatching = new ProjectTestStore(disk.User, "default");
        await Code("TestStoreCorrupt", () => mismatching.ListSourcesAsync());
        await Code("TestStoreCorrupt", () => mismatching.PublishSourceAsync(Owner, Source()));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
        Assert.False(Directory.Exists(StoreRoot(disk.User)));
        Assert.False(File.Exists(Path.Combine(metadata, "testing.lock")));
    }

    [Fact]
    public async Task FreezeUsesSameLeaseAndRawBytesThenExactArchiveSurvivesCurrentEdit()
    {
        using var disk = new TestDirectory();
        var store = new ProjectTestStore(disk.User, "default");
        var source = await store.PublishSourceAsync(Owner, Source());
        var saved = await store.CreateCaseSetAsync(Owner, Draft(source));
        var bytes = await File.ReadAllBytesAsync(CasePath(disk.User, saved.Id));
        await using (var lease = await store.AcquireStoreLeaseAsync(false, default))
        {
            Assert.NotNull(lease);
            var frozen = await store.FreezeCaseRevisionLockedAsync(lease, saved.Id, saved.Revision, default);
            Assert.Equal(saved.Revision, frozen.Revision);
            Assert.Equal(source.Revision, (await store.ReadSourceLockedAsync(lease, source.Id, default)).Revision);
            await Code("RevisionConflict", () => store.FreezeCaseRevisionLockedAsync(lease, saved.Id, Hash, default));
        }
        var archive = Path.Combine(StoreRoot(disk.User), "case-revisions", saved.Id, saved.Revision + ".json");
        Assert.Equal(bytes, await File.ReadAllBytesAsync(archive));
        await store.UpdateCaseSetAsync(Owner, saved.Id, new("Later", saved.Cases), saved.Revision);
        Assert.Equal("Set", (await new ProjectTestStore(disk.User, "default").ReadCaseRevisionAsync(saved.Id, saved.Revision)).Name);
        await File.AppendAllTextAsync(archive, " ");
        await Code("TestStoreCorrupt", () => store.ReadCaseRevisionAsync(saved.Id, saved.Revision));
        Assert.Equal(bytes.Length + 1, new FileInfo(archive).Length);
    }

    [Theory]
    [InlineData("schema")]
    [InlineData("unknown")]
    [InlineData("duplicate")]
    [InlineData("unicode")]
    [InlineData("null")]
    [InlineData("trace")]
    [InlineData("action")]
    [InlineData("case-id")]
    [InlineData("step-id")]
    [InlineData("missing")]
    public async Task IndependentMalformedCaseEnvelopesNeverBecomeEditableDefaults(string mutation)
    {
        using var disk = new TestDirectory();
        var store = new ProjectTestStore(disk.User, "default");
        var source = await store.PublishSourceAsync(Owner, Source());
        var path = CasePath(disk.User, Id);
        var fixture = """
            {"schemaVersion":1,"id":"11111111111111111111111111111111","projectId":"default","producerPluginId":"com.projectworkbench.testing","updatedUtc":"2026-01-01T00:00:00Z","name":"Independent","cases":[{"id":"22222222222222222222222222222222","title":"Case","prerequisites":"","traces":[TRACE],"steps":[{"id":"33333333333333333333333333333333","instruction":"Set","expected":"Title","traces":[TRACE],"action":{"kind":"SetText","elementId":"task.title","value":"Test"}}]}]}
            """;
        var trace = "{\"snapshotId\":\"" + source.Id + "\",\"snapshotRevision\":\"" + source.Revision + "\",\"locator\":\"../../x\",\"criterionIndex\":0}";
        fixture = fixture.Replace("TRACE", trace, StringComparison.Ordinal);
        await File.WriteAllTextAsync(path, fixture);
        var valid = await store.ReadCaseSetAsync(Id);
        Assert.Equal("Independent", valid.Name);
        var boundary = Encoding.UTF8.GetBytes(fixture + new string(' ', 4 * 1024 * 1024 - Encoding.UTF8.GetByteCount(fixture)));
        await File.WriteAllBytesAsync(path, boundary);
        Assert.Equal(Sha(boundary), (await store.ReadCaseSetAsync(Id)).Revision);
        await File.WriteAllBytesAsync(path, boundary.Concat(new byte[] { 32 }).ToArray());
        await Code("StoreLimitExceeded", () => store.ReadCaseSetAsync(Id));
        var node = JsonNode.Parse(fixture)!.AsObject();
        switch (mutation)
        {
            case "schema": node["schemaVersion"] = 2; break;
            case "unknown": node["cases"]![0]!["extra"] = true; break;
            case "null": node["cases"]![0]!["steps"] = null; break;
            case "trace": node["cases"]![0]!["traces"]![0]!["snapshotRevision"] = Hash; break;
            case "action": node["cases"]![0]!["steps"]![0]!["action"]!["kind"] = 99; break;
            case "case-id": node["cases"]![0]!["id"] = "../../private"; break;
            case "step-id": node["cases"]![0]!["steps"]![0]!["id"] = "22222222222222222222222222222222"; break;
            case "missing": node.Remove("updatedUtc"); break;
        }
        var text = node.ToJsonString();
        if (mutation == "duplicate") text = fixture.Replace("\"instruction\":\"Set\"", "\"instruction\":\"Set\",\"instruction\":\"Other\"", StringComparison.Ordinal);
        if (mutation == "unicode") text = fixture.Replace("Independent", "\\uDC00", StringComparison.Ordinal);
        var bytes = Encoding.UTF8.GetBytes(text);
        await File.WriteAllBytesAsync(path, bytes);
        await Code("TestStoreCorrupt", () => store.ReadCaseSetAsync(Id));
        await Code("TestStoreCorrupt", () => store.UpdateCaseSetAsync(Owner, Id, new("Dirty", valid.Cases), Sha(bytes)));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task RealWriteFailureLeavesDirtyInputAndOriginalBytesAndNoOwnedStaging()
    {
        using var disk = new TestDirectory();
        var store = new ProjectTestStore(disk.User, "default");
        var source = await store.PublishSourceAsync(Owner, Source());
        var saved = await store.CreateCaseSetAsync(Owner, Draft(source));
        var path = CasePath(disk.User, saved.Id);
        var before = await File.ReadAllBytesAsync(path);
        var dirty = new TestCaseSetContent("Dirty unsaved model", saved.Cases);
        if (OperatingSystem.IsWindows())
        {
            using var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            await Code("StoreIoFailure", () => store.UpdateCaseSetAsync(Owner, saved.Id, dirty, saved.Revision));
        }
        else
        {
            var directory = Path.GetDirectoryName(path)!;
            var mode = File.GetUnixFileMode(directory);
            File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserExecute);
            try { await Code("StoreIoFailure", () => store.UpdateCaseSetAsync(Owner, saved.Id, dirty, saved.Revision)); }
            finally { File.SetUnixFileMode(directory, mode); }
        }
        Assert.Equal("Dirty unsaved model", dirty.Name);
        Assert.Equal(before, await File.ReadAllBytesAsync(path));
        Assert.Empty(Directory.EnumerateFiles(StoreRoot(disk.User), "*.tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task UpdateCopiesAllCallerCollectionsAtRealLeaseGateAndRefusesReordering()
    {
        using var disk = new TestDirectory();
        var store = new ProjectTestStore(disk.User, "default");
        var source = await store.PublishSourceAsync(Owner, Source());
        var draft = Draft(source);
        var saved = await store.CreateCaseSetAsync(Owner, new("Set", [draft.Cases[0], draft.Cases[0]]));
        var steps = saved.Cases[0].Steps.ToArray();
        var cases = new[] { saved.Cases[0] with { Steps = steps }, saved.Cases[1] };
        var leasePath = Path.Combine(disk.User, ".workbench-project", "testing.lock");
        Task<TestCaseSetSnapshot> update;
        using (var lease = new FileStream(leasePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            update = store.UpdateCaseSetAsync(Owner, saved.Id, new("Captured update", cases), saved.Revision);
            steps[0] = steps[0] with { Expected = "mutated", Action = null };
            cases[1] = cases[1] with { Title = "mutated" };
            Assert.False(update.IsCompleted);
        }
        var updated = await update;
        Assert.Equal("Title entered", updated.Cases[0].Steps[0].Expected);
        Assert.Equal("Case", updated.Cases[1].Title);
        await Code("InvalidCaseSet", () => store.UpdateCaseSetAsync(Owner, saved.Id, new("Reordered", updated.Cases.Reverse().ToArray()), updated.Revision));
        var changedStep = updated.Cases[0] with { Steps = [updated.Cases[0].Steps[0] with { Id = Id }] };
        await Code("InvalidCaseSet", () => store.UpdateCaseSetAsync(Owner, saved.Id, new("Changed step", [changedStep, updated.Cases[1]]), updated.Revision));
        await Code("InvalidProducer", () => store.PublishSourceAsync("../../private", Source()));
        await Code("InvalidId", () => store.ReadSourceAsync("../../private"));
    }

    private const string ValidSource = """
        {"schemaVersion":1,"id":"11111111111111111111111111111111","projectId":"default","producerPluginId":"com.projectworkbench.testing","createdUtc":"2026-01-01T00:00:00Z","kind":"Requirements","providerId":"independent","providerVersion":"1","documentId":"doc","title":"Independent","originalBytes":"e30=","originalSha256":"44136fa355b3678a1146ad16f7e8649e94fb4fc21fe77e8310c060f61caaff8a","items":[{"locator":"../../x","title":"Item","description":"","acceptanceCriteria":["Criterion"],"virtualScenario":null}]}
        """;

    internal static TestSourceDraft Source(byte[]? bytes = null, IReadOnlyList<TestSourceItem>? items = null) => new(TestSourceKind.Requirements, "builtin.requirements", "1", "doc", "Source", bytes ?? Encoding.UTF8.GetBytes("{}"), items ?? [new("../../x", "Item", "", ["Criterion"], null)]);
    internal static TestCaseSetDraft Draft(TestSourceSnapshot source)
    {
        TestTrace[] traces = [new(source.Id, source.Revision, "../../x", 0)];
        return new("Set", [new("Case", "", traces, [new("Set title", "Title entered", traces, new(TestActionKind.SetText, "task.title", "Test"))])]);
    }
    internal static string StoreRoot(string user) => Path.Combine(user, ".workbench-project", "testing");
    internal static string SourcePath(string user, string id) => Path.Combine(StoreRoot(user), "sources", id + ".json");
    internal static string CasePath(string user, string id) => Path.Combine(StoreRoot(user), "cases", id + ".json");
    internal static string Sha(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    internal static async Task Code(string expected, Func<Task> action)
    {
        var failure = await Assert.ThrowsAsync<PluginTestingException>(action);
        Assert.Equal(expected, failure.ErrorCode);
        Assert.Equal("Testing operation failed.", failure.Message);
        Assert.Null(failure.InnerException);
    }
}
