using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Workbench.Core;
using Workbench.Core.Projects;
using Workbench.Sdk;
using static Workbench.App.NativeRuntimeProbe;
using static Workbench.App.NativeManagementProbe;

namespace Workbench.App;

internal static partial class TestingNativeProbe
{
    private static async Task<TestRunSnapshot> RunCaseAsync(MainWindow window, FrameworkElement page, string caseId, TestRunState state)
    {
        await WaitReadyAsync(page); SelectCase(page, caseId);
        var before = (await Store(window).ListRunsAsync()).Select(r => r.Id).ToHashSet(StringComparer.Ordinal);
        await WaitAsync(() => Task.FromResult(Find<Button>(page, "运行选中测试用例").IsEnabled));
        Invoke(Find<Button>(page, "运行选中测试用例"));
        string? id = null;
        await WaitAsync(async () => { id = (await Store(window).ListRunsAsync()).SingleOrDefault(r => !before.Contains(r.Id))?.Id; return id is not null; });
        var run = await WaitRunAsync(window, id!, state);
        await WaitReadyAsync(page); return run;
    }
    private static async Task ShowFrameAsync(FrameworkElement page, string runId, long sequence, string text, string frameHash)
    {
        await WaitAsync(() => Task.FromResult(Find<ComboBox>(page, "测试执行列表").Items.OfType<ComboBoxItem>().Any(i => (string)i.Tag == runId)));
        SelectRun(page, runId);
        await WaitAsync(() => Task.FromResult(Find<TextBlock>(page, "测试执行详情").Text.Contains(runId, StringComparison.Ordinal)
            && Find<ComboBox>(page, "测试执行事件").IsEnabled
            && Find<ComboBox>(page, "测试执行事件").Items.OfType<ComboBoxItem>().Any(i => i.Tag is KeyValuePair<string, long> key && key.Key == runId && key.Value == sequence)));
        var events = Find<ComboBox>(page, "测试执行事件");
        Require(events.IsEnabled, "Native history event selection did not reopen after the actual run selection settled.");
        events.SelectedItem = events.Items.OfType<ComboBoxItem>().Single(i => i.Tag is KeyValuePair<string, long> key && key.Key == runId && key.Value == sequence);
        await WaitAsync(() => Task.FromResult(Find<TextBlock>(page, "测试实际观察文本").Text == text
            && Find<Image>(page, "SIMULATED 测试帧").Source is BitmapImage { PixelWidth: 320, PixelHeight: 180 }
            && Find<TextBlock>(page, "测试帧状态").Text.Contains("已解码", StringComparison.Ordinal)
            && Find<TextBlock>(page, "测试帧状态").Text.Contains(frameHash, StringComparison.Ordinal)));
    }
    private static async Task VerifyHistorySelectionAsync(MainWindow window, FrameworkElement page, TestRunSnapshot a, TestRunSnapshot b)
    {
        var aFirst = a.Events.First(e => e.Kind == TestRunEventKind.StepFinished);
        var aLast = a.Events.Last(e => e.Kind == TestRunEventKind.StepFinished);
        var bSame = b.Events.Single(e => e.Sequence == aFirst.Sequence);
        Require(bSame.Kind == TestRunEventKind.StepFinished && bSame.NativePlainTextState != aFirst.NativePlainTextState
            && b.Events.All(e => e.Sequence != aLast.Sequence), "Native history assertion requires distinct A/B observations and an A-only event sequence.");
        foreach (var selected in new[] { (Run: a, Event: aLast), (Run: b, Event: bSame), (Run: a, Event: aFirst), (Run: a, Event: aLast) })
        {
            var evidence = await Store(window).ReadRunEvidenceAsync(selected.Run.Plan.Id, selected.Event.EvidenceIds.Single());
            await ShowFrameAsync(page, selected.Run.Plan.Id, selected.Event.Sequence, selected.Event.NativePlainTextState!, evidence.Receipt.Sha256);
            var key = (KeyValuePair<string, long>)((ComboBoxItem)Find<ComboBox>(page, "测试执行事件").SelectedItem).Tag;
            Require(key.Key == selected.Run.Plan.Id && key.Value == selected.Event.Sequence
                && Find<TextBlock>(page, "测试执行详情").Text.Contains(selected.Run.Plan.Id, StringComparison.Ordinal),
                "Native displayed run/event identity did not match exact observed text and decoded receipt.");
        }
        await VerifySameEventEvidenceRetryAsync(window, page, b, bSame);
    }
    private static async Task VerifySameEventEvidenceRetryAsync(MainWindow window, FrameworkElement page, TestRunSnapshot run, TestRunEvent selected)
    {
        var previous = run.Events.Last(e => e.Kind == TestRunEventKind.StepFinished);
        Require(previous.Sequence != selected.Sequence, "Evidence retry assertion needs a different initially loaded event.");
        var previousFrame = await Store(window).ReadRunEvidenceAsync(run.Plan.Id, previous.EvidenceIds.Single());
        var expected = await Store(window).ReadRunEvidenceAsync(run.Plan.Id, selected.EvidenceIds.Single());
        await ShowFrameAsync(page, run.Plan.Id, previous.Sequence, previous.NativePlainTextState!, previousFrame.Receipt.Sha256);
        var identity = new KeyValuePair<string, long>(run.Plan.Id, selected.Sequence);
        var framePath = Path.Combine(window.SmokeRuntime.UserRoot, ".workbench-project", "testing", "runs", run.Plan.Id, "evidence", selected.EvidenceIds.Single(), "frame.bmp");
        using (var obstruction = new FileStream(framePath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var events = Find<ComboBox>(page, "测试执行事件");
            events.SelectedItem = events.Items.OfType<ComboBoxItem>().Single(i => i.Tag is KeyValuePair<string, long> key && key.Equals(identity));
            await WaitAsync(() => Task.FromResult(Find<TextBlock>(page, "测试帧状态").Text.Contains("证据加载失败", StringComparison.Ordinal)
                && Find<TextBlock>(page, "测试帧状态").Text.Contains("StoreIoFailure", StringComparison.Ordinal)));
            Require(Find<Image>(page, "SIMULATED 测试帧").Source is null
                && Find<TextBlock>(page, "测试实际观察文本").Text == selected.NativePlainTextState,
                "A refused evidence read fabricated a frame or changed its committed observed text.");
        }
        await CommandAsync(window, "refresh"); // Existing useful command: retry the SAME failed selection after releasing real IO obstruction.
        await WaitAsync(() => Task.FromResult(Find<Image>(page, "SIMULATED 测试帧").Source is BitmapImage { PixelWidth: 320, PixelHeight: 180 }
            && Find<TextBlock>(page, "测试帧状态").Text.Contains(expected.Receipt.Sha256, StringComparison.Ordinal)));
        var refreshed = (KeyValuePair<string, long>)((ComboBoxItem)Find<ComboBox>(page, "测试执行事件").SelectedItem).Tag;
        Require(refreshed.Equals(identity) && Find<TextBlock>(page, "测试实际观察文本").Text == selected.NativePlainTextState,
            "Evidence recovery required a different event or lost its run/sequence/text association.");
    }
    private static void VerifyBmp(TestEvidence evidence)
    {
        var b = evidence.Bytes;
        Require(evidence.Receipt.IsSimulated && evidence.Receipt.MimeType == "image/bmp" && b.Length == 172854
            && b[0] == 'B' && b[1] == 'M' && BitConverter.ToInt32(b, 2) == b.Length && BitConverter.ToInt32(b, 10) == 54
            && BitConverter.ToInt32(b, 18) == 320 && BitConverter.ToInt32(b, 22) == 180
            && BitConverter.ToInt16(b, 28) == 24 && BitConverter.ToInt32(b, 30) == 0
            && Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(b)).ToLowerInvariant() == evidence.Receipt.Sha256,
            "Actual persisted SIMULATED BMP/receipt bytes differed.");
    }
    private static async Task VerifyCancelAsync(MainWindow window, FrameworkElement page, List<string> checks)
    {
        await CommandAsync(window, "queue-mapped");
        var queued = (await Store(window).ListRunsAsync()).Single(r => r.State == TestRunState.Queued);
        var leasePath = Path.Combine(window.SmokeRuntime.UserRoot, ".workbench-project", "testing.lock");
        Task<PluginCommandResult> cancel;
        using (var actualLease = new FileStream(leasePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            await CommandAsync(window, "start-queued");
            // Start owns the real delegate; it is waiting on actual store IO, before the first target action.
            var cancelButton = Find<Button>(page, "取消测试执行");
            Require(cancelButton.IsEnabled, "Native Cancel was queued behind or disabled by actual Run work.");
            Invoke(cancelButton);
            // The useful registered command joins the same cancellation as the real native button.
            cancel = window.SmokeManager.ExecuteCommandAsync(Id, "cancel-run");
            Require(!cancel.IsCompleted, "Cancel did not retain actual outstanding store work.");
            try { using var duplicate = HostRuntime.AcquireLease(window.SmokeRuntime.UserRoot); throw new InvalidOperationException("Cancellation released the live root."); }
            catch (UserStoreInUseException) { }
        }
        Require((await cancel).Succeeded, "Actual cancel command did not settle.");
        var cancelled = await WaitRunAsync(window, queued.Id, TestRunState.Cancelled);
        Require(cancelled.Events.Any(e => e.Kind == TestRunEventKind.CancelRequested) && cancelled.Events.All(e => e.Kind != TestRunEventKind.StepStarted),
            "Cancel-before-first-step store IO was misreported or fabricated target work.");
        await WaitReadyAsync(page);
        checks.Add("cancel-real-owned-store-io-before-first-step");
    }
    private static async Task<string> VerifyIsolationAsync(MainWindow window, RunOptions options, string setId, List<string> checks)
    {
        // Actual native New dialog and project selector; every root stays under this dedicated GUID.
        var pending = window.WorkspaceNewDialogAsync();
        await WaitAsync(() => Task.FromResult(window.WorkspaceDialog is { IsLoaded: true } d && Descendants<Button>(d).Any()));
        var dialog = window.WorkspaceDialog!;
        Find<TextBox>((DependencyObject)dialog.Content, "项目名称").Text = "Synthetic testing isolated B";
        Invoke(Descendants<Button>(dialog).Single(b => Equals(b.Content, "创建并打开")));
        await pending.WaitAsync(TimeSpan.FromSeconds(40));
        Require(window.Workspace.CanUseCurrentRuntime && window.Workspace.ActiveProject?.Id != "default", "Native New did not open isolated B.");
        var id = window.Workspace.ActiveProject!.Id;
        Require((await Store(window).ListSourcesAsync()).Count == 0 && (await Store(window).ListCaseSetsAsync()).Count == 0
            && (await Store(window).ListRunsAsync()).Count == 0, "Project B inherited testing records.");
        await window.WorkspaceActionAsync(async runtime =>
        {
            Require((await runtime.Packages.InstallAsync(PackageZip)).Succeeded, "B independent package install failed.");
            await runtime.Manager.DiscoverAsync();
        });
        await EnableAsync(window);
        Require((await Store(window).ListSourcesAsync()).Count == 0 && !Directory.Exists(Path.Combine(window.SmokeRuntime.UserRoot, ".workbench-project", "testing")),
            "Missing-store reads created/copied testing data.");
        var selector = window.WorkspaceSelector;
        selector.SelectedItem = selector.Items.OfType<ComboBoxItem>().Single(i => (string)i.Tag == "default");
        await WaitAsync(() => Task.FromResult(!window.SmokeActionBusy && window.Workspace.CanUseCurrentRuntime && window.Workspace.ActiveProject?.Id == "default"));
        Require((await Store(window).ReadCaseSetAsync(setId)).Id == setId, "Switch back lost A records.");
        checks.Add("native-project-isolation-empty-read");
        return id;
    }
    private static async Task VerifyAsync(MainWindow window, RunOptions options, List<string> checks)
    {
        var state = System.Text.Json.JsonSerializer.Deserialize<ProbeState>(await File.ReadAllBytesAsync(StatePath(options.UserRoot)), Json)
            ?? throw new InvalidOperationException("Testing seed identity is missing.");
        Require(state.SchemaVersion == 1 && state.ProcessId != Environment.ProcessId, "Verify was not a separate actual process.");
        var page = await PageAsync(window); // Enabled intent restores actual independent plugin/context before this page.
        var interrupted = await Store(window).ReadRunAsync(state.InterruptedSeedId);
        Require(interrupted.State == TestRunState.Interrupted && interrupted.Events.Last().Kind == TestRunEventKind.Interrupted
            && interrupted.Events.All(e => e.Kind != TestRunEventKind.StepFinished), "New actual context did not mark the labeled synthetic seed Interrupted.");
        checks.Add("separate-process-context-interrupted-synthetic-seed");
        var set = await Store(window).ReadCaseSetAsync(state.SetId);
        Require(set.Name == state.ImmediateLeaveText, "Normal previous Closed lost the last native accepted input.");
        checks.Add("restart-final-native-input-persisted");
        var original = await Store(window).ReadRunAsync(state.PassedRunId);
        var record = original.Events.Single(e => e.Sequence == state.ObservedSequence);
        var frame = await Store(window).ReadRunEvidenceAsync(state.PassedRunId, state.EvidenceId);
        Require(record.NativePlainTextState == state.ObservedText && frame.Receipt.Sha256 == state.FrameSha256, "Reopened real journal/evidence changed exact text or frame bytes.");
        VerifyBmp(frame);
        await CommandAsync(window, "refresh");
        await ShowFrameAsync(page, state.PassedRunId, state.ObservedSequence, state.ObservedText, state.FrameSha256);
        checks.Add("reopened-exact-observed-text-bmp-native-decoder");
        SelectRun(page, state.InterruptedSeedId);
        await WaitAsync(() => Task.FromResult(Find<Button>(page, "重试测试执行").IsEnabled));
        Invoke(Find<Button>(page, "重试测试执行"));
        string? retryId = null;
        await WaitAsync(async () => { retryId = (await Store(window).ListRunsAsync()).SingleOrDefault(r => r.ParentRunId == state.InterruptedSeedId)?.Id; return retryId is not null; });
        var retry = await WaitRunAsync(window, retryId!, TestRunState.Completed);
        Require(retry.Plan.Id != state.InterruptedSeedId && retry.Plan.SessionId != interrupted.Plan.SessionId
            && retry.Plan.CaseRevision == state.FrozenRevision
            && System.Text.Json.JsonSerializer.Serialize(retry.Plan.Cases) == System.Text.Json.JsonSerializer.Serialize(interrupted.Plan.Cases)
            && retry.Events.First(e => e.Kind == TestRunEventKind.StepStarted).StepId == retry.Plan.Cases[0].Steps[0].Id,
            "Linked retry did not use a fresh session and exact frozen cases from step one.");
        checks.Add("linked-new-session-retry-frozen-step-one");
        Require(window.Workspace.Catalog!.Projects.Any(p => p.Id == state.SecondaryId), "Separate-process project roster lost B.");
        checks.Add("restart-project-roster-preserved");
    }
}
