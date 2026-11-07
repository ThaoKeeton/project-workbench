using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Workbench.Sdk;
using static Workbench.App.NativeRuntimeProbe;
using static Workbench.App.NativeManagementProbe;

namespace Workbench.App;

internal static partial class TestingNativeProbe
{
    private static async Task SeedAsync(MainWindow window, RunOptions options, List<string> checks)
    {
        var installed = window.SmokeManager.Plugins.Single(p => p.Manifest?.Id == Id);
        Require(installed.Manifest is { Version: "0.1.0", MinimumHostVersion: "0.4.0", EntryAssembly: "Workbench.Testing.dll" }, "Installed independent package identity differed.");
        var page = await EnableAsync(window);
        Require(page.IsLoaded && page.XamlRoot is not null && window.WorkspaceMenu.Items.Count == 1
            && window.WorkspaceSelector.Visibility != Visibility.Visible, "Default same-window UI was not clean and attached.");
        checks.Add("independent-zip-native-page");
        Invoke(Find<Button>(page, "导入打包需求示例"));
        await WaitAsync(async () => (await Store(window).ListSourcesAsync()).Count == 1 && Find<Button>(page, "导入打包 Figma 示例").IsEnabled);
        Invoke(Find<Button>(page, "导入打包 Figma 示例"));
        await WaitAsync(async () => (await Store(window).ListSourcesAsync()).Count == 2 && Find<Button>(page, "生成测试用例").IsEnabled);
        foreach (var source in await Store(window).ListSourcesAsync())
        {
            var actual = await Store(window).ReadSourceAsync(source.Id);
            var name = source.Kind == TestSourceKind.Requirements ? "requirements.synthetic.json" : "figma.synthetic.json";
            var packagedBytes = await File.ReadAllBytesAsync(Path.Combine(installed.PackagePath, "Fixtures", name));
            Require(actual.OriginalBytes.SequenceEqual(packagedBytes), "Import did not retain actual packaged file bytes.");
        }
        checks.Add("packaged-requirements-figma-io");
        Invoke(Find<Button>(page, "生成测试用例"));
        await WaitAsync(async () => (await Store(window).ListCaseSetsAsync()).Count == 1 && Find<TextBox>(page, "测试用例集名称").IsEnabled);
        var set = await CurrentSetAsync(window, page);
        Require(set.Cases.Any(c => c.Steps.Any(s => s.Action is null)) && set.Cases.Any(c => c.Steps.All(s => s.Action is not null)), "Generation lost manual or explicit mapped cases.");
        checks.Add("traceable-manual-mapped-generation");
        SelectCase(page, set.Cases[0].Id);
        var editor = Find<TextBox>(page, "测试用例标题");
        editor.Text = "Synthetic =SUM(1,2) \"quoted\" 🧪\r\nfinal metadata";
        var acceptedTitle = editor.Text; // Actual WinUI accepted text, not an IME/line-ending assumption.
        await CommandAsync(window, "export-local-csv");
        set = await CurrentSetAsync(window, page);
        Require(set.Cases[0].Title == acceptedTitle && set.Cases[0].Steps[0].Action is not null, "Immediate native metadata input was lost or mapping was invalidated.");
        var csvPath = Path.Combine(window.SmokeRuntime.UserRoot, "data", Id, "testing-cases.csv");
        var csv = await File.ReadAllBytesAsync(csvPath);
        Require(csv.AsSpan(0, 3).SequenceEqual(new byte[] { 239, 187, 191 }) && Encoding.UTF8.GetString(csv).Contains("\"'" + acceptedTitle.Replace("\"", "\"\"") + "\"", StringComparison.Ordinal)
            && Encoding.UTF8.GetString(csv).EndsWith("\r\n", StringComparison.Ordinal), "Actual native CSV writer bytes differed.");
        checks.Add("immediate-metadata-csv-bytes");
        await WaitReadyAsync(page);
        var instruction = Find<TextBox>(page, "测试步骤说明");
        instruction.Text = "Synthetic final step input 🧪\r\nmanual after edit";
        var acceptedInstruction = instruction.Text;
        await CommandAsync(window, "export-local-csv");
        set = await CurrentSetAsync(window, page);
        Require(set.Cases[0].Steps[0].Instruction == acceptedInstruction && set.Cases[0].Steps[0].Action is null, "Native step edit did not persist or invalidate its mapping.");
        await WaitReadyAsync(page);
        Require(!Find<Button>(page, "运行选中测试用例").IsEnabled, "Manual case was exposed as an automatic pass.");
        checks.Add("immediate-step-mapping-invalidation");
        await VerifySaveFailureAsync(window, page, set, checks);
        await VerifyConflictAsync(window, page, checks);
        // A new generation uses exact imported sources. It does not restore mapping on edited steps.
        await CommandAsync(window, "generate-all");
        await WaitReadyAsync(page);
        set = await CurrentSetAsync(window, page);
        var passCase = set.Cases.Single(c => c.Steps.Any(s => s.Action is { Kind: TestActionKind.SetText, Value: not "" })
            && !c.Title.Contains("DELIBERATE", StringComparison.Ordinal) && c.Title.Contains("Create a task", StringComparison.Ordinal));
        var emptyCase = set.Cases.Single(c => c.Steps.Any(s => s.Action is { Kind: TestActionKind.SetText, Value: "" }));
        var failedCase = set.Cases.Single(c => c.Title.Contains("DELIBERATE", StringComparison.Ordinal));
        var passed = await RunCaseAsync(window, page, passCase.Id, TestRunState.Completed);
        Require(passed.Events.Count(e => e.Kind == TestRunEventKind.StepFinished && e.Outcome == TestStepOutcome.Passed) == passCase.Steps.Count,
            "Completed run did not pass every planned step.");
        var observed = passed.Events.First(e => e.Kind == TestRunEventKind.StepFinished);
        Require(observed.NativePlainTextState is not null && observed.NativePlainTextState.Contains("🧪", StringComparison.Ordinal)
            && observed.NativePlainTextState.Contains('\n') && observed.NativePlainTextState != passCase.Steps[0].Action!.Value,
            "Committed actual observed state was replaced by planned action input or truncated.");
        var frame = await Store(window).ReadRunEvidenceAsync(passed.Plan.Id, observed.EvidenceIds.Single());
        VerifyBmp(frame);
        await ShowFrameAsync(page, passed.Plan.Id, observed.Sequence, observed.NativePlainTextState!, frame.Receipt.Sha256);
        checks.Add("positive-actual-journal-bmp-native-decoder");
        var empty = await RunCaseAsync(window, page, emptyCase.Id, TestRunState.Completed);
        Require(empty.Events.Any(e => e.NativePlainTextState?.Contains("Title required", StringComparison.Ordinal) == true), "Negative input was not actually rejected by the virtual form.");
        checks.Add("negative-empty-title-run");
        var failed = await RunCaseAsync(window, page, failedCase.Id, TestRunState.Failed);
        Require(failed.Events.Any(e => e.Kind == TestRunEventKind.StepFinished && e.Outcome == TestStepOutcome.Failed && e.ErrorCode == "AssertionFailed")
            && failed.Events.All(e => e.Kind != TestRunEventKind.Completed), "Deliberate failure was reported as passed.");
        await VerifyHistorySelectionAsync(window, page, passed, failed);
        checks.Add("labeled-deliberate-assertion-failure");
        await VerifyCancelAsync(window, page, checks);
        var secondary = await VerifyIsolationAsync(window, options, set.Id, checks);
        page = await PageAsync(window);
        var sets = Find<ComboBox>(page, "测试用例集");
        sets.SelectedItem = sets.Items.OfType<ComboBoxItem>().Single(i => (string)i.Tag == set.Id);
        await WaitReadyAsync(page);
        // Seed AFTER all context switches so the separate process initializer is the first new owner.
        // This public Core journal setup performs NO target action and records NO result.
        var seed = await Store(window).CreateRunAsync(Id, set.Id, set.Revision, [passCase.Id]);
        seed = await Store(window).AppendRunEventAsync(Id, seed.Plan.Id, seed.Sequence, seed.HeadHash, new(TestRunEventKind.RunStarted));
        seed = await Store(window).AppendRunEventAsync(Id, seed.Plan.Id, seed.Sequence, seed.HeadHash, new(TestRunEventKind.StepStarted, passCase.Id, passCase.Steps[0].Id));
        Require(seed.State == TestRunState.Running && seed.Events.All(e => e.Kind != TestRunEventKind.StepFinished), "Synthetic interruption seed must contain no invented action/result.");
        checks.Add("synthetic-inflight-journal-seed-no-action");
        await WriteNewAsync(StatePath(options.UserRoot), System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new ProbeState(1, Environment.ProcessId,
            set.Id, passed.Plan.Id, observed.Sequence, observed.EvidenceIds.Single(), observed.NativePlainTextState!, frame.Receipt.Sha256,
            seed.Plan.Id, seed.Plan.CaseRevision, secondary, ""), Json));
    }
    private static async Task VerifySaveFailureAsync(MainWindow window, FrameworkElement page, TestCaseSetSnapshot set, List<string> checks)
    {
        await WaitReadyAsync(page);
        var path = Path.Combine(window.SmokeRuntime.UserRoot, ".workbench-project", "testing", "cases", set.Id + ".json");
        var original = await File.ReadAllBytesAsync(path);
        var manager = window.SmokeManager;
        var generation = window.Workspace.RuntimeGeneration;
        var editor = Find<TextBox>(page, "测试用例集名称");
        string accepted;
        using (var obstruction = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            editor.Text = "Synthetic retained dirty buffer obstructed by actual Windows file sharing"; accepted = editor.Text;
            await WaitAsync(() => Task.FromResult(Find<TextBlock>(page, "测试自动保存状态").Text.Contains("StoreIoFailure", StringComparison.Ordinal)));
            Require(!await window.SmokeCloseAttemptAsync(), "Actual close discarded an unsaved obstructed draft.");
            var obstructedBytes = await File.ReadAllBytesAsync(path);
            Require(ReferenceEquals(manager, window.SmokeManager) && generation == window.Workspace.RuntimeGeneration && page.IsLoaded
                && editor.IsEnabled && editor.Text == accepted && original.SequenceEqual(obstructedBytes),
                "Failed close detached/replaced the current page, lost accepted input or changed obstructed bytes.");
        }
        editor.Text = accepted + " corrected";
        var corrected = editor.Text;
        await CommandAsync(window, "export-local-csv");
        Require((await Store(window).ReadCaseSetAsync(set.Id)).Name == corrected, "Same native editor correction did not reach storage.");
        checks.Add("actual-file-share-save-failure-close-correction");
    }
    private static async Task VerifyConflictAsync(MainWindow window, FrameworkElement page, List<string> checks)
    {
        await WaitReadyAsync(page);
        var set = await CurrentSetAsync(window, page);
        var external = await Store(window).UpdateCaseSetAsync(Id, set.Id, new("Synthetic external revision", set.Cases), set.Revision);
        var path = Path.Combine(window.SmokeRuntime.UserRoot, ".workbench-project", "testing", "cases", set.Id + ".json");
        var externalBytes = await File.ReadAllBytesAsync(path);
        var editor = Find<TextBox>(page, "测试用例集名称");
        editor.Text = "Synthetic exact dirty conflict draft 🧪"; var dirty = editor.Text;
        await WaitAsync(() => Task.FromResult(Find<TextBlock>(page, "测试自动保存状态").Text.Contains("RevisionConflict", StringComparison.Ordinal)));
        var afterConflict = await File.ReadAllBytesAsync(path);
        Require(editor.Text == dirty && externalBytes.SequenceEqual(afterConflict), "Conflict discarded dirty input or overwrote external bytes.");
        var count = (await Store(window).ListCaseSetsAsync()).Count;
        Invoke(Find<Button>(page, "保留测试草稿并重新加载"));
        await WaitAsync(async () => (await Store(window).ListCaseSetsAsync()).Count == count + 1 && editor.Text == external.Name && editor.IsEnabled);
        var preserved = (await Store(window).ListCaseSetsAsync()).Single(s => s.Name == dirty);
        var afterRecovery = await File.ReadAllBytesAsync(path);
        Require((await Store(window).ReadCaseSetAsync(preserved.Id)).Name == dirty && externalBytes.SequenceEqual(afterRecovery), "Conditional copy-as-new changed the original or failed to preserve exact input.");
        checks.Add("conflict-preserve-copy-reload-bytes");
    }
}
