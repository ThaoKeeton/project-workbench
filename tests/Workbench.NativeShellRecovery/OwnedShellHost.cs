using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

// This helper only creates normal Workbench processes and inspects their owned windows.
public sealed class OwnedShellHost : IDisposable
{
    private sealed class Handle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public Handle() : base(true) { }
        public Handle(IntPtr value) : base(true) { SetHandle(value); }
        protected override bool ReleaseHandle() { return CloseHandle(handle); }
    }
    private Handle job, process;
    public int Id { get; private set; }
    public long Birth { get; private set; }
    public bool Exited { get { return WaitForSingleObject(process, 0) == 0; } }
    public int ExitCode { get { uint c; if (!Exited || !GetExitCodeProcess(process, out c)) throw new InvalidOperationException(); return unchecked((int)c); } }
    private static long FileTime(FILETIME f) { return ((long)f.High << 32) | f.Low; }
    private long ReadBirth() { FILETIME a,b,c,d; if (!GetProcessTimes(process, out a, out b, out c, out d)) throw new InvalidOperationException(); return FileTime(a); }
    private bool AliveIdentity() { return process != null && !process.IsInvalid && !Exited && GetProcessId(process) == (uint)Id && ReadBirth() == Birth; }
    public void Validate(IntPtr window)
    {
        uint id;
        if (!AliveIdentity() || window == IntPtr.Zero || !IsWindow(window) || !IsWindowVisible(window) || GetWindowThreadProcessId(window, out id) == 0 || id != (uint)Id)
            throw new InvalidOperationException();
    }
    public static bool PathExists(string path)
    {
        uint a = GetFileAttributesW(path); if (a != 0xffffffff) return true;
        int e = Marshal.GetLastWin32Error(); if (e == 2 || e == 3) return false; throw new InvalidOperationException();
    }
    // Creation must fail if any normal profile appeared after preflight.
    public static void CreateDirectoryExclusive(string path)
    {
        if (!CreateDirectoryW(path, IntPtr.Zero)) throw new InvalidOperationException();
    }
    public static void RejectLinks(string path)
    {
        for (string p = Path.GetFullPath(path); p != null; p = Path.GetDirectoryName(p))
        {
            uint a = GetFileAttributesW(p);
            if (a != 0xffffffff) { if ((a & 0x400) != 0) throw new InvalidOperationException(); }
            else { int e = Marshal.GetLastWin32Error(); if (e != 2 && e != 3) throw new InvalidOperationException(); }
        }
    }
    private static IntPtr CleanEnvironment()
    {
        string[] names = { "SystemRoot", "WINDIR", "SystemDrive", "ComSpec", "TEMP", "TMP", "USERPROFILE", "LOCALAPPDATA", "APPDATA", "HOMEDRIVE", "HOMEPATH", "ProgramData", "ProgramFiles", "ProgramFiles(x86)", "ProgramW6432", "CommonProgramFiles", "CommonProgramFiles(x86)", "CommonProgramW6432", "OS", "PROCESSOR_ARCHITECTURE", "PROCESSOR_IDENTIFIER", "NUMBER_OF_PROCESSORS", "USERNAME", "USERDOMAIN", "COMPUTERNAME", "SESSIONNAME" };
        var values = new SortedDictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach (string name in names) { string value = Environment.GetEnvironmentVariable(name); if (!String.IsNullOrEmpty(value)) values[name] = value; }
        string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        values["PATH"] = Path.Combine(windows, "System32") + ";" + windows;
        var block = new StringBuilder(); foreach (var pair in values) block.Append(pair.Key).Append('=').Append(pair.Value).Append('\0'); block.Append('\0');
        return Marshal.StringToHGlobalUni(block.ToString());
    }
    public static OwnedShellHost Start(string executable)
    {
        var host = new OwnedShellHost(); Handle thread = null; IntPtr environment = IntPtr.Zero;
        try
        {
            host.job = CreateJobObjectW(IntPtr.Zero, null); if (host.job.IsInvalid) throw new InvalidOperationException();
            var limits = new ExtendedLimits(); limits.Basic.LimitFlags = 0x2000;
            if (!SetInformationJobObject(host.job, 9, ref limits, Marshal.SizeOf(typeof(ExtendedLimits)))) throw new InvalidOperationException();
            var startup = new StartupInfo(); startup.Size = Marshal.SizeOf(typeof(StartupInfo)); ProcessInfo created;
            environment = CleanEnvironment();
            if (!CreateProcessW(executable, new StringBuilder("\"" + executable + "\""), IntPtr.Zero, IntPtr.Zero, false, 0x00000004 | 0x00000400, environment, Path.GetDirectoryName(executable), ref startup, out created)) throw new InvalidOperationException();
            host.process = new Handle(created.Process); thread = new Handle(created.Thread); host.Id = checked((int)created.ProcessId); host.Birth = host.ReadBirth();
            if (host.Birth <= 0 || !AssignProcessToJobObject(host.job, host.process) || ResumeThread(thread) == 0xffffffff) throw new InvalidOperationException();
            return host;
        }
        catch { if (host.process != null && !host.process.IsInvalid) { TerminateProcess(host.process, 1); WaitForSingleObject(host.process, 5000); } host.Dispose(); throw; }
        finally { if (thread != null) thread.Dispose(); if (environment != IntPtr.Zero) Marshal.FreeHGlobal(environment); }
    }
    public IntPtr Window()
    {
        if (!AliveIdentity()) throw new InvalidOperationException(); IntPtr found = IntPtr.Zero; int count = 0;
        if (!EnumWindows(delegate(IntPtr w, IntPtr u) { uint id; GetWindowThreadProcessId(w, out id); if (id == (uint)Id && IsWindowVisible(w) && GetWindow(w, 4) == IntPtr.Zero) { found = w; count++; } return true; }, IntPtr.Zero)) throw new InvalidOperationException();
        if (count > 1) throw new InvalidOperationException(); if (count == 1) Validate(found); return found;
    }
    private bool OwnsPopup(IntPtr popup, IntPtr root)
    {
        IntPtr p = popup; for (int i=0; i<16; i++) { p = GetWindow(p, 4); if (p == root) return true; if (p == IntPtr.Zero) return false; } return false;
    }
    public IntPtr Popup(IntPtr root)
    {
        Validate(root); IntPtr found = IntPtr.Zero; int count = 0;
        if (!EnumWindows(delegate(IntPtr w, IntPtr u) { if (w != root && IsWindowVisible(w) && OwnsPopup(w, root)) { found = w; count++; } return true; }, IntPtr.Zero)) throw new InvalidOperationException();
        Validate(root); if (count > 1) throw new InvalidOperationException(); return found;
    }
    public void ValidatePopup(IntPtr root, IntPtr popup)
    {
        Validate(root); if (popup == IntPtr.Zero || !IsWindow(popup) || !IsWindowVisible(popup) || !OwnsPopup(popup, root)) throw new InvalidOperationException();
    }
    public void SubmitPickerAddress(IntPtr root, IntPtr popup, IntPtr edit)
    {
        ValidatePopup(root, popup);
        if (edit == IntPtr.Zero || !IsWindow(edit) || !IsChild(popup, edit)) throw new InvalidOperationException();
        if (!PostMessage(edit, 0x0100, new IntPtr(13), IntPtr.Zero) || !PostMessage(edit, 0x0101, new IntPtr(13), IntPtr.Zero)) throw new InvalidOperationException();
        ValidatePopup(root, popup);
    }
    public static bool WindowExists(IntPtr window) { return IsWindow(window); }
    public int LiveCount()
    {
        int size = 8 + 256 * IntPtr.Size; IntPtr buffer = Marshal.AllocHGlobal(size);
        try { int returned; if (!QueryInformationJobObject(job, 3, buffer, size, out returned)) throw new InvalidOperationException(); int count = Marshal.ReadInt32(buffer, 4); if (count < 0 || count > 256) throw new InvalidOperationException(); return count; }
        finally { Marshal.FreeHGlobal(buffer); }
    }
    public int[] Capture(IntPtr window, string destination)
    {
        Validate(window); Rect before, after; if (!GetWindowRect(window, out before)) throw new InvalidOperationException();
        int width = before.Right-before.Left, height = before.Bottom-before.Top;
        if (width < 400 || height < 300 || width > 4096 || height > 4096) throw new InvalidOperationException();
        using (var bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb))
        {
            using (var graphics = Graphics.FromImage(bitmap)) { IntPtr dc = graphics.GetHdc(); try { if (!PrintWindow(window, dc, 2)) throw new InvalidOperationException(); } finally { graphics.ReleaseHdc(dc); } }
            int baseline = bitmap.GetPixel(width/2,height/2).ToArgb(), varied = 0; int[] coverage = new int[4];
            for (int y=8; y<height-8; y+=8) for (int x=8; x<width-8; x+=8) { Color c = bitmap.GetPixel(x,y); if (c.ToArgb()!=baseline) varied++; if (c.R+c.G+c.B>12) coverage[(y>=height/2?2:0)+(x>=width/2?1:0)]++; }
            if (varied < 32) throw new InvalidOperationException(); foreach (int c in coverage) if (c < 32) throw new InvalidOperationException();
            Validate(window); if (!GetWindowRect(window, out after) || before.Left!=after.Left || before.Top!=after.Top || before.Right!=after.Right || before.Bottom!=after.Bottom) throw new InvalidOperationException();
            using (var file = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None)) bitmap.Save(file, ImageFormat.Png);
        }
        Validate(window); return new int[] { before.Left,before.Top,before.Right,before.Bottom };
    }
    private static bool SamePixels(Icon a, Icon b)
    {
        using (var aa = new Icon(a,32,32)) using (var bb = new Icon(b,32,32)) using (Bitmap x=aa.ToBitmap()) using (Bitmap y=bb.ToBitmap())
        { int varied=0; for(int j=0;j<32;j++) for(int i=0;i<32;i++) { if(x.GetPixel(i,j).ToArgb()!=y.GetPixel(i,j).ToArgb()) return false; if(x.GetPixel(i,j).A!=0) varied++; } return varied>32; }
    }
    public bool IconMatches(IntPtr window, string executable, string asset)
    {
        Validate(window); IntPtr h = SendMessage(window, 0x7f, new IntPtr(1), IntPtr.Zero); if (h == IntPtr.Zero) h = GetClassLongPtr(window, -14);
        if (h == IntPtr.Zero) return false;
        using (var file = new Icon(asset,32,32)) using (var embedded = Icon.ExtractAssociatedIcon(executable)) using (var actual = (Icon)Icon.FromHandle(h).Clone())
        { bool same = embedded != null && SamePixels(file, embedded) && SamePixels(file, actual); Validate(window); return same; }
    }
    public void TerminateOwned() { if (!TerminateJobObject(job, 1)) throw new InvalidOperationException(); }
    public void Dispose() { if (process != null) { process.Dispose(); process=null; } if (job != null) { job.Dispose(); job=null; } }
    [StructLayout(LayoutKind.Sequential)] private struct FILETIME { public uint Low, High; }
    [StructLayout(LayoutKind.Sequential)] private struct BasicLimits { public long ProcessTime, JobTime; public uint LimitFlags; public UIntPtr Minimum, Maximum; public uint Active; public UIntPtr Affinity; public uint Priority, Scheduling; }
    [StructLayout(LayoutKind.Sequential)] private struct IoCounters { public ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes; }
    [StructLayout(LayoutKind.Sequential)] private struct ExtendedLimits { public BasicLimits Basic; public IoCounters Io; public UIntPtr ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory; }
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] private struct StartupInfo { public int Size; public string Reserved,Desktop,Title; public uint X,Y,Width,Height,XCount,YCount,Fill,Flags; public ushort Show,ReservedSize; public IntPtr ReservedPointer,Input,Output,Error; }
    [StructLayout(LayoutKind.Sequential)] private struct ProcessInfo { public IntPtr Process,Thread; public uint ProcessId,ThreadId; }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left,Top,Right,Bottom; }
    private delegate bool WindowCallback(IntPtr w, IntPtr u);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern uint GetFileAttributesW(string p);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern bool CreateDirectoryW(string path, IntPtr security);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern Handle CreateJobObjectW(IntPtr a,string n);
    [DllImport("kernel32.dll",SetLastError=true)] private static extern bool SetInformationJobObject(Handle j,int k,ref ExtendedLimits v,int l);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern bool CreateProcessW(string e,StringBuilder c,IntPtr a,IntPtr b,bool i,uint f,IntPtr env,string d,ref StartupInfo s,out ProcessInfo p);
    [DllImport("kernel32.dll",SetLastError=true)] private static extern bool AssignProcessToJobObject(Handle j,Handle p);
    [DllImport("kernel32.dll",SetLastError=true)] private static extern uint ResumeThread(Handle t);
    [DllImport("kernel32.dll",SetLastError=true)] private static extern uint WaitForSingleObject(Handle h,uint ms);
    [DllImport("kernel32.dll",SetLastError=true)] private static extern bool GetExitCodeProcess(Handle h,out uint c);
    [DllImport("kernel32.dll",SetLastError=true)] private static extern uint GetProcessId(Handle h);
    [DllImport("kernel32.dll",SetLastError=true)] private static extern bool GetProcessTimes(Handle h,out FILETIME a,out FILETIME b,out FILETIME c,out FILETIME d);
    [DllImport("kernel32.dll",SetLastError=true)] private static extern bool QueryInformationJobObject(Handle h,int k,IntPtr b,int l,out int r);
    [DllImport("kernel32.dll",SetLastError=true)] private static extern bool TerminateJobObject(Handle h,uint c);
    [DllImport("kernel32.dll",SetLastError=true)] private static extern bool TerminateProcess(Handle h,uint c);
    [DllImport("user32.dll",SetLastError=true)] private static extern bool EnumWindows(WindowCallback c,IntPtr u);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr w,out uint p);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr w);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr w);
    [DllImport("user32.dll")] private static extern bool IsChild(IntPtr parent, IntPtr child);
    [DllImport("user32.dll",SetLastError=true)] private static extern bool PostMessage(IntPtr w,uint message,IntPtr a,IntPtr b);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr w,uint command);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr w,out Rect r);
    [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr w,IntPtr dc,uint flags);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr w,uint m,IntPtr a,IntPtr b);
    [DllImport("user32.dll",EntryPoint="GetClassLongPtrW")] private static extern IntPtr GetClassLongPtr(IntPtr w,int index);
}
