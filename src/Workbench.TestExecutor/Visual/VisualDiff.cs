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

    private static bool Ignored(IReadOnlyList<PixelRect> ignore, int x, int y)
        => ignore.Any(rect => x >= rect.X && y >= rect.Y && x < rect.X + rect.Width && y < rect.Y + rect.Height);
}
