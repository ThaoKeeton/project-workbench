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
}
