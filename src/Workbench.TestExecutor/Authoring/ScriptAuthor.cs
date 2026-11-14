using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;

namespace Workbench.TestExecutor;

public interface IScriptModel
{
    string Name { get; }

    bool Billed { get; }

    Task<ScriptModelReply> CompleteAsync(ScriptModelCall call, CancellationToken cancellationToken);
}

public sealed record ScriptModelCall(string Prompt, string? ApiKey, string Model);

public sealed record ScriptModelReply(string Text, int Tokens, decimal Cost);

public sealed class ScriptModelException : Exception
{
    public ScriptModelException(string message, bool sent) : base(message)
    {
        Sent = sent;
    }

    public bool Sent { get; }
}

public static class AuthorMessages
{
    public const string Empty = "请先写下测试目标，未发送。";
    public const string Image = "文字结构里含有图像数据，未发送。";
    public const string KeyLeak = "文字里出现了本机密钥，未发送。";
    public const string Disabled = "模型调用已关闭。已准备文本提示，未发送。";
    public const string MissingKey = "未配置本机密钥，未发送。";
    public const string MissingModel = "未配置模型，未发送。";
    public const string OverBudget = "这次调用会超过本机每日花费上限，未调用模型。";
    public const string Review = "请审阅。确认后才保存，不会外发。";
    public const string Invalid = "模型输出不是可回放脚本，未保存。";
    public const string Unavailable = "模型没有响应，未保存。";
    public const string Saved = "已保存到本机，未发送。";
    public const string NoFailure = "没有失败步骤，未发送。";
}

public sealed record AuthorDraft(string Status, string Message, string Prompt, string? ScriptJson, int ModelCalls);

public sealed record AuthorGate(bool Enabled, decimal DailySpendCap, decimal CostPerThousandTokens, string Model)
{
    public const string Schema = "workbench.test-executor.author-local.v1";
    public static AuthorGate Closed { get; } = new(false, 0, 0.01m, "gpt-4.1-mini");

    public static AuthorGate Load(string previewDirectory)
    {
        var path = Path.Combine(Path.GetFullPath(previewDirectory), "author.local.json");
        if (!File.Exists(path)) return Closed;
        var info = new FileInfo(path);
        if (info.Attributes.HasFlag(FileAttributes.ReparsePoint) || info.Length is < 2 or > 8192) return Closed;
        try
        {
            if (JsonNode.Parse(File.ReadAllText(path)) is not JsonObject node) return Closed;
            if (node["schema"]?.GetValue<string>() != Schema || node["schemaVersion"]?.GetValue<int>() != 1) return Closed;
            var model = node["model"]?.GetValue<string>() ?? Closed.Model;
            if (!TextRules.IsSafeId(model)) return Closed;
            if (!TryDecimal(node["dailySpendCap"], out var cap)) cap = 0;
            if (cap is < 0 or > 10000) return Closed;
            var rate = Closed.CostPerThousandTokens;
            if (node["costPerThousandTokens"] is not null && !TryDecimal(node["costPerThousandTokens"], out rate)) return Closed;
            if (rate is <= 0 or > 1000) rate = Closed.CostPerThousandTokens;
            var enabled = node["enabled"]?.GetValue<bool>() ?? false;
            return new AuthorGate(enabled, cap, rate, model);
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException)
        {
            return Closed;
        }
    }

    private static bool TryDecimal(JsonNode? node, out decimal value)
    {
        value = 0;
        if (node is null) return false;
        if (node is not JsonValue json) return false;
        if (json.TryGetValue<decimal>(out value)) return true;
        if (json.TryGetValue<int>(out var integer))
        {
            value = integer;
            return true;
        }
        if (json.TryGetValue<double>(out var real))
        {
            value = (decimal)real;
            return true;
        }
        return false;
    }
}

public interface IAuthorKeyProtector
{
    byte[] Protect(byte[] plaintext);
    byte[] Unprotect(byte[] ciphertext);
}

/// <summary>Current-user DPAPI. Off Windows this refuses to store a key as plaintext.</summary>
public sealed class DpapiAuthorKeyProtector : IAuthorKeyProtector
{
    private static readonly byte[] Entropy = "workbench.test-executor.author-key.v1"u8.ToArray();

    public byte[] Protect(byte[] plaintext)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        return System.Security.Cryptography.ProtectedData.Protect(plaintext, Entropy, System.Security.Cryptography.DataProtectionScope.CurrentUser);
    }

    public byte[] Unprotect(byte[] ciphertext)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        return System.Security.Cryptography.ProtectedData.Unprotect(ciphertext, Entropy, System.Security.Cryptography.DataProtectionScope.CurrentUser);
    }
}

