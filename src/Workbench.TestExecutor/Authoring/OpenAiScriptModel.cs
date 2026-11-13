using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Workbench.TestExecutor;

public sealed class OpenAiScriptModel : IScriptModel
{
    public const string Endpoint = "https://api.openai.com/v1/chat/completions";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _http;

    public OpenAiScriptModel(HttpMessageHandler? handler = null)
    {
        _http = new HttpClient(handler ?? new SocketsHttpHandler(), disposeHandler: false)
        {
            Timeout = TimeSpan.FromSeconds(90)
        };
    }

    public string Name => "openai-text";

    public bool Billed => true;

    public async Task<ScriptModelReply> CompleteAsync(ScriptModelCall call, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(call.ApiKey)) throw new ScriptModelException(AuthorMessages.MissingKey, false);
        if (AuthorPrompt.ContainsImage(call.Prompt)) throw new ScriptModelException(AuthorMessages.Image, false);
        var body = JsonSerializer.Serialize(new
        {
            model = call.Model,
            temperature = 0,
            max_tokens = 4096,
            messages = new object[]
            {
                new { role = "system", content = "Reply with one JSON object and nothing else." },
                new { role = "user", content = call.Prompt }
            }
        }, Json);
        if (AuthorPrompt.ContainsImage(body) || body.Contains(call.ApiKey, StringComparison.Ordinal))
            throw new ScriptModelException(AuthorMessages.Image, false);
        using var message = new HttpRequestMessage(HttpMethod.Post, Endpoint);
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", call.ApiKey);
        message.Content = new StringContent(body, Encoding.UTF8, "application/json");
        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(message, cancellationToken);
        }
        catch (HttpRequestException)
        {
            throw new ScriptModelException(AuthorMessages.Unavailable, true);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ScriptModelException(AuthorMessages.Unavailable, true);
        }

        using (response)
        {
            var text = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode || text.Length > 256 * 1024 || text.Contains(call.ApiKey, StringComparison.Ordinal))
                throw new ScriptModelException(AuthorMessages.Unavailable, true);
            using var document = JsonDocument.Parse(text);
            var content = document.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
            var tokens = document.RootElement.TryGetProperty("usage", out var usage) && usage.TryGetProperty("total_tokens", out var total)
                ? total.GetInt32()
                : Math.Max(1, call.Prompt.Length / 4);
            return new ScriptModelReply(Unwrap(content), tokens, 0);
        }
    }

    internal static string Unwrap(string text)
    {
        var trimmed = text.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal)) return trimmed;
        var first = trimmed.IndexOf('\n', StringComparison.Ordinal);
        var last = trimmed.LastIndexOf("```", StringComparison.Ordinal);
        if (first < 0 || last <= first) return trimmed;
        return trimmed[(first + 1)..last].Trim();
    }
}
