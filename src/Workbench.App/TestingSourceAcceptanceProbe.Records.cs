using System.Text;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Workbench.Core.Projects;
using Workbench.Core.Testing;
using Workbench.Sdk;
using static Workbench.App.NativeRuntimeProbe;

namespace Workbench.App;

internal static partial class TestingSourceAcceptanceProbe
{
    private const string FigmaJson = """
        {"name":"Synthetic source acceptance file","version":"source-v1","nodes":{"2:3":{"document":{"id":"2:3","name":"Synthetic heading","type":"TEXT","characters":"Synthetic heading text"}},"1:2":{"document":{"id":"1:2","name":"Synthetic confirmation","type":"TEXT","characters":"Synthetic confirmation text"}}}}
        """;
    private sealed record Inventory(IReadOnlyList<TestSourceSummary> Sources, IReadOnlyList<TestCaseSetSummary> Cases, IReadOnlyList<TestRunSummary> Runs);
    private static async Task<Inventory> InventoryAsync(ProjectTestStore store) => new(await ReadAsync(ct => store.ListSourcesAsync(ct)),
        await ReadAsync(ct => store.ListCaseSetsAsync(ct)), await ReadAsync(ct => store.ListRunsAsync(ct)));
    private static async Task<TestSourceSnapshot> NewSourceAsync(Session s, Inventory before)
    {
        var after = await InventoryAsync(s.Store);
        Require(after.Sources.Count == before.Sources.Count + 1 && after.Runs.Count == 0, "SourcePublicationCountInvalid");
        var added = after.Sources.Single(source => before.Sources.All(old => old.Id != source.Id));
        return await ReadAsync(ct => s.Store.ReadSourceAsync(added.Id, ct));
    }
    private static async Task AssertFigmaAsync(Session s, TestSourceSnapshot source)
    {
        Require(source.ProjectId == s.ProjectId && source.ProducerPluginId == Id && source.Kind == TestSourceKind.Figma
            && source.ProviderId == "figma.rest.nodes.manual-criteria" && source.ProviderVersion == "1" && source.DocumentId == "SourceAcceptanceFile1@source-v1"
            && source.Title == "Synthetic source acceptance file" && source.OriginalBytes.AsSpan().SequenceEqual(Encoding.UTF8.GetBytes(FigmaJson))
            && source.OriginalSha256 == Hash(Encoding.UTF8.GetBytes(FigmaJson)) && source.Items.Count == 2, "SourceFigmaRawBytesInvalid");
        Require(source.Items[0] == source.Items[0] with { Locator = "2:3", Title = "Synthetic heading", Description = "Observed type: TEXT\nObserved characters: Synthetic heading text", VirtualScenario = null }
            && source.Items[0].AcceptanceCriteria.SequenceEqual(new[] { "Synthetic heading is visible.", "Synthetic navigation is reviewed manually." })
            && source.Items[1] == source.Items[1] with { Locator = "1:2", Title = "Synthetic confirmation", Description = "Observed type: TEXT\nObserved characters: Synthetic confirmation text", VirtualScenario = null }
            && source.Items[1].AcceptanceCriteria.SequenceEqual(new[] { LastCriterion }), "SourceFigmaCriteriaInvalid");
        Require(await HashFileAsync(StoreFile(s.Root, "sources", source.Id), 2 * 1024 * 1024) == source.Revision, "SourceFigmaRevisionInvalid");
    }
    private static string ExpectedInput(TestSourceSnapshot figma) => $$"""
        {"sources":[{"snapshotId":"{{figma.Id}}","snapshotRevision":"{{figma.Revision}}","kind":"Figma","providerId":"figma.rest.nodes.manual-criteria","providerVersion":"1","documentId":"SourceAcceptanceFile1@source-v1","title":"Synthetic source acceptance file","items":[{"locator":"2:3","title":"Synthetic heading","description":"Observed type: TEXT\nObserved characters: Synthetic heading text","criteria":[{"criterionIndex":0,"text":"Synthetic heading is visible."},{"criterionIndex":1,"text":"Synthetic navigation is reviewed manually."}]},{"locator":"1:2","title":"Synthetic confirmation","description":"Observed type: TEXT\nObserved characters: Synthetic confirmation text","criteria":[{"criterionIndex":2,"text":"Synthetic confirmation is reviewed manually."}]}]}]}
        """;
    private static byte[] ExpectedResponse(bool partial)
    {
        // Independent fixed oracle; no expected body, trace or ID is taken from fixture state/receipt response.
        var prerequisites = partial ? new string('p', 4096) : "Synthetic manual review only.";
        var cases = new[] { 2, 0, 1 }.Select(index => "{\"criterionIndex\":" + index + ",\"title\":\"Synthetic manual case " + index
            + "\",\"prerequisites\":\"" + prerequisites + "\",\"steps\":[{\"instruction\":\"Review synthetic criterion " + index
            + " manually.\",\"expected\":\"Synthetic criterion " + index + " is satisfied.\"}]}");
        var generated = "{\"cases\":[" + string.Join(',', cases) + "]}";
        return JsonSerializer.SerializeToUtf8Bytes(new { id = "resp_source_acceptance_1", model = Model, status = "completed",
            output = new[] { new { type = "message", role = "assistant", status = "completed", content = new[] { new { type = "output_text", text = generated } } } } });
    }
    private static TestTrace[] OriginalTraces(TestSourceSnapshot figma) => [new(figma.Id, figma.Revision, "2:3", 0),
        new(figma.Id, figma.Revision, "2:3", 1), new(figma.Id, figma.Revision, "1:2", 0)];
    private static async Task AssertReceiptAsync(Session s, TestSourceSnapshot receipt, TestSourceSnapshot figma, bool partial)
    {
        Require(receipt.ProjectId == s.ProjectId && receipt.ProducerPluginId == Id && receipt.Kind == TestSourceKind.Requirements
            && receipt.ProviderId == "openai.responses.test-cases" && receipt.ProviderVersion == "1" && receipt.DocumentId == "resp_source_acceptance_1"
            && receipt.Title == "AI generation receipt (manual review required)" && receipt.OriginalSha256 == Hash(receipt.OriginalBytes), "SourceReceiptIdentityInvalid");
        using var document = JsonDocument.Parse(receipt.OriginalBytes, new() { MaxDepth = 16 });
        var root = document.RootElement;
        Require(root.GetProperty("schemaVersion").GetInt32() == 1 && root.GetProperty("requestedModel").GetString() == Model
            && root.GetProperty("observedModel").GetString() == Model && root.GetProperty("responseId").GetString() == "resp_source_acceptance_1", "SourceReceiptModelInvalid");
        var input = root.GetProperty("inputUtf8Base64").GetBytesFromBase64(); var response = root.GetProperty("responseUtf8Base64").GetBytesFromBase64();
        Require(input.AsSpan().SequenceEqual(Encoding.UTF8.GetBytes(ExpectedInput(figma))) && response.AsSpan().SequenceEqual(ExpectedResponse(partial))
            && root.GetProperty("inputSha256").GetString() == Hash(input) && root.GetProperty("responseSha256").GetString() == Hash(response), "SourceReceiptRawBytesInvalid");
        var traces = root.GetProperty("originalTraces").Deserialize<TestTrace[]>(Json);
        Require(traces is not null && traces.SequenceEqual(OriginalTraces(figma)) && receipt.Items.Count == 3, "SourceReceiptOriginalTracesInvalid");
        var indexes = new[] { 2, 0, 1 };
        for (var i = 0; i < 3; i++)
        {
            var item = receipt.Items[i]; var index = indexes[i];
            Require(item.Locator == "case-" + (i + 1) && item.Title == "Synthetic manual case " + index && item.Description == "AI generated; manual review required."
                && item.VirtualScenario is null && item.AcceptanceCriteria.SequenceEqual(new[] { "Synthetic criterion " + index + " is satisfied." }), "SourceReceiptManualItemsInvalid");
        }
        Require(await HashFileAsync(StoreFile(s.Root, "sources", receipt.Id), 2 * 1024 * 1024) == receipt.Revision, "SourceReceiptRevisionInvalid");
    }
    private static async Task AssertManualAsync(Session s, FrameworkElement page, TestSourceSnapshot figma, TestSourceSnapshot receipt, string setId)
    {
        var set = await ReadAsync(ct => s.Store.ReadCaseSetAsync(setId, ct));
        Require(set.Id == setId && set.ProjectId == s.ProjectId && set.ProducerPluginId == Id && set.Name == "AI 测试用例（待人工审核）" && set.Cases.Count == 3
            && await HashFileAsync(StoreFile(s.Root, "cases", set.Id), 2 * 1024 * 1024) == set.Revision, "SourceManualSetInvalid");
        var selector = Find<ComboBox>(page, "测试用例集");
        selector.SelectedItem = selector.Items.OfType<ComboBoxItem>().Single(item => (string)item.Tag == set.Id);
        await PollAsync(() => Task.FromResult(SelectedSet(page) == set.Id && Find<ListView>(page, "测试用例列表").Items.Count == 3));
        var indexes = new[] { 2, 0, 1 }; var original = OriginalTraces(figma);
        for (var i = 0; i < 3; i++)
        {
            var item = set.Cases[i]; var index = indexes[i];
            TestTrace[] expected = [original[index], new(receipt.Id, receipt.Revision, "case-" + (i + 1), 0)];
            Require(item.Title == "Synthetic manual case " + index && item.Prerequisites == "AI generated; manual review required. Model: " + Model + ".\nSynthetic manual review only."
                && item.Traces.SequenceEqual(expected) && item.Steps.Count == 1 && item.Steps[0].Action is null
                && item.Steps[0].Traces.SequenceEqual(expected) && item.Steps[0].Instruction == "Review synthetic criterion " + index + " manually."
                && item.Steps[0].Expected == "Synthetic criterion " + index + " is satisfied.", "SourceManualCaseOrTracesInvalid");
            var list = Find<ListView>(page, "测试用例列表"); list.SelectedItem = list.Items.OfType<ListViewItem>().Single(row => (string)row.Tag == item.Id);
            await PollAsync(() => Task.FromResult(Find<TextBox>(page, "测试用例标题").Text == item.Title));
            var mapping = Find<TextBlock>(page, "测试映射与追溯").Text;
            Require(mapping.StartsWith("手动：不会自动通过", StringComparison.Ordinal) && expected.All(trace => mapping.Contains($"来源 {trace.SnapshotId}@{trace.SnapshotRevision} · 定位 {trace.Locator} · 条件 {trace.CriterionIndex}", StringComparison.Ordinal))
                && !Find<Button>(page, "运行选中测试用例").IsEnabled, "SourceManualUiBoundaryInvalid");
        }
        Require((await ReadAsync(ct => s.Store.ListRunsAsync(ct))).Count == 0 && s.Window.SmokeManager.GetBackgroundTaskSnapshots(Id).All(task => task.State is not (PluginBackgroundTaskState.Running or PluginBackgroundTaskState.Stopping)), "SourceManualCaseStartedAutomation");
    }
    private static string StoreFile(string root, string area, string id)
    {
        Require(Hex(id, 32) && area is "sources" or "cases", "SourceEvidenceIdInvalid");
        return Path.Combine(root, ".workbench-project", "testing", area, id + ".json");
    }
    private static async Task<Evidence[]> FilesAsync(string root, string projectId, Inventory inventory)
    {
        var files = new List<Evidence>();
        Require(inventory.Sources.Count <= 64 && inventory.Cases.Count <= 128 && inventory.Runs.Count == 0, "SourceRecordInventoryInvalid");
        foreach (var (area, id, revision) in inventory.Sources.Select(source => ("sources", source.Id, source.Revision))
            .Concat(inventory.Cases.Select(set => ("cases", set.Id, set.Revision))).OrderBy(row => row.Item1 + "/" + row.Item2, StringComparer.Ordinal))
        {
            var bytes = await ReadBytesAsync(StoreFile(root, area, id), 2 * 1024 * 1024);
            Require(Hash(bytes) == revision, "SourceEnvelopeHashChanged");
            foreach (var secret in Secrets) Require(!Encoding.UTF8.GetString(bytes).Contains(secret.Value, StringComparison.Ordinal), "SourceCredentialInEvidence");
            files.Add(new(projectId, area, id, projectId + "/" + area + "/" + id + ".json", bytes.Length, Hash(bytes)));
        }
        return files.ToArray();
    }
    private static async Task AssertPreservedAsync(string root, Evidence[] files)
    {
        foreach (var file in files) Require(await HashFileAsync(StoreFile(root, file.Area, file.Id), 2 * 1024 * 1024) == file.Sha256, "SourcePriorBytesChanged");
    }
    private static async Task<Evidence[]> ExportEvidenceAsync(Session s)
    {
        var output = Path.Combine(Path.GetDirectoryName(s.Options.ReportPath!)!, s.Options.SourceAcceptanceProbe + "-evidence");
        HostRuntime.RejectLinks(output); Require(!Directory.Exists(output) && !File.Exists(output), "SourceEvidenceAlreadyExists");
        var all = new List<Evidence>();
        var catalog = new ProjectWorkspaceCatalog(s.Options.UserRoot);
        foreach (var project in s.Window.Workspace.Catalog!.Projects.OrderBy(project => project.Id, StringComparer.Ordinal))
        {
            var root = catalog.GetProjectRoot(project.Id); var store = new ProjectTestStore(root, project.Id);
            var files = await FilesAsync(root, project.Id, await InventoryAsync(store));
            foreach (var file in files)
            {
                var bytes = await ReadBytesAsync(StoreFile(root, file.Area, file.Id), 2 * 1024 * 1024);
                Require(bytes.Length == file.Length && Hash(bytes) == file.Sha256, "SourceEvidenceChangedDuringCopy");
                await WriteNewAsync(Path.Combine(output, file.Path.Replace('/', Path.DirectorySeparatorChar)), bytes); all.Add(file);
            }
        }
        return all.ToArray();
    }
}
