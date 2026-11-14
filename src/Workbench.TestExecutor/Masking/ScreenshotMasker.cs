namespace Workbench.TestExecutor;

public sealed class ScreenshotMasker
{
    public static readonly Rgba8 Ink = new(17, 17, 17, 255);

    public static MaskedImage Apply(byte[] png, IReadOnlyList<PixelRect> regions)
    {
        var image = PngImage.Decode(png);
        foreach (var region in regions) Paint(image, region);
        return new MaskedImage(image.Encode());
    }

    private static void Paint(PngImage image, PixelRect region)
    {
        var left = Math.Clamp(region.X, 0, image.Width);
        var top = Math.Clamp(region.Y, 0, image.Height);
        var right = Math.Clamp(region.X + Math.Max(region.Width, 0), 0, image.Width);
        var bottom = Math.Clamp(region.Y + Math.Max(region.Height, 0), 0, image.Height);
        for (var y = top; y < bottom; y++)
        {
            for (var x = left; x < right; x++) image[x, y] = Ink;
        }
    }
}

public sealed class OriginalScreenshotStore
{
    public static readonly TimeSpan Retention = TimeSpan.FromDays(7);
    private readonly string _root;
    private readonly TimeProvider _clock;

    public OriginalScreenshotStore(string root, TimeProvider? clock = null)
    {
        _root = Path.GetFullPath(root);
        _clock = clock ?? TimeProvider.System;
    }

    public string Root => _root;

    public async Task SaveAsync(byte[] png, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_root);
        var name = _clock.GetUtcNow().ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture)
            + "-" + Guid.NewGuid().ToString("N") + ".png";
        var path = ContainedPath.Combine(_root, name);
        await File.WriteAllBytesAsync(path, png, cancellationToken);
    }

    public int PurgeExpired()
    {
        if (!Directory.Exists(_root)) return 0;
        var directory = new DirectoryInfo(_root);
        if (directory.Attributes.HasFlag(FileAttributes.ReparsePoint)) return 0;
        var cutoff = _clock.GetUtcNow() - Retention;
        var removed = 0;
        foreach (var file in directory.EnumerateFiles("*.png"))
        {
            if (file.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
            if (!TryReadStamp(file.Name, out var stamp) || stamp > cutoff) continue;
            file.Delete();
            removed++;
        }
        return removed;
    }

    private static bool TryReadStamp(string name, out DateTimeOffset stamp)
    {
        stamp = default;
        var separator = name.IndexOf('-', StringComparison.Ordinal);
        if (separator <= 0) return false;
        if (!long.TryParse(name.AsSpan(0, separator), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var unixMs))
            return false;
        if (unixMs < 0) return false;
        stamp = DateTimeOffset.FromUnixTimeMilliseconds(unixMs);
        return true;
    }
}

public static class SyntheticFrames
{
    public static RawFrame PasswordField()
    {
        var rgba = new byte[32 * 32 * 4];
        for (var index = 0; index < rgba.Length; index += 4)
        {
            rgba[index] = 10;
            rgba[index + 1] = 20;
            rgba[index + 2] = 200;
            rgba[index + 3] = 255;
        }
        var image = PngImage.Create(32, 32, rgba);
        for (var y = 8; y < 16; y++)
        {
            for (var x = 8; x < 24; x++) image[x, y] = new Rgba8(240, 20, 20, 255);
        }
        return new RawFrame(image.Encode(), [new PixelRect(8, 8, 16, 8)]);
    }
}
