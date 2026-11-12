using System.Text;
using System.Text.Json;
using Workbench.Service.Abstractions;

namespace Workbench.DingTalkInsights.V2;

/// <summary>
/// On-demand insights for an imported synthetic chat export.
/// Send stays disabled. This module does not open a socket or call a webhook.
/// </summary>
public sealed class DingTalkInsightsModule : IWorkbenchPluginModule
{
    public const string PluginId = "com.projectworkbench.dingtalk-insights.v2";
    public const string DataSchema = "workbench.dingtalk.insights.v2.preview";
    private const string ChatSchema = "workbench.dingtalk.chat.v2.preview";
    private const string ChatFile = "chat.v2.json";
    private const string StateFile = "insights.v2.json";

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
            "state" => ReadStateAsync(request, cancellationToken),
            "import" => ImportAsync(request, "local-file", cancellationToken),
            "sample" => ImportAsync(request, "synthetic-sample", cancellationToken),
            "analyze" => AnalyzeAsync(request, cancellationToken),
            "send" => Task.FromResult(RefuseSend(request)),
            _ => Task.FromResult(PluginResponses.Fail(404, "NotFound"))
        });

    private static PluginModuleResponse RefuseSend(PluginModuleRequest request)
    {
        RequireData(request.GrantedPermissions);
        return PluginResponses.Body(409, new
        {
            error = "SendDisabled",
            autoSend = false,
            sendEnabled = false,
            webhookConfigured = false,
            delivered = false
        });
    }

    private static async Task<PluginModuleResponse> ReadStateAsync(PluginModuleRequest request, CancellationToken cancellationToken)
    {
        RequireData(request.GrantedPermissions);
        var path = PluginDataFile.Contain(request.DataDirectory, StateFile);
        if (!File.Exists(path)) return PluginResponses.Ok(Empty());
        await using var stream = File.OpenRead(path);
        var document = await JsonSerializer.DeserializeAsync<InsightState>(stream, PluginResponses.Json, cancellationToken);
        return PluginResponses.Ok(Validate(document));
    }

    private async Task<PluginModuleResponse> ImportAsync(PluginModuleRequest request, string source, CancellationToken cancellationToken)
    {
        RequireData(request.GrantedPermissions);
        var text = source == "synthetic-sample" ? SampleText() : ReadText(request.JsonBody);
        var chat = ChatExport.Parse(text);
        var stored = new StoredChat
        {
            Schema = ChatSchema,
            SchemaVersion = 2,
            Source = source,
            Conversation = chat.Conversation,
            Messages = chat.Messages.Select(message => new StoredMessage
            {
                Id = message.Id,
                At = message.At.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
                Speaker = message.Speaker,
                Text = message.Text
            }).ToArray()
        };
        var state = Empty() with
        {
            Stored = true,
            Source = source,
            Imported = true,
            Conversation = chat.Conversation,
            MessageCount = chat.Messages.Count
        };
        await WriteAsync(request.DataDirectory, ChatFile, stored, cancellationToken);
        await WriteAsync(request.DataDirectory, StateFile, state, cancellationToken);
        return PluginResponses.Ok(state);
    }

    private static async Task<PluginModuleResponse> AnalyzeAsync(PluginModuleRequest request, CancellationToken cancellationToken)
    {
        RequireData(request.GrantedPermissions);
        var path = PluginDataFile.Contain(request.DataDirectory, ChatFile);
        if (!File.Exists(path)) return PluginResponses.Fail(409, "NothingImported");
        await using var stream = File.OpenRead(path);
        var stored = await JsonSerializer.DeserializeAsync<StoredChat>(stream, PluginResponses.Json, cancellationToken);
        if (stored is null || stored.Schema != ChatSchema || stored.SchemaVersion != 2 || stored.Messages is not { Count: > 0 })
            throw new PluginDataException();
        if (stored.Source is not ("local-file" or "synthetic-sample")) throw new PluginDataException();
        var chat = ChatExport.Parse(ToExport(stored));
        var state = Empty() with
        {
            Stored = true,
            Source = stored.Source ?? "local-file",
            Imported = true,
            Analyzed = true,
            Conversation = chat.Conversation,
            MessageCount = chat.Messages.Count,
            Insights = InsightReport.Build(chat)
        };
        await WriteAsync(request.DataDirectory, StateFile, state, cancellationToken);
        return PluginResponses.Ok(state);
    }

    private static string ToExport(StoredChat stored)
    {
        var body = new
        {
            schema = ChatExport.ExportSchema,
            schemaVersion = 2,
            conversation = stored.Conversation,
            messages = stored.Messages
        };
        return JsonSerializer.Serialize(body, PluginResponses.Json);
    }

    private static string ReadText(string? json)
    {
        if (json is not { Length: > 0 and <= 262144 }) throw new PluginDataException();
        var body = JsonSerializer.Deserialize<ImportBody>(json, PluginResponses.Json) ?? throw new PluginDataException();
        if (body.Text is not { Length: > 0 and <= 65536 }) throw new PluginDataException();
        return body.Text;
    }

    private static InsightState Empty() => new()
    {
        Schema = DataSchema,
        SchemaVersion = 2,
        AutoSend = false,
        SendEnabled = false,
        WebhookConfigured = false,
        Delivered = false,
        Stored = false,
        Source = "none",
        Imported = false,
        Analyzed = false,
        Conversation = "",
        MessageCount = 0,
        Insights = []
    };

    private static InsightState Validate(InsightState? state)
    {
        if (state is null || state.Schema != DataSchema || state.SchemaVersion != 2)
            throw new PluginDataException();
        if (state.AutoSend || state.SendEnabled || state.WebhookConfigured || state.Delivered || state.Insights is null)
            throw new PluginDataException();
        return state;
    }

    private static async Task WriteAsync(string directory, string fileName, object document, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(directory);
        var path = PluginDataFile.Contain(directory, fileName);
        await PluginDataFile.WriteAtomicAsync(path, JsonSerializer.SerializeToUtf8Bytes(document, PluginResponses.Json), cancellationToken);
    }

    private static string SampleText()
    {
        using var stream = typeof(DingTalkInsightsModule).Assembly.GetManifestResourceStream("chat-sample.txt")
            ?? throw new InvalidOperationException("missing-sample");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static void RequireData(IReadOnlySet<string> granted)
    {
        PluginPermissions.Require(granted, "project.read");
        PluginPermissions.Require(granted, "plugin.data.read");
        PluginPermissions.Require(granted, "plugin.data.write");
    }

    private sealed record ImportBody
    {
        public string? Name { get; init; }
        public string? Text { get; init; }
    }

    private sealed record StoredChat
    {
        public string? Schema { get; init; }
        public int SchemaVersion { get; init; }
        public string? Source { get; init; }
        public string? Conversation { get; init; }
        public IReadOnlyList<StoredMessage>? Messages { get; init; }
    }

    private sealed record StoredMessage
    {
        public string? Id { get; init; }
        public string? At { get; init; }
        public string? Speaker { get; init; }
        public string? Text { get; init; }
    }

    private sealed record InsightState
    {
        public required string Schema { get; init; }
        public required int SchemaVersion { get; init; }
        public required bool AutoSend { get; init; }
        public required bool SendEnabled { get; init; }
        public required bool WebhookConfigured { get; init; }
        public required bool Delivered { get; init; }
        public required bool Stored { get; init; }
        public required string Source { get; init; }
        public required bool Imported { get; init; }
        public required bool Analyzed { get; init; }
        public required string Conversation { get; init; }
        public required int MessageCount { get; init; }
        public required IReadOnlyList<Insight> Insights { get; init; }
    }
}
