using System.Text;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Workbench.Core;
using Workbench.Core.Projects;
using Workbench.Core.Testing;
using static Workbench.App.NativeRuntimeProbe;
using static Workbench.App.NativeManagementProbe;

namespace Workbench.App;

internal static partial class TestingSourceAcceptanceProbe
{
    private sealed record ReleaseDescriptor(int ProtocolVersion, string FixtureId, string PluginId, string RunId, string ProjectId,
        string InstanceId, long Generation, string Provider, string Scenario, string ReleaseId);
    private sealed record HeldEvidence(string Root, long RuntimeGeneration, ReleaseDescriptor Descriptor);
    private sealed record Held(PluginManager Manager, string Root, string ProjectId, long RuntimeGeneration, FixtureState Before,
        FixtureState State, ReleaseDescriptor Descriptor);
    private static async Task<Held> HoldAsync(Session s, FrameworkElement page, string provider)
    {
        var manager = s.Window.SmokeManager; var root = s.Root; var projectId = s.ProjectId; var generation = s.Window.Workspace.RuntimeGeneration;
        var before = await StateAsync(manager);
        await CommandAsync(manager, $"source-acceptance-arm-{provider}-hold");
        var started = false;
        try
        {
            var armed = await StateAsync(manager);
            Require(armed.Phase == "armed" && armed.Figma == before.Figma && armed.Ai == before.Ai, "SourceHoldArmEnteredTransport");
            Click(page, provider == "figma" ? FigmaButton : AiButton); started = true;
            FixtureState? held = null;
            await PollAsync(async () => { held = await StateAsync(manager); return held.Phase == "held" && held.Generation == before.Generation + 1; });
            AssertDelta(before, held!, provider, false);
            Require(!Find<TextBox>(page, "Figma 文件 key").IsEnabled && !Find<Button>(page, FigmaButton).IsEnabled && !Find<Button>(page, AiButton).IsEnabled
                && !Find<Button>(page, "导入打包需求示例").IsEnabled && !Find<Button>(page, "生成测试用例").IsEnabled
                && !Find<Button>(page, "运行选中测试用例").IsEnabled && Find<Button>(page, CancelButton).IsEnabled, "SourceHeldAdmissionOpened");
            ReleaseDescriptor? descriptor = null;
            await PollAsync(async () =>
            {
                var path = Path.Combine(root, "data", Id, "source-acceptance-release-v1.held.json"); HostRuntime.RejectLinks(path);
                if (!File.Exists(path)) return false;
                var candidate = await ReadDescriptorAsync(path);
                if (candidate.InstanceId != held!.InstanceId || candidate.Generation != held.Generation) return false; // Prior valid descriptor is not this capability.
                ValidateDescriptor(candidate, s.Binding.RunId, projectId, held, root, s.Options.UserRoot);
                descriptor = candidate; return true;
            });
            s.Holds.Add(new(root, generation, descriptor!));
            return new(manager, root, projectId, generation, before, held!, descriptor!);
        }
        catch
        {
            if (started)
            {
                // Before context closure, explicit command cleanup also covers descriptor publication failure.
                Require((await manager.ExecuteCommandAsync(Id, "source-acceptance-release")).Succeeded, "SourceOpenHoldCleanupFailed");
                await PollAsync(async () => (provider == "figma" ? (await StateAsync(manager)).Figma : (await StateAsync(manager)).Ai).Settled
                    == (provider == "figma" ? before.Figma : before.Ai).Settled + 1);
                await ReadyAsync(page);
            }
            throw;
        }
    }
    private static async Task<ReleaseDescriptor> ReadDescriptorAsync(string path)
    {
        var bytes = await ReadBytesAsync(path, 2048); var text = new UTF8Encoding(false, true).GetString(bytes);
        using var document = JsonDocument.Parse(text, new() { MaxDepth = 8 }); var root = document.RootElement;
        string[] names = ["protocolVersion", "fixtureId", "pluginId", "runId", "projectId", "instanceId", "generation", "provider", "scenario", "releaseId"];
        var fields = root.EnumerateObject().ToArray();
        Require(fields.Length == names.Length && fields.Select(field => field.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() == names.Length
            && fields.All(field => names.Contains(field.Name, StringComparer.Ordinal)), "SourceDescriptorFieldsInvalid");
        return JsonSerializer.Deserialize<ReleaseDescriptor>(bytes, Json) ?? throw new InvalidDataException("SourceDescriptorInvalid");
    }
    private static void ValidateDescriptor(ReleaseDescriptor descriptor, string runId, string projectId, FixtureState state, string root, string baseRoot)
    {
        var expectedRoot = projectId == "default" ? baseRoot : Path.Combine(baseRoot, ".workspaces", "projects", projectId);
        Require(root == expectedRoot && descriptor is { ProtocolVersion: 1, Scenario: "hold" } && descriptor.FixtureId == Id && descriptor.PluginId == Id
            && descriptor.RunId == runId && descriptor.ProjectId == projectId && descriptor.InstanceId == state.InstanceId && descriptor.Generation == state.Generation
            && descriptor.Provider == state.Provider && Hex(descriptor.ReleaseId, 32), "SourceDescriptorNotBound");
        HostRuntime.RejectLinks(Path.Combine(root, "data", Id));
    }
    private static async Task SignalAsync(Held held)
    {
        // This exact immutable capability was validated before closure. No command/delegate or fresh post-close lookup is used.
        var descriptor = held.Descriptor;
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new { descriptor.ProtocolVersion, descriptor.FixtureId, descriptor.PluginId,
            descriptor.RunId, descriptor.ProjectId, descriptor.InstanceId, descriptor.Generation, descriptor.Provider, descriptor.Scenario, descriptor.ReleaseId, action = "release" }, Json);
        Require(bytes.Length <= 2048, "SourceReleaseBound");
        var directory = Path.Combine(held.Root, "data", Id); HostRuntime.RejectLinks(directory);
        var staging = Path.Combine(directory, "source-acceptance-release-v1.release.tmp");
        var target = Path.Combine(directory, "source-acceptance-release-v1.release.json");
        HostRuntime.RejectLinks(target); Require(!File.Exists(target) && !Directory.Exists(target), "SourceReleaseTargetExists");
        await WriteNewAsync(staging, bytes); HostRuntime.RejectLinks(staging); HostRuntime.RejectLinks(target); File.Move(staging, target);
    }
    private static async Task ReleaseOpenAsync(Held held)
        => Require((await held.Manager.ExecuteCommandAsync(Id, "source-acceptance-release")).Succeeded, "SourceExplicitOpenReleaseFailed");
    private static async Task<FixtureState> SettleOpenAsync(Held held)
    {
        FixtureState? final = null; var provider = held.State.Provider;
        await PollAsync(async () =>
        {
            final = await StateAsync(held.Manager);
            return (provider == "figma" ? final.Figma : final.Ai).Settled == (provider == "figma" ? held.Before.Figma : held.Before.Ai).Settled + 1;
        });
        AssertDelta(held.Before, final!, provider, true); return final!;
    }
    private static async Task NavigationHoldAsync(Session s, FrameworkElement page)
    {
        var before = await InventoryAsync(s.Store); var files = await FilesAsync(s.Root, s.ProjectId, before);
        var held = await HoldAsync(s, page, "figma"); var released = false;
        try
        {
            await s.Window.SmokeNavigateAsync("home");
            Require((await StateAsync(held.Manager)).Phase == "held" && (await InventoryAsync(s.Store)).Sources.Count == before.Sources.Count, "SourceNavigationReleasedOrPublished");
            await ReleaseOpenAsync(held); released = true; await SettleOpenAsync(held);
            await PollAsync(async () => (await InventoryAsync(s.Store)).Sources.Count == before.Sources.Count + 1);
            page = await PageAsync(s.Window); await ReadyAsync(page); AssertForm(page);
            var source = await NewSourceAsync(s, before); await AssertFigmaAsync(s, source);
            Require(Find<TextBlock>(page, StatusName).Text.Contains(source.Id + "@" + source.Revision, StringComparison.Ordinal), "SourceNavigationAcknowledgmentMissing");
            await AssertPreservedAsync(s.Root, files); await s.RecordAsync("source-held-navigation");
        }
        finally { if (!released) { await ReleaseOpenAsync(held); await SettleOpenAsync(held); } }
    }
    private static async Task CancelHoldAsync(Session s, FrameworkElement page, string provider)
    {
        var before = await InventoryAsync(s.Store); var files = await FilesAsync(s.Root, s.ProjectId, before); var selected = SelectedSet(page);
        var held = await HoldAsync(s, page, provider); var released = false;
        try
        {
            Require(!(provider == "figma" ? held.Before.Figma : held.Before.Ai).CancellationObserved, "SourceCancelEdgeAlreadySticky");
            Click(page, CancelButton);
            await PollAsync(async () => (provider == "figma" ? (await StateAsync(held.Manager)).Figma : (await StateAsync(held.Manager)).Ai).CancellationObserved);
            var canceled = await StateAsync(held.Manager);
            Require(canceled.Phase == "held" && (provider == "figma" ? canceled.Figma : canceled.Ai).Settled == (provider == "figma" ? held.Before.Figma : held.Before.Ai).Settled
                && !Find<Button>(page, FigmaButton).IsEnabled && !(await held.Manager.ExecuteCommandAsync(Id, $"source-acceptance-arm-{provider}-happy")).Succeeded, "SourceCancelReleasedOrRearmed");
            AssertRootOwned(held.Root);
            Require((await InventoryAsync(s.Store)).Sources.Count == before.Sources.Count, "SourceCancelPublishedBeforeRelease");
            await ReleaseOpenAsync(held); released = true; await SettleOpenAsync(held); await ReadyAsync(page);
            var after = await InventoryAsync(s.Store);
            Require(after.Sources.Count == before.Sources.Count && after.Cases.Count == before.Cases.Count && after.Runs.Count == 0 && SelectedSet(page) == selected
                && Find<TextBlock>(page, StatusName).Text.Contains("操作未完成：Cancelled", StringComparison.Ordinal), "SourceCanceledPublicationOrUiInvalid");
            AssertForm(page); await AssertPreservedAsync(s.Root, files); await s.RecordAsync("source-" + provider + "-cancel-retained");
        }
        finally { if (!released) { await ReleaseOpenAsync(held); await SettleOpenAsync(held); } }
    }
    private static void AssertRootOwned(string root)
    {
        var refused = false;
        try { using var lease = HostRuntime.AcquireLease(root); }
        catch (UserStoreInUseException) { refused = true; }
        Require(refused, "SourcePendingRootLeaseReleased");
    }
    private static async Task PrepareSecondaryAsync(Session s)
    {
        SetOperation(s, SourceOperation.SecondaryDialog, SourceOperationResult.Started);
        var pending = s.Window.WorkspaceNewDialogAsync();
        try
        {
            await PollAsync(() => Task.FromResult(s.Window.WorkspaceDialog is { IsLoaded: true } dialog && Descendants<Button>(dialog).Any()));
            var dialog = s.Window.WorkspaceDialog!;
            Find<TextBox>((DependencyObject)dialog.Content, "项目名称").Text = "Synthetic source acceptance B";
            var create = Descendants<Button>(dialog).Where(button => Equals(button.Content, "创建并打开")).ToArray();
            Require(create.Length == 1 && create[0].IsEnabled, "SourceNewProjectDisabled"); Invoke(create[0]);
        }
        finally { await pending; } // Own and join actual dialog/transition rather than an observation-only wait.
        Require(Hex(s.ProjectId, 32) && s.Root == Path.Combine(s.Options.UserRoot, ".workspaces", "projects", s.ProjectId)
            && s.Window.Workspace.CanUseCurrentRuntime && !s.Window.SmokeActionBusy, "SourceSecondaryProjectNotOpened");
        s.OperationResult = SourceOperationResult.Completed;
        s.Records = s.Records with { SecondaryId = s.ProjectId };
        await ObservedWorkspaceActionAsync(s, SourceOperation.SecondaryInstall, async runtime =>
        {
            var installed = await runtime.Packages.InstallAsync(PackageZip);
            if (!installed.Succeeded) { s.OperationReason = InstallRefusalReason(installed); return false; }
            SetOperation(s, SourceOperation.SecondaryDiscover, SourceOperationResult.Started);
            await runtime.Manager.DiscoverAsync(); return true;
        });
        await EnableAsync(s);
        SetOperation(s, SourceOperation.SecondaryIsolation, SourceOperationResult.Started);
        await AssertEmptySecondaryAsync(s); s.OperationResult = SourceOperationResult.Completed;
        SetOperation(s, SourceOperation.ReturnPrimary, SourceOperationResult.Started);
        await SelectProjectAsync(s, "default"); s.OperationResult = SourceOperationResult.Completed;
        var page = await EnableAsync(s); AssertResetForm(page); AssertFresh(await StateAsync(s.Window.SmokeManager));
    }
    private static async Task AssertEmptySecondaryAsync(Session s)
    {
        var inventory = await InventoryAsync(s.Store);
        Require(inventory.Sources.Count == 0 && inventory.Cases.Count == 0 && inventory.Runs.Count == 0
            && !Directory.Exists(Path.Combine(s.Root, ".workbench-project", "testing")), "SourceSecondaryInheritedRecords");
        foreach (var secret in Secrets)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            Require(await s.Window.SmokeManager.ReadSecretAsync(Id, secret.Id, cts.Token) is null && !cts.IsCancellationRequested, "SourceSecondaryInheritedCredential");
        }
        AssertFresh(await StateAsync(s.Window.SmokeManager));
        var page = await PageAsync(s.Window); AssertResetForm(page);
    }
    private static void SelectProject(MainWindow window, string id)
    {
        var selector = window.WorkspaceSelector; Require(selector.IsEnabled, "SourceProjectSelectorDisabled");
        var choices = selector.Items.OfType<ComboBoxItem>().Where(item => (string)item.Tag == id).ToArray();
        Require(choices.Length == 1, "SourceProjectSelectionUnavailable"); selector.SelectedItem = choices[0];
    }
    private static Task SelectProjectAsync(Session s, string id)
    {
        SelectProject(s.Window, id);
        return PollAsync(() => Task.FromResult(!s.Window.SmokeActionBusy && s.Window.Workspace.CanUseCurrentRuntime && s.ProjectId == id), 40);
    }
    private static async Task SwitchHoldAsync(Session s, string provider)
    {
        var page = await PageAsync(s.Window); AssertResetForm(page); FillForm(page);
        if (provider == "ai") SelectSource(page, s.Records.FigmaId!);
        var before = await InventoryAsync(s.Store); var files = await FilesAsync(s.Root, s.ProjectId, before);
        var held = await HoldAsync(s, page, provider); var released = false;
        try
        {
            SelectProject(s.Window, s.Records.SecondaryId!);
            await PollAsync(async () =>
            {
                var state = await StateAsync(held.Manager);
                return (provider == "figma" ? state.Figma : state.Ai).CancellationObserved;
            });
            Require(s.ProjectId == held.ProjectId && ReferenceEquals(s.Window.SmokeManager, held.Manager) && s.Window.Workspace.RuntimeGeneration == held.RuntimeGeneration
                && held.Manager.HasPendingProjectWork && (await StateAsync(held.Manager)).Phase == "held", "SourceSwitchReplacedPendingManager");
            AssertRootOwned(held.Root); Require((await InventoryAsync(new ProjectTestStore(held.Root, held.ProjectId))).Sources.Count == before.Sources.Count, "SourceSwitchPublishedCanceledHold");
            // Let normal finite preparation report Pending. Its actual work still belongs to A and is callable.
            await PollAsync(() => Task.FromResult(!s.Window.SmokeActionBusy), 15);
            Require(s.ProjectId == held.ProjectId && s.Window.Workspace.Transition.TargetProjectId == s.Records.SecondaryId && held.Manager.HasPendingProjectWork, "SourcePreparationDidNotRetainTarget");
            await ReleaseOpenAsync(held); released = true; await SettleOpenAsync(held);
            var notice = Find<InfoBar>((DependencyObject)s.Window.Content, "操作结果与错误");
            Require(notice.ActionButton is Button retry && retry.IsEnabled, "SourceSwitchRetryUnavailable");
            Invoke((Button)notice.ActionButton);
            await PollAsync(() => Task.FromResult(!s.Window.SmokeActionBusy && s.Window.Workspace.CanUseCurrentRuntime && s.ProjectId == s.Records.SecondaryId), 40);
            await AssertEmptySecondaryAsync(s); await AssertPreservedAsync(held.Root, files);
            var originalStore = new ProjectTestStore(held.Root, held.ProjectId); var retired = await InventoryAsync(originalStore);
            Require(retired.Sources.Count == before.Sources.Count && retired.Cases.Count == before.Cases.Count && retired.Runs.Count == 0, "SourceSwitchLatePublication");
            await SelectProjectAsync(s, "default"); page = await EnableAsync(s); AssertResetForm(page);
            var replacement = await StateAsync(s.Window.SmokeManager); AssertFresh(replacement);
            Require(replacement.InstanceId != held.State.InstanceId && s.Window.Workspace.RuntimeGeneration > held.RuntimeGeneration
                && !ReferenceEquals(s.Window.SmokeManager, held.Manager), "SourceSwitchReusedInstance");
            await AssertPreservedAsync(s.Root, files); await SecretsAsync(s, false); await s.RecordAsync("source-project-switch-" + provider);
        }
        finally
        {
            if (!released) { await ReleaseOpenAsync(held); await SettleOpenAsync(held); }
            // Release is never queued behind the shell transition. Normal host close/retry owns its actual task.
        }
    }
    private static async Task StopAsync(Session s, string provider)
    {
        var page = await EnableAsync(s); var initial = await StateAsync(s.Window.SmokeManager); AssertFresh(initial);
        s.Records = s.Records with { SeedInstanceId = initial.InstanceId };
        await SecretsAsync(s, true); page = await PageAsync(s.Window); FillForm(page);
        if (provider == "ai")
        {
            var empty = await InventoryAsync(s.Store); await ActionAsync(s, page, "figma", "happy"); var figma = await NewSourceAsync(s, empty);
            await AssertFigmaAsync(s, figma); s.Records = s.Records with { FigmaId = figma.Id }; SelectSource(page, figma.Id);
        }
        var before = await InventoryAsync(s.Store); var files = await FilesAsync(s.Root, s.ProjectId, before);
        var held = await HoldAsync(s, page, provider); Task<PluginOperationResult>? stop = null; var signaled = false;
        try
        {
            await s.RecordAsync("source-held-entered", held.State);
            await s.RecordAsync("source-release-descriptor-bound", held.State);
            stop = held.Manager.DisableAsync(Id); // Actual public manager closes SDK and retains its original owned Stop.
            var first = await stop;
            Require(!first.Succeeded && held.Manager.HasPendingStops && held.Manager.Plugins.Single(p => p.Manifest?.Id == Id) is { State: PluginState.Stopping, HasPendingStop: true }, "SourceStopDidNotRetainActualWork");
            Require(!(await held.Manager.ExecuteCommandAsync(Id, "source-acceptance-state")).Succeeded
                && !(await held.Manager.ExecuteCommandAsync(Id, "source-acceptance-release")).Succeeded, "SourceStopDidNotRevokeCommands");
            await s.RecordAsync("source-stop-revokes-commands", held.State);
            AssertRootOwned(held.Root); await AssertPreservedAsync(held.Root, files);
            await s.RecordAsync("source-stop-retains-work", held.State);
            Require(!(await held.Manager.EnableAsync(Id, confirmTrust: true)).Succeeded && held.Manager.HasPendingStops, "SourceStopReplacedPendingInstance");
            await s.RecordAsync("source-stop-refuses-new-instance", held.State);
        }
        finally
        {
            // Only this saved capability can release after SDK closure. Cancellation/timeout/Stop/Dispose do not signal.
            await SignalAsync(held); signaled = true;
            try { if (stop is not null) await stop; } // Join the actual admitted call even if an assertion failed.
            finally
            {
                // Even a faulted observer call cannot skip retry/join of the manager's retained internal Stop.
                var joined = await held.Manager.DisableAsync(Id);
                Require(joined.Succeeded && !held.Manager.HasPendingStops && held.Manager.Plugins.Single(p => p.Manifest?.Id == Id) is { State: PluginState.Disabled, HasPendingStop: false }, "SourceStopJoinFailed");
            }
        }
        Require(signaled, "SourceExplicitSignalMissing"); await s.RecordAsync("source-explicit-release", held.State);
        await s.RecordAsync("source-stop-joined", held.State);
        var after = await InventoryAsync(new ProjectTestStore(held.Root, held.ProjectId));
        Require(after.Sources.Count == before.Sources.Count && after.Cases.Count == before.Cases.Count && after.Runs.Count == 0, "SourcePostStopCanceledPublication");
        await AssertPreservedAsync(held.Root, files); await s.RecordAsync("source-canceled-no-publication", held.State);
    }
}
