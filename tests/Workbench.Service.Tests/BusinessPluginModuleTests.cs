using System.Text;
using System.Text.Json;
using Workbench.DingTalkInsights.V2;
using Workbench.Service.Abstractions;
using Workbench.TestingDesign.V2;
using Workbench.UpcamStandup.V2;
using Xunit;

namespace Workbench.Service.Tests;

public sealed class BusinessPluginModuleTests
{
    private static readonly HashSet<string> Granted = new(StringComparer.Ordinal)
    {
        "project.read", "plugin.data.read", "plugin.data.write"
    };

    [Fact]
    public void Clock_uses_shanghai_and_rejects_an_empty_search()
    {
        Assert.Equal(TimeSpan.FromHours(8), PreviewClock.Zone().BaseUtcOffset);
        Assert.Equal(TimeSpan.FromHours(8), PreviewClock.Zone(["missing-zone", "Asia/Shanghai"]).BaseUtcOffset);
        Assert.Throws<InvalidOperationException>(() => PreviewClock.Zone([]));
        var today = PreviewClock.Today();
        Assert.InRange(today.Year, 2026, 2100);
    }

    [Fact]
    public async Task Data_files_stay_inside_the_plugin_directory()
    {
        using var box = new Sandbox();
        var path = PluginDataFile.Contain(box.Directory, "digest.v2.json");
        Assert.StartsWith(box.Directory, path, StringComparison.Ordinal);
        Assert.Throws<PluginDataException>(() => PluginDataFile.Contain(box.Directory, ".."));
        Assert.Throws<PluginDataException>(() => PluginDataFile.Contain(box.Directory, "nested/file.json"));
        Assert.Equal("secret.txt", PluginDataFile.BaseName("../secret.txt"));
        Assert.Equal("secret.txt", PluginDataFile.BaseName("..\\secret.txt"));
        Assert.Equal("未命名记录", PluginDataFile.BaseName(""));
        Assert.Equal("未命名记录", PluginDataFile.BaseName("bad\u0001.txt"));
        await PluginDataFile.WriteAtomicAsync(path, Encoding.UTF8.GetBytes("[]"), CancellationToken.None);
        Assert.Equal("[]", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task Upcam_extracts_today_and_keeps_the_draft_local()
    {
        var module = new UpcamStandupModule();
        await module.StartAsync(new PluginModuleStart(Granted), CancellationToken.None);
        await Assert.ThrowsAnyAsync<Exception>(() => module.StartAsync(new PluginModuleStart(new HashSet<string>()), CancellationToken.None));
        using var box = new Sandbox();
        var empty = await Call(module, "draft", null, box.Directory);
        Assert.Equal(200, empty.Status);
        Assert.False(empty.Json.GetProperty("stored").GetBoolean());
        Assert.False(empty.Json.GetProperty("autoSend").GetBoolean());

        var sample = await Call(module, "sample", """{"webhookUrl":"https://example.invalid/hook"}""", box.Directory);
        Assert.Equal(200, sample.Status);
        Assert.Equal("SYN-014", sample.Json.GetProperty("items")[0].GetProperty("id").GetString());
        Assert.Equal("界面", sample.Json.GetProperty("items")[0].GetProperty("owner").GetString());
        Assert.Equal("进行中", sample.Json.GetProperty("items")[0].GetProperty("status").GetString());
        Assert.Contains("登录页今天完成对照", sample.Json.GetProperty("speechSummary").GetString(), StringComparison.Ordinal);
        Assert.Contains("自动发送已关闭", sample.Json.GetProperty("summary").GetString(), StringComparison.Ordinal);
        Assert.False(sample.Json.GetProperty("delivered").GetBoolean());
        Assert.DoesNotContain("example.invalid", sample.Text, StringComparison.Ordinal);
        var draftPath = Path.Combine(box.Directory, "digest.v2.json");
        Assert.DoesNotContain("example.invalid", await File.ReadAllTextAsync(draftPath), StringComparison.Ordinal);
        Assert.Equal("SYN-014", (await Call(module, "draft", null, box.Directory)).Json.GetProperty("items")[0].GetProperty("id").GetString());

        var today = PreviewClock.Today();
        var imported = await Call(module, "import", ImportBody(today, "../outside.txt"), box.Directory);
        Assert.Equal("SYN-044", imported.Json.GetProperty("items")[0].GetProperty("id").GetString());
        Assert.Equal("NotToday", imported.Json.GetProperty("skipped")[0].GetProperty("reason").GetString());
        Assert.Equal("yesterday.txt", imported.Json.GetProperty("skipped")[0].GetProperty("name").GetString());
        Assert.DoesNotContain("..", imported.Json.GetProperty("skipped")[0].GetRawText(), StringComparison.Ordinal);
        Assert.Contains("待复核", imported.Text, StringComparison.Ordinal);
        Assert.Contains("有一条事项编号无法识别", imported.Text, StringComparison.Ordinal);
        Assert.Contains("有一条重复的事项编号", imported.Text, StringComparison.Ordinal);

        Assert.Equal(400, (await Call(module, "import", """{"files":[]}""", box.Directory)).Status);
        Assert.Equal(403, (await Call(module, "draft", null, box.Directory, new HashSet<string>(StringComparer.Ordinal))).Status);
        Assert.Equal(404, (await Call(module, "send", null, box.Directory)).Status);
        await File.WriteAllTextAsync(draftPath, """{"schema":"workbench.upcam.digest.v2.preview","schemaVersion":2,"autoSend":true,"delivered":false,"stored":true,"source":"local-file","meetingDate":"2026-11-12","summary":"x","speechSummary":"","items":[],"speeches":[],"skipped":[],"issues":[]}""");
        Assert.Equal(400, (await Call(module, "draft", null, box.Directory)).Status);
    }

    [Fact]
    public async Task DingTalk_analyzes_on_demand_and_refuses_to_send()
    {
        var module = new DingTalkInsightsModule();
        await module.StartAsync(new PluginModuleStart(Granted), CancellationToken.None);
        using var box = new Sandbox();
        var empty = await Call(module, "state", null, box.Directory);
        Assert.False(empty.Json.GetProperty("sendEnabled").GetBoolean());
        Assert.False(empty.Json.GetProperty("webhookConfigured").GetBoolean());
        Assert.Equal(409, (await Call(module, "analyze", null, box.Directory)).Status);

        var imported = await Call(module, "sample", """{"webhookUrl":"https://example.invalid/hook"}""", box.Directory);
        Assert.True(imported.Json.GetProperty("imported").GetBoolean());
        Assert.False(imported.Json.GetProperty("analyzed").GetBoolean());
        Assert.Equal(0, imported.Json.GetProperty("insights").GetArrayLength());
        Assert.DoesNotContain("example.invalid", await File.ReadAllTextAsync(Path.Combine(box.Directory, "chat.v2.json")), StringComparison.Ordinal);

        var analyzed = await Call(module, "analyze", null, box.Directory);
        Assert.Equal(200, analyzed.Status);
        Assert.Contains("登录页对照出现 3 次", analyzed.Text, StringComparison.Ordinal);
        Assert.Contains("09 时有 5 条消息", analyzed.Text, StringComparison.Ordinal);
        Assert.Contains("有待确认的提问", analyzed.Text, StringComparison.Ordinal);
        Assert.Contains("登录页对照今天收尾。", analyzed.Text, StringComparison.Ordinal);
        Assert.False(analyzed.Json.GetProperty("delivered").GetBoolean());

        var send = await Call(module, "send", """{"webhookUrl":"https://example.invalid/hook"}""", box.Directory);
        Assert.Equal(409, send.Status);
        Assert.Equal("SendDisabled", send.Json.GetProperty("error").GetString());
        Assert.False(send.Json.GetProperty("autoSend").GetBoolean());
        Assert.DoesNotContain("example.invalid", send.Text, StringComparison.Ordinal);
        Assert.Equal(403, (await Call(module, "send", null, box.Directory, new HashSet<string>(StringComparer.Ordinal))).Status);

        const string export = """{"schema":"workbench.dingtalk.export.v2.preview","schemaVersion":2,"conversation":"合成项目群","webhookUrl":"https://example.invalid/hook","messages":[{"id":"M-010","at":"2026-11-12T09:15:00+08:00","speaker":"界面","text":"登录页对照今天收尾。"},{"id":"M-011","at":"2026-11-12T09:16:00+08:00","speaker":"测试","text":"登录页对照还要再看？"},{"id":"M-012","at":"2026-11-12T09:17:00+08:00","speaker":"导出","text":"导出确认不要代发。"}]}""";
        var fromJson = await Call(module, "import", JsonSerializer.Serialize(new { name = "chat.json", text = export, webhookUrl = "https://example.invalid/hook" }), box.Directory);
        Assert.Equal(3, fromJson.Json.GetProperty("messageCount").GetInt32());
        Assert.DoesNotContain("example.invalid", await File.ReadAllTextAsync(Path.Combine(box.Directory, "chat.v2.json")), StringComparison.Ordinal);
        Assert.Contains("登录页对照出现 2 次", (await Call(module, "analyze", null, box.Directory)).Text, StringComparison.Ordinal);
        Assert.Equal(400, (await Call(module, "import", """{"text":"会话: 群\nM-001 | 2026-11-12T09:15:00 | 界面 | 没有时区。"}""", box.Directory)).Status);
        Assert.Equal(404, (await Call(module, "draft", null, box.Directory)).Status);
        await File.WriteAllTextAsync(Path.Combine(box.Directory, "insights.v2.json"), """{"schema":"workbench.dingtalk.insights.v2.preview","schemaVersion":2,"autoSend":false,"sendEnabled":true,"webhookConfigured":false,"delivered":false,"stored":true,"source":"local-file","imported":true,"analyzed":true,"conversation":"合成项目群","messageCount":1,"insights":[]}""");
        Assert.Equal(400, (await Call(module, "state", null, box.Directory)).Status);
    }

    [Fact]
    public async Task Testing_dashboard_labels_synthetic_checks_as_not_accepted()
    {
        var module = new TestingDesignModule();
        await module.StartAsync(new PluginModuleStart(Granted), CancellationToken.None);
        using var box = new Sandbox();
        var board = await Call(module, "dashboard", null, box.Directory);
        Assert.Equal(200, board.Status);
        Assert.False(board.Json.GetProperty("humanAcceptance").GetBoolean());
        Assert.False(board.Json.GetProperty("autoSend").GetBoolean());
        Assert.Contains("不替代人工验收", board.Json.GetProperty("label").GetString(), StringComparison.Ordinal);
        Assert.Equal(6, board.Json.GetProperty("summary").GetProperty("passed").GetInt32());
        Assert.Equal(0, board.Json.GetProperty("summary").GetProperty("failed").GetInt32());
        Assert.Equal("未验收", board.Json.GetProperty("runs")[0].GetProperty("humanAcceptance").GetString());
        Assert.Equal("SYN-TC-001", board.Json.GetProperty("cases")[0].GetProperty("id").GetString());
        var path = Path.Combine(box.Directory, "dashboard.v2.json");
        await File.WriteAllTextAsync(path, """{"humanAcceptance":true}""");
        var again = await Call(module, "dashboard", null, box.Directory);
        Assert.Equal("未验收", again.Json.GetProperty("runs")[5].GetProperty("humanAcceptance").GetString());
        Assert.Contains("未验收", await File.ReadAllTextAsync(path), StringComparison.Ordinal);
        Assert.Equal(403, (await Call(module, "dashboard", null, box.Directory, new HashSet<string>(StringComparer.Ordinal))).Status);
        Assert.Equal(404, (await Call(module, "send", null, box.Directory)).Status);
    }

    private static async Task<CallResult> Call(IWorkbenchPluginModule module, string action, string? body, string directory, IReadOnlySet<string>? granted = null)
    {
        var response = await module.HandleAsync(new PluginModuleRequest(action, "default", directory, granted ?? Granted, body), CancellationToken.None);
        var text = Encoding.UTF8.GetString(response.Body);
        return new CallResult(response.StatusCode, text, JsonSerializer.Deserialize<JsonElement>(text));
    }

    private static string ImportBody(DateOnly today, string name)
    {
        var todayText = $"""
            日期: {today:yyyy-MM-dd}

            事项: SYN-044
            标题: 本机导入
            负责人: 界面
            状态: 进行中
            发言: 这条来自本机文件。

            事项: SYN-044
            标题: 重复事项
            负责人: 界面
            状态: 进行中
            发言: 不该再收一次。

            事项: NOT-AN-ID
            标题: 编号无效
            负责人: 界面
            状态: 进行中

            事项: SYN-045
            标题: 状态不认识
            状态: 未知
            发言: 负责人空着。

            事项: SYN-046
            负责人: 界面
            状态: 进行中

            事项: SYN-047
            标题: 发言有控制符
            负责人: 界面
            状态: 已完成
            发言: 不好{'\u0001'}
            """;
        var yesterday = $"""
            日期: {today.AddDays(-1):yyyy-MM-dd}

            事项: SYN-099
            标题: 昨天的事项
            负责人: 文案
            状态: 已完成
            发言: 不进入今日草稿。
            """;
        return JsonSerializer.Serialize(new
        {
            files = new[]
            {
                new { name, text = todayText },
                new { name = "yesterday.txt", text = yesterday }
            },
            webhookUrl = "https://example.invalid/hook"
        });
    }

    private sealed record CallResult(int Status, string Text, JsonElement Json);

    private sealed class Sandbox : IDisposable
    {
        public string Directory { get; } = Path.Combine(Path.GetTempPath(), "wb-plugin-" + Guid.NewGuid().ToString("N"));

        public Sandbox() => System.IO.Directory.CreateDirectory(Directory);

        public void Dispose() => System.IO.Directory.Delete(Directory, recursive: true);
    }
}
