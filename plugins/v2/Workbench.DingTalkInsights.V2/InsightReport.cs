using Workbench.Service.Abstractions;

namespace Workbench.DingTalkInsights.V2;

internal static class InsightReport
{
    private static readonly (string Id, string Phrase)[] Topics =
    [
        ("topic-login-review", "登录页对照"),
        ("topic-export", "导出确认"),
        ("topic-synthetic-check", "合成检查")
    ];

    public static IReadOnlyList<Insight> Build(ChatDocument chat)
    {
        var insights = new List<Insight>();
        foreach (var (id, phrase) in Topics)
        {
            var hits = chat.Messages.Where(message => message.Text.Contains(phrase, StringComparison.Ordinal)).ToArray();
            if (hits.Length == 0) continue;
            insights.Add(new Insight
            {
                Id = id,
                Kind = "topic",
                Title = phrase + "出现 " + hits.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + " 次",
                Detail = "只按原文计数。",
                Evidence = Evidence(hits)
            });
        }
        var busiest = chat.Messages
            .GroupBy(message => TimeZoneInfo.ConvertTime(message.At, PreviewClock.Zone()).Hour)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key)
            .FirstOrDefault();
        if (busiest is { } hour && hour.Count() >= 3)
        {
            insights.Add(new Insight
            {
                Id = "cadence-" + hour.Key.ToString("00", System.Globalization.CultureInfo.InvariantCulture),
                Kind = "cadence",
                Title = hour.Key.ToString("00", System.Globalization.CultureInfo.InvariantCulture) + " 时有 " + hour.Count().ToString(System.Globalization.CultureInfo.InvariantCulture) + " 条消息",
                Detail = "按导出时间归到同一小时。",
                Evidence = Evidence(hour.Take(3).ToArray())
            });
        }
        foreach (var message in chat.Messages.Where(message => message.Text.Contains('？') || message.Text.Contains('?')).Take(8))
        {
            insights.Add(new Insight
            {
                Id = "question-" + message.Id,
                Kind = "question",
                Title = "有待确认的提问",
                Detail = "原文里有提问，没有代为回复。",
                Evidence = Evidence([message])
            });
        }
        return insights;
    }

    private static IReadOnlyList<EvidenceItem> Evidence(IReadOnlyList<ChatMessage> messages)
        => messages.Take(8).Select(message => new EvidenceItem
        {
            MessageId = message.Id,
            Excerpt = message.Text.Length <= 120 ? message.Text : message.Text[..120]
        }).ToArray();
}

internal sealed record Insight
{
    public required string Id { get; init; }
    public required string Kind { get; init; }
    public required string Title { get; init; }
    public required string Detail { get; init; }
    public required IReadOnlyList<EvidenceItem> Evidence { get; init; }
}

internal sealed record EvidenceItem
{
    public required string MessageId { get; init; }
    public required string Excerpt { get; init; }
}
