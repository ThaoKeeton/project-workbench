using Xunit;

namespace Workbench.TestExecutor.Tests;

public sealed class DailyRerunTests
{
    [Fact]
    public void Startup_is_per_user_and_off_until_asked()
    {
        var root = Fixtures.Temp();
        var startup = Path.Combine(root, "Startup");
        var app = Path.Combine(root, "ProjectWorkbench.exe");
        File.WriteAllBytes(app, [0x4D, 0x5A]);
        Assert.False(DailyRerun.UsesModel);
        Assert.True(DailyRerun.ClosesAppItOpened);
        Assert.False(DailyRerun.ShouldToast("pass"));
        Assert.True(DailyRerun.ShouldToast("fail"));
        Assert.True(DailyRerun.ShouldToast("needs-update"));
        Assert.False(DailyRerun.SetStartWithWindows(startup, app, false));
        Assert.False(File.Exists(Path.Combine(startup, DailyRerun.StartupFileName)));
        Assert.True(DailyRerun.SetStartWithWindows(startup, app, true));
        var script = File.ReadAllText(Path.Combine(startup, DailyRerun.StartupFileName));
        Assert.Contains(app, script, StringComparison.Ordinal);
        Assert.DoesNotContain("dingtalk", script, StringComparison.OrdinalIgnoreCase);
        Assert.False(DailyRerun.SetStartWithWindows(startup, app, false));
        Assert.False(File.Exists(Path.Combine(startup, DailyRerun.StartupFileName)));
        Assert.False(DailyRerun.SetStartWithWindows(startup, Path.Combine(root, "missing.exe"), true));
        Directory.Delete(root, true);
    }
}
