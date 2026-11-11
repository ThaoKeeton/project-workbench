using System.Buffers.Binary;
using System.IO.Compression;

namespace Workbench.TestExecutor;

public readonly record struct Rgba8(byte R, byte G, byte B, byte A);

public sealed class PngImage
{
    public const int MaxEdge = 8192;
    private readonly byte[] _rgba;

    private PngImage(int width, int height, byte[] rgba)
    {
        Width = width;
        Height = height;
        _rgba = rgba;
    }

    public int Width { get; }

    public int Height { get; }

    public Rgba8 this[int x, int y]
    {
        get
        {
            var index = (y * Width + x) * 4;
            return new Rgba8(_rgba[index], _rgba[index + 1], _rgba[index + 2], _rgba[index + 3]);
        }
        set
        {
            var index = (y * Width + x) * 4;
            _rgba[index] = value.R;
            _rgba[index + 1] = value.G;
            _rgba[index + 2] = value.B;
            _rgba[index + 3] = value.A;
        }
    }

    public static PngImage Create(int width, int height, byte[] rgba)
    {
        if (width is < 1 or > MaxEdge || height is < 1 or > MaxEdge || rgba.Length != width * height * 4)
            throw new ExecutorDataException(ExecutorError.CaptureFailed);
        return new PngImage(width, height, rgba);
    }

