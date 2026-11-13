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
