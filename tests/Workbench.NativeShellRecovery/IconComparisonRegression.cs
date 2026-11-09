using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

// Runs only in the owned Windows PowerShell driver. Every HICON is local to this test.
public static class IconComparisonRegression
{
    private sealed class InjectedFailure : Exception { }
    private static void Require(bool condition) { if (!condition) throw new InvalidOperationException(); }

    // GetIconInfo creates bitmap copies; delete them even when only probing a handle.
    private static bool IsValidIcon(IntPtr handle)
    {
        IconInfo info;
        if (!GetIconInfo(handle, out info)) return false;
        bool maskDeleted = info.Mask == IntPtr.Zero || DeleteObject(info.Mask);
        bool colorDeleted = info.Color == IntPtr.Zero || DeleteObject(info.Color);
        Require(maskDeleted && colorDeleted);
        return true;
    }

    private static bool CheckReferenceLifetime(string asset, Icon icon, IntPtr candidateHandle, IconComparisonTrace trace, bool expectReference, bool injectFailure)
    {
        IntPtr observed = IntPtr.Zero; int observations = 0; bool injected = false, result = false;
        try
        {
            result = OwnedShellHost.IconAssetMatches(asset, icon, trace, delegate(IntPtr reference)
            {
                observations++; observed = reference;
                Require(reference != candidateHandle && IsValidIcon(reference));
                if (injectFailure) throw new InjectedFailure();
            });
        }
        catch (InjectedFailure) { injected = true; }
        Require(injected == injectFailure && observations == (expectReference ? 1 : 0));
        if (expectReference) Require(!IsValidIcon(observed));
        Require(IsValidIcon(candidateHandle));
        return result;
    }

    private static void CheckInvalidAssets(Icon icon, IntPtr candidateHandle, IconComparisonTrace trace)
    {
        string directory = Path.Combine(Path.GetTempPath(), "workbench-icon-regression-" + Guid.NewGuid().ToString("N"));
        OwnedShellHost.CreateDirectoryExclusive(directory);
        string malformed = Path.Combine(directory, "malformed.ico");
        string undecodable = Path.Combine(directory, "undecodable.ico");
        try
        {
            File.WriteAllBytes(malformed, new byte[] { 0, 0, 1 });
            trace.Begin("regression16");
            Require(!OwnedShellHost.IconAssetMatches(malformed, icon, trace));
            trace.Begin("regression16");
            Require(!CheckReferenceLifetime(malformed, icon, candidateHandle, trace, false, false));
            Require(trace.Stage == "directory");

            // Structurally valid 16px directory with an invalid image payload.
            File.WriteAllBytes(undecodable, new byte[] { 0,0,1,0,1,0, 16,16,0,0,1,0,32,0,1,0,0,0,22,0,0,0, 0 });
            trace.Begin("regression16");
            Require(!OwnedShellHost.IconAssetMatches(undecodable, icon, trace));
            trace.Begin("regression16");
            Require(!CheckReferenceLifetime(undecodable, icon, candidateHandle, trace, false, false));
            Require(trace.Stage == "reference");
        }
        finally { File.Delete(malformed); File.Delete(undecodable); Directory.Delete(directory); }
    }