public sealed class AuthorKeyVault
{
    public const string FileName = "author-key.bin";
    private readonly IAuthorKeyProtector _protector;

    public AuthorKeyVault(IAuthorKeyProtector? protector = null)
        => _protector = protector ?? new DpapiAuthorKeyProtector();

    public bool Save(string previewDirectory, string key)
    {
        var normalized = AuthorSecrets.Normalize(key);
        if (normalized is null) return false;
        var root = Path.GetFullPath(previewDirectory);
        Directory.CreateDirectory(root);
        var cipher = _protector.Protect(System.Text.Encoding.UTF8.GetBytes(normalized));
        var path = Path.Combine(root, FileName);
        File.WriteAllBytes(path, cipher);
        return !System.Text.Encoding.UTF8.GetString(cipher).Contains(normalized, StringComparison.Ordinal);
    }

    public string? Read(string previewDirectory)
    {
        var path = Path.Combine(Path.GetFullPath(previewDirectory), FileName);
        if (!File.Exists(path)) return null;
        var info = new FileInfo(path);
        if (info.Attributes.HasFlag(FileAttributes.ReparsePoint) || info.Length is < 8 or > 4096) return null;
        try
        {
            var plain = _protector.Unprotect(File.ReadAllBytes(path));
            return AuthorSecrets.Normalize(System.Text.Encoding.UTF8.GetString(plain));
        }
        catch (Exception error) when (error is System.Security.Cryptography.CryptographicException or PlatformNotSupportedException or InvalidOperationException or IOException)
        {
            return null;
        }
    }
}

public sealed class AuthorSecrets
{
    public const string EnvironmentVariable = "WORKBENCH_AUTHOR_API_KEY";
    private readonly Func<string, string?> _environment;
    private readonly AuthorKeyVault _vault;

    public AuthorSecrets(Func<string, string?>? environment = null, AuthorKeyVault? vault = null)
    {
        _environment = environment ?? Environment.GetEnvironmentVariable;
        _vault = vault ?? new AuthorKeyVault();
    }

    public string? Read(string previewDirectory)
    {
        var fromEnv = _environment(EnvironmentVariable);
        if (fromEnv is not null) return Normalize(fromEnv);
        return _vault.Read(previewDirectory);
    }

    internal static string? Normalize(string? value)
    {
        var text = value?.Trim();
        if (text is not { Length: >= 8 and <= 200 }) return null;
        if (text.Any(char.IsWhiteSpace) || text.Any(char.IsControl)) return null;
        return text;
    }
}

public sealed class AuthorLedger
{
    private readonly string _path;
    private readonly TimeProvider _clock;
    private readonly object _gate = new();

    public AuthorLedger(string previewDirectory, TimeProvider? clock = null)
    {
        var root = Path.GetFullPath(previewDirectory);
        Directory.CreateDirectory(root);
        _path = Path.Combine(root, "author-spend.json");
        _clock = clock ?? TimeProvider.System;
    }

    public bool WouldFit(decimal cost, decimal cap)
    {
        lock (_gate) return Spent() is var spent && spent != decimal.MaxValue && spent + cost <= cap;
    }

    public void Add(decimal cost)
    {
        lock (_gate)
        {
            var spent = Spent();
            if (spent == decimal.MaxValue) return;
            Write(Math.Max(0, spent) + cost);
        }
    }

    private decimal Spent()
    {
        if (!File.Exists(_path)) return 0;
        var info = new FileInfo(_path);
        if (info.Attributes.HasFlag(FileAttributes.ReparsePoint) || info.Length > 8192) return decimal.MaxValue;
        try
        {
            if (JsonNode.Parse(File.ReadAllText(_path)) is not JsonObject node) return decimal.MaxValue;
            if (node["schema"]?.GetValue<string>() != "workbench.test-executor.author-spend.v1" || node["schemaVersion"]?.GetValue<int>() != 1)
                return decimal.MaxValue;
            if (node["day"]?.GetValue<string>() != Day()) return 0;
            if (node["spent"] is JsonValue spent && spent.TryGetValue<decimal>(out var amount) && amount >= 0) return amount;
            return decimal.MaxValue;
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or IOException)
        {
            return decimal.MaxValue;
        }
    }

    private void Write(decimal spent)
    {
        var json = JsonSerializer.Serialize(new
        {
            schema = "workbench.test-executor.author-spend.v1",
            schemaVersion = 1,
            day = Day(),
            spent
        });
        var temporary = _path + ".tmp";
        File.WriteAllText(temporary, json);
        File.Move(temporary, _path, overwrite: true);
    }

    private string Day() => _clock.GetUtcNow().UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}