    public static PngImage Decode(byte[] png)
    {
        if (png is not { Length: > 8 } || png[0] != 137 || png[1] != 80 || png[2] != 78 || png[3] != 71)
            throw new ExecutorDataException(ExecutorError.CaptureFailed);
        var offset = 8;
        var width = 0;
        var height = 0;
        var colorType = (byte)0;
        var seenHeader = false;
        using var data = new MemoryStream();
        while (offset + 8 <= png.Length)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(offset, 4));
            if (length < 0 || offset + 12L + length > png.Length) throw new ExecutorDataException(ExecutorError.CaptureFailed);
            var type = System.Text.Encoding.ASCII.GetString(png, offset + 4, 4);
            var chunk = png.AsSpan(offset + 8, length);
            if (type == "IHDR")
            {
                if (length < 13) throw new ExecutorDataException(ExecutorError.CaptureFailed);
                width = BinaryPrimitives.ReadInt32BigEndian(chunk);
                height = BinaryPrimitives.ReadInt32BigEndian(chunk[4..]);
                if (width is < 1 or > MaxEdge || height is < 1 or > MaxEdge || chunk[8] != 8 || chunk[10] != 0 || chunk[11] != 0 || chunk[12] != 0)
                    throw new ExecutorDataException(ExecutorError.CaptureFailed);
                colorType = chunk[9];
                if (colorType is not (0 or 2 or 4 or 6)) throw new ExecutorDataException(ExecutorError.CaptureFailed);
                seenHeader = true;
            }
            else if (type == "IDAT")
            {
                data.Write(chunk);
            }
            else if (type == "IEND")
            {
                break;
            }
            offset += 12 + length;
        }
        if (!seenHeader) throw new ExecutorDataException(ExecutorError.CaptureFailed);
        var bytesPerPixel = colorType switch { 0 => 1, 2 => 3, 4 => 2, _ => 4 };
        var stride = width * bytesPerPixel;
        var inflated = Inflate(data.ToArray(), height * (stride + 1));
        if (inflated.Length < height * (stride + 1)) throw new ExecutorDataException(ExecutorError.CaptureFailed);
        var rgba = new byte[width * height * 4];
        var previous = new byte[stride];
        var current = new byte[stride];
        var cursor = 0;
        for (var y = 0; y < height; y++)
        {
            var filter = inflated[cursor++];
            Buffer.BlockCopy(inflated, cursor, current, 0, stride);
            cursor += stride;
            Unfilter(filter, current, previous, bytesPerPixel);
            Expand(colorType, current, rgba, y * width);
            Buffer.BlockCopy(current, 0, previous, 0, stride);
        }
        return new PngImage(width, height, rgba);
    }

    public byte[] Encode()
    {
        var stride = Width * 4;
        var raw = new byte[Height * (stride + 1)];
        for (var y = 0; y < Height; y++)
        {
            var row = y * (stride + 1);
            raw[row] = 0;
            Buffer.BlockCopy(_rgba, y * stride, raw, row + 1, stride);
        }
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
            zlib.Write(raw);
        using var output = new MemoryStream();
        output.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, Width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), Height);
        header[8] = 8;
        header[9] = 6;
        WriteChunk(output, "IHDR", header);
        WriteChunk(output, "IDAT", compressed.ToArray());
        WriteChunk(output, "IEND", []);
        return output.ToArray();
    }

    internal static PngImage FromFiltered(int width, int height, byte colorType, byte[] scanlines)
    {
        var encoded = EncodeScanlines(width, height, colorType, scanlines);
        return Decode(encoded);
    }

    internal static byte[] EncodeScanlines(int width, int height, byte colorType, byte[] scanlines)
    {
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
            zlib.Write(scanlines);
        using var output = new MemoryStream();
        output.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8;
        header[9] = colorType;
        WriteChunk(output, "IHDR", header);
        WriteChunk(output, "IDAT", compressed.ToArray());
        WriteChunk(output, "IEND", []);
        return output.ToArray();
    }

    private static void Unfilter(byte filter, byte[] current, byte[] previous, int bytesPerPixel)
    {
        for (var index = 0; index < current.Length; index++)
        {
            var left = index >= bytesPerPixel ? current[index - bytesPerPixel] : (byte)0;
            var up = previous[index];
            var upperLeft = index >= bytesPerPixel ? previous[index - bytesPerPixel] : (byte)0;
            var predictor = filter switch
            {
                0 => 0,
                1 => left,
                2 => up,
                3 => (left + up) / 2,
                4 => Paeth(left, up, upperLeft),
                _ => throw new ExecutorDataException(ExecutorError.CaptureFailed)
            };
            current[index] = (byte)(current[index] + predictor);
        }
    }

    private static int Paeth(byte left, byte up, byte upperLeft)
    {
        var estimate = left + up - upperLeft;
        var leftDistance = Math.Abs(estimate - left);
        var upDistance = Math.Abs(estimate - up);
        var upperLeftDistance = Math.Abs(estimate - upperLeft);
        if (leftDistance <= upDistance && leftDistance <= upperLeftDistance) return left;
        return upDistance <= upperLeftDistance ? up : upperLeft;
    }

    private static void Expand(byte colorType, byte[] current, byte[] rgba, int pixelIndex)
    {
        var target = pixelIndex * 4;
        switch (colorType)
        {
            case 6:
                Buffer.BlockCopy(current, 0, rgba, target, current.Length);
                break;
            case 2:
                for (var pixel = 0; pixel < current.Length; pixel += 3)
                {
                    rgba[target++] = current[pixel];
                    rgba[target++] = current[pixel + 1];
                    rgba[target++] = current[pixel + 2];
                    rgba[target++] = 255;
                }
                break;
            case 0:
                foreach (var gray in current)
                {
                    rgba[target++] = gray;
                    rgba[target++] = gray;
                    rgba[target++] = gray;
                    rgba[target++] = 255;
                }
                break;
            default:
                for (var pixel = 0; pixel < current.Length; pixel += 2)
                {
                    rgba[target++] = current[pixel];
                    rgba[target++] = current[pixel];
                    rgba[target++] = current[pixel];
                    rgba[target++] = current[pixel + 1];
                }
                break;
        }
    }

    private static byte[] Inflate(byte[] data, int expected)
    {
        using var input = new MemoryStream(data);
        using var zlib = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream(expected);
        zlib.CopyTo(output);
        return output.ToArray();
    }

    private static void WriteChunk(Stream output, string type, byte[] data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        output.Write(length);
        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        output.Write(typeBytes);
        output.Write(data);
        var crc = Crc(typeBytes, data);
        Span<byte> crcBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc);
        output.Write(crcBytes);
    }

    private static uint Crc(byte[] type, byte[] data)
    {
        var crc = 0xFFFFFFFFu;
        crc = Update(crc, type);
        crc = Update(crc, data);
        return crc ^ 0xFFFFFFFFu;
    }

    private static uint Update(uint crc, ReadOnlySpan<byte> data)
    {
        foreach (var value in data)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
                crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
        }
        return crc;
    }
}
