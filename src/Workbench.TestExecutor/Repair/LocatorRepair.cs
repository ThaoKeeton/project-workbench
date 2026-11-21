using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using YamlDotNet.Serialization;

namespace Workbench.TestExecutor;

public sealed record LocatorRepairRequest(string CaseId, string StepId, string FailedLocator, string Snapshot);

public sealed record LocatorSuggestion(string Selector, int Tokens);

public interface ILocatorRepairProvider
{
    string Name { get; }

    Task<LocatorSuggestion?> SuggestAsync(LocatorRepairRequest request, CancellationToken cancellationToken);
}

public sealed class DisabledLocatorRepair : ILocatorRepairProvider
{
    public string Name => "disabled";

    public Task<LocatorSuggestion?> SuggestAsync(LocatorRepairRequest request, CancellationToken cancellationToken)
        => Task.FromResult<LocatorSuggestion?>(null);
}

public sealed class OpenAiLocatorRepairProvider : ILocatorRepairProvider
{
    public const string Endpoint = "https://api.openai.com/v1/chat/completions";
    private readonly IApiKeyStore _keys;
    private readonly Func<ExecutionPolicy> _policy;
    private readonly HttpClient _http;

    public OpenAiLocatorRepairProvider(IApiKeyStore keys, Func<ExecutionPolicy> policy, HttpMessageHandler? handler = null)
    {
        _keys = keys;
        _policy = policy;
        _http = new HttpClient(handler ?? new SocketsHttpHandler(), disposeHandler: false) { Timeout = Timeout.InfiniteTimeSpan };
    }

    public string Name => "openai-text";

    public async Task<LocatorSuggestion?> SuggestAsync(LocatorRepairRequest request, CancellationToken cancellationToken)
    {
        var policy = _policy();
        if (!policy.CanRepair) return null;
        var key = _keys.Read(CredentialTargets.OpenAi);
        if (string.IsNullOrEmpty(key)) throw new ComputerUseException(ExecutorError.CredentialMissing);
        var snapshot = request.Snapshot.Length > 4096 ? request.Snapshot[..4096] : request.Snapshot;
        var body = JsonSerializer.Serialize(new
        {
            model = policy.RepairModel,
            messages = new object[]
            {
                new { role = "system", content = "Reply with one JSON object {\"selector\":\"...\"}. Text only." },
                new { role = "user", content = "failed=" + request.FailedLocator + "\n" + snapshot }
            }
        });
        if (body.Contains("image_url", StringComparison.Ordinal) || body.Contains("input_image", StringComparison.Ordinal))
            throw new ComputerUseException(ExecutorError.ProviderUnavailable);
        using var message = new HttpRequestMessage(HttpMethod.Post, Endpoint);
        message.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", key);
        message.Content = new StringContent(body, Encoding.UTF8, "application/json");
        using var response = await _http.SendAsync(message, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode || text.Contains(key, StringComparison.Ordinal))
            throw new ComputerUseException(ExecutorError.ProviderUnavailable);
        using var document = JsonDocument.Parse(text);
        var content = document.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
        using var suggestion = JsonDocument.Parse(content);
        var selector = suggestion.RootElement.GetProperty("selector").GetString();
        var tokens = document.RootElement.TryGetProperty("usage", out var usage) && usage.TryGetProperty("total_tokens", out var total)
            ? total.GetInt32()
            : Math.Max(1, snapshot.Length / 4);
        return string.IsNullOrWhiteSpace(selector) ? null : new LocatorSuggestion(selector, tokens);
    }
}

