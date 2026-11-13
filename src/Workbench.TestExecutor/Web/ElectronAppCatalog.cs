using System.Text;

namespace Workbench.TestExecutor;

public sealed record DiscoveredElectronApp(string Name, string ExecutablePath, string Source);

public sealed record AttachDecision(string Kind, string Message);

/// <summary>
/// Finds Electron apps from a start-menu folder and ordinary install folders.
/// A folder counts when an executable sits beside resources/app.asar or resources/app.
/// </summary>
public static class ElectronAppCatalog
{
    public const string InstallSource = "install-dir";
    public const string StartMenuSource = "start-menu";

    public static IReadOnlyList<DiscoveredElectronApp> Discover(IEnumerable<string> roots)
    {
        var found = new List<DiscoveredElectronApp>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots)
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) continue;
            var info = new DirectoryInfo(root);
            if (info.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
            Walk(info, 0, found, seen);
        }
        return found;
    }

    public static bool HasElectronLayout(string executablePath)
    {
        var directory = Path.GetDirectoryName(executablePath);
        if (string.IsNullOrEmpty(directory)) return false;
        var resources = Path.Combine(directory, "resources");
        return File.Exists(Path.Combine(resources, "app.asar")) || Directory.Exists(Path.Combine(resources, "app"));
    }

    /// <summary>
    /// Launch when the app is Electron and not already open without a debugging port.
    /// The port and the isolated profile are chosen by <see cref="ElectronTargetLauncher"/>.
    /// </summary>
    public static AttachDecision Decide(string executablePath, bool processAlreadyRunning, bool debugPortOpen)
    {
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
            return new AttachDecision("missing", "请重新选择程序。");
        if (!HasElectronLayout(executablePath))
            return new AttachDecision("unsupported", "这个程序还不能自动测试。");
        if (processAlreadyRunning && !debugPortOpen)
            return new AttachDecision("busy", "这个程序已经开着，需要由工作台重新打开它");
        return new AttachDecision("launch", "已连接");
    }

    public static IReadOnlyList<string> ReadShortcutTargets(byte[] bytes)
    {
        if (bytes.Length is < 4 or > 65536) return [];
        var text = Encoding.Unicode.GetString(bytes);
        var found = new List<string>();
        foreach (var chunk in text.Split('\0', '\r', '\n', '"'))
        {
            var value = chunk.Trim();
            if (value.Length is < 8 or > 260) continue;
            if (!value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;
            if (!value.Contains('\\') && !value.Contains('/')) continue;
            if (value.IndexOfAny(['*', '?', '<', '>', '|']) >= 0) continue;
            if (!found.Contains(value, StringComparer.OrdinalIgnoreCase)) found.Add(value);
        }
        return found;
    }

    private static void Walk(DirectoryInfo directory, int depth, List<DiscoveredElectronApp> found, HashSet<string> seen)
    {
        if (depth > 6 || found.Count >= 40) return;
        FileSystemInfo[] entries;
        try
        {
            entries = directory.GetFileSystemInfos();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return;
        }
        foreach (var entry in entries)
        {
            if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
            if (entry is DirectoryInfo child)
            {
                Walk(child, depth + 1, found, seen);
                continue;
            }
            if (entry is not FileInfo file) continue;
            if (file.Extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase))
                AddShortcuts(file, found, seen);
            else if (file.Extension.Equals(".exe", StringComparison.OrdinalIgnoreCase))
                AddExecutable(file.FullName, InstallSource, found, seen);
        }
    }

    private static void AddShortcuts(FileInfo file, List<DiscoveredElectronApp> found, HashSet<string> seen)
    {
        if (file.Length is < 4 or > 65536) return;
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(file.FullName);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return;
        }
        foreach (var target in ReadShortcutTargets(bytes))
        {
            if (!File.Exists(target)) continue;
            AddExecutable(target, StartMenuSource, found, seen);
        }
    }

    private static void AddExecutable(string path, string source, List<DiscoveredElectronApp> found, HashSet<string> seen)
    {
        string full;
        try
        {
            full = Path.GetFullPath(path);
        }
        catch (Exception error) when (error is IOException or ArgumentException or NotSupportedException)
        {
            return;
        }
        if (!seen.Add(full) || !HasElectronLayout(full)) return;
        var name = Path.GetFileNameWithoutExtension(full);
        if (string.IsNullOrWhiteSpace(name)) return;
        found.Add(new DiscoveredElectronApp(name, full, source));
    }
}