public sealed class ScriptAuthor
{
    public const string SchemaName = "workbench.test-executor.authored-script.v1";
    public const int Version = 1;
    private static readonly JsonSchema Schema = JsonSchema.FromText(SuiteLoader.ReadResource("Workbench.TestExecutor.authored-script.v1.schema.json"));
    private static readonly HashSet<string> WebActions = new(StringComparer.Ordinal)
    {
        "click", "type", "set-value", "invoke", "expand", "wait-for", "read", "assert-text", "assert-visible", "assert-network", "screenshot", "open-file", "switch-window"
    };
    private static readonly HashSet<string> SceneActions = new(StringComparer.Ordinal)
    {
        "click", "read-scene", "project", "drag", "orbit", "assert-scene", "probe", "snapshot", "diff", "wheel", "compare-pixels"
    };
    private readonly IScriptModel? _model;
    private readonly AuthorSecrets _secrets;
    private readonly TimeProvider _clock;

    public ScriptAuthor(IScriptModel? model = null, AuthorSecrets? secrets = null, TimeProvider? clock = null)
    {
        _model = model;
        _secrets = secrets ?? new AuthorSecrets();
        _clock = clock ?? TimeProvider.System;
    }

    public static decimal Estimate(string prompt, decimal perThousand)
    {
        var rate = perThousand < 0.01m ? 0.01m : perThousand;
        var tokens = Math.Max(1, prompt.Length / 4);
        var cost = decimal.Round(tokens / 1000m * rate, 6, MidpointRounding.AwayFromZero);
        return cost < 0.000001m ? 0.000001m : cost;
    }

    public Task<AuthorDraft> GenerateAsync(AuthorRequest request, string previewDirectory, CancellationToken cancellationToken)
        => CompleteAsync(request, previewDirectory, false, cancellationToken);

    public Task<AuthorDraft> RepairAsync(AuthorRequest request, string previewDirectory, CancellationToken cancellationToken)
        => CompleteAsync(request, previewDirectory, true, cancellationToken);

    public static TestSuite ToSuite(string scriptJson)
    {
        if (AuthorPrompt.ContainsImage(scriptJson) || HasHook(scriptJson))
            throw new ExecutorDataException(ExecutorError.InvalidSuite);
        JsonObject script;
        try
        {
            script = JsonNode.Parse(scriptJson) as JsonObject ?? throw new ExecutorDataException(ExecutorError.InvalidSuite);
        }
        catch (JsonException)
        {
            throw new ExecutorDataException(ExecutorError.InvalidSuite);
        }
        if (!SchemaCheck.Matches(Schema, script)) throw new ExecutorDataException(ExecutorError.InvalidSuite);
        RequirePairs(script);
        var suite = new JsonObject
        {
            ["schema"] = ExecutorSchemas.Suite,
            ["schemaVersion"] = ExecutorSchemas.Version,
            ["id"] = "authored",
            ["name"] = script["name"]!.DeepClone(),
            ["stopOnFirstFailure"] = false,
            ["maskPasswordControls"] = true,
            ["cases"] = new JsonArray(CaseOf(script))
        };
        return SuiteLoader.Load(suite.ToJsonString());
    }

