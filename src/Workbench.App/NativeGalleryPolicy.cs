namespace Workbench.App;

internal readonly record struct NativeGalleryBounds(double X, double Y, double Width, double Height);

// Portable geometry and pixel policy shared by the native capture path and Core tests.
internal static class NativeGalleryPolicy
{
    internal const int MaxDimension = 4096;
    internal const int MaxBgraBytes = 67_108_864;
    internal const int MaxPngBytes = 16_777_216;

    internal static bool TryIntersect(NativeGalleryBounds first, NativeGalleryBounds second,
        out NativeGalleryBounds intersection)
    {
        intersection = default;
        static bool Valid(NativeGalleryBounds bounds) =>
            double.IsFinite(bounds.X) && double.IsFinite(bounds.Y)
            && double.IsFinite(bounds.Width) && double.IsFinite(bounds.Height)
            && bounds.Width > 0 && bounds.Height > 0
            && double.IsFinite(bounds.X + bounds.Width) && double.IsFinite(bounds.Y + bounds.Height)
            && bounds.X + bounds.Width > bounds.X && bounds.Y + bounds.Height > bounds.Y;
        if (!Valid(first) || !Valid(second)) return false;
        var left = Math.Max(first.X, second.X);
        var top = Math.Max(first.Y, second.Y);
        var right = Math.Min(first.X + first.Width, second.X + second.Width);
        var bottom = Math.Min(first.Y + first.Height, second.Y + second.Height);
        var candidate = new NativeGalleryBounds(left, top, right - left, bottom - top);
        if (!Valid(candidate)) return false;
        intersection = candidate;
        return true;
    }

    internal static bool IsValidFrame(ReadOnlySpan<byte> bgra, int width, int height)
    {
        if (width is < 1 or > MaxDimension || height is < 1 or > MaxDimension) return false;
        var length = (long)width * height * 4;
        if (length > MaxBgraBytes || bgra.Length != length) return false;
        var visible = false;
        var varied = false;
        for (var i = 0; i < bgra.Length; i += 4)
        {
            visible |= bgra[i + 3] != 0;
            if (i != 0 && !bgra.Slice(i, 4).SequenceEqual(bgra.Slice(0, 4))) varied = true;
        }
        return visible && varied;
    }
}
