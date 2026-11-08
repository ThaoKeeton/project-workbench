namespace Workbench.App;

// Portable pixel policy shared by the native capture path and Core tests.
internal static class NativeGalleryPolicy
{
    internal const int MaxDimension = 4096;
    internal const int MaxBgraBytes = 67_108_864;
    internal const int MaxPngBytes = 16_777_216;

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
