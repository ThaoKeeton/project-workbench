using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Workbench.Core;
using Workbench.Sdk;
using Workbench.WinUI.Sdk;
using static Workbench.App.NativeRuntimeProbe;

namespace Workbench.App;

/// <summary>Opt-in tests of immutable external packages in the current shipping host. No business references or provider replacements.</summary>
internal sealed class BusinessPluginSmoke(MainWindow window, RunOptions options)
{
    private const string Ding = "com.projectworkbench.dingtalk-insights";
    private const string Upcam = "com.upcam.task-digest";
    private const string Draft = "Synthetic integration: verify evidence before organizing fictional issues.";
    private const string Marker = "ProjectWorkbench independently built business probe v1";
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };
    private static string InputsRoot => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "fixtures", "business"));
    private PluginManager Manager => window.SmokeManager;
    private PluginPackageStore Packages => window.SmokeRuntime.Packages;
    private readonly List<string> _checks = [];
    private readonly List<object> _observations = [];
    private readonly Dictionary<int, Process> _workers = [];
    private object? _ding;
    private Input[] _inputs = [];

    private sealed record Input(string PluginId, string Version, string SourceCommit, string SourceRepository,
        long WorkflowRunId, string Sha256, string Transport, string PackageFile, string ProvenanceFile);

    public static async Task PrepareAsync(HostRuntime runtime, RunOptions options)
    {
        var inputs = ReadInputs();
        var marker = Path.Combine(options.UserRoot, "isolated-business-probe.txt");
        HostRuntime.RejectLinks(marker);
        if (options.BusinessProbe == "seed")
        {
            Require(!File.Exists(marker) && !Directory.EnumerateFileSystemEntries(Path.Combine(options.UserRoot, "Plugins")).Any(), "Business seed requires a fresh isolated root.");
            foreach (var input in inputs)
                Require((await runtime.Packages.InstallAsync(Package(input))).Succeeded, "Pinned business ZIP installation through the production store failed.");
            await File.WriteAllTextAsync(marker, Marker);
        }
        else Require(File.Exists(marker) && await File.ReadAllTextAsync(marker) == Marker, "Business restart requires its previously seeded isolated root.");
    }

    public static async Task RunAsync(MainWindow window, RunOptions options)
    {
        var probe = new BusinessPluginSmoke(window, options);
        try { await probe.RunAsync(); }
        catch (Exception error)
        {
            probe.WriteFailureEvidence(error);
            throw;
        }
    }

    private async Task RunAsync()
    {
        window.AssertUiThread();
        _inputs = ReadInputs();
        if (options.BusinessProbe != "seed") VerifyRetainedState();
        switch (options.BusinessProbe)
        {
            case "seed":
                Require(Manager.Plugins.Count == 2, "Business probe must discover exactly the two external packages, without samples.");
                foreach (var input in _inputs)
                {
                    await VerifyInstalledAsync(input, input.Version);
                    Require(Snapshot(input.PluginId) is { IsLoaded: false, IsTrusted: false, DesiredEnabled: false }, "Install executed or approved external code.");
                    Require(!(await Manager.EnableAsync(input.PluginId)).Succeeded, "External package enabled without explicit fingerprint approval.");
                    await TrustAsync(input.PluginId, approve: false);
                    await TrustAsync(input.PluginId, approve: true);
                }
                Record("Both pinned independently built ZIPs installed through shipping PluginPackageStore; disabled/untrusted/unloaded first; real native trust cancel and explicit approval of verified bytes");
                await DingSeedAsync();
                await UpcamAsync(full: true);
                foreach (var input in _inputs)
                {
                    await File.WriteAllTextAsync(Path.Combine(options.UserRoot, "data", input.PluginId, "integration-retention.txt"), "synthetic-current-data-never-rolled-back");
                    await Manager.GetSettings(input.PluginId).SetAsync("integration-note", "synthetic-state-survives-code-maintenance");
                }
                break;
            case "restart":
                await VerifyRestartAsync();
                await StageUpdatesAsync(fault: false);
                break;
            case "update":
                await VerifyMaintenanceAsync(original: false);
                foreach (var input in _inputs) await TrustAsync(input.PluginId, approve: true);
                await VerifyNativePersistenceAsync();
                await StageUpdatesAsync(fault: true);
                break;
            case "fault":
                Require(window.SmokeRuntime.MaintenanceResults.Count == 2 && window.SmokeRuntime.MaintenanceResults.All(result => !result.Succeeded)
                    && Packages.PendingOperations.Count == 2 && Packages.PendingOperations.All(p => p.Phase == PackageOperationPhase.Prepared), "Synthetic staged metadata corruption did not fail safely before code movement.");
                foreach (var input in _inputs)
                {
                    await VerifyInstalledAsync(input, UpdatedVersion(input, false));
                    Require((await Packages.CancelPendingAsync(input.PluginId)).Succeeded, "Untouched failed Prepared operation could not be canceled.");
                    var history = Packages.History.Single(item => item.PluginId == input.PluginId && item.Version == input.Version);
                    Require((await Packages.StageRollbackAsync(input.PluginId, history.HistoryId)).Succeeded, "Actual business package code rollback staging failed.");
                }
                VerifyStoppedTasks();
                Record("Fourth actual process rejected deliberately corrupted synthetic staged manifests, retained current code/data, canceled untouched journals and staged code-only rollback by real HistoryId");
                break;
            case "rollback":
                await VerifyMaintenanceAsync(original: true);
                foreach (var input in _inputs) await TrustAsync(input.PluginId, approve: true);
                await VerifyNativePersistenceAsync();
                foreach (var input in _inputs) Require((await Packages.StageRemoveAsync(input.PluginId)).Succeeded, "Business package removal staging failed.");
                Require(Manager.Plugins.All(p => p.IsLoaded) && Packages.PendingOperations.Count == 2, "Staged removal changed current loaded code.");
                Record("Fifth actual process restored original publisher bytes with fresh trust and working native pages, preserved current business state and staged keep-data removal");
                break;
            case "remove":
                Require(Manager.Plugins.Count == 0 && Packages.PendingOperations.Count == 0 && Packages.History.Count == 6
                    && window.SmokeRuntime.MaintenanceResults.Count == 2 && window.SmokeRuntime.MaintenanceResults.All(r => r.Succeeded), "Removal lost code history or left active business code.");
                Require(!AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name is "DingTalkInsights.Workbench" or "UPCAM.Workbench"), "Removed business code was loaded.");
                Record("Sixth actual process applied both removals before discovery; native host has no business code loaded and retains current data/settings and all six code-history backups");
                break;
            default: throw new InvalidOperationException("Unknown business probe phase.");
        }
        if (_ding is null && Manager.Contributions.Views.Any(v => v.PluginId == Ding)) _ding = DingTarget();
        _observations.Add(new { stage = "before-real-close", packages = Manager.Plugins, tasks = Manager.Plugins.SelectMany(p => Manager.GetBackgroundTaskSnapshots(p.Manifest!.Id)).ToArray(),
            maintenance = window.SmokeRuntime.MaintenanceResults, history = Packages.History });
        // This handler runs only from the actual WinUI Closed event, after the shipping close path joined the manager.
        window.Closed += (_, _) =>
        {
            try
            {
                if (_ding is not null)
                {
                    Require(DingTask("MonitoringCompletion").IsCompleted && DingTask("Retirement").IsCompleted, "Real window close did not retire the actual business controller/monitor.");
                    var resources = DingObservation().GetProperty("Resources");
                    Require(!resources.GetProperty("StoreOwned").GetBoolean() && resources.GetProperty("Subscriptions").GetInt32() == 0
                        && resources.GetProperty("PendingOperations").GetInt32() == 0, "Real close retained DingTalk operations/subscriptions/canonical lease.");
                    _observations.Add(new { stage = "real-close-dingtalk-retirement", resources });
                }
                Require(CaptureWorkers().Length == 0 && _workers.Values.All(p => p.HasExited), "Real close left an owned UPCAM worker process alive.");
                var workers = _workers.Values.Select(p => new { processId = p.Id, exitCode = p.ExitCode, hasExited = p.HasExited }).ToArray();
                using (HostRuntime.AcquireLease(options.UserRoot)) { }
                VerifyBusinessLeaseReleased();
                var retainedStateSha256 = PersistRetainedState();
                _checks.Add("Actual WinUI Closed followed shipping host cleanup; real monitor/worker retirement and root/business leases released; runner must still validate actual OS PID/exitCode");
                options.WriteReport(new { succeeded = true, probe = options.BusinessProbe, userRoot = options.UserRoot, processId = Environment.ProcessId,
                    actualWindowClosed = true, provenance = _inputs, checks = _checks, observations = _observations, workers,
                    retainedStateSha256,
                    credentialCoverage = "DingTalk: three definitions only, host value consumption deferred upstream. UPCAM: generated resume-binding key only; no automatic authority configured, therefore no key created. Business credentials stay canonical worker/plugin DPAPI.",
                    providerCoverage = "DingTalk actual local demo read/search/organize and MonitorSupervisor; UPCAM production worker fictional preview and missing-authorization fault. No DWS/Teambition/OpenAI/Webhook/HTTP send action or actual credential.",
                    limits = new[] { "Synthetic manifest/version and staged metadata fault variants are integration inputs, not publisher releases.", "UPCAM positive automatic provider delivery is unexecuted: upstream does not publish its distinct acceptance-ID binary.", "Backups cover host UserRoot only; external canonical state/DWS profiles and arbitrary schema downgrade are not covered.", "Windows11 manual keyboard/theme/DPI/accessibility acceptance remains unexecuted." },
                    startupStages = options.StartupStages, completedUtc = DateTimeOffset.UtcNow });
            }
            catch (Exception error)
            {
                Environment.ExitCode = 1;
                WriteFailureEvidence(error);
                options.WriteFailure("business-real-close-verification", error);
            }
            finally { foreach (var process in _workers.Values) process.Dispose(); }
        };
        var handle = WinRT.Interop.WindowNative.GetWindowHandle(window);
        Require(handle != IntPtr.Zero && PostMessage(handle, 0x0112, 0xF060, 0), "Actual native WM_SYSCOMMAND/SC_CLOSE was not admitted.");
        // Return to the dispatcher: the real AppWindow.Closing handler owns stop/cleanup and native close.
    }

    private async Task DingSeedAsync()
    {
        _ding = DingTarget();
        var task = Manager.GetBackgroundTaskSnapshots(Ding).Single();
        Require(task is { Id: "monitor", RequiresPluginStart: true, State: PluginBackgroundTaskState.Stopped, ResumeOnStartup: false, DesiredRunning: false }, "Actual DingTalk guarded monitor metadata differs from the selected artifact.");
        Require(Manager.GetSecretDefinitions(Ding).Select(s => s.Id).Order().SequenceEqual(new[] { "dingtalk.webhook-secret", "dingtalk.webhook-url", "openai.api-key" })
            && Manager.GetSecretSnapshots(Ding).All(s => s.IsConfigured == false), "DingTalk host secret metadata/value coverage was misrepresented.");
        var before = DingObservation();
        var initialLoop = DingTask("MonitoringCompletion");
        var hostStart = await Manager.StartBackgroundTaskAsync(Ding, "monitor");
        var hostResume = await Manager.SetBackgroundTaskResumeOnStartupAsync(Ding, "monitor", true);
        Require(!hostStart.Succeeded && !hostResume.Succeeded,
            "Generic host Start/resume bypassed the actual one-use business preview.");
        Require(DingObservation().GetProperty("Resources").GetProperty("Dispatches").GetInt64() == before.GetProperty("Resources").GetProperty("Dispatches").GetInt64()
            && DingObservation().GetProperty("RegisteredMonitor").GetRawText() == before.GetProperty("RegisteredMonitor").GetRawText()
            && ReferenceEquals(initialLoop, DingTask("MonitoringCompletion")), "Rejected generic Start changed the actual business bridge/loop or dispatched work.");
        _observations.Add(new { stage = "actual-guarded-task-host-refusal", hostStart, hostResume, task, actualBridgeUnchanged = true });
        var background = await window.SmokeNavigateAsync("background");
        Require(!Find<Button>(background, $"启动后台任务 {Ding}/monitor").IsEnabled && !Find<ToggleSwitch>(background, $"恢复后台任务 {Ding}/monitor").IsEnabled,
            "Current shipping background UI offered generic Start/resume for the real guarded business task.");
        Invoke(Find<Button>(background, $"打开后台任务所属插件页面 {Ding}/monitor"));
        await DingIdleAsync();
        Require(AutomationProperties.GetName(window.SmokeCurrentPage!) == "DTI 原生工作区", "Generic task UI did not route to the real business confirmation page.");
        var credentials = await window.SmokeNavigateAsync("credentials");
        foreach (var input in _inputs)
            foreach (var definition in Manager.GetSecretDefinitions(input.PluginId))
                Require(Find<PasswordBox>(credentials, $"新凭据值 {input.PluginId}/{definition.Id}").Password.Length == 0, "Host credential metadata page prefilled a business secret value.");
        var settings = await DingPageAsync("settings");
        foreach (var name in new[] { "DTI 新 OpenAI key", "DTI 新 Webhook URL", "DTI 新 Webhook 签名密钥" }) Require(Find<PasswordBox>(settings, name).Password.Length == 0, "Business credential editor exposed a saved value.");
        Find<TextBox>(settings, "DTI 轮询秒数").Text = "60";
        Find<TextBox>(settings, "DTI 保留天数").Text = "7";
        Find<TextBox>(settings, "DTI 整理事项").Text = Draft;
        Invoke(Find<Button>(settings, "DTI 保存设置"));
        await DingActionAsync("DTI 保存设置", () => Find<TextBlock>(settings, "DTI 操作结果").Text.Contains("配置已保存"));
        Require(File.Exists(Path.Combine(options.UserRoot, "data", Ding, "connection-draft.dpapi")), "Actual native settings did not persist the encrypted business draft.");
        Require(!File.ReadAllText(Path.Combine(options.UserRoot, "settings", Ding, "settings.json")).Contains(Draft), "Business draft entered host plaintext preferences.");
        await window.SmokeNavigateAsync("home");
        var page = await DingPageAsync("workspace");
        Require(Find<CheckBox>(page, "DTI 使用模拟来源").IsChecked == true && Find<TextBox>(page, "DTI 整理事项").Text == Draft, "Recreated real page lost its default demo or draft.");
        Invoke(Find<Button>(page, "DTI 打开模拟"));
        await DingActionAsync("DTI 打开模拟", () => DingObservation().GetProperty("Resources").GetProperty("StoreOwned").GetBoolean());
        var dispatches = DingObservation().GetProperty("Resources").GetProperty("Dispatches").GetInt64();
        await ConfirmAsync(page, "DTI 读取一次", false);
        await DingIdleAsync();
        Require(DingObservation().GetProperty("Resources").GetProperty("Dispatches").GetInt64() == dispatches, "Canceled native read dispatched business work.");
        await ConfirmAsync(page, "DTI 读取一次", true);
        await DingActionAsync("DTI 读取一次", () => DingObservation().GetProperty("Candidates").GetInt32() == 4 && Find<TextBox>(page, "DTI 来源候选").Text.Contains("demo-2"));
        Find<TextBox>(page, "DTI 搜索词").Text = "修复";
        await ConfirmAsync(page, "DTI 搜索", true);
        await DingActionAsync("DTI 搜索", () => DingObservation().GetProperty("Candidates").GetInt32() > 0 && Find<TextBox>(page, "DTI 来源候选").Text.Length > 0);
        await ConfirmAsync(page, "DTI 整理候选", true);
        await DingActionAsync("DTI 整理候选", () => Find<TextBox>(page, "DTI 待核实结果").Text.Contains("待核实") && Find<ComboBox>(page, "DTI 历史结果").Items.Count > 0);
        Record("Actual DingTalk native settings/DPAPI draft and recreated page; real Core demo read cancel/confirm, local search, evidence-backed organization/history; no provider network or send");
        await StartDingAsync(page);
        var loop = DingTask("MonitoringCompletion");
        await window.SmokeNavigateAsync("home");
        page = await DingPageAsync("workspace");
        Require(ReferenceEquals(loop, DingTask("MonitoringCompletion")) && !Find<Button>(page, "DTI 开始监控").IsEnabled
            && DingObservation().GetProperty("Resources").GetProperty("Subscriptions").GetInt32() == 1, "Navigation duplicated real monitor or subscriptions.");
        Invoke(Find<Button>(page, "DTI 停止监控"));
        await DingActionAsync("DTI 停止监控", () => loop.IsCompleted && !DingObservation().GetProperty("IsMonitoring").GetBoolean());
        await WaitUntilAsync(() => Manager.GetBackgroundTaskSnapshots(Ding).Single().State == PluginBackgroundTaskState.Stopped);
        await VerifyNoDingWritesAsync();
        await StartDingAsync(page);
        var hostLoop = DingTask("MonitoringCompletion");
        Require((await Manager.StopBackgroundTaskAsync(Ding, "monitor")).Succeeded && hostLoop.IsCompleted, "Host Stop failed to join actual MonitorSupervisor.");
        Require(DingObservation().GetProperty("Resources").GetProperty("StoreOwned").GetBoolean(), "Task-only Stop unexpectedly discarded the explicit local connection.");
        await WaitUntilAsync(() => !DingObservation().GetProperty("RegisteredMonitor").GetProperty("Stopping").GetBoolean());
        await StartDingAsync(page);
        var disabledLoop = DingTask("MonitoringCompletion");
        await DisableAsync(Ding);
        Require(disabledLoop.IsCompleted && DingTask("Retirement").IsCompleted && !DingObservation().GetProperty("Resources").GetProperty("StoreOwned").GetBoolean(), "Native disable retained actual business authority/lease.");
        await VerifyNoDingWritesAsync();
        await TrustAsync(Ding, true);
        _ding = DingTarget();
        Require(!DingObservation().GetProperty("IsMonitoring").GetBoolean(), "Re-enable revived business monitoring.");
        await LoadDingSettingsAsync();
        page = await DingPageAsync("workspace");
        Invoke(Find<Button>(page, "DTI 打开模拟"));
        await DingActionAsync("DTI 打开模拟", () => DingObservation().GetProperty("Resources").GetProperty("StoreOwned").GetBoolean());
        await StartDingAsync(page); // Remains active for the real shipping-window close path.
        Record("Actual registered guarded monitor rejects host Start/resume; real supervisor survives navigation singly; native Stop, host Stop and active native disable join work; new instance needs a new preview");
    }

    private async Task UpcamAsync(bool full)
    {
        var tasks = Manager.GetBackgroundTaskSnapshots(Upcam);
        Require(tasks is [{ Id: "digest-scheduler", RequiresPluginStart: false, State: PluginBackgroundTaskState.Stopped, ResumeOnStartup: false }], "Actual UPCAM scheduler did not register default-stopped.");
        Require(Manager.GetSecretDefinitions(Upcam).Select(s => s.Id).SequenceEqual(new[] { "upcam.resume-binding-key" }), "UPCAM host secret is not its generated binding key.");
        Require(CaptureWorkers().Length == 0, "Business initialization/restart left an unexpected UPCAM process before native activation.");
        var status = await Manager.ExecuteCommandAsync(Upcam, "status");
        Require(status.Succeeded && status.Message == "UPCAM backend not started.", "Initialize started the production worker before explicit native activation.");
        var settings = await window.SmokeNavigateAsync("view", Upcam, "preferences");
        Require(AutomationProperties.GetName(settings) == "UPCAM native settings", "UPCAM factory did not supply its actual native settings.");
        await UpcamReadyAsync(settings);
        CaptureWorkers();
        Require(_workers.Count > 0, "Production UPCAM native view did not start its actual owned worker.");
        if (full)
        {
            await WaitForUpcamViewAsync(settings, "settings-tabs", 0,
                content => TryById<Button>(content, "default-profile") is { IsLoaded: true, IsEnabled: true });
            Invoke(ById<Button>(settings, "default-profile"));
            await WaitUntilAsync(() => Find<TextBlock>(settings, "UPCAM operation result").Text.Contains("bind-profile · succeeded"));
            await UpcamReadyAsync(settings);
            CaptureWorkers();
            Require(Directory.EnumerateFiles(Path.Combine(options.UserRoot, "settings", Upcam), "*.json").Any(), "Native default-profile binding was not persisted.");
        }
        var page = await window.SmokeNavigateAsync("view", Upcam, "digest");
        await UpcamReadyAsync(page);
        Require((await Manager.ExecuteCommandAsync(Upcam, "preview-sample")).Succeeded, "Actual production worker/Core fictional sample failed.");
        // The vendor rebuilds tab bodies on worker results. Always re-resolve controls after the operation.
        await WaitForUpcamViewAsync(page, "digest-tabs", 1,
            content => TryById<TextBox>(content, "diagnostic-output") is { IsLoaded: true } output && output.Text.Length > 50);
        var characters = ById<TextBox>(page, "diagnostic-output").Text.Length;
        var workerIds = CaptureWorkers();
        Require(workerIds.Length > 0, "Sample preview did not use the real production worker.");
        await window.SmokeNavigateAsync("home");
        page = await window.SmokeNavigateAsync("view", Upcam, "digest");
        await UpcamReadyAsync(page);
        await WaitForUpcamViewAsync(page, "digest-tabs", 1,
            content => TryById<TextBox>(content, "diagnostic-output") is { IsLoaded: true });
        Require(ById<TextBox>(page, "diagnostic-output").Text.Length == characters && CaptureWorkers().SequenceEqual(workerIds), "Page recreation lost real preview output or duplicated workers.");
        if (full)
        {
            Require(!(await Manager.ExecuteCommandAsync(Upcam, "scheduler-start")).Succeeded, "Production UPCAM started without saved automatic business authority.");
            Require(Manager.GetBackgroundTaskSnapshots(Upcam).Single().State == PluginBackgroundTaskState.Stopped, "Failed explicit authorization check started the registered task.");
            Require((await Manager.StartBackgroundTaskAsync(Upcam, "digest-scheduler")).Succeeded, "Generic task admission was not exercised.");
            await WaitUntilAsync(() => Manager.GetBackgroundTaskSnapshots(Upcam).Single().State == PluginBackgroundTaskState.Faulted);
            Require(Manager.GetBackgroundTaskSnapshots(Upcam).Single().ErrorCode == "BackgroundTaskFailed"
                && Manager.GetSecretSnapshots(Upcam).Single().IsConfigured == false, "Missing automatic authority did not fail closed before creating a binding key.");
            var fault = Manager.GetBackgroundTaskSnapshots(Upcam).Single();
            Require((await Manager.StopBackgroundTaskAsync(Upcam, "digest-scheduler")).Succeeded, "Host could not clear the real faulted UPCAM task.");
            Require((await Manager.ExecuteCommandAsync(Upcam, "preview-sample")).Succeeded && CaptureWorkers().SequenceEqual(workerIds), "Task-only Stop retired or duplicated the session-owned preview worker.");
            _observations.Add(new { stage = "upcam-real-production-worker", previewCharacters = characters, workerIds, taskFault = fault,
                automaticProviderCoverage = "Unexecuted: no saved authority/credentials; selected upstream workflow publishes production only, not the separate synthetic acceptance-ID binary." });
            await DisableAsync(Upcam);
            await WaitUntilAsync(() => _workers.Values.All(p => p.HasExited));
            await TrustAsync(Upcam, true);
            page = await window.SmokeNavigateAsync("view", Upcam, "digest");
            await UpcamReadyAsync(page);
            Require((await Manager.ExecuteCommandAsync(Upcam, "preview-sample")).Succeeded, "Production UPCAM worker could not restart after native disable/retirement.");
            CaptureWorkers();
        }
        Record("Actual production UPCAM native view/settings and profile binding; real private headless worker/Core fictional preview survives page recreation; missing-authority start/fault safety and task Stop are distinct from positive automatic business/provider coverage");
    }

    private async Task VerifyRestartAsync()
    {
        Require(Manager.Plugins.Count == 2 && Manager.Plugins.All(p => p.IsLoaded && p.IsTrusted && p.DesiredEnabled), "Actual next process did not restore byte-identical enabled plugins.");
        VerifyStoppedTasks();
        _ding = DingTarget();
        Require(DingObservation().GetProperty("Resources").GetProperty("Dispatches").GetInt64() == 0 && !DingObservation().GetProperty("Resources").GetProperty("StoreOwned").GetBoolean(), "Restart restored DingTalk business authority.");
        await LoadDingSettingsAsync();
        var page = await DingPageAsync("workspace");
        Require(Find<CheckBox>(page, "DTI 使用模拟来源").IsChecked == true, "Restart did not default to demo.");
        Invoke(Find<Button>(page, "DTI 打开模拟"));
        await DingActionAsync("DTI 打开模拟", () => DingObservation().GetProperty("Resources").GetProperty("StoreOwned").GetBoolean());
        await ConfirmAsync(page, "DTI 开始监控", false);
        await DingIdleAsync();
        Require(!DingObservation().GetProperty("IsMonitoring").GetBoolean(), "Canceled new restart preview started monitoring.");
        await StartDingAsync(page);
        await UpcamAsync(full: false);
        Record("Second actual OS process restored only trusted plugin registration, kept both tasks stopped/no duplicate worker, retained encrypted draft/profile settings and required a new one-use DingTalk preview");
    }

    private async Task VerifyMaintenanceAsync(bool original)
    {
        Require(window.SmokeRuntime.MaintenanceResults.Count == 2 && window.SmokeRuntime.MaintenanceResults.All(r => r.Succeeded) && Packages.PendingOperations.Count == 0, "Startup business maintenance did not commit before discovery.");
        foreach (var input in _inputs)
        {
            Require(Snapshot(input.PluginId) is { IsTrusted: false, IsLoaded: false, DesiredEnabled: false }, "Code replacement inherited trust/enabled authority.");
            Require(!(await Manager.EnableAsync(input.PluginId)).Succeeded, "Replaced business code enabled without fresh fingerprint trust.");
            await VerifyInstalledAsync(input, original ? input.Version : UpdatedVersion(input, false));
            var history = Packages.History.First(h => h.PluginId == input.PluginId);
            Require(history.DataBackupPath is not null && File.ReadAllText(Path.Combine(history.DataBackupPath, "data", input.PluginId, "integration-retention.txt")) == "synthetic-current-data-never-rolled-back", "Actual package maintenance omitted its host-managed data backup.");
        }
        Record(original ? "Code-only rollback restored exact original package files and revoked approval; current business data/settings stayed intact" : "Third actual process applied explicitly synthetic manifest-only version updates, kept vendor DLLs unchanged, revoked approval and retained current business data plus host-managed backups");
    }

    private async Task VerifyNativePersistenceAsync()
    {
        _ding = DingTarget();
        VerifyStoppedTasks();
        await LoadDingSettingsAsync();
        await DingPageAsync("workspace");
        await UpcamAsync(full: false);
    }

    private async Task StageUpdatesAsync(bool fault)
    {
        foreach (var input in _inputs)
        {
            var version = UpdatedVersion(input, fault);
            var source = Path.Combine(options.UserRoot, $"synthetic-{input.PluginId}-{version}.zip");
            using (var original = ZipFile.OpenRead(Package(input)))
            using (var output = ZipFile.Open(source, ZipArchiveMode.Create))
                foreach (var entry in original.Entries.Where(e => !e.FullName.EndsWith('/')))
                {
                    var target = output.CreateEntry(entry.FullName);
                    using var destination = target.Open();
                    using var content = entry.Open();
                    if (entry.FullName == "plugin.json")
                    {
                        var manifest = JsonNode.Parse(content)!.AsObject();
                        manifest["version"] = version;
                        using var writer = new Utf8JsonWriter(destination);
                        manifest.WriteTo(writer);
                    }
                    else content.CopyTo(destination);
                }
            Require((await Packages.StageUpdateAsync(input.PluginId, source)).Succeeded, "Synthetic manifest-only business version staging failed.");
            var pending = Packages.PendingOperations.Single(p => p.PluginId == input.PluginId);
            if (fault) await File.AppendAllTextAsync(Path.Combine(pending.StagedPath!, "plugin.json"), "\n"); // Only unloaded test-stage metadata; publisher DLLs remain untouched.
            _observations.Add(new { stage = fault ? "synthetic-staged-metadata-fault" : "synthetic-manifest-version-update", input.PluginId,
                version, derivedZipSha256 = Hash(source), publisherZipSha256 = input.Sha256, pending });
        }
        var refused = await Packages.ApplyPendingAsync();
        Require(refused.Count == 2 && refused.All(r => !r.Succeeded) && Packages.PendingOperations.Count == 2, "Discovered current host unexpectedly applied maintenance in-process.");
        foreach (var input in _inputs) await VerifyInstalledAsync(input, fault ? UpdatedVersion(input, false) : input.Version);
        Record(fault ? "Staged two explicitly synthetic higher-version packages, corrupted only unloaded staged manifest bytes, and retained current loaded vendor code for next-process failure verification" : "Active current shipping host staged two synthetic manifest-only upgrades without replacing loaded DLLs; in-process Apply refused until real exit/restart");
    }

    private void VerifyStoppedTasks()
    {
        foreach (var input in _inputs)
        {
            var tasks = Manager.GetBackgroundTaskSnapshots(input.PluginId);
            Require(tasks.Count == (Snapshot(input.PluginId).IsLoaded ? 1 : 0) && tasks.All(t => t.State == PluginBackgroundTaskState.Stopped && !t.ResumeOnStartup && !t.DesiredRunning), "Business restart/maintenance revived background authority or omitted actual registrations.");
        }
    }

    private async Task LoadDingSettingsAsync()
    {
        var settings = await DingPageAsync("settings");
        Invoke(Find<Button>(settings, "DTI 载入设置"));
        await DingActionAsync("DTI 载入设置", () => Find<TextBlock>(settings, "DTI 操作结果").Text.Contains("已载入配置"));
        Require(Find<TextBox>(settings, "DTI 轮询秒数").Text == "60" && Find<TextBox>(settings, "DTI 整理事项").Text == Draft, "Actual cross-process native business settings/draft did not persist.");
    }

    private async Task<FrameworkElement> DingPageAsync(string view)
    {
        var page = await window.SmokeNavigateAsync("view", Ding, view);
        Require(AutomationProperties.GetName(page) == (view == "settings" ? "DTI 原生设置" : "DTI 原生工作区"), "Actual DingTalk page factory returned an error page.");
        await DingIdleAsync();
        return page;
    }

    private async Task StartDingAsync(FrameworkElement page)
    {
        await ConfirmAsync(page, "DTI 开始监控", true);
        await DingActionAsync("DTI 开始监控", () => DingObservation().GetProperty("IsMonitoring").GetBoolean() && !DingTask("MonitoringCompletion").IsCompleted
            && DingObservation().GetProperty("Health").GetProperty("NextReadAt").ValueKind == JsonValueKind.String);
    }

    private async Task VerifyNoDingWritesAsync()
    {
        var before = DingObservation().GetProperty("Resources");
        var state = Path.Combine(options.UserRoot, "data", Ding, "demo", "state.dpapi");
        var hash = Hash(state);
        await Task.Delay(300);
        var after = DingObservation().GetProperty("Resources");
        Require(before.GetProperty("Dispatches").GetInt64() == after.GetProperty("Dispatches").GetInt64()
            && before.GetProperty("OwnedStoreWrites").GetInt64() == after.GetProperty("OwnedStoreWrites").GetInt64() && hash == Hash(state), "Business dispatch/state writes continued beyond the actual stop barrier.");
        _observations.Add(new { stage = "dingtalk-stop-barrier", resources = after, stateSha256 = hash });
    }

    private async Task TrustAsync(string id, bool approve)
    {
        await HostIdleAsync();
        var snapshot = Snapshot(id);
        var page = await window.SmokeNavigateAsync("plugins");
        var button = Find<Button>(page, $"启用 {snapshot.Manifest!.Name}");
        Invoke(button);
        if (!snapshot.IsTrusted) await DialogAsync(approve, "信任并启用");
        else Require(approve, "Cancel coverage requires untrusted code.");
        await HostIdleAsync();
        Require(approve ? Snapshot(id).IsLoaded && Snapshot(id).IsTrusted : !Snapshot(id).IsLoaded && !Snapshot(id).IsTrusted, "Real native trust action did not establish its requested result.");
        var fresh = await window.SmokeNavigateAsync("plugins");
        var next = Find<Button>(fresh, $"{(approve ? "停用" : "启用")} {snapshot.Manifest.Name}");
        Require(!ReferenceEquals(page, fresh) && !ReferenceEquals(button, next) && next.IsLoaded && next.XamlRoot is not null && next.IsEnabled, "Trust completion used a stale/detached native control.");
        using var state = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(options.UserRoot, "plugin-state.json")));
        _observations.Add(new { stage = approve ? "native-approved-known-fingerprint" : "native-trust-canceled", pluginId = id,
            trustedFingerprint = state.RootElement.GetProperty("plugins").GetProperty(id).GetProperty("trustedFingerprint").GetString(),
            newNativeControl = !ReferenceEquals(button, next), targetLoaded = next.IsLoaded, targetAttached = next.XamlRoot is not null });
    }

    private async Task DisableAsync(string id)
    {
        await HostIdleAsync();
        var page = await window.SmokeNavigateAsync("plugins");
        Invoke(Find<Button>(page, $"停用 {Snapshot(id).Manifest!.Name}"));
        await HostIdleAsync();
        Require(Snapshot(id) is { State: PluginState.Disabled, HasRuntime: false }, "Actual native disable did not retire the business instance.");
        var fresh = await window.SmokeNavigateAsync("plugins");
        Require(!ReferenceEquals(page, fresh), "Disable reused detached controls.");
    }

    private async Task ConfirmAsync(FrameworkElement page, string action, bool approve)
    {
        await DingIdleAsync();
        await WaitUntilAsync(() => page.IsLoaded && page.XamlRoot is not null && Descendants<Button>(page).Any(b => AutomationProperties.GetName(b) == action && b.IsLoaded && b.IsEnabled));
        Invoke(Find<Button>(page, action));
        await DialogAsync(approve, "确认本次操作");
    }

    private async Task DialogAsync(bool approve, string primary)
    {
        await WaitUntilAsync(() => OpenDialogs().Any());
        var dialog = OpenDialogs().Single();
        await WaitUntilAsync(() => Descendants<Button>(dialog).Any(b => Equals(b.Content, approve ? primary : "取消") && b.IsLoaded));
        Invoke(Descendants<Button>(dialog).Single(b => Equals(b.Content, approve ? primary : "取消")));
        await WaitUntilAsync(() => !OpenDialogs().Any());
    }

    private IEnumerable<ContentDialog> OpenDialogs() => VisualTreeHelper.GetOpenPopupsForXamlRoot(((FrameworkElement)window.Content).XamlRoot).SelectMany(p => Descendants<ContentDialog>(p.Child));
    private Task HostIdleAsync() => WaitUntilAsync(() => !window.SmokeActionBusy && !OpenDialogs().Any());
    private Task DingIdleAsync() => WaitUntilAsync(() => DingObservation().GetProperty("Page") is var page && page.ValueKind == JsonValueKind.Object
        && page.GetProperty("Ready").GetBoolean() && !page.GetProperty("Busy").GetBoolean());
    private Task DingActionAsync(string action, Func<bool> rendered) => WaitUntilAsync(() => DingObservation().GetProperty("Page") is var page && page.GetProperty("Ready").GetBoolean()
        && page.GetProperty("LastAction").GetString() == action && page.GetProperty("Outcome").GetString() == "completed" && !page.GetProperty("Busy").GetBoolean() && rendered());
    private static Task UpcamReadyAsync(FrameworkElement page) => WaitForUpcamViewAsync(page,
        AutomationProperties.GetAutomationId(page) == "settings-page" ? "settings-tabs" : "digest-tabs", seconds: 60);

    private static async Task WaitForUpcamViewAsync(FrameworkElement page, string tabId, int? selectedIndex = null,
        Func<FrameworkElement, bool>? contentReady = null, int seconds = 20)
    {
        TabView? selectionTarget = null;
        TabView? previousHost = null;
        FrameworkElement? previousContent = null;
        try
        {
            await WaitUntilAsync(() =>
            {
                page.UpdateLayout();
                var tabs = CurrentLogicalTabs(page, tabId);
                if (tabs is not null && selectedIndex is int index && !ReferenceEquals(tabs, selectionTarget))
                {
                    Require(index >= 0 && index < tabs.TabItems.Count, "Required UPCAM native tab is absent.");
                    tabs.SelectedIndex = index;
                    selectionTarget = tabs;
                }
                tabs?.ApplyTemplate();
                page.UpdateLayout();
                var content = (tabs?.SelectedItem as TabViewItem)?.Content as FrameworkElement;
                var ready = TryById<TextBlock>(page, "plugin-status")?.Text == "Ready · local profile"
                    && page.IsLoaded && page.XamlRoot is not null && tabs is { IsLoaded: true, ActualWidth: > 0, ActualHeight: > 0 }
                    && content is { IsLoaded: true, ActualWidth: > 0, ActualHeight: > 0 }
                    && ReferenceEquals(CurrentLogicalTabs(page, tabId), tabs) && ReferenceEquals(TryById<TabView>(page, tabId), tabs)
                    && ReferenceEquals(tabs.XamlRoot, page.XamlRoot) && ReferenceEquals(content.XamlRoot, page.XamlRoot)
                    && Descendants<FrameworkElement>(tabs).Any(element => ReferenceEquals(element, content))
                    && (selectedIndex is null || tabs.SelectedIndex == selectedIndex) && (contentReady?.Invoke(content) ?? true);
                var stable = ready && ReferenceEquals(tabs, previousHost) && ReferenceEquals(content, previousContent);
                previousHost = ready ? tabs : null;
                previousContent = ready ? content : null;
                return stable;
            }, seconds);
        }
        catch (TimeoutException error)
        {
            throw new InvalidOperationException($"UPCAM current native tab did not become attached, realized and stable: {tabId}. {ControlMetadata(page)}", error);
        }
    }

    private static TabView? CurrentLogicalTabs(DependencyObject page, string tabId)
    {
        // Render can replace Content while its visual predecessor is still present. Resolve the current owner first.
        var hosts = Descendants<ContentControl>(page).Select(owner => owner.Content).OfType<TabView>()
            .Where(tabs => AutomationProperties.GetAutomationId(tabs) == tabId).Distinct<TabView>(ReferenceEqualityComparer.Instance).ToArray();
        return hosts.Length == 1 ? hosts[0] : null;
    }
    private object DingTarget() => ((WinUiView)Manager.Contributions.Views.Single(v => v.PluginId == Ding && v.View.Id == "workspace").View).CreateView.Target ?? throw new InvalidOperationException("Actual DingTalk diagnostic target unavailable.");
    private JsonElement DingObservation() => JsonDocument.Parse((string)_ding!.GetType().GetProperty("ObservationJson")!.GetValue(_ding)!).RootElement.Clone();
    private Task DingTask(string property) => (Task)_ding!.GetType().GetProperty(property)!.GetValue(_ding)!;
    private PluginSnapshot Snapshot(string id) => Manager.Plugins.Single(p => p.Manifest?.Id == id);

    private int[] CaptureWorkers()
    {
        var root = Path.TrimEndingDirectorySeparator(Path.Combine(options.UserRoot, "Plugins", Upcam)) + Path.DirectorySeparatorChar;
        List<int> current = [];
        foreach (var process in Process.GetProcessesByName("UPCAMPluginWorker"))
        {
            var owned = false;
            try
            {
                owned = process.MainModule?.FileName.StartsWith(root, StringComparison.OrdinalIgnoreCase) == true;
                if (!owned) continue;
                _ = process.Handle; // Keep the real OS process handle, so retirement and exitCode survive PID reuse.
                Require(process.MainWindowHandle == IntPtr.Zero, "Private UPCAM worker opened an independent GUI window.");
                current.Add(process.Id);
                if (!_workers.ContainsKey(process.Id)) _workers.Add(process.Id, process);
                else owned = false;
            }
            finally { if (!owned) process.Dispose(); }
        }
        return current.Order().ToArray();
    }

    private static Input[] ReadInputs()
    {
        var inputs = JsonSerializer.Deserialize<Input[]>(File.ReadAllText(Path.Combine(InputsRoot, "inputs.json")), Json)!;
        Require(inputs.Length == 2 && inputs.Select(i => i.PluginId).Order().SequenceEqual(new[] { Ding, Upcam }.Order()), "Exactly the pinned DingTalk and UPCAM production inputs are required.");
        foreach (var input in inputs)
        {
            HostRuntime.RejectLinks(Package(input));
            Require(Hash(Package(input)).Equals(input.Sha256, StringComparison.OrdinalIgnoreCase), "External package SHA256 changed after verified import.");
        }
        return inputs;
    }

    private async Task VerifyInstalledAsync(Input input, string version)
    {
        var snapshot = Snapshot(input.PluginId);
        Require(snapshot.Manifest!.Version == version, "Business package version does not match its actual maintenance phase.");
        using var archive = ZipFile.OpenRead(Package(input));
        var files = archive.Entries.Where(e => !e.FullName.EndsWith('/')).ToArray();
        Require(Directory.EnumerateFiles(snapshot.PackagePath, "*", SearchOption.AllDirectories).Count() == files.Length, "Installed business package inventory differs from pinned external input.");
        foreach (var entry in files)
        {
            using var source = entry.Open();
            var destination = Path.Combine(snapshot.PackagePath, entry.FullName);
            HostRuntime.RejectLinks(destination);
            if (entry.FullName == "plugin.json" && version != input.Version)
            {
                var original = JsonNode.Parse(source)!.AsObject();
                original["version"] = version;
                Require(JsonNode.DeepEquals(original, JsonNode.Parse(await File.ReadAllTextAsync(destination))), "Synthetic update changed more than the explicitly labeled manifest version.");
            }
            else Require(Convert.ToHexString(SHA256.HashData(source)) == Hash(destination), "Installed business DLL/file bytes differ from pinned vendor input.");
        }
        _observations.Add(new { stage = "verified-installed-publisher-files", input.PluginId, version, input.SourceCommit, publisherZipSha256 = input.Sha256, fileCount = files.Length });
    }

    private Dictionary<string, string> RetainedState()
    {
        Dictionary<string, string> state = [];
        foreach (var input in _inputs)
            foreach (var name in new[] { "data", "settings", "secrets" })
            {
                var root = Path.Combine(options.UserRoot, name, input.PluginId);
                if (!Directory.Exists(root)) continue;
                foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Where(p => !p.EndsWith(".lock", StringComparison.OrdinalIgnoreCase)).Order(StringComparer.Ordinal))
                {
                    HostRuntime.RejectLinks(path);
                    state.Add(Path.GetRelativePath(options.UserRoot, path).Replace('\\', '/'), Hash(path));
                }
            }
        return state;
    }

    private Dictionary<string, string> PersistRetainedState()
    {
        var state = RetainedState();
        File.WriteAllText(Path.Combine(options.UserRoot, "business-retained-state.json"), JsonSerializer.Serialize(state, Json));
        return state;
    }
    private void VerifyRetainedState()
    {
        var expected = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(options.UserRoot, "business-retained-state.json")), Json)!;
        var current = RetainedState();
        Require(expected.Count > 2 && expected.All(pair => current.TryGetValue(pair.Key, out var hash) && hash == pair.Value), "Business restart/package maintenance changed or deleted retained current data/settings/ciphertext.");
        _observations.Add(new { stage = "verified-retained-current-state", expectedSha256 = expected, actualSha256 = current });
        Record("Current isolated business data/settings/ciphertext hashes survived the preceding actual process exit and startup maintenance");
    }

    private void VerifyBusinessLeaseReleased()
    {
        var path = Path.Combine(options.UserRoot, "data", Ding, "demo", "state.dpapi.lock");
        if (File.Exists(path)) using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
    }

    private void Record(string check)
    {
        _checks.Add(check);
        options.WriteReport(new { succeeded = false, probe = options.BusinessProbe, processId = Environment.ProcessId, userRoot = options.UserRoot,
            provenance = _inputs, checks = _checks, observations = _observations, startupStages = options.StartupStages });
    }

    private void WriteFailureEvidence(Exception error)
    {
        if (options.ReportPath is null) return;
        try
        {
            var evidence = new { succeeded = false, probe = options.BusinessProbe, processId = Environment.ProcessId, userRoot = options.UserRoot,
                provenance = _inputs, checks = _checks, observations = _observations, observedWorkerProcessIds = _workers.Keys.ToArray(),
                errorType = error.GetType().Name, errorHResult = $"0x{unchecked((uint)error.HResult):X8}", startupStages = options.StartupStages };
            File.WriteAllText(Path.ChangeExtension(options.ReportPath, ".evidence.json"), JsonSerializer.Serialize(evidence,
                new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        }
        catch { /* Supplemental evidence must not replace the original failing verdict or exception. */ }
    }

    private static string Package(Input input) => Path.Combine(InputsRoot, input.PackageFile);
    private static string UpdatedVersion(Input input, bool fault) => input.PluginId == Ding ? (fault ? "0.3.3" : "0.3.2") : (fault ? "0.1.2" : "0.1.1");
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private static T? TryById<T>(DependencyObject root, string id) where T : DependencyObject
    {
        var matches = Descendants<T>(root).Where(element => AutomationProperties.GetAutomationId(element) == id).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }
    private static T ById<T>(DependencyObject root, string id) where T : DependencyObject => TryById<T>(root, id)
        ?? throw new InvalidOperationException($"Required native control is absent or ambiguous: {id}/{typeof(T).Name}. {ControlMetadata(root)}");

    private static string ControlMetadata(DependencyObject root)
    {
        // IDs, types and layout/loading only. Never read Text, Name, Header, Password or provider/profile values.
        static string Id(DependencyObject element)
        {
            var id = AutomationProperties.GetAutomationId(element);
            return id.Length is > 0 and <= 80 && id.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_') ? id : "<other>";
        }
        var elements = Descendants<DependencyObject>(root).ToArray();
        var counts = elements.GroupBy(element => (Id: Id(element), Type: element.GetType().Name))
            .OrderBy(group => group.Key.Id, StringComparer.Ordinal).ThenBy(group => group.Key.Type, StringComparer.Ordinal)
            .Take(80).Select(group => $"{group.Key.Id}/{group.Key.Type}={group.Count()}");
        var logical = elements.OfType<ContentControl>().Select(owner => owner.Content).OfType<TabView>().Select(tabs =>
        {
            var content = (tabs.SelectedItem as TabViewItem)?.Content as FrameworkElement;
            return $"{Id(tabs)}/TabView[loaded={tabs.IsLoaded},index={tabs.SelectedIndex},content={content?.GetType().Name ?? "none"},loaded={content?.IsLoaded == true},measured={content is { ActualWidth: > 0, ActualHeight: > 0 }}]";
        }).Distinct(StringComparer.Ordinal).Take(12);
        return $"Control metadata: {string.Join(", ", counts)}; logical tabs: {string.Join(", ", logical)}.";
    }
    private static void Invoke(Button button)
    {
        Require(button.IsLoaded && button.XamlRoot is not null && button.IsEnabled, "A native action target was detached or disabled.");
        new ButtonAutomationPeer(button).Invoke();
    }
    private static async Task WaitUntilAsync(Func<bool> predicate, int seconds = 20)
    {
        var watch = Stopwatch.StartNew();
        while (!predicate())
        {
            if (watch.Elapsed > TimeSpan.FromSeconds(seconds)) throw new TimeoutException("Actual business native condition did not become ready within its unchanged deadline.");
            await Task.Delay(25);
        }
    }

    [DllImport("user32.dll", EntryPoint = "PostMessageW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr windowHandle, uint message, nuint wParam, nint lParam);
}
