namespace Workbench.TestExecutor;

public static class VisualDiff
{
    public static double MismatchRatio(byte[] actual, byte[] baseline, int channelTolerance, IReadOnlyList<PixelRect> ignore)
    {
        var left = PngImage.Decode(actual);
        var right = PngImage.Decode(baseline);
        if (left.Width != right.Width || left.Height != right.Height) return 1;
        var compared = 0;
        var mismatched = 0;
        for (var y = 0; y < left.Height; y++)
        {
            for (var x = 0; x < left.Width; x++)
            {
                if (Ignored(ignore, x, y)) continue;
                compared++;
                var a = left[x, y];
                var b = right[x, y];
                if (Math.Abs(a.R - b.R) > channelTolerance || Math.Abs(a.G - b.G) > channelTolerance || Math.Abs(a.B - b.B) > channelTolerance)
                    mismatched++;
            }
        }
        return compared == 0 ? 0 : (double)mismatched / compared;
    }

    public static byte[] DiffImage(byte[] actual, byte[] baseline, int channelTolerance, IReadOnlyList<PixelRect> ignore)
    {
        var left = PngImage.Decode(actual);
        var right = PngImage.Decode(baseline);
        var width = Math.Min(left.Width, right.Width);
        var height = Math.Min(left.Height, right.Height);
        if (width < 1 || height < 1) throw new ExecutorDataException(ExecutorError.CaptureFailed);
        var image = PngImage.Create(width, height, new byte[width * height * 4]);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var pixel = left[x, y];
                var same = x < right.Width && y < right.Height && !ChannelDiffers(pixel, right[x, y], channelTolerance);
                image[x, y] = Ignored(ignore, x, y) || same
                    ? new Rgba8(pixel.R, pixel.G, pixel.B, 255)
                    : new Rgba8(255, 0, 0, 255);
            }
        }
        return image.Encode();
    }

    private static bool ChannelDiffers(Rgba8 left, Rgba8 right, int channelTolerance)
        => Math.Abs(left.R - right.R) > channelTolerance || Math.Abs(left.G - right.G) > channelTolerance || Math.Abs(left.B - right.B) > channelTolerance;

    private static bool Ignored(IReadOnlyList<PixelRect> ignore, int x, int y)
        => ignore.Any(rect => x >= rect.X && y >= rect.Y && x < rect.X + rect.Width && y < rect.Y + rect.Height);
}
