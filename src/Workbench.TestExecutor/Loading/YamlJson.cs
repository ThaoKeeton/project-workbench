using System.Globalization;
using System.Text.Json.Nodes;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Workbench.TestExecutor;

internal static class YamlJson
{
    public static JsonNode Parse(string text)
    {
        try
        {
            var stream = new YamlStream();
            using var reader = new StringReader(text);
            stream.Load(reader);
            if (stream.Documents.Count != 1) throw new ExecutorDataException(ExecutorError.InvalidSuite);
            return Convert(stream.Documents[0].RootNode) ?? throw new ExecutorDataException(ExecutorError.InvalidSuite);
        }
        catch (ExecutorDataException)
        {
            throw;
        }
        catch (Exception error) when (error is YamlException or InvalidOperationException)
        {
            throw new ExecutorDataException(ExecutorError.InvalidSuite);
        }
    }

    private static JsonNode? Convert(YamlNode node) => node switch
    {
        YamlScalarNode scalar => ConvertScalar(scalar),
        YamlSequenceNode sequence => ConvertSequence(sequence),
        YamlMappingNode mapping => ConvertMapping(mapping),
        _ => throw new ExecutorDataException(ExecutorError.InvalidSuite)
    };

    private static JsonArray ConvertSequence(YamlSequenceNode sequence)
    {
        var array = new JsonArray();
        foreach (var child in sequence.Children) array.Add(Convert(child));
        return array;
    }

    private static JsonObject ConvertMapping(YamlMappingNode mapping)
    {
        var obj = new JsonObject();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in mapping.Children)
        {
            if (entry.Key is not YamlScalarNode key || string.IsNullOrEmpty(key.Value) || !seen.Add(key.Value))
                throw new ExecutorDataException(ExecutorError.InvalidSuite);
            obj[key.Value] = Convert(entry.Value);
        }
        return obj;
    }

    private static JsonValue? ConvertScalar(YamlScalarNode scalar)
    {
        var value = scalar.Value ?? "";
        if (scalar.Style != ScalarStyle.Plain) return JsonValue.Create(value);
        if (value is "null" or "~" or "Null" or "NULL") return null;
        if (value is "true" or "True" or "TRUE") return JsonValue.Create(true);
        if (value is "false" or "False" or "FALSE") return JsonValue.Create(false);
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer))
            return JsonValue.Create(integer);
        if (value.Contains('.', StringComparison.Ordinal)
            && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            return JsonValue.Create(number);
        return JsonValue.Create(value);
    }
}
