using System.Globalization;
using System.Text;
using System.Text.Json;
using Workbench.Service.Abstractions;

namespace Workbench.UpcamStandup.V2;

/// <summary>
/// Builds a local standup draft from imported notes. Nothing here opens a socket or sends.
/// </summary>
public sealed class UpcamStandupModule : IWorkbenchPluginModule
{
    public const string PluginId = "com.projectworkbench.upcam-standup.v2";
    public const string DataSchema = "workbench.upcam.digest.v2.preview";
    private const string DraftFile = "digest.v2.json";

    public string Id => PluginId;

    public Task StartAsync(PluginModuleStart start, CancellationToken cancellationToken)
    {
        RequireData(start.GrantedPermissions);
        _ = SampleText();
        return Task.CompletedTask;
    }

    public Task<PluginModuleResponse> HandleAsync(PluginModuleRequest request, CancellationToken cancellationToken)
        => PluginResponses.Run(() => request.Action switch
        {
            "draft" => ReadAsync(request, cancellationToken),
            "import" => ImportAsync(request, "local-file", cancellationToken),
            "sample" => ImportAsync(request, "synthetic-sample", cancellationToken),
            _ => Task.FromResult(PluginResponses.Fail(404, "NotFound"))
        });

    private static async Task<PluginModuleResponse> ReadAsync(PluginModuleRequest request, CancellationToken cancellationToken)
    {
        RequireData(request.GrantedPermissions);
        var path = PluginDataFile.Contain(request.DataDirectory, DraftFile);
        if (!File.Exists(path)) return PluginResponses.Ok(Empty(PreviewClock.Today()));
        await using var stream = File.OpenRead(path);
        var document = await JsonSerializer.DeserializeAsync<DigestDocument>(stream, PluginResponses.Json, cancellationToken);
        return PluginResponses.Ok(Validate(document));
    }

    private async Task<PluginModuleResponse> ImportAsync(PluginModuleRequest request, string source, CancellationToken cancellationToken)
    {
        RequireData(request.GrantedPermissions);
        var today = PreviewClock.Today();
        var files = source == "synthetic-sample" ? SampleFiles(today) : ReadFiles(request.JsonBody);
        if (files.Count is 0 or > 32) throw new PluginDataException();
        var items = new List<StandupNotes.NoteItem>();
        var skipped = new List<SkippedFile>();
        var issues = new List<string>();
        foreach (var file in files)
        {
            var parsed = StandupNotes.Parse(file.Name, file.Text, today);
            issues.AddRange(parsed.Issues);
            if (parsed.Skipped)
            {
                skipped.Add(new SkippedFile { Name = parsed.Name, Reason = parsed.Reason ?? "UnreadableDate" });
                continue;
            }
            foreach (var item in parsed.Items)
            {
                if (items.Any(existing => existing.Id == item.Id))
                {
                    issues.Add("有一条重复的事项编号");
                    continue;
                }
                items.Add(item);
            }
        }
        if (items.Count == 0) issues.Add("今日没有可写入草稿的事项");
        var document = Build(today, source, items, skipped, issues);
        await WriteAsync(request.DataDirectory, document, cancellationToken);
        return PluginResponses.Ok(document);
    }

    private static DigestDocument Build(DateOnly today, string source, IReadOnlyList<StandupNotes.NoteItem> items, IReadOnlyList<SkippedFile> skipped, IReadOnlyList<string> issues)
        => new()
        {
            Schema = DataSchema,
            SchemaVersion = 2,
            AutoSend = false,
            Delivered = false,
            Stored = true,
            Source = source,
            MeetingDate = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Summary = StandupNotes.Summary(today, items),
            SpeechSummary = StandupNotes.SpeechSummary(items),
            Items = items.Select(item => new DigestItem { Id = item.Id, Title = item.Title, Owner = item.Owner, Status = item.Status }).ToArray(),
            Speeches = items.Where(item => item.Speech.Length > 0).Select(item => new SpeechItem { Owner = item.Owner, Text = item.Speech }).ToArray(),
            Skipped = skipped,
            Issues = issues
        };

    private static DigestDocument Empty(DateOnly today) => new()
    {
        Schema = DataSchema,
        SchemaVersion = 2,
        AutoSend = false,
        Delivered = false,
        Stored = false,
        Source = "none",
        MeetingDate = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        Summary = "还没有今日草稿。",
        SpeechSummary = "",
        Items = [],
        Speeches = [],
        Skipped = [],
        Issues = []
    };

    private static DigestDocument Validate(DigestDocument? document)
    {
        if (document is null || document.Schema != DataSchema || document.SchemaVersion != 2 || document.AutoSend || document.Delivered)
            throw new PluginDataException();
        if (document.Items is null || document.Speeches is null || document.Skipped is null || document.Issues is null)
            throw new PluginDataException();
        return document;
    }

    private static IReadOnlyList<ImportFile> ReadFiles(string? json)
    {
        if (json is not { Length: > 0 and <= 262144 }) throw new PluginDataException();
        var body = JsonSerializer.Deserialize<ImportBody>(json, PluginResponses.Json) ?? throw new PluginDataException();
        if (body.Files is not { Count: > 0 and <= 32 }) throw new PluginDataException();
        if (body.Files.Any(file => file.Text is not { Length: > 0 and <= 65536 })) throw new PluginDataException();
        return body.Files;
    }

    private static IReadOnlyList<ImportFile> SampleFiles(DateOnly today)
    {
        var text = SampleText().Replace("{{today}}", today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), StringComparison.Ordinal);
        return [new ImportFile { Name = "synthetic-standup.txt", Text = text }];
    }

    private static string SampleText()
    {
        using var stream = typeof(UpcamStandupModule).Assembly.GetManifestResourceStream("standup-sample.txt")
            ?? throw new InvalidOperationException("missing-sample");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static async Task WriteAsync(string directory, DigestDocument document, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(directory);
        var path = PluginDataFile.Contain(directory, DraftFile);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(document, PluginResponses.Json);
        await PluginDataFile.WriteAtomicAsync(path, bytes, cancellationToken);
    }

    private static void RequireData(IReadOnlySet<string> granted)
    {
        PluginPermissions.Require(granted, "project.read");
        PluginPermissions.Require(granted, "plugin.data.read");
        PluginPermissions.Require(granted, "plugin.data.write");
    }

    private sealed record ImportBody
    {
        public IReadOnlyList<ImportFile>? Files { get; init; }
    }

    private sealed record ImportFile
    {
        public string? Name { get; init; }
        public string? Text { get; init; }
    }

    private sealed record DigestDocument
    {
        public required string Schema { get; init; }
        public required int SchemaVersion { get; init; }
        public required bool AutoSend { get; init; }
        public required bool Delivered { get; init; }
        public required bool Stored { get; init; }
        public required string Source { get; init; }
        public required string MeetingDate { get; init; }
        public required string Summary { get; init; }
        public required string SpeechSummary { get; init; }
        public required IReadOnlyList<DigestItem> Items { get; init; }
        public required IReadOnlyList<SpeechItem> Speeches { get; init; }
        public required IReadOnlyList<SkippedFile> Skipped { get; init; }
        public required IReadOnlyList<string> Issues { get; init; }
    }

    private sealed record DigestItem
    {
        public required string Id { get; init; }
        public required string Title { get; init; }
        public required string Owner { get; init; }
        public required string Status { get; init; }
    }

    private sealed record SpeechItem
    {
        public required string Owner { get; init; }
        public required string Text { get; init; }
    }

    private sealed record SkippedFile
    {
        public required string Name { get; init; }
        public required string Reason { get; init; }
    }
}
