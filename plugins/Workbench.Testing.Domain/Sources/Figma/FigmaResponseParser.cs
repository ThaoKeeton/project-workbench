using System.Text;
using System.Text.Json;

namespace Workbench.Testing.Domain.Sources.Figma;

internal static class FigmaResponseParser
{
    internal static FigmaCapture Parse(FigmaReadRequest request, byte[] bytes)
    {
        try
        {
            _ = new UTF8Encoding(false, true).GetCharCount(bytes);
            ValidateTokens(bytes);
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 });
            var root = document.RootElement;
            var name = Text(Property(root, "name"), 200, true);
            var version = Text(Property(root, "version"), 64, true);
            if (request.Version is { } expected && !string.Equals(version, expected, StringComparison.Ordinal)) throw Invalid();
            var nodes = Property(root, "nodes");
            if (nodes.ValueKind != JsonValueKind.Object) throw Invalid();
            var requested = new HashSet<string>(request.NodeIds, StringComparer.Ordinal);
            var count = 0;
            foreach (var entry in nodes.EnumerateObject())
            {
                if (!requested.Contains(entry.Name)) throw Invalid();
                count++;
            }
            if (count != requested.Count) throw Invalid();
            var selected = new List<FigmaNodeCapture>();
            foreach (var id in request.NodeIds)
            {
                var node = Property(Property(nodes, id), "document");
                if (Text(Property(node, "id"), 64, true) != id) throw Invalid();
                selected.Add(new(id, Text(Property(node, "name"), 200, true), Text(Property(node, "type"), 64, true),
                    node.TryGetProperty("characters", out var characters) ? Text(characters, 4096, false) : null));
            }
            return new(request, bytes, name, version, selected);
        }
        catch (Exception error) when (error is JsonException or DecoderFallbackException or InvalidOperationException or FormatException or ArgumentException)
        { throw Invalid(); }
    }

    private static JsonElement Property(JsonElement value, string name)
        => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var property) ? property : throw Invalid();

    private static string Text(JsonElement value, int maximum, bool required)
    {
        if (value.ValueKind != JsonValueKind.String) throw Invalid();
        var text = value.GetString()!;
        return FigmaInputCapture.Text(text, maximum, required) ? text : throw Invalid();
    }

    private static void ValidateTokens(ReadOnlySpan<byte> bytes)
    {
        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { MaxDepth = 32 });
        var objects = new Stack<HashSet<string>>();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.StartObject) objects.Push(new(StringComparer.Ordinal));
            else if (reader.TokenType == JsonTokenType.EndObject) objects.Pop();
            else if (reader.TokenType is JsonTokenType.PropertyName or JsonTokenType.String)
            {
                if (reader.ValueIsEscaped) ValidateEscapes(reader.ValueSpan);
                var text = reader.GetString()!;
                if (!FigmaInputCapture.Text(text, int.MaxValue, false)
                    || reader.TokenType == JsonTokenType.PropertyName && !objects.Peek().Add(text)) throw Invalid();
            }
        }
    }

    // Utf8JsonReader validates escape syntax; reject unpaired escaped UTF16 before any decoder can replace it.
    private static void ValidateEscapes(ReadOnlySpan<byte> token)
    {
        for (var i = 0; i < token.Length; i++)
        {
            if (token[i] != '\\' || token[++i] != 'u') continue;
            var scalar = Hex(token.Slice(i + 1, 4));
            i += 4;
            if (scalar is >= 0xdc00 and <= 0xdfff) throw Invalid();
            if (scalar is < 0xd800 or > 0xdbff) continue;
            if (i + 6 >= token.Length || token[i + 1] != '\\' || token[i + 2] != 'u' || Hex(token.Slice(i + 3, 4)) is < 0xdc00 or > 0xdfff) throw Invalid();
            i += 6;
        }
    }

    private static int Hex(ReadOnlySpan<byte> bytes)
    {
        var value = 0;
        foreach (var b in bytes) value = (value << 4) + (b is >= (byte)'0' and <= (byte)'9' ? b - '0' : b is >= (byte)'a' and <= (byte)'f' ? b - 'a' + 10 : b - 'A' + 10);
        return value;
    }

    private static FigmaReadException Invalid() => new(FigmaReadError.ResponseInvalid);
}
