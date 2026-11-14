using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Workbench.Service.Abstractions;

namespace Workbench.DingTalkInsights.V2;

internal static partial class ChatExport
{
    public const string ExportSchema = "workbench.dingtalk.export.v2.preview";

    [GeneratedRegex("^M-\\d{3,6}$")]
    private static partial Regex MessageIdPattern();

    public static ChatDocument Parse(string? text)
    {
        if (text is not { Length: > 0 and <= 65536 }) throw new PluginDataException();
        var body = text.Trim().TrimStart('\uFEFF');
        return body.StartsWith('{') ? ParseJson(body) : ParseLines(body);
    }

    private static ChatDocument ParseJson(string body)
    {
        var document = JsonSerializer.Deserialize<ExportBody>(body, PluginResponses.Json) ?? throw new PluginDataException();
        if (document.Schema != ExportSchema || document.SchemaVersion != 2 || document.Messages is not { Count: > 0 and <= 200 })
            throw new PluginDataException();
        return Finish(document.Conversation, document.Messages.Select(message => (message.Id, message.At, message.Speaker, message.Text)));
    }

    private static ChatDocument ParseLines(string body)
    {
        string? conversation = null;
        var rows = new List<(string? Id, string? At, string? Speaker, string? Text)>();
        foreach (var raw in body.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            if (line.StartsWith("会话:", StringComparison.Ordinal))
            {
                conversation = line["会话:".Length..].Trim();
                continue;
            }
            var parts = line.Split(" | ", 4, StringSplitOptions.None);
            if (parts.Length != 4) throw new PluginDataException();
            rows.Add((parts[0].Trim(), parts[1].Trim(), parts[2].Trim(), parts[3].Trim()));
        }
        if (rows.Count is 0 or > 200) throw new PluginDataException();
        return Finish(conversation, rows);
    }

    private static ChatDocument Finish(string? conversation, IEnumerable<(string? Id, string? At, string? Speaker, string? Text)> rows)
    {
        if (!Valid(conversation, 40)) throw new PluginDataException();
        var messages = new List<ChatMessage>();
        foreach (var (id, at, speaker, text) in rows)
        {
            if (id is null || !MessageIdPattern().IsMatch(id) || messages.Any(existing => existing.Id == id))
                throw new PluginDataException();
            if (at is null || (at.IndexOf('+') < 0 && !at.EndsWith('Z'))) throw new PluginDataException();
            if (!DateTimeOffset.TryParse(at, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
                throw new PluginDataException();
            if (!Valid(speaker, 20) || !Valid(text, 400)) throw new PluginDataException();
            messages.Add(new ChatMessage(id, parsed, speaker!, text!));
        }
        return new ChatDocument(conversation!, messages);
    }

    private static bool Valid(string? value, int limit)
        => value is { Length: > 0 and var length } && length <= limit && !value.Any(char.IsControl);

    private sealed record ExportBody
    {
        public string? Schema { get; init; }
        public int SchemaVersion { get; init; }
        public string? Conversation { get; init; }
        public IReadOnlyList<ExportMessage>? Messages { get; init; }
    }

    private sealed record ExportMessage
    {
        public string? Id { get; init; }
        public string? At { get; init; }
        public string? Speaker { get; init; }
        public string? Text { get; init; }
    }
}

internal sealed record ChatDocument(string Conversation, IReadOnlyList<ChatMessage> Messages);

internal sealed record ChatMessage(string Id, DateTimeOffset At, string Speaker, string Text);
