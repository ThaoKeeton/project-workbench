using System.Text.Json;

namespace Workbench.TestExecutor;

public static class ComputerUseResponseParser
{
    public static IReadOnlyList<ComputerUseAction> Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array)
                return [];
            var actions = new List<ComputerUseAction>();
            foreach (var item in output.EnumerateArray())
            {
                if (!item.TryGetProperty("type", out var type) || type.GetString() != "computer_call") continue;
                if (!item.TryGetProperty("action", out var action)) continue;
                var parsed = ParseAction(action);
                if (parsed is not null) actions.Add(parsed);
            }
            return actions;
        }
        catch (JsonException)
        {
            throw new ComputerUseException(ExecutorError.ProviderUnavailable);
        }
    }

    private static ComputerUseAction? ParseAction(JsonElement action)
    {
        var kind = action.TryGetProperty("type", out var type) ? type.GetString() : null;
        return kind switch
        {
            "click" or "double_click" => new ComputerUseAction(kind == "double_click" ? "double-click" : "click", Int(action, "x"), Int(action, "y"), null, null),
            "move" or "drag" => new ComputerUseAction(kind, Int(action, "x"), Int(action, "y"), null, null),
            "scroll" => new ComputerUseAction("scroll", Int(action, "x"), Int(action, "y"), null, Scroll(action)),
            "type" => new ComputerUseAction("type", null, null, Text(action, "text"), null),
            "keypress" => new ComputerUseAction("key", null, null, null, Keys(action)),
            _ => null
        };
    }

    private static int? Int(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.TryGetInt32(out var number) ? number : null;

    private static string? Text(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String) return null;
        var text = value.GetString();
        return string.IsNullOrEmpty(text) ? null : TextRules.Clip(text, 200);
    }

    private static string? Keys(JsonElement element)
    {
        if (!element.TryGetProperty("keys", out var keys) || keys.ValueKind != JsonValueKind.Array) return null;
        var parts = keys.EnumerateArray().Select(key => key.GetString()).Where(key => !string.IsNullOrEmpty(key)).Select(key => TextRules.Clip(key, 20)).ToArray();
        return parts.Length == 0 ? null : string.Join('+', parts);
    }

    private static string? Scroll(JsonElement element)
    {
        var y = Int(element, "scroll_y") ?? Int(element, "scrollY");
        return y?.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}