public static class SuiteLocatorPatch
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static void Apply(string suiteFile, string caseId, string stepId, string selector)
    {
        var text = File.ReadAllText(suiteFile);
        var yaml = !suiteFile.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
        var node = yaml ? YamlJson.Parse(text) : JsonNode.Parse(text) ?? throw new ExecutorDataException(ExecutorError.InvalidSuite);
        var cases = node["cases"]?.AsArray() ?? throw new ExecutorDataException(ExecutorError.InvalidSuite);
        var updated = false;
        foreach (var testCase in cases)
        {
            if (testCase?["id"]?.GetValue<string>() != caseId) continue;
            foreach (var step in testCase["steps"]?.AsArray() ?? [])
            {
                if (step?["id"]?.GetValue<string>() != stepId) continue;
                var target = step["target"]?.AsObject() ?? new JsonObject();
                target["selector"] = selector;
                step["target"] = target;
                updated = true;
            }
        }
        if (!updated) throw new ExecutorDataException(ExecutorError.InvalidSuite);
        var payload = yaml
            ? new SerializerBuilder().Build().Serialize(ToPlain(node))
            : node.ToJsonString(Json);
        var temporary = suiteFile + ".tmp";
        File.WriteAllText(temporary, payload);
        File.Move(temporary, suiteFile, overwrite: true);
    }

    private static object? ToPlain(JsonNode? node) => node switch
    {
        null => null,
        JsonObject obj => obj.ToDictionary(pair => pair.Key, pair => ToPlain(pair.Value)),
        JsonArray array => array.Select(ToPlain).ToList(),
        JsonValue value when value.TryGetValue<bool>(out var boolean) => boolean,
        JsonValue value when value.TryGetValue<int>(out var integer) => integer,
        JsonValue value when value.TryGetValue<long>(out var number) => number,
        JsonValue value when value.TryGetValue<double>(out var real) => real,
        JsonValue value when value.TryGetValue<string>(out var text) => text,
        _ => node?.ToJsonString()
    };
}

public sealed class AiBudgetLedger
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Usage> _runs = [];
    private readonly Dictionary<string, Usage> _days = [];
    private readonly string? _root;
    private readonly TimeProvider _clock;

    public AiBudgetLedger(string? root, TimeProvider? clock = null)
    {
        _root = root;
        _clock = clock ?? TimeProvider.System;
        if (root is not null) Directory.CreateDirectory(root);
    }

    public static AiBudgetLedger InMemory() => new(null);

    public bool TryCharge(string runId, int tokens, decimal cost, ExecutionPolicy policy)
    {
        lock (_gate)
        {
            var day = Day();
            var run = Get(_runs, runId);
            var daily = Get(_days, day);
            if (run.Tokens + tokens > policy.RunTokenCap || daily.Tokens + tokens > policy.DailyTokenCap
                || run.Cost + cost > policy.RunCostCap || daily.Cost + cost > policy.DailyCostCap)
            {
                run.Skipped++;
                return false;
            }
            run.Tokens += tokens;
            run.Cost += cost;
            daily.Tokens += tokens;
            daily.Cost += cost;
            return true;
        }
    }

    public int Skipped(string runId)
    {
        lock (_gate) return _runs.TryGetValue(runId, out var usage) ? usage.Skipped : 0;
    }

    public void WriteRun(string runId, string directory)
    {
        Usage usage;
        lock (_gate) usage = _runs.TryGetValue(runId, out var found) ? found : new Usage();
        Directory.CreateDirectory(directory);
        var json = JsonSerializer.Serialize(new
        {
            schema = "workbench.test-executor.ai-usage.v2.preview",
            schemaVersion = 2,
            runId,
            tokens = usage.Tokens,
            cost = usage.Cost,
            skipped = usage.Skipped
        });
        File.WriteAllText(Path.Combine(directory, "ai-usage.json"), json);
    }

    private string Day() => _clock.GetUtcNow().UtcDateTime.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    private static Usage Get(Dictionary<string, Usage> store, string key)
    {
        if (!store.TryGetValue(key, out var usage)) store[key] = usage = new Usage();
        return usage;
    }

    private sealed class Usage
    {
        public int Tokens { get; set; }
        public decimal Cost { get; set; }
        public int Skipped { get; set; }
    }
}
