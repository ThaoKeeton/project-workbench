using System.Diagnostics;
using Workbench.TestExecutor;
using Workbench.TestExecutor.Windows;
using Xunit;

namespace Workbench.TestExecutor.WindowsTests;

public sealed class FlaUiSampleTests
{
    [Fact(Timeout = 60000)]
    public async Task Native_file_dialog_can_be_filled_without_leaving_the_machine()
    {
        var exe = Path.Combine(AppContext.BaseDirectory, "TestExecutor.SampleApp.exe");
        var chosen = Path.Combine(Path.GetTempPath(), "synthetic-dialog.txt");
        await File.WriteAllTextAsync(chosen, "synthetic");
        using var process = Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false })
            ?? throw new InvalidOperationException("sample app did not start");
        try
        {
            Assert.True(process.WaitForInputIdle(15000), "sample did not reach the file dialog");
            await using var session = await new FlaUiSessionFactory().OpenAsync(new ApplicationTarget("TestExecutor.SampleApp", "合成被测窗口"), CancellationToken.None);
            var filled = await session.ExecuteAsync(new UiAction("open-file", new ElementSelector("打开", "1148"), chosen, null, true), CancellationToken.None);
            string? label;
            try
            {
                var read = await session.ExecuteAsync(new UiAction("read", new ElementSelector(null, "sample-value"), null, "synthetic-dialog.txt", false), CancellationToken.None);
                label = read.Log + " " + read.Actual;
            }
            catch (Exception error)
            {
                label = error.GetType().Name + " " + error.Message;
            }
            Assert.True(filled.Succeeded && label.EndsWith("synthetic-dialog.txt", StringComparison.Ordinal), filled.Log + " " + filled.Actual + " label=" + label);
            File.Delete(chosen);
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
    }

}
