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

}