    private static int[] CheckAssetHandle(string asset, int size, string subject, IconComparisonTrace trace)
    {
        trace.Begin(subject);
        trace.SetStage("load"); int[] legacy = null;
        IntPtr handle = LoadImageW(IntPtr.Zero, asset, 1, size, size, 0x10);
        if (handle == IntPtr.Zero) throw new InvalidOperationException();
        try
        {
            using (Icon icon = Icon.FromHandle(handle))
            {
                trace.SetStage("bitmap");
                using (Bitmap bitmap = icon.ToBitmap())
                {
                    trace.ActualWidth = bitmap.Width; trace.ActualHeight = bitmap.Height;
                    Require(bitmap.Width == size && bitmap.Height == size);
                }
                if (size == 16)
                {
                    // Characterize the old fixed loop if the constructor keeps the smaller image.
                    trace.SetStage("legacy");
                    using (var requested = new Icon(icon, 32, 32)) using (Bitmap bitmap = requested.ToBitmap())
                    {
                        bool threw = false;
                        if (bitmap.Width < 32 || bitmap.Height < 32)
                        {
                            try { bitmap.GetPixel(31,31); } catch (ArgumentOutOfRangeException) { threw = true; }
                            Require(threw);
                        }
                        legacy = new int[] { bitmap.Width, bitmap.Height, threw ? 1 : 0 };
                    }
                }
                bool matches = OwnedShellHost.IconAssetMatches(asset, icon, trace);
                Require(size == 17 ? !matches : matches);
                if (size == 17) Require(trace.Stage == "directory" && trace.ReferenceWidth == 0 && trace.ReferenceHeight == 0);
                else Require(trace.Stage == "complete" && trace.ReferenceWidth == size && trace.ReferenceHeight == size);
                trace.Begin(subject);
                matches = CheckReferenceLifetime(asset, icon, handle, trace, size != 17, false);
                Require(size == 17 ? !matches : matches);
                if (size == 16)
                {
                    // An exception after acquisition must release the reference, not the candidate.
                    trace.Begin(subject);
                    CheckReferenceLifetime(asset, icon, handle, trace, true, true);
                    CheckInvalidAssets(icon, handle, trace);
                }
                return legacy;
            }
        }
        finally { if (!DestroyIcon(handle)) throw new InvalidOperationException(); }
    }

    public static int[] Run(string asset, IconComparisonTrace trace)
    {
        int[] legacy = CheckAssetHandle(asset, 16, "regression16", trace);
        CheckAssetHandle(asset, 32, "regression32", trace);
        trace.Begin("regressionMismatch");
        using (var bitmap = new Bitmap(16,16,PixelFormat.Format32bppArgb))
        {
            using (var graphics = Graphics.FromImage(bitmap)) graphics.Clear(Color.Magenta);
            IntPtr handle = bitmap.GetHicon();
            if (handle == IntPtr.Zero) throw new InvalidOperationException();
            try
            {
                using (Icon icon = Icon.FromHandle(handle))
                {
                    trace.SetStage("bitmap");
                    using (Bitmap observed = icon.ToBitmap())
                    {
                        trace.ActualWidth = observed.Width; trace.ActualHeight = observed.Height;
                        Require(observed.Width == 16 && observed.Height == 16);
                        int magenta = 0;
                        for (int y=0; y<16; y++) for (int x=0; x<16; x++)
                        {
                            Color pixel = observed.GetPixel(x,y);
                            if (pixel.A != 0 && pixel.R > 200 && pixel.G < 30 && pixel.B > 200) magenta++;
                        }
                        Require(magenta > 32);
                    }
                    Require(!OwnedShellHost.IconAssetMatches(asset, icon, trace));
                    Require(trace.Stage == "pixels" && trace.ReferenceWidth == 16 && trace.ReferenceHeight == 16 && trace.Difference != null);
                    trace.Begin("regressionMismatch");
                    Require(!CheckReferenceLifetime(asset, icon, handle, trace, true, false));
                    Require(trace.Stage == "pixels" && trace.ReferenceWidth == 16 && trace.ReferenceHeight == 16 && trace.Difference != null);
                }
            }
            finally { if (!DestroyIcon(handle)) throw new InvalidOperationException(); }
        }
        CheckAssetHandle(asset, 17, "regressionMissing", trace);
        return legacy;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IconInfo
    {
        [MarshalAs(UnmanagedType.Bool)] public bool IsIcon;
        public uint XHotspot, YHotspot;
        public IntPtr Mask, Color;
    }
    [DllImport("user32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    private static extern IntPtr LoadImageW(IntPtr instance, string name, uint type, int width, int height, uint flags);
    [DllImport("user32.dll", SetLastError=true)]
    private static extern bool DestroyIcon(IntPtr icon);
    [DllImport("user32.dll", SetLastError=true)]
    private static extern bool GetIconInfo(IntPtr icon, out IconInfo info);
    [DllImport("gdi32.dll", SetLastError=true)]
    private static extern bool DeleteObject(IntPtr obj);
}
