using System.Text;
using Xunit;

namespace Workbench.TestExecutor.Tests;

public sealed class ElectronAppCatalogTests
{
    [Fact]
    public void Discover_reads_install_folders_and_start_menu_shortcuts()
    {
        var root = Fixtures.Temp();
        var cutter = Path.Combine(root, "Programs", "Cutter");
        Directory.CreateDirectory(Path.Combine(cutter, "resources"));
        File.WriteAllBytes(Path.Combine(cutter, "Cutter.exe"), [0x4D, 0x5A]);
        File.WriteAllText(Path.Combine(cutter, "resources", "app.asar"), "synthetic");
        var notepad = Path.Combine(root, "Programs", "Notepad");
        Directory.CreateDirectory(notepad);
        File.WriteAllBytes(Path.Combine(notepad, "Notepad.exe"), [0x4D, 0x5A]);

        var mill = Path.Combine(root, "Apps", "Mill");
        Directory.CreateDirectory(Path.Combine(mill, "resources", "app"));
        var millExe = Path.Combine(mill, "Mill.exe");
        File.WriteAllBytes(millExe, [0x4D, 0x5A]);
        var menu = Path.Combine(root, "Start Menu", "Programs");
        Directory.CreateDirectory(menu);
        var shortcut = Encoding.Unicode.GetBytes("\0" + millExe + "\0");
        File.WriteAllBytes(Path.Combine(menu, "Mill.lnk"), shortcut);
        File.WriteAllBytes(Path.Combine(menu, "Missing.lnk"), Encoding.Unicode.GetBytes(@"C:\missing\Gone.exe"));
        File.WriteAllBytes(Path.Combine(menu, "Notes.lnk"), Encoding.Unicode.GetBytes("not a path"));

        var installed = ElectronAppCatalog.Discover([Path.Combine(root, "Programs"), Path.Combine(root, "missing")]);
        Assert.Single(installed);
        Assert.Equal("Cutter", installed[0].Name);
        Assert.Equal(ElectronAppCatalog.InstallSource, installed[0].Source);
        Assert.DoesNotContain(installed, app => app.Name == "Notepad");

        var fromMenu = ElectronAppCatalog.Discover([menu, Path.Combine(root, "Apps")]);
        Assert.Single(fromMenu);
        Assert.Equal("Mill", fromMenu[0].Name);
        Assert.Equal(ElectronAppCatalog.StartMenuSource, fromMenu[0].Source);

        Assert.Equal("missing", ElectronAppCatalog.Decide(Path.Combine(root, "nope.exe"), false, false).Kind);
        Assert.Equal("unsupported", ElectronAppCatalog.Decide(Path.Combine(notepad, "Notepad.exe"), false, false).Kind);
        Assert.Equal("这个程序还不能自动测试。", ElectronAppCatalog.Decide(Path.Combine(notepad, "Notepad.exe"), false, false).Message);
        var busy = ElectronAppCatalog.Decide(Path.Combine(cutter, "Cutter.exe"), true, false);
        Assert.Equal("busy", busy.Kind);
        Assert.Equal("这个程序已经开着，需要由工作台重新打开它", busy.Message);
        Assert.Equal("launch", ElectronAppCatalog.Decide(Path.Combine(cutter, "Cutter.exe"), true, true).Kind);
        Assert.Equal("launch", ElectronAppCatalog.Decide(Path.Combine(cutter, "Cutter.exe"), false, false).Kind);
        Assert.Empty(ElectronAppCatalog.ReadShortcutTargets([1, 2, 3]));
        Assert.Empty(ElectronAppCatalog.Discover([]));
        Assert.Null(ExecutablePath.NormalizeLaunch("Cutter.exe"));
        Assert.Null(ExecutablePath.NormalizeLaunch(Path.Combine(cutter, "..", "Cutter.exe")));
        Assert.Null(ExecutablePath.NormalizeLaunch(Path.Combine(cutter, "Cutter.exe") + "\n"));
        Assert.Null(ExecutablePath.NormalizeInstalled(millExe, [menu]));
        Directory.Delete(root, true);
    }
}
