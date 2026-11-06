using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Workbench.Core;
using Workbench.Sdk;
using static Workbench.App.NativeRuntimeProbe;

namespace Workbench.App;

/// <summary>Separate upstream acceptance-ID proof. All business algorithms/providers remain in the pinned package.</summary>
internal sealed partial class BusinessPluginSmoke
{
    private const string AcceptanceId = "com.upcam.task-digest.acceptance";
    private const string AcceptanceMarker = "ProjectWorkbench genuine UPCAM synthetic acceptance probe v1";
    private const string BindingKey = "schedule-binding-v1";
    private const string BindingSecret = "upcam.resume-binding-key";
    private const string AcceptanceCredentialCoverage = "UPCAM acceptance: actual generated 32-byte host-DPAPI resume-binding key; retained ciphertext and binding verified across OS restart; restore reads existing authority, never business credentials. Synthetic service credentials remain upstream in-memory placeholders. DingTalk registers its production definitions without granting any purpose in this separate root.";
    private const string AcceptanceProviderCoverage = "Separately compiled UPCAM acceptance ID: actual registered 15-second scheduler and Core collection/summary/history/ledger, with upstream SyntheticClient/SyntheticSummarizer/SyntheticDingTalk local fake transport only. Synthetic sent means local fake success, never real delivery. Production UPCAM and DingTalk provider delivery remain unexecuted. No real HTTP/Webhook/Teambition/OpenAI/DWS calls, messages or credentials.";
    private string AcceptanceProfile => Path.Combine(options.UserRoot, "data", AcceptanceId, "profile");
    private string BindingFile => SecretFile(options.UserRoot);
    private string _acceptanceStage = "not-started";
    private bool _acceptanceModeVerified;
    private DateTime? _acceptanceDue;
    private JsonObject? _lastAcceptanceStatus;
    private int? _observedSyntheticSentRuns;
    private sealed record AcceptanceCheckpoint(string Phase, string? KeyCipherSha256, string? BindingSha256, int LastObservedSyntheticSentRuns);

