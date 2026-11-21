using System.Globalization;
using System.Text.RegularExpressions;
using Workbench.Service.Abstractions;

namespace Workbench.UpcamStandup.V2;

internal static partial class StandupNotes
{
    private static readonly string[] Statuses = ["进行中", "待确认", "已完成", "待复核"];

    [GeneratedRegex("^SYN-\\d{3,6}$")]
    private static partial Regex ItemIdPattern();

    public static NoteFile Parse(string? name, string? text, DateOnly today)
    {
        var label = PluginDataFile.BaseName(name);
        if (text is not { Length: > 0 and <= 65536 }) throw new PluginDataException();
        DateOnly? date = null;
        var items = new List<NoteItem>();
        var issues = new List<string>();
        NoteItem? current = null;
        foreach (var raw in text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            if (line.StartsWith("日期:", StringComparison.Ordinal))
            {
                date = ReadDate(line);
                continue;
            }
            if (line.StartsWith("事项:", StringComparison.Ordinal))
            {
                if (current is not null) Finish(current, items, issues);
                current = new NoteItem { Id = Value(line, "事项:") };
                continue;
            }
            if (current is null) continue;
            if (line.StartsWith("标题:", StringComparison.Ordinal)) current.Title = Value(line, "标题:");
            else if (line.StartsWith("负责人:", StringComparison.Ordinal)) current.Owner = Value(line, "负责人:");
            else if (line.StartsWith("状态:", StringComparison.Ordinal)) current.Status = Value(line, "状态:");
            else if (line.StartsWith("发言:", StringComparison.Ordinal)) current.Speech = Value(line, "发言:");
        }
        if (current is not null) Finish(current, items, issues);
        if (date is null) return NoteFile.Skip(label, "UnreadableDate", issues);
        if (date.Value != today) return NoteFile.Skip(label, "NotToday", issues);
        return new NoteFile(label, false, null, items, issues);
    }

    public static string Summary(DateOnly today, IReadOnlyList<NoteItem> items)
    {
        if (items.Count == 0) return "今日没有可写入草稿的事项。自动发送已关闭，没有外发。";
        return $"今日 {today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} 共 {items.Count} 项。进行中 {Count(items, "进行中")}，待确认 {Count(items, "待确认")}，已完成 {Count(items, "已完成")}，待复核 {Count(items, "待复核")}。自动发送已关闭，草稿只留在本机。";
    }

    public static string SpeechSummary(IReadOnlyList<NoteItem> items)
    {
        var lines = items.Where(item => item.Speech.Length > 0).Select(item => item.Owner + "：" + item.Speech).ToArray();
        return lines.Length == 0 ? "今日事项没有发言摘录。" : string.Join("\n", lines);
    }

    private static void Finish(NoteItem item, List<NoteItem> items, List<string> issues)
    {
        if (!ItemIdPattern().IsMatch(item.Id) || items.Any(existing => existing.Id == item.Id))
        {
            issues.Add(items.Any(existing => existing.Id == item.Id) ? "有一条重复的事项编号" : "有一条事项编号无法识别");
            return;
        }
        if (!Valid(item.Title, 80))
        {
            issues.Add("有一条事项缺少标题");
            return;
        }
        if (!Valid(item.Owner, 20))
        {
            item.Owner = "待复核";
            issues.Add("有一条事项的负责人已改为待复核");
        }
        if (!Statuses.Contains(item.Status, StringComparer.Ordinal))
        {
            item.Status = "待复核";
            issues.Add("有一条事项的状态已改为待复核");
        }
        if (item.Speech.Length > 0 && !Valid(item.Speech, 500))
        {
            issues.Add("有一条发言无法收录");
            item.Speech = "";
        }
        items.Add(item);
    }

    private static DateOnly? ReadDate(string line)
    {
        var value = Value(line, "日期:");
        return DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;
    }

    private static string Value(string line, string prefix) => line[prefix.Length..].Trim();

    private static int Count(IReadOnlyList<NoteItem> items, string status) => items.Count(item => item.Status == status);

    private static bool Valid(string? value, int limit)
        => value is { Length: > 0 and var length } && length <= limit && !value.Any(char.IsControl);

    internal sealed class NoteItem
    {
        public string Id { get; init; } = "";
        public string Title { get; set; } = "";
        public string Owner { get; set; } = "";
        public string Status { get; set; } = "";
        public string Speech { get; set; } = "";
    }

    internal sealed record NoteFile(string Name, bool Skipped, string? Reason, IReadOnlyList<NoteItem> Items, IReadOnlyList<string> Issues)
    {
        public static NoteFile Skip(string name, string reason, IReadOnlyList<string> issues) => new(name, true, reason, [], issues);
    }
}
