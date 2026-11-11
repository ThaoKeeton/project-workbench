using System.Text;
using Workbench.Service.Abstractions;
using Workbench.TestCases.V2;
using Xunit;

namespace Workbench.Service.Tests;

public sealed class TestCasesModuleTests
{
    private static readonly HashSet<string> Granted = new(StringComparer.Ordinal)
    {
        "project.read", "plugin.data.read", "plugin.data.write", "local.file.export"
    };

    [Fact]
    public async Task Saves_reads_and_exports_without_sending()
    {
        var module = new TestCasesModule();
        await module.StartAsync(new PluginModuleStart(Granted), CancellationToken.None);
        var directory = Path.Combine(Path.GetTempPath(), "wb-module-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var read = await module.HandleAsync(new PluginModuleRequest("cases.get", "default", directory, Granted, null), CancellationToken.None);
            Assert.Equal(200, read.StatusCode);
            const string edited = """
                {"schema":"workbench.test-cases.v2.preview","schemaVersion":2,"suiteName":"原生工作台演示","rows":[
                {"id":"EX-001","action":"查看本地示例","expected":"页面出现在主窗口"},
                {"id":"EX-002","action":"编辑摘要示例","expected":"草稿保存到本地数据目录"},
                {"id":"EX-003","action":"停用示例插件","expected":"页面与命令入口撤销"}]}
                """;
            var saved = await module.HandleAsync(new PluginModuleRequest("cases.put", "default", directory, Granted, edited), CancellationToken.None);
            Assert.Equal(200, saved.StatusCode);
            var again = await module.HandleAsync(new PluginModuleRequest("cases.get", "default", directory, Granted, null), CancellationToken.None);
            Assert.Contains("EX-001", Encoding.UTF8.GetString(again.Body), StringComparison.Ordinal);
            var denied = await module.HandleAsync(new PluginModuleRequest("cases.export", "default", directory, new HashSet<string>(StringComparer.Ordinal), null), CancellationToken.None);
            Assert.Equal(403, denied.StatusCode);
            var exported = await module.HandleAsync(new PluginModuleRequest("cases.export", "default", directory, Granted, null), CancellationToken.None);
            Assert.Equal(200, exported.StatusCode);
            Assert.Contains("EX-001", Encoding.UTF8.GetString(exported.Body), StringComparison.Ordinal);
            var invalid = await module.HandleAsync(new PluginModuleRequest("cases.put", "default", directory, Granted, "{}"), CancellationToken.None);
            Assert.Equal(400, invalid.StatusCode);
            var missing = await module.HandleAsync(new PluginModuleRequest("missing", "default", directory, Granted, null), CancellationToken.None);
            Assert.Equal(404, missing.StatusCode);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Rejects_missing_permissions_and_invalid_documents()
    {
        var module = new TestCasesModule();
        var readOnly = new HashSet<string>(StringComparer.Ordinal) { "project.read" };
        await Assert.ThrowsAnyAsync<Exception>(() => module.StartAsync(new PluginModuleStart(readOnly), CancellationToken.None));
        await module.StartAsync(new PluginModuleStart(Granted), CancellationToken.None);
        var directory = Path.Combine(Path.GetTempPath(), "wb-module-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            Assert.Equal(400, (await module.HandleAsync(new PluginModuleRequest("cases.put", "default", directory, Granted, null), CancellationToken.None)).StatusCode);
            Assert.Equal(400, (await module.HandleAsync(new PluginModuleRequest("cases.put", "default", directory, Granted, ""), CancellationToken.None)).StatusCode);
            Assert.Equal(403, (await module.HandleAsync(new PluginModuleRequest("cases.get", "default", directory, readOnly, null), CancellationToken.None)).StatusCode);
            Assert.Equal(403, (await module.HandleAsync(new PluginModuleRequest("cases.put", "default", directory, readOnly, "{}"), CancellationToken.None)).StatusCode);

            var quoted = """
                {"schema":"workbench.test-cases.v2.preview","schemaVersion":2,"suiteName":"原生,演示","rows":[
                {"id":"EX-001","action":"say \"hi\"","expected":"页面出现在主窗口"},
                {"id":"EX-002","action":"编辑摘要示例","expected":"草稿保存到本地数据目录"},
                {"id":"EX-003","action":"停用示例插件","expected":"页面与命令入口撤销"}]}
                """;
            var saved = await module.HandleAsync(new PluginModuleRequest("cases.put", "default", directory, Granted, quoted), CancellationToken.None);
            Assert.Equal(200, saved.StatusCode);
            var exported = await module.HandleAsync(new PluginModuleRequest("cases.export", "default", directory, Granted, null), CancellationToken.None);
            var csv = Encoding.UTF8.GetString(exported.Body);
            Assert.Contains("\"原生,演示\"", csv, StringComparison.Ordinal);
            Assert.Contains("\"say \"\"hi\"\"\"", csv, StringComparison.Ordinal);

            await File.WriteAllTextAsync(Path.Combine(directory, "cases.v2.json"), "{");
            Assert.Equal(400, (await module.HandleAsync(new PluginModuleRequest("cases.get", "default", directory, Granted, null), CancellationToken.None)).StatusCode);
            Assert.Equal(400, (await module.HandleAsync(new PluginModuleRequest("cases.put", "default", directory, Granted, """{"schema":"other","schemaVersion":2,"suiteName":"x","rows":[]}"""), CancellationToken.None)).StatusCode);
            Assert.Equal(400, (await module.HandleAsync(new PluginModuleRequest("cases.put", "default", directory, Granted, """{"schema":"workbench.test-cases.v2.preview","schemaVersion":1,"suiteName":"原生工作台演示","rows":[{"id":"EX-001","action":"a","expected":"b"},{"id":"EX-002","action":"a","expected":"b"},{"id":"EX-003","action":"a","expected":"b"}]}"""), CancellationToken.None)).StatusCode);
            Assert.Equal(400, (await module.HandleAsync(new PluginModuleRequest("cases.put", "default", directory, Granted, """{"schema":"workbench.test-cases.v2.preview","schemaVersion":2,"suiteName":"原生工作台演示","rows":[{"id":"EX-009","action":"a","expected":"b"},{"id":"EX-002","action":"a","expected":"b"},{"id":"EX-003","action":"a","expected":"b"}]}"""), CancellationToken.None)).StatusCode);
            Assert.Equal(400, (await module.HandleAsync(new PluginModuleRequest("cases.put", "default", directory, Granted, """{"schema":"workbench.test-cases.v2.preview","schemaVersion":2,"suiteName":"","rows":[{"id":"EX-001","action":"a","expected":"b"},{"id":"EX-002","action":"a","expected":"b"},{"id":"EX-003","action":"a","expected":"b"}]}"""), CancellationToken.None)).StatusCode);
            var control = "{\"schema\":\"workbench.test-cases.v2.preview\",\"schemaVersion\":2,\"suiteName\":\"原生工作台演示\",\"rows\":[{\"id\":\"EX-001\",\"action\":\"a\\u0001\",\"expected\":\"b\"},{\"id\":\"EX-002\",\"action\":\"a\",\"expected\":\"b\"},{\"id\":\"EX-003\",\"action\":\"a\",\"expected\":\"b\"}]}";
            Assert.Equal(400, (await module.HandleAsync(new PluginModuleRequest("cases.put", "default", directory, Granted, control), CancellationToken.None)).StatusCode);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
