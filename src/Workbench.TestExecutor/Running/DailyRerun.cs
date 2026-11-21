namespace Workbench.TestExecutor;

/// <summary>
/// A daily rerun opens the saved program, runs the saved checks, and closes it again.
/// It does not call a model. A local toast is only for a failed or outdated check.
/// </summary>
public static class DailyRerun
{
    public const string StartupFileName = "ProjectWorkbench.cmd";

    public static bool UsesModel => false;

    public static bool ClosesAppItOpened => true;

    public static bool ShouldToast(string outcome) => outcome is "fail" or "needs-update";

    /// <summary>A local recap names a failed check and a page that needs updating separately.</summary>
    public static string Recap(int ran, int passed, int failed, int needsUpdate)
    {
        var text = $"今天跑了 {ran} 个，{passed} 个通过";
        if (failed > 0) text += $"，{failed} 个没通过";
        if (needsUpdate > 0) text += $"，{needsUpdate} 个需要更新";
        return text;
    }

    /// <summary>
    /// Per-user Startup folder. No administrator rights. Off deletes the file.
    /// </summary>
    public static bool SetStartWithWindows(string startupFolder, string exePath, bool enabled)
    {
        var destination = Path.Combine(startupFolder, StartupFileName);
        if (!enabled)
        {
            if (File.Exists(destination)) File.Delete(destination);
            return false;
        }
        var executable = ExecutablePath.NormalizeLaunch(exePath);
        if (executable is null) return false;
        Directory.CreateDirectory(startupFolder);
        File.WriteAllText(destination, "@echo off\r\nstart \"\" \"" + executable + "\"\r\n");
        return true;
    }
}