    public static void SaveConfirmed(string previewDirectory, string scriptJson)
    {
        _ = ToSuite(scriptJson);
        var root = Path.GetFullPath(previewDirectory);
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "confirmed-script.json");
        if (!ContainedPath.IsInside(root, path)) throw new ExecutorDataException(ExecutorError.InvalidSuite);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, scriptJson);
        File.Move(temporary, path, overwrite: true);
    }

    private async Task<AuthorDraft> CompleteAsync(AuthorRequest request, string previewDirectory, bool requireFailure, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string root;
        try
        {
            root = Path.GetFullPath(previewDirectory);
            Directory.CreateDirectory(root);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return new AuthorDraft("unavailable", AuthorMessages.Unavailable, "", null, 0);
        }

        if (requireFailure && string.IsNullOrWhiteSpace(request.FailedStep))
            return await StoreAsync(root, "empty", AuthorMessages.NoFailure, "", "", null, 0, null);
        if (string.IsNullOrWhiteSpace(request.Goal))
            return await StoreAsync(root, "empty", AuthorMessages.Empty, "", "", null, 0, null);
        if (HasImage(request))
            return await StoreAsync(root, "image", AuthorMessages.Image, "", "", null, 0, null);

        var prompt = AuthorPrompt.Build(request);
        var key = _secrets.Read(root);
        if (!string.IsNullOrEmpty(key) && prompt.Contains(key, StringComparison.Ordinal))
            return await StoreAsync(root, "key-leak", AuthorMessages.KeyLeak, prompt, "", key, 0, null);
        var gate = AuthorGate.Load(root);
        if (!gate.Enabled)
            return await StoreAsync(root, "disabled", AuthorMessages.Disabled, prompt, "", key, 0, null);
        if (_model is null)
            return await StoreAsync(root, "missing-key", AuthorMessages.MissingModel, prompt, "", key, 0, null);
        if (_model.Billed && string.IsNullOrEmpty(key))
            return await StoreAsync(root, "missing-key", AuthorMessages.MissingKey, prompt, "", key, 0, null);

        var ledger = new AuthorLedger(root, _clock);
        var estimate = _model.Billed ? Estimate(prompt, gate.CostPerThousandTokens) : 0;
        if (_model.Billed && !ledger.WouldFit(estimate, gate.DailySpendCap))
            return await StoreAsync(root, "over-budget", AuthorMessages.OverBudget, prompt, "", key, 0, _model.Name);

        ScriptModelReply reply;
        try
        {
            reply = await _model.CompleteAsync(new ScriptModelCall(prompt, key, gate.Model), cancellationToken);
        }
        catch (ScriptModelException error)
        {
            if (error.Sent && _model.Billed) ledger.Add(estimate);
            return await StoreAsync(root, "unavailable", error.Message, prompt, "", key, error.Sent ? 1 : 0, _model.Name);
        }
        catch (Exception error) when (error is HttpRequestException or JsonException || (error is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            if (_model.Billed) ledger.Add(estimate);
            return await StoreAsync(root, "unavailable", AuthorMessages.Unavailable, prompt, "", key, 1, _model.Name);
        }

        if (_model.Billed) ledger.Add(Math.Max(estimate, reply.Cost));
        if (!string.IsNullOrEmpty(key) && reply.Text.Contains(key, StringComparison.Ordinal))
            return await StoreAsync(root, "invalid", AuthorMessages.Invalid, prompt, reply.Text, key, 1, _model.Name);
        try
        {
            _ = ToSuite(reply.Text);
        }
        catch (ExecutorDataException)
        {
            return await StoreAsync(root, "invalid", AuthorMessages.Invalid, prompt, reply.Text, key, 1, _model.Name);
        }
        return await StoreAsync(root, "review", AuthorMessages.Review, prompt, reply.Text, key, 1, _model.Name);
    }

    private static JsonObject CaseOf(JsonObject script)
        => new()
        {
            ["id"] = script["id"]!.DeepClone(),
            ["name"] = script["name"]!.DeepClone(),
            ["allowFallback"] = false,
            ["steps"] = script["steps"]!.DeepClone()
        };

    private static void RequirePairs(JsonObject script)
    {
        foreach (var step in script["steps"]?.AsArray() ?? [])
        {
            if (step is not JsonObject obj) throw new ExecutorDataException(ExecutorError.InvalidSuite);
            var type = obj["type"]?.GetValue<string>();
            var action = obj["action"]?.GetValue<string>();
            var allowed = type == "web" ? WebActions : type == "scene3d" ? SceneActions : null;
            if (allowed is null || action is null || !allowed.Contains(action))
                throw new ExecutorDataException(ExecutorError.InvalidSuite);
        }
    }

    private static bool HasHook(string script)
        => script.Contains("测试钩子", StringComparison.Ordinal)
           || script.Contains("__TEST__", StringComparison.Ordinal)
           || script.Contains("test hook", StringComparison.OrdinalIgnoreCase);

    private static bool HasImage(AuthorRequest request)
        => AuthorPrompt.ContainsImage(request.Goal)
           || AuthorPrompt.ContainsImage(request.DomText)
           || AuthorPrompt.ContainsImage(request.SceneSnapshot)
           || AuthorPrompt.ContainsImage(request.FailedStep)
           || AuthorPrompt.ContainsImage(request.DiffText);

    private static async Task<AuthorDraft> StoreAsync(string root, string status, string message, string prompt, string response, string? key, int modelCalls, string? model)
    {
        var safePrompt = TextRules.Redact(prompt, key, 20000);
        var safeResponse = TextRules.Redact(response, key, 20000);
        var script = status == "review" ? response : null;
        var directory = Path.Combine(root, "author-log");
        Directory.CreateDirectory(directory);
        var json = JsonSerializer.Serialize(new
        {
            schema = "workbench.test-executor.author-log.v1",
            schemaVersion = 1,
            status,
            modelCalls,
            model = model ?? "",
            prompt = safePrompt,
            response = safeResponse
        });
        await File.WriteAllTextAsync(Path.Combine(directory, Guid.NewGuid().ToString("N") + ".json"), json);
        return new AuthorDraft(status, message, safePrompt, script, modelCalls);
    }
}
