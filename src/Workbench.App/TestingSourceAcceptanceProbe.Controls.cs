using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Workbench.Core;
using Workbench.Sdk;
using static Workbench.App.NativeRuntimeProbe;
using static Workbench.App.NativeManagementProbe;

namespace Workbench.App;

internal static partial class TestingSourceAcceptanceProbe
{
    private const string FigmaButton = "读取 Figma 并导入";
    private const string AiButton = "发送所选来源到 OpenAI 并生成";
    private const string CancelButton = "取消远程操作";
    private const string StatusName = "远程来源操作状态";
    private const string FirstCriteria = "Synthetic heading is visible.\nSynthetic navigation is reviewed manually.";
    private const string LastCriterion = "Synthetic confirmation is reviewed manually.";
    private static readonly (string Id, string Value)[] Secrets = [("figma-token", "source-acceptance-figma-token"), ("openai-api-key", "source-acceptance-openai-key")];
    private static async Task<FrameworkElement> PageAsync(MainWindow window) => await window.SmokeNavigateAsync("view", Id, "testing");
    private static T Find<T>(DependencyObject root, string name, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0) where T : DependencyObject
    {
        try { return NativeRuntimeProbe.Find<T>(root, name); }
        catch (InvalidOperationException) { throw new SourceProbeFailure(SourceProbeFailureKind.ControlLookup, file, line); }
    }
    private static void Click(FrameworkElement page, string name)
    {
        var button = Find<Button>(page, name); Require(button.IsEnabled, "SourceControlDisabled"); Invoke(button);
    }
    private static Task ReadyAsync(FrameworkElement page) => PollAsync(() => Task.FromResult(Find<Button>(page, FigmaButton).IsEnabled
        && Find<TextBox>(page, "Figma 文件 key").IsEnabled && !Find<Button>(page, CancelButton).IsEnabled));
    private static async Task<FrameworkElement> EnableAsync(Session s)
    {
        var manager = s.Window.SmokeManager;
        var installed = manager.Plugins.Single(p => p.Manifest?.Id == Id);
        Require(installed.Manifest is { Version: "0.2.0", MinimumHostVersion: "0.4.0", ApiVersion: 1,
            EntryAssembly: "Workbench.Testing.SourceAcceptance.dll", EntryType: "Workbench.Testing.SourceAcceptance.SourceAcceptancePlugin" }, "SourceInstalledIdentityInvalid");
        Require(manager.Plugins.Count == 1, "SourceUnexpectedInstalledPackage");
        if (installed.State != PluginState.Enabled)
        {
            Require(installed.State == PluginState.Disabled && !installed.IsLoaded && !installed.IsTrusted && !installed.DesiredEnabled, "SourceFreshDiscoveryInvalid");
            await manager.GetSettings(Id).SetAsync(Setting, s.Binding.RunId);
            await s.Window.WorkspaceActionAsync(async runtime => Require((await runtime.Manager.EnableAsync(Id, confirmTrust: true)).Succeeded, "SourceEnableFailed"));
        }
        var optIn = await manager.GetSettings(Id).GetAsync(Setting, JsonSerializer.SerializeToElement(""));
        Require(optIn.ValueKind == JsonValueKind.String && optIn.GetString() == s.Binding.RunId, "SourceOptInSettingChanged");
        var page = await PageAsync(s.Window);
        Require(page.IsLoaded && page.XamlRoot is not null && manager.Plugins.Single(p => p.Manifest?.Id == Id) is { State: PluginState.Enabled, IsLoaded: true, IsTrusted: true, DesiredEnabled: true }, "SourceNormalContributionUnavailable");
        await ReadyAsync(page); return page;
    }
    private static void FillForm(FrameworkElement page)
    {
        Find<TextBox>(page, "Figma 文件 key").Text = "SourceAcceptanceFile1";
        Find<TextBox>(page, "Figma 版本").Text = "source-v1";
        Find<TextBox>(page, "Figma 节点 1 ID").Text = "2:3";
        Find<TextBox>(page, "Figma 节点 1 验收条件").Text = FirstCriteria;
        Click(page, "添加 Figma 节点");
        Find<TextBox>(page, "Figma 节点 2 ID").Text = "1:2";
        Find<TextBox>(page, "Figma 节点 2 验收条件").Text = LastCriterion;
        AssertForm(page);
    }
    // TextBox/RichEdit returns CR on Windows. Compare only CRLF/CR/LF line boundaries; never change accepted controls or source bytes.
    private static string CriterionLineEndings(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
    private static void AssertForm(FrameworkElement page)
    {
        Require(Find<TextBox>(page, "Figma 文件 key").Text == "SourceAcceptanceFile1", "SourceFormChanged");
        Require(Find<TextBox>(page, "Figma 版本").Text == "source-v1", "SourceFormChanged");
        Require(Find<TextBox>(page, "Figma 节点 1 ID").Text == "2:3", "SourceFormChanged");
        Require(CriterionLineEndings(Find<TextBox>(page, "Figma 节点 1 验收条件").Text) == FirstCriteria, "SourceFormChanged");
        Require(Find<TextBox>(page, "Figma 节点 2 ID").Text == "1:2", "SourceFormChanged");
        Require(Find<TextBox>(page, "Figma 节点 2 验收条件").Text == LastCriterion, "SourceFormChanged");
    }
    private static void AssertResetForm(FrameworkElement page)
        => Require(Find<TextBox>(page, "Figma 文件 key").Text == "" && Find<TextBox>(page, "Figma 版本").Text == ""
            && Find<TextBox>(page, "Figma 节点 1 ID").Text == "" && Find<TextBox>(page, "Figma 节点 1 验收条件").Text == ""
            && Descendants<TextBox>(page).Count(box => AutomationProperties.GetName(box).StartsWith("Figma 节点 ", StringComparison.Ordinal)) == 2
            && Find<ListView>(page, "测试来源列表").SelectedItems.Count == 0 && !Find<Button>(page, AiButton).IsEnabled
            && Find<TextBlock>(page, StatusName).Text == "表单仅保留在当前项目会话；已成功导入的记录持久保存。", "SourceNewInstanceFormNotReset");
    private static void SelectSource(FrameworkElement page, string id)
    {
        var list = Find<ListView>(page, "测试来源列表"); Require(list.IsEnabled, "SourceSelectionDisabled");
        list.SelectedItems.Clear(); list.SelectedItems.Add(list.Items.OfType<ListViewItem>().Single(item => (string)item.Tag == id));
        Require(list.SelectedItems.Count == 1 && (string)((ListViewItem)list.SelectedItems[0]).Tag == id && Find<Button>(page, AiButton).IsEnabled, "SourceSelectionNotCaptured");
    }
    private static string? SelectedSet(FrameworkElement page) => (Find<ComboBox>(page, "测试用例集").SelectedItem as ComboBoxItem)?.Tag as string;
    private static async Task SecretsAsync(Session s, bool save)
    {
        var manager = s.Window.SmokeManager;
        Require(manager.IsSecretStorageSupported(Id), "SourceWindowsDpapiUnavailable");
        var page = await s.Window.SmokeNavigateAsync("credentials");
        foreach (var secret in Secrets)
        {
            var input = Find<PasswordBox>(page, $"新凭据值 {Id}/{secret.Id}");
            Require(input.Password.Length == 0, "SourceCredentialEditorNotBlank");
            if (save)
            {
                input.Password = secret.Value;
                Click(page, $"保存凭据 {Id}/{secret.Id}");
                await PollAsync(() => Task.FromResult(!s.Window.SmokeActionBusy && input.Password.Length == 0
                    && Find<TextBlock>(page, $"凭据状态 {Id}/{secret.Id}").Text.StartsWith("已保存", StringComparison.Ordinal)));
            }
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            Require(await manager.ReadSecretAsync(Id, secret.Id, cts.Token) == secret.Value && !cts.IsCancellationRequested, "SourceSyntheticDpapiRoundtripFailed");
            var path = Path.Combine(s.Root, "secrets", Id, Hash(Encoding.UTF8.GetBytes(secret.Id)) + ".bin");
            var cipher = await ReadBytesAsync(path, 65536);
            Require(cipher.AsSpan().IndexOf(Encoding.UTF8.GetBytes(secret.Value)) < 0, "SourceCredentialPlaintextAtRest");
        }
        await s.Window.SmokeNavigateAsync("home"); page = await s.Window.SmokeNavigateAsync("credentials");
        foreach (var secret in Secrets)
            Require(Find<PasswordBox>(page, $"新凭据值 {Id}/{secret.Id}").Password.Length == 0
                && Descendants<TextBlock>(page).All(text => !text.Text.Contains(secret.Value, StringComparison.Ordinal)), "SourceCredentialDisclosure");
    }
    private static void AssertFresh(FixtureState state)
        => Require(state.Generation == 0 && state.Phase == "idle" && state.Figma == new Counters(0, 0, 0, 0, false, false)
            && state.Ai == new Counters(0, 0, 0, 0, false, false), "SourceNewInstanceTransportNotReset");
    private static void AssertDelta(FixtureState before, FixtureState after, string provider, bool settled)
    {
        Require(before.InstanceId == after.InstanceId && before.ProjectId == after.ProjectId && after.Generation == before.Generation + 1
            && after.Provider == provider, "SourceActionIdentityChanged");
        var old = provider == "figma" ? before.Figma : before.Ai;
        var current = provider == "figma" ? after.Figma : after.Ai;
        Require(current.Entered == old.Entered + 1 && current.Accepted == old.Accepted + 1 && current.Settled == old.Settled + (settled ? 1 : 0)
            && (provider == "figma" ? after.Ai == before.Ai : after.Figma == before.Figma)
            && (!settled || after.Outcome == "replied"), "SourceTransportDeltaInvalid");
    }
    private static async Task<FixtureState> ActionAsync(Session s, FrameworkElement page, string provider, string scenario)
    {
        var manager = s.Window.SmokeManager; var before = await StateAsync(manager);
        await CommandAsync(manager, $"source-acceptance-arm-{provider}-{scenario}");
        var armed = await StateAsync(manager);
        Require(armed.Generation == before.Generation + 1 && armed.Phase == "armed" && armed.Figma == before.Figma && armed.Ai == before.Ai, "SourceArmWasActive");
        Click(page, provider == "figma" ? FigmaButton : AiButton);
        FixtureState? after = null;
        await PollAsync(async () =>
        {
            after = await StateAsync(manager);
            var current = provider == "figma" ? after.Figma : after.Ai; var old = provider == "figma" ? before.Figma : before.Ai;
            return current.Settled == old.Settled + 1 && Find<Button>(page, FigmaButton).IsEnabled && !Find<Button>(page, CancelButton).IsEnabled;
        });
        AssertDelta(before, after!, provider, true); AssertForm(page); return after!;
    }
    private static async Task SeedAsync(Session s)
    {
        var page = await EnableAsync(s); var first = await StateAsync(s.Window.SmokeManager); AssertFresh(first); AssertResetForm(page);
        s.Records = s.Records with { SeedInstanceId = first.InstanceId };
        await s.RecordAsync("source-installed-same-page");
        FillForm(page);
        Click(page, "添加 Figma 节点"); Click(page, "移除 Figma 节点 3"); AssertForm(page);
        await s.Window.SmokeNavigateAsync("home"); await s.Window.SmokeNavigateAsync("credentials");
        await CommandAsync(s.Window.SmokeManager, "refresh"); page = await PageAsync(s.Window); AssertForm(page);
        Require(!Find<Button>(page, AiButton).IsEnabled && await StateAsync(s.Window.SmokeManager) == first, "SourcePassiveUiEnteredTransport");
        await s.RecordAsync("source-passive-form");
        await SecretsAsync(s, true); page = await PageAsync(s.Window); AssertForm(page);
        Require(await StateAsync(s.Window.SmokeManager) == first, "SourceCredentialSaveEnteredTransport");
        await s.RecordAsync("source-synthetic-dpapi");
        Click(page, "导入打包需求示例");
        await PollAsync(async () => (await InventoryAsync(s.Store)).Sources.Count == 1 && Find<Button>(page, FigmaButton).IsEnabled);
        Require(await StateAsync(s.Window.SmokeManager) == first, "SourceDecoyEnteredTransport");
        var before = await InventoryAsync(s.Store);
        await ActionAsync(s, page, "figma", "happy");
        var figma = await NewSourceAsync(s, before); await AssertFigmaAsync(s, figma);
        Require(Find<TextBlock>(page, StatusName).Text.Contains(figma.Id + "@" + figma.Revision, StringComparison.Ordinal), "SourceFigmaAcknowledgmentMissing");
        s.Records = s.Records with { FigmaId = figma.Id };
        await s.RecordAsync("source-figma-happy");
        SelectSource(page, figma.Id); await s.Window.SmokeNavigateAsync("home"); page = await PageAsync(s.Window);
        Require(Find<ListView>(page, "测试来源列表").SelectedItems.Count == 1 && (string)((ListViewItem)Find<ListView>(page, "测试来源列表").SelectedItems[0]).Tag == figma.Id, "SourceSameInstanceSelectionLost");
        before = await InventoryAsync(s.Store); await ActionAsync(s, page, "ai", "happy");
        var receipt = await NewSourceAsync(s, before); var set = (await InventoryAsync(s.Store)).Cases.Single();
        s.Records = s.Records with { HappyReceiptId = receipt.Id, CaseSetId = set.Id };
        await AssertReceiptAsync(s, receipt, figma, false); await AssertManualAsync(s, page, figma, receipt, set.Id);
        await s.RecordAsync("source-ai-selected-manual");
        await ErrorAsync(s, page, "figma", "http-failure", "TestingFailed", "source-figma-http-failure");
        await ErrorAsync(s, page, "figma", "malformed", "InvalidTestSource", "source-figma-malformed");
        SelectSource(page, figma.Id); await ErrorAsync(s, page, "ai", "malformed", "InvalidTestSource", "source-ai-malformed");
        before = await InventoryAsync(s.Store); var preserved = await FilesAsync(s.Root, s.ProjectId, before); var selectedSet = SelectedSet(page);
        SelectSource(page, figma.Id); await ActionAsync(s, page, "ai", "partial");
        var partial = await NewSourceAsync(s, before); await AssertReceiptAsync(s, partial, figma, true);
        var after = await InventoryAsync(s.Store);
        Require(after.Cases.Count == before.Cases.Count && SelectedSet(page) == selectedSet
            && Find<TextBlock>(page, StatusName).Text.Contains("仅回执已确认保存", StringComparison.Ordinal)
            && Find<TextBlock>(page, StatusName).Text.Contains(partial.Id + "@" + partial.Revision, StringComparison.Ordinal)
            && Find<TextBlock>(page, StatusName).Text.Contains("Failed · StoreLimitExceeded", StringComparison.Ordinal), "SourcePartialWasReportedAsCaseSuccess");
        await AssertPreservedAsync(s.Root, preserved); s.Records = s.Records with { PartialReceiptId = partial.Id };
        await s.RecordAsync("source-ai-partial-receipt");
        await NavigationHoldAsync(s, page); page = await PageAsync(s.Window);
        await CancelHoldAsync(s, page, "figma"); SelectSource(page, figma.Id); await CancelHoldAsync(s, page, "ai");
        await PrepareSecondaryAsync(s);
        await SwitchHoldAsync(s, "figma"); await SwitchHoldAsync(s, "ai");
        var final = await StateAsync(s.Window.SmokeManager); AssertFresh(final);
        s.Records = s.Records with { SeedInstanceId = final.InstanceId };
        await SecretsAsync(s, false); page = await PageAsync(s.Window); AssertResetForm(page);
        await AssertFigmaAsync(s, await ReadAsync(ct => s.Store.ReadSourceAsync(figma.Id, ct)));
        await AssertManualAsync(s, page, figma, receipt, set.Id);
        var inventory = await InventoryAsync(s.Store); Require(inventory.Runs.Count == 0, "SourceGeneratedCaseExecuted");
        var files = await FilesAsync(s.Root, s.ProjectId, inventory);
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        await WriteNewAsync(StatePath(s.Options.UserRoot), JsonSerializer.SerializeToUtf8Bytes(new DurableState(1, Environment.ProcessId,
            process.StartTime.ToUniversalTime().ToFileTimeUtc(), s.Binding, s.Records, files,
            s.Window.Workspace.Catalog!.Projects.Select(p => p.Id).Order(StringComparer.Ordinal).ToArray()), Json));
        await s.RecordAsync("source-durable-seed");
    }
    private static async Task ErrorAsync(Session s, FrameworkElement page, string provider, string scenario, string code, string check)
    {
        var before = await InventoryAsync(s.Store); var files = await FilesAsync(s.Root, s.ProjectId, before); var selected = SelectedSet(page);
        await ActionAsync(s, page, provider, scenario);
        var after = await InventoryAsync(s.Store);
        Require(after.Sources.Count == before.Sources.Count && after.Cases.Count == before.Cases.Count && after.Runs.Count == 0
            && SelectedSet(page) == selected && Find<TextBlock>(page, StatusName).Text.Contains("操作未完成：" + code, StringComparison.Ordinal), "SourceFailurePublishedOrLostSelection");
        if (provider == "ai") Require(Find<ListView>(page, "测试来源列表").SelectedItems.Count == 1 &&
            (string)((ListViewItem)Find<ListView>(page, "测试来源列表").SelectedItems[0]).Tag == s.Records.FigmaId, "SourceFailureLostSourceSelection");
        await AssertPreservedAsync(s.Root, files); await s.RecordAsync(check);
    }
    private static async Task VerifyAsync(Session s, long birth)
    {
        var state = await ReadJsonAsync<DurableState>(StatePath(s.Options.UserRoot), 65536);
        Require(state.SchemaVersion == 1 && state.Binding == s.Binding && (state.ProcessId != Environment.ProcessId || state.ProcessCreationFileTimeUtc != birth), "SourceRestartIdentityInvalid");
        s.Records = state.Records;
        var page = await EnableAsync(s); var current = await StateAsync(s.Window.SmokeManager); AssertFresh(current); AssertResetForm(page);
        Require(current.InstanceId != state.Records.SeedInstanceId, "SourceRestartReusedInstance");
        await s.RecordAsync("source-restart-instance-reset");
        var inventory = await InventoryAsync(s.Store); var files = await FilesAsync(s.Root, s.ProjectId, inventory);
        Require(JsonSerializer.Serialize(files, Json) == JsonSerializer.Serialize(state.Files, Json)
            && s.Window.Workspace.Catalog!.Projects.Select(p => p.Id).Order(StringComparer.Ordinal).SequenceEqual(state.ProjectIds), "SourceDurableBytesOrRosterChanged");
        var figma = await ReadAsync(ct => s.Store.ReadSourceAsync(state.Records.FigmaId!, ct));
        var receipt = await ReadAsync(ct => s.Store.ReadSourceAsync(state.Records.HappyReceiptId!, ct));
        await AssertFigmaAsync(s, figma); await AssertReceiptAsync(s, receipt, figma, false);
        await AssertReceiptAsync(s, await ReadAsync(ct => s.Store.ReadSourceAsync(state.Records.PartialReceiptId!, ct)), figma, true);
        await s.RecordAsync("source-restart-records");
        await SecretsAsync(s, false); await s.RecordAsync("source-restart-dpapi");
        page = await PageAsync(s.Window); AssertResetForm(page); await AssertManualAsync(s, page, figma, receipt, state.Records.CaseSetId!);
        await s.RecordAsync("source-restart-manual-ui");
        await CommandAsync(s.Window.SmokeManager, "refresh"); await s.Window.SmokeNavigateAsync("home"); page = await PageAsync(s.Window);
        Require(await StateAsync(s.Window.SmokeManager) == current && JsonSerializer.Serialize(await FilesAsync(s.Root, s.ProjectId, await InventoryAsync(s.Store)), Json) == JsonSerializer.Serialize(state.Files, Json), "SourcePassiveRestartPublished");
        Require((await InventoryAsync(s.Store)).Runs.Count == 0, "SourcePassiveRestartExecuted");
        await s.RecordAsync("source-no-passive-publication");
    }
}