    private static string SecretFile(string root) => Path.Combine(root, "secrets", AcceptanceId,
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(BindingSecret))) + ".bin");

    private static async Task PrepareAcceptanceAsync(RunOptions run)
    {
        var stage = "assert-no-preceding-worker";
        try
        {
            // The production/acceptance variants share the producer's singleton. Never overlap or retire unrelated processes.
            var processes = Process.GetProcessesByName("UPCAMPluginWorker");
            try { Require(processes.Length == 0, "Acceptance requires the preceding production host and all UPCAM workers to have actually exited."); }
            finally { foreach (var process in processes) process.Dispose(); }
            if (run.BusinessProbe == "acceptance-seed") return;
            stage = "verify-previous-closed-phase-and-retained-hashes";
            var phase = Array.IndexOf(RunOptions.BusinessProbePhases, run.BusinessProbe);
            var checkpoint = JsonSerializer.Deserialize<AcceptanceCheckpoint>(await File.ReadAllTextAsync(Path.Combine(run.UserRoot, "acceptance-checkpoint.json")), Json)!;
            Require(phase > 6 && checkpoint.Phase == RunOptions.BusinessProbePhases[phase - 1], "Acceptance phases require their preceding completed actual process.");
            var retained = JsonSerializer.Deserialize<Dictionary<string, string>>(await File.ReadAllTextAsync(Path.Combine(run.UserRoot, "business-retained-state.json")), Json)!;
            Require(retained.Count > 2, "Acceptance restart has no closed-process persistence evidence.");
            foreach (var pair in retained)
            {
                var path = Path.Combine(run.UserRoot, pair.Key);
                HostRuntime.RejectLinks(path);
                Require(File.Exists(path) && Hash(path) == pair.Value, "Closed-process synthetic business data/settings/ciphertext changed before startup.");
            }
            if (run.BusinessProbe == "acceptance-config")
            {
                stage = "change-owned-synthetic-schedule-negative";
                // Explicitly labeled negative fixture, after real OS exit: change only this synthetic profile's saved schedule.
                var path = Path.Combine(run.UserRoot, "data", AcceptanceId, "profile", "settings.json");
                var settings = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
                var old = settings["morning_run_at"]!.GetValue<string>();
                settings["morning_run_at"] = DateTime.ParseExact(old, "HH:mm", CultureInfo.InvariantCulture).AddHours(3).ToString("HH:mm", CultureInfo.InvariantCulture);
                await File.WriteAllTextAsync(path, settings.ToJsonString());
            }
            if (run.BusinessProbe == "acceptance-missing")
            {
                stage = "remove-owned-generated-key-negative";
                var path = SecretFile(run.UserRoot);
                HostRuntime.RejectLinks(path);
                Require(File.Exists(path), "The missing-key fixture requires the preceding real generated protected key.");
                File.Delete(path); // Only this GUID root's generated binding key, never a business credential.
            }
            stage = "prepared-for-actual-restore";
        }
        finally
        {
            try
            {
                if (run.ReportPath is not null)
                    await File.WriteAllTextAsync(Path.ChangeExtension(run.ReportPath, ".preparation.json"), JsonSerializer.Serialize(new
                    {
                        probe = run.BusinessProbe, processId = Environment.ProcessId, stage, keyPresent = File.Exists(SecretFile(run.UserRoot))
                    }, Json));
            }
            catch { /* Preserve the original verdict; this is bounded, value-free supplemental evidence only. */ }
        }
    }

    private async Task RunAcceptanceAsync()
    {
        _acceptanceStage = options.BusinessProbe!;
        Require(Manager.Plugins.Count == 2 && !Manager.Plugins.Any(p => p.Manifest?.Id == Upcam), "Acceptance must be a distinct two-package root without production UPCAM.");
        foreach (var input in _inputs) await VerifyInstalledAsync(input, input.Version);
        if (options.BusinessProbe == "acceptance-seed")
        {
            foreach (var input in _inputs)
            {
                Require(Snapshot(input.PluginId) is { IsLoaded: false, IsTrusted: false, DesiredEnabled: false }, "Synthetic artifact installation executed or trusted code.");
                Require(!(await Manager.EnableAsync(input.PluginId)).Succeeded, "Synthetic artifact bypassed fingerprint trust.");
                await TrustAsync(input.PluginId, false);
                await TrustAsync(input.PluginId, true);
            }
            Record("Separate genuine compiled acceptance ID and production DingTalk installed disabled/untrusted through shipping Store, then actual native trust cancel/approve; no production worker overlap");
            await AcceptanceSeedAsync();
        }
        else
        {
            var previous = JsonSerializer.Deserialize<AcceptanceCheckpoint>(await File.ReadAllTextAsync(Path.Combine(options.UserRoot, "acceptance-checkpoint.json")), Json)!;
            Record("Pre-startup audit verified all preceding Closed-process synthetic data/settings/ciphertext hashes before this actual OS process restored plugins");
            switch (options.BusinessProbe)
            {
                case "acceptance-restore":
                    Require(window.SmokeCurrentPage is not null && CaptureWorkers().Length <= 1, "Restore duplicated the actual synthetic worker.");
                    await WaitAcceptanceRunningAsync();
                    await VerifyBindingAsync(previous);
                    Require((await Manager.ExecuteCommandAsync(AcceptanceId, "status")).Message == "UPCAM backend ready.", "No-view restore did not start the genuine worker.");
                    var running = CaptureWorkers();
                    Require(running.Length == 1, "Authorized restore must own exactly one headless worker.");
                    Require((await Manager.StartBackgroundTaskAsync(AcceptanceId, "digest-scheduler")).Succeeded, "Idempotent host Start was refused.");
                    await WaitAcceptanceRunningAsync();
                    Require(CaptureWorkers().SequenceEqual(running), "Idempotent Start duplicated or replaced the worker.");
                    await VerifyBindingAsync(previous);
                    Record("Actual OS startup restored Running without opening a plugin view, validated unchanged existing DPAPI key/binding, and idempotent Start kept exactly one owned worker");
                    var restoredRevision = (await AcceptanceStatusAsync())["checkRevision"]!.GetValue<long>();
                    _acceptanceStage = "no-view-restored-scheduled-check";
                    await WaitAcceptanceAsync(async () =>
                    {
                        var status = await AcceptanceStatusAsync();
                        return status["checkRevision"]!.GetValue<long>() > restoredRevision && status["lastCheckOperation"]?.GetValue<string>() == "scheduled-run"
                            && status["lastCheckSucceeded"]?.GetValue<bool>() == true;
                    });
                    await VerifyAcceptanceHistoryAsync();
                    Record("Restored actual scheduler completed a timer check before any view; persisted synthetic sent history remained exactly one, without replaying the already recorded slot");
                    await AcceptanceStopAsync(native: false);
                    break;
                case "acceptance-stopped":
                    AssertAcceptanceStopped(resume: true);
                    Require(CaptureWorkers().Length == 0 && (await Manager.ExecuteCommandAsync(AcceptanceId, "status")).Message == "UPCAM backend not started.", "Explicit Stop restored work or a private worker despite retained Resume preference.");
                    await VerifyBindingAsync(previous);
                    Record("Explicit Stop persisted cleared running intent across actual OS restart despite retained Resume preference; no worker or backend started");
                    await StartAcceptanceAsync();
                    await OptInAcceptanceResumeAsync();
                    break;
                case "acceptance-config":
                    await WaitAcceptanceFaultAsync();
                    await VerifyBindingAsync(previous);
                    await VerifyFailureQuiescentAsync();
                    Record("Changed synthetic saved schedule invalidated restore; real callback Faulted with unchanged existing key/binding and no automatic Core progress or replacement authority");
                    var repair = await AcceptancePageAsync(settings: true);
                    await AcceptanceActionAsync(repair, "save-settings", "save-settings");
                    await WaitAcceptanceRunningAsync();
                    Require(Hash(BindingFile) != previous.KeyCipherSha256, "Explicit native repair did not replace the rejected grant.");
                    await OptInAcceptanceResumeAsync();
                    Record("Explicit native Save revalidated synthetic business settings and established fresh user-request authority; restore refusal itself minted nothing");
                    break;
                case "acceptance-missing":
                    await WaitAcceptanceFaultAsync();
                    Require(!File.Exists(BindingFile) && await Manager.ReadSecretAsync(AcceptanceId, BindingSecret) is null,
                        "Missing-key restore minted a replacement generated key.");
                    Require(await BindingHashAsync() == previous.BindingSha256, "Missing-key restore replaced its existing binding.");
                    await VerifyFailureQuiescentAsync();
                    Record("Missing owned generated key rejected actual OS restore without minting a replacement, changing binding or progressing Core");
                    await StartAcceptanceAsync();
                    await OptInAcceptanceResumeAsync();
                    await DisableAsync(AcceptanceId);
                    await WaitUntilAsync(() => _workers.Values.All(p => p.HasExited));
                    Require(CaptureWorkers().Length == 0 && !File.Exists(BindingFile) && await BindingHashAsync() is null
                        && Manager.GetBackgroundTaskSnapshots(AcceptanceId).Count == 0 && !Manager.HasPendingStops, "Active native Disable retained worker, work or resume authority.");
                    await TrustAsync(AcceptanceId, true);
                    AssertAcceptanceStopped(resume: false);
                    Require(CaptureWorkers().Length == 0, "Re-enable started a worker or restored revoked authority.");
                    await DisableAsync(AcceptanceId);
                    Record("After explicit new authorization, native Disable joined active scheduler and real worker, removed generated key/binding; re-enable registered Stopped with Resume off, then persisted Disable");
                    break;
                case "acceptance-disabled":
                    Require(Snapshot(AcceptanceId) is { IsLoaded: false, DesiredEnabled: false, HasRuntime: false }
                        && CaptureWorkers().Length == 0 && Manager.GetBackgroundTaskSnapshots(AcceptanceId).Count == 0 && !File.Exists(BindingFile), "Disabled acceptance plugin resumed code, work or generated authority on process restart.");
                    Require(File.Exists(Path.Combine(AcceptanceProfile, "history.sqlite3")), "Disable lost the actual synthetic business history.");
                    Record("Disabled actual OS restart loaded no acceptance code/task/worker, retained synthetic business data and restored no authority");
                    break;
                default: throw new InvalidOperationException("Unknown synthetic acceptance phase.");
            }
        }
        // DingTalk is independently installed in this root but receives no monitoring preview, credential purpose or provider action.
        _ding = DingTarget();
        Require(Manager.GetBackgroundTaskSnapshots(Ding).Single() is { State: PluginBackgroundTaskState.Stopped, RequiresPluginStart: true, ResumeOnStartup: false, DesiredRunning: false }
            && DingObservation().GetProperty("Resources").GetProperty("Dispatches").GetInt64() == 0, "Joint synthetic acceptance restored DingTalk business authority.");
        var sent = _observedSyntheticSentRuns ?? JsonSerializer.Deserialize<AcceptanceCheckpoint>(await File.ReadAllTextAsync(Path.Combine(options.UserRoot, "acceptance-checkpoint.json")), Json)!.LastObservedSyntheticSentRuns;
        var keyHash = File.Exists(BindingFile) ? Hash(BindingFile) : null;
        var checkpoint = new AcceptanceCheckpoint(options.BusinessProbe!, keyHash, await BindingHashAsync(), sent);
        await File.WriteAllTextAsync(Path.Combine(options.UserRoot, "acceptance-checkpoint.json"), JsonSerializer.Serialize(checkpoint, Json));
        _observations.Add(new { stage = "separate-synthetic-acceptance-checkpoint", checkpoint, syntheticAcceptance = true, productionDeliveryProven = false });
    }

    private async Task AcceptanceSeedAsync()
    {
        AssertAcceptanceStopped(resume: false);
        Require(CaptureWorkers().Length == 0, "Acceptance Initialize started a worker before native activation.");
        var settings = await AcceptancePageAsync(settings: true);
        await AcceptanceTabAsync(settings, "settings-tabs", 2, "probe-project");
        var end = DateTime.Now.AddMinutes(-1);
        ById<TextBox>(settings, "window-start").Text = end.AddHours(-2).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        ById<TextBox>(settings, "window-end").Text = end.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        await AcceptanceActionAsync(settings, "probe-project", "probe-project");
        await AcceptanceActionAsync(settings, "save-settings", "save-settings");
        await AcceptanceTabAsync(settings, "settings-tabs", 1, "account-tabs");
        await AcceptanceTabAsync(settings, "account-tabs", 2, "refresh-models");
        await AcceptanceActionAsync(settings, "refresh-models", "refresh-models");
        await AcceptanceTabAsync(settings, "settings-tabs", 5, "auto-run");
        // A past slot is excluded by saved enabled-since. Leave at least a minute for Save/authorization, including midnight rollover.
        var due = DateTime.Now.AddMinutes(2);
        due = new DateTime(due.Year, due.Month, due.Day, due.Hour, due.Minute, 0);
        _acceptanceDue = due;
        _observations.Add(new { stage = "configured-synthetic-due-slot", dueLocal = due, localTimeZone = TimeZoneInfo.Local.Id,
            localUtcOffset = TimeZoneInfo.Local.GetUtcOffset(due).ToString(), schedulerCalendar = "Every calendar day; producer uses PC local wall clock and saved enabled-since. Asia/Shanghai conversion applies to report windows, not schedule admission." });
        ById<TextBox>(settings, "morning-run-at").Text = due.ToString("HH:mm", CultureInfo.InvariantCulture);
        ById<TextBox>(settings, "morning-end-at").Text = due.AddMinutes(-1).ToString("HH:mm", CultureInfo.InvariantCulture);
        ById<TextBox>(settings, "morning-start-at").Text = due.ToString("HH:mm", CultureInfo.InvariantCulture);
        ById<TextBox>(settings, "afternoon-run-at").Text = due.AddHours(4).ToString("HH:mm", CultureInfo.InvariantCulture);
        ById<ToggleSwitch>(settings, "auto-run").IsOn = true;
        await AcceptanceActionAsync(settings, "save-settings", "save-settings");
        await WaitAcceptanceRunningAsync();
        Require(DateTime.Now < due, "Native synthetic authorization missed its explicitly configured future slot.");
        Require(!Manager.GetBackgroundTaskSnapshots(AcceptanceId).Single().ResumeOnStartup, "Business auto-save silently selected host startup resume.");
        await VerifyGeneratedKeyAsync();
        await window.SmokeNavigateAsync("home");
        var revisionBeforeDue = (await AcceptanceStatusAsync())["checkRevision"]!.GetValue<long>();
        _acceptanceStage = "await-actual-due-core-progress";
        await WaitAcceptanceAsync(async () =>
        {
            var status = await AcceptanceStatusAsync();
            if (DateTime.Now < due) { revisionBeforeDue = status["checkRevision"]!.GetValue<long>(); return false; }
            return status["lastCheckOperation"]?.GetValue<string>() == "scheduled-run" && status["lastCheckSucceeded"]?.GetValue<bool>() == true
                && status["checkRevision"]!.GetValue<long>() > revisionBeforeDue;
        }, 170);
        await VerifyAcceptanceHistoryAsync();
        _observations.Add(new { stage = "actual-automatic-core-progress", syntheticSentRuns = 1, localFakeSummaryObserved = true, dueLocal = due, status = await AcceptanceStatusAsync(), workerIds = CaptureWorkers() });
        Record("Native verified synthetic authority and local fake model cache enabled actual registered scheduler; navigation away retained work, actual due slot ran Core and recorded one fake sent result with fictional summary");
        await AcceptanceStopAsync(native: true);
        await StartAcceptanceAsync();
        await OptInAcceptanceResumeAsync();
        Record("After active-task native Stop/quiescence, explicit new Start plus separate native Resume opt-in prepared consent-bound real OS restore");
    }

    private async Task VerifyAcceptanceHistoryAsync()
    {
        var digest = await AcceptancePageAsync(settings: false);
        await AcceptanceTabAsync(digest, "digest-tabs", 2, "history-list");
        await AcceptanceActionAsync(digest, "refresh-history", "refresh-history");
        var history = ById<ListView>(digest, "history-list");
        var sent = history.Items.OfType<ListViewItem>().Count(item => Descendants<TextBlock>(item.Content as DependencyObject ?? item)
            .Any(text => text.Text.Split('\n')[0].EndsWith(" · sent", StringComparison.Ordinal)));
        _observedSyntheticSentRuns = sent;
        _observations.Add(new { stage = "actual-synthetic-history-observed", syntheticHistoryRows = history.Items.Count, syntheticSentRuns = sent });
        Require(history.Items.Count == 1 && sent == 1, "Actual authorized scheduled Core did not retain exactly one local synthetic sent history row.");
        history.SelectedIndex = 0;
        await AcceptanceActionAsync(digest, "history-detail-open", "history-detail");
        await AcceptanceTabAsync(digest, "digest-tabs", 2, "history-detail");
        Require(ById<TextBox>(digest, "history-detail").Text.Contains("CI 虚构摘要", StringComparison.Ordinal), "Actual scheduled Core history lacks the upstream fake summarizer result.");
    }

    private async Task<FrameworkElement> AcceptancePageAsync(bool settings)
    {
        _acceptanceStage = settings ? "attach-synthetic-settings" : "attach-synthetic-digest";
        var page = await window.SmokeNavigateAsync("view", AcceptanceId, settings ? "preferences" : "digest");
        await WaitForUpcamViewAsync(page, settings ? "settings-tabs" : "digest-tabs", seconds: 60, synthetic: true);
        Require(Descendants<TextBlock>(page).Any(t => t.Text == "CI 虚构数据模式 · 全部服务操作均为模拟。" || t.Text == "CI synthetic data · all service operations are simulated."), "Genuine acceptance native synthetic banner is absent; no provider operation is permitted.");
        var marker = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(AcceptanceProfile, ".plugin-synthetic.json")))!;
        Require(marker["format"]?.GetValue<string>() == "upcam-plugin-synthetic-v1", "The worker profile lacks its genuine synthetic-mode marker.");
        using var metadata = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(Snapshot(AcceptanceId).PackagePath, "plugin-package.json")));
        Require(metadata.RootElement.GetProperty("syntheticAcceptance").GetBoolean() && metadata.RootElement.GetProperty("pluginId").GetString() == AcceptanceId, "Installed input is not the independently compiled synthetic variant.");
        Require(CaptureWorkers().Length == 1, "Synthetic native activation must own exactly one headless worker.");
        _acceptanceModeVerified = true;
        return page;
    }

    private static Task AcceptanceTabAsync(FrameworkElement page, string tabs, int index, string control) =>
        WaitForUpcamViewAsync(page, tabs, index, content => Descendants<DependencyObject>(content).Any(element => Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(element) == control), synthetic: true);

    private async Task AcceptanceActionAsync(FrameworkElement page, string control, string operation)
    {
        _acceptanceStage = "native-" + operation;
        _observations.Add(new { stage = _acceptanceStage, keyPresent = File.Exists(BindingFile), syntheticWorkerModeVerified = _acceptanceModeVerified });
        var before = ById<TextBlock>(page, "operation-result").Text;
        Invoke(ById<Button>(page, control));
        await WaitUntilAsync(() => ById<TextBlock>(page, "operation-result").Text is var result && result != before && result.Contains($" · {operation} · succeeded ·", StringComparison.Ordinal)
            && ById<TextBlock>(page, "plugin-status").Text == "Ready · CI synthetic data", 60);
        await WaitForUpcamViewAsync(page, Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(page) == "settings-page" ? "settings-tabs" : "digest-tabs", synthetic: true);
    }

    private async Task<JsonObject> AcceptanceStatusAsync()
    {
        var result = await Manager.ExecuteCommandAsync(AcceptanceId, "scheduler-status");
        Require(result.Succeeded, "Genuine plugin scheduler-status command failed.");
        var status = JsonNode.Parse(result.Message)!.AsObject();
        _lastAcceptanceStatus = status;
        return status;
    }

    private Task WaitAcceptanceRunningAsync(long afterRevision = -1) => WaitAcceptanceAsync(async () =>
    {
        var task = Manager.GetBackgroundTaskSnapshots(AcceptanceId).Single();
        var status = await AcceptanceStatusAsync();
        return task.State == PluginBackgroundTaskState.Running && task.ErrorCode is null && task.DesiredRunning
            && status["checkRevision"]!.GetValue<long>() > afterRevision
            && status["lastCheckSucceeded"]?.GetValue<bool>() == true && status["lastCheckOperation"]?.GetValue<string>() is "authorize-scheduler" or "scheduled-run";
    });

    private Task WaitAcceptanceFaultAsync() => WaitUntilAsync(() => Manager.GetBackgroundTaskSnapshots(AcceptanceId).Single() is
        { State: PluginBackgroundTaskState.Faulted, ErrorCode: "BackgroundTaskFailed" }, 60);

    private void AssertAcceptanceStopped(bool resume) => Require(Manager.GetBackgroundTaskSnapshots(AcceptanceId).Single() is var task
        && task.State == PluginBackgroundTaskState.Stopped && !task.DesiredRunning && task.ResumeOnStartup == resume && !task.RequiresPluginStart,
        "Genuine acceptance task did not have expected stopped intent/resume policy.");

    private async Task StartAcceptanceAsync()
    {
        await AcceptancePageAsync(settings: true); // Recheck banner/mode before any user-request authorization.
        var revision = (await AcceptanceStatusAsync())["checkRevision"]!.GetValue<long>();
        Require((await Manager.ExecuteCommandAsync(AcceptanceId, "scheduler-start")).Succeeded, "Genuine native plugin command refused explicit synthetic Start.");
        await WaitAcceptanceRunningAsync(revision);
        await VerifyGeneratedKeyAsync();
    }

    private async Task OptInAcceptanceResumeAsync()
    {
        var page = await AcceptancePageAsync(settings: true);
        await AcceptanceTabAsync(page, "settings-tabs", 5, "scheduler-resume");
        var toggle = ById<ToggleSwitch>(page, "scheduler-resume");
        if (!toggle.IsOn)
        {
            var before = ById<TextBlock>(page, "operation-result").Text;
            toggle.IsOn = true;
            await WaitUntilAsync(() => ById<TextBlock>(page, "operation-result").Text is var result && result != before
                && result.Contains(" · scheduler-resume · succeeded ·", StringComparison.Ordinal), 60);
            await WaitForUpcamViewAsync(page, "settings-tabs", synthetic: true);
        }
        await WaitAcceptanceAsync(async () => Manager.GetBackgroundTaskSnapshots(AcceptanceId).Single().ResumeOnStartup
            && (await AcceptanceStatusAsync())["lastCheckSucceeded"]?.GetValue<bool>() == true);
        await window.SmokeNavigateAsync("home");
    }

    private async Task AcceptanceStopAsync(bool native)
    {
        _acceptanceStage = native ? "native-active-stop" : "host-active-stop";
        await WaitAcceptanceRunningAsync();
        var workers = CaptureWorkers();
        if (native)
        {
            var page = await AcceptancePageAsync(settings: true);
            await AcceptanceTabAsync(page, "settings-tabs", 5, "scheduler-stop");
            await AcceptanceActionAsync(page, "scheduler-stop", "scheduler-stop");
        }
        else Require((await Manager.StopBackgroundTaskAsync(AcceptanceId, "digest-scheduler")).Succeeded, "Shipping host Stop failed for an active genuine scheduler.");
        var task = Manager.GetBackgroundTaskSnapshots(AcceptanceId).Single();
        Require(task.State == PluginBackgroundTaskState.Stopped && !task.DesiredRunning, "Stop did not join the active scheduler and clear intent.");
        var status = await AcceptanceStatusAsync();
        var hashes = AcceptanceDatabaseHashes();
        await window.SmokeNavigateAsync("home");
        _acceptanceStage = "stopped-quiescence-over-real-timer-period";
        await Task.Delay(TimeSpan.FromSeconds(16)); // Longer than the real producer's 15-second timer; no clock or worker substitution.
        var later = await AcceptanceStatusAsync();
        Require(later["checkRevision"]!.GetValue<long>() == status["checkRevision"]!.GetValue<long>()
            && hashes.Count >= 2 && hashes.OrderBy(x => x.Key).SequenceEqual(AcceptanceDatabaseHashes().OrderBy(x => x.Key))
            && CaptureWorkers().SequenceEqual(workers), "Stopped scheduler made later checks/writes or replaced its idle session-owned worker.");
        _observations.Add(new { stage = native ? "native-active-stop-quiescent" : "host-active-stop-quiescent", stopped = task, timerPeriodSeconds = 15, observedQuiescentSeconds = 16,
            checkRevision = later["checkRevision"]!.GetValue<long>(), databaseSha256 = hashes, workerIds = workers });
        Record("Actual running authorized task Stop joined work and cleared intent; more than one timer period later, check revision/canonical database hashes stayed unchanged and the single idle session worker was preserved");
    }

    private Dictionary<string, string> AcceptanceDatabaseHashes() => Directory.EnumerateFiles(AcceptanceProfile, "*.sqlite3*", SearchOption.AllDirectories)
        .Order(StringComparer.Ordinal).ToDictionary(path => Path.GetRelativePath(AcceptanceProfile, path), Hash);

    private async Task VerifyFailureQuiescentAsync()
    {
        _acceptanceStage = "failed-restore-quiescence";
        var status = await AcceptanceStatusAsync();
        var hashes = AcceptanceDatabaseHashes();
        CaptureWorkers();
        await Task.Delay(TimeSpan.FromSeconds(16));
        Require(hashes.Count >= 2 && hashes.OrderBy(x => x.Key).SequenceEqual(AcceptanceDatabaseHashes().OrderBy(x => x.Key))
            && (await AcceptanceStatusAsync())["checkRevision"]!.GetValue<long>() == status["checkRevision"]!.GetValue<long>(), "Failed restore continued automatic checks or Core database writes.");
        _observations.Add(new { stage = "rejected-restore-quiescent", databaseSha256 = hashes, status, observedQuiescentSeconds = 16 });
    }

    private async Task VerifyGeneratedKeyAsync()
    {
        var value = await Manager.ReadSecretAsync(AcceptanceId, BindingSecret);
        Require(value is not null && File.Exists(BindingFile), "Authorized actual callback did not create its host-protected binding key.");
        var bytes = Convert.FromBase64String(value!);
        try { Require(bytes.Length == 32, "The genuine generated binding key length differs from the producer contract."); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
        Require(await BindingHashAsync() is not null, "Authorized actual callback did not persist a binding.");
        var binding = await Manager.GetSettings(AcceptanceId).GetAsync<JsonObject?>(BindingKey, null);
        Require(binding?["schema"]?.GetValue<int>() == 1 && string.Equals(binding["profileDirectory"]?.GetValue<string>(), AcceptanceProfile, StringComparison.OrdinalIgnoreCase)
            && binding["proof"]?.GetValue<string>() is { Length: 64 } proof && proof.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f'),
            "Generated binding does not identify this isolated profile with the producer's schema/proof shape.");
        foreach (var path in Directory.EnumerateFiles(options.UserRoot, "*.json", SearchOption.TopDirectoryOnly).Concat(Directory.EnumerateFiles(Path.Combine(options.UserRoot, "settings"), "*.json", SearchOption.AllDirectories)))
            Require(!File.ReadAllText(path).Contains(value!, StringComparison.Ordinal), "Generated binding key leaked into host JSON.");
    }

    private async Task VerifyBindingAsync(AcceptanceCheckpoint previous)
    {
        await VerifyGeneratedKeyAsync();
        Require(previous.KeyCipherSha256 is not null && previous.BindingSha256 is not null && Hash(BindingFile) == previous.KeyCipherSha256
            && await BindingHashAsync() == previous.BindingSha256, "Restore changed existing generated key ciphertext or profile/config binding.");
    }

    private async Task<string?> BindingHashAsync()
    {
        var binding = await Manager.GetSettings(AcceptanceId).GetAsync<JsonObject?>(BindingKey, null);
        return binding is null ? null : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(binding.ToJsonString())));
    }

    private static async Task WaitAcceptanceAsync(Func<Task<bool>> predicate, int seconds = 60)
    {
        var watch = Stopwatch.StartNew();
        while (!await predicate())
        {
            if (watch.Elapsed > TimeSpan.FromSeconds(seconds)) throw new TimeoutException("Genuine synthetic acceptance condition did not complete within its bounded deadline.");
            await Task.Delay(25);
        }
    }

    private object AcceptanceFailureObservation() => new
    {
        stage = _acceptanceStage, dueLocal = _acceptanceDue, keyPresent = File.Exists(BindingFile),
        syntheticWorkerModeVerified = _acceptanceModeVerified,
        schedulerState = _lastAcceptanceStatus?["state"]?.GetValue<string>(),
        checkRevision = _lastAcceptanceStatus?["checkRevision"]?.GetValue<long>(),
        lastCheckSucceeded = _lastAcceptanceStatus?["lastCheckSucceeded"]?.GetValue<bool>(),
        lastCheckOperation = _lastAcceptanceStatus?["lastCheckOperation"]?.GetValue<string>(),
        observedSyntheticSentRuns = _observedSyntheticSentRuns,
        controls = window.SmokeCurrentPage is DependencyObject page ? ControlMetadata(page) : "No attached page"
    };
}
