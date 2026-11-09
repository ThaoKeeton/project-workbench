using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

// Runs only in the owned Windows PowerShell driver. Every HICON is local to this test.
public static class IconComparisonRegression
{
    private static void Require(bool condition) { if (!condition) throw new InvalidOperationException(); }

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
                }
            }
            finally { if (!DestroyIcon(handle)) throw new InvalidOperationException(); }
        }
        CheckAssetHandle(asset, 17, "regressionMissing", trace);
        return legacy;
    }

    [DllImport("user32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    private static extern IntPtr LoadImageW(IntPtr instance, string name, uint type, int width, int height, uint flags);
    [DllImport("user32.dll", SetLastError=true)]
    private static extern bool DestroyIcon(IntPtr icon);
}
