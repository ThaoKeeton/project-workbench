using System.Diagnostics;
using Workbench.TestExecutor;
using Workbench.TestExecutor.Windows;
using Xunit;

namespace Workbench.TestExecutor.WindowsTests;

public sealed class FlaUiSampleTests
{
    [Fact(Timeout = 60000)]
    public async Task Synthetic_window_accepts_a_deterministic_flaui_suite()
    {
        var exe = Path.Combine(AppContext.BaseDirectory, "TestExecutor.SampleApp.exe");
        Assert.True(File.Exists(exe), "sample app was not staged");
        using var process = Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false })
            ?? throw new InvalidOperationException("sample app did not start");
        try
        {
            process.WaitForInputIdle(15000);
            var root = Path.Combine(Path.GetTempPath(), "executor-win-" + Guid.NewGuid().ToString("N"));
            var reports = Path.Combine(root, "reports");
            Directory.CreateDirectory(reports);
            var runner = new SuiteRunner(new RunnerDependencies
            {
                Automation = new FlaUiSessionFactory(),
                ComputerUse = new FakeComputerUseProvider(),
                Masker = new ScreenshotMasker(),
                Originals = new OriginalScreenshotStore(Path.Combine(root, "originals")),
                Artifacts = new RunArtifactStore(),
                Exporter = new IgnoringExporter(),
                Deadlines = new DeadlineFactory()
            });
            var sink = new CollectingSink();
            await runner.RunAsync(Suite(), new RunRequest("win-run", new ExecutionPolicy(true, false, ExportMode.Local, null, null, "computer-use-preview"), reports), sink, CancellationToken.None);
            Assert.True(sink.Summary?.Status == "passed", string.Join(" | ", sink.Steps.Select(step => step.StepId + "=" + step.Status + "/" + step.ErrorType + "/" + step.Message)));
            Assert.DoesNotContain(sink.Steps, step => step.Status == "failed");
            await using var session = await new FlaUiSessionFactory().OpenAsync(new ApplicationTarget("TestExecutor.SampleApp", "合成被测窗口"), CancellationToken.None);
            var raw = await session.CaptureAsync(CancellationToken.None);
            var secret = session.Locate(new ElementSelector(null, "sample-secret"));
            Assert.NotNull(secret);
            Assert.True(secret.Value.Width > 0 && secret.Value.Height > 0);
            var masked = new ScreenshotMasker().Apply(raw.Png, [secret.Value]);
            Assert.NotEqual(raw.Png, masked.Png);
            Directory.Delete(root, true);
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
    }

    [Fact(Timeout = 60000)]
    public async Task Native_file_dialog_can_be_filled_without_leaving_the_machine()
    {
        var exe = Path.Combine(AppContext.BaseDirectory, "TestExecutor.SampleApp.exe");
        using var process = Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false })
            ?? throw new InvalidOperationException("sample app did not start");
        try
        {
            process.WaitForInputIdle(15000);
            var chosen = Path.Combine(Path.GetTempPath(), "synthetic-dialog.txt");
            await File.WriteAllTextAsync(chosen, "synthetic");
            await using var session = await new FlaUiSessionFactory().OpenAsync(new ApplicationTarget("TestExecutor.SampleApp", "合成被测窗口"), CancellationToken.None);
            var opening = session.ExecuteAsync(new UiAction("click", new ElementSelector(null, "sample-browse"), null, null, false), CancellationToken.None);
            var filled = false;
            for (var attempt = 0; attempt < 20 && !filled; attempt++)
            {
                await Task.Delay(250);
                var result = await session.ExecuteAsync(new UiAction("open-file", new ElementSelector("打开", "1148"), chosen, null, true), CancellationToken.None);
                filled = result.Succeeded;
            }
            Assert.True(filled);
            await opening;
            var read = await session.ExecuteAsync(new UiAction("read", new ElementSelector(null, "sample-value"), null, "synthetic-dialog.txt", false), CancellationToken.None);
            Assert.Equal("synthetic-dialog.txt", read.Actual);
            File.Delete(chosen);
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
    }

    private static TestSuite Suite() => new(
        "synthetic-window",
        "合成窗口",
        false,
        true,
        new ApplicationTarget("TestExecutor.SampleApp", "合成被测窗口"),
        [new SelectorMask(new ElementSelector(null, "sample-secret"))],
        [
            new TestCase("CASE-NAME", "写入名称并读取结果", true,
            [
                new TestStep("type-name", StepKind.FlaUi, "set-value", new ElementSelector(null, "sample-name"), "名称框接受输入", "合成值", 15, true, false),
                new TestStep("press-ok", StepKind.FlaUi, "invoke", new ElementSelector("确定", null), "结果标签更新", null, 15, true, false),
                new TestStep("read-value", StepKind.FlaUi, "read", new ElementSelector(null, "sample-value"), "合成值", null, 15, true, false),
                new TestStep("wait-secret", StepKind.FlaUi, "wait-for", new ElementSelector(null, "sample-secret"), "密码框出现", null, 15, true, false),
                new TestStep("expand-list", StepKind.FlaUi, "expand", new ElementSelector(null, "sample-expand"), "列表展开", null, 15, true, false),
                new TestStep("click-ok", StepKind.FlaUi, "click", new ElementSelector(null, "sample-ok"), "再次点击", null, 15, true, false)
            ])
        ]);

    private sealed class CollectingSink : IRunSink
    {
        public List<StepStatus> Steps { get; } = [];
        public RunSummary? Summary { get; private set; }
        public void Publish(StepStatus status) => Steps.Add(status);
        public void Complete(RunSummary summary) => Summary = summary;
    }

    private sealed class IgnoringExporter : IFailureExporter
    {
        public Task<ExportResult> ExportAsync(FailureReport report, byte[]? maskedPng, ExecutionPolicy policy, CancellationToken cancellationToken)
            => Task.FromResult(new ExportResult("skipped-local", null));
    }
}
