using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Json.Schema;

namespace Workbench.TestExecutor;

public sealed class FailureReport
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public string Schema { get; init; } = ExecutorSchemas.FailureReport;
    public int SchemaVersion { get; init; } = ExecutorSchemas.Version;
    public required string CaseId { get; init; }
    public required string StepId { get; init; }
    public required string Expected { get; init; }
    public required string Actual { get; init; }
    public required string Engine { get; init; }
    public required string ErrorType { get; init; }
    public int TimeoutSeconds { get; init; }
    public int Attempts { get; init; }
    public bool NeedsHumanReview { get; init; }
    public required IReadOnlyList<string> Log { get; init; }
    public string? MaskedScreenshotFile { get; init; }
    public required string Fingerprint { get; init; }

    [JsonIgnore]
    public string Markdown { get; set; } = "";

    public static string FingerprintFor(string caseId, string stepId, string errorType)
    {
        if (!TextRules.IsSafeId(caseId) || !TextRules.IsSafeId(stepId) || !TextRules.IsSafeId(errorType))
            throw new ExecutorDataException(ExecutorError.InvalidRun);
        return caseId + "|" + stepId + "|" + errorType;
    }

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    public static string ToMarkdown(FailureReport report)
    {
        var lines = new List<string>
        {
            "# 失败报告",
            "",
            "- 用例：" + report.CaseId,
            "- 步骤：" + report.StepId,
            "- 引擎：" + report.Engine,
            "- 期望：" + report.Expected,
            "- 实际：" + report.Actual,
            "- 错误类型：" + report.ErrorType,
            "- 超时：" + report.TimeoutSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture) + " 秒",
            "- 尝试次数：" + report.Attempts.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "- 人工复核：" + (report.NeedsHumanReview ? "是" : "否"),
            "- 指纹：" + report.Fingerprint,
            "",
            "## 日志",
            ""
        };
        if (report.Log.Count == 0) lines.Add("- （无）");
        else lines.AddRange(report.Log.Select(line => "- " + line));
        lines.Add("");
        lines.Add("## 截图");
        lines.Add("");
        lines.Add(report.MaskedScreenshotFile is null ? "遮罩后的截图：无" : "遮罩后的截图：" + report.MaskedScreenshotFile);
        lines.Add("");
        return string.Join('\n', lines);
    }
}

public sealed record ExportResult(string Status, string? IssueId);

public interface IFailureExporter
{
    Task<ExportResult> ExportAsync(FailureReport report, byte[]? maskedPng, ExecutionPolicy policy, CancellationToken cancellationToken);
}

public sealed class RunArtifactStore
{
    private static readonly JsonSchema ReportSchema = JsonSchema.FromText(SuiteLoader.ReadResource("Workbench.TestExecutor.failure-report.v2.schema.json"));
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static async Task<FailureReport> WriteFailureAsync(string directory, FailureReport report, MaskedImage? masked, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(directory);
        string? file = null;
        if (masked is not null)
        {
            file = report.CaseId + "-" + report.StepId + "-" + report.ErrorType + ".png";
            await File.WriteAllBytesAsync(ContainedPath.Combine(directory, file), masked.Png, cancellationToken);
        }
        var stored = new FailureReport
        {
            CaseId = report.CaseId,
            StepId = report.StepId,
            Expected = report.Expected,
            Actual = report.Actual,
            Engine = report.Engine,
            ErrorType = report.ErrorType,
            TimeoutSeconds = report.TimeoutSeconds,
            Attempts = report.Attempts,
            NeedsHumanReview = report.NeedsHumanReview,
            Log = report.Log,
            MaskedScreenshotFile = file,
            Fingerprint = report.Fingerprint,
            Markdown = ""
        };
        var json = stored.ToJson();
        if (JsonNode.Parse(json) is not JsonNode node || !SchemaCheck.Matches(ReportSchema, node))
            throw new ExecutorDataException(ExecutorError.InvalidRun);
        var markdown = FailureReport.ToMarkdown(stored);
        var stem = report.CaseId + "-" + report.StepId + "-" + report.ErrorType;
        await File.WriteAllTextAsync(ContainedPath.Combine(directory, stem + ".json"), json, cancellationToken);
        await File.WriteAllTextAsync(ContainedPath.Combine(directory, stem + ".md"), markdown, cancellationToken);
        stored.Markdown = markdown;
        return stored;
    }

    public static async Task WriteEvidenceAsync(string directory, string caseId, string stepId, string status, string engine, IReadOnlyList<string> log, MaskedImage? masked, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(directory);
        string? file = null;
        if (masked is not null)
        {
            file = caseId + "-" + stepId + ".png";
            await File.WriteAllBytesAsync(ContainedPath.Combine(directory, file), masked.Png, cancellationToken);
        }
        var json = JsonSerializer.Serialize(new
        {
            schema = ExecutorSchemas.StepEvidence,
            schemaVersion = ExecutorSchemas.Version,
            caseId,
            stepId,
            status,
            engine,
            log,
            maskedScreenshotFile = file
        }, Json);
        await File.WriteAllTextAsync(ContainedPath.Combine(directory, caseId + "-" + stepId + ".evidence.json"), json, cancellationToken);
    }

    public static (FailureReport Report, byte[]? Png) ReadFailure(string directory, string fingerprint)
    {
        if (!Directory.Exists(directory)) throw new ExecutorDataException(ExecutorError.ReportNotFound);
        foreach (var path in Directory.EnumerateFiles(directory, "*.json"))
        {
            if (path.EndsWith(".evidence.json", StringComparison.Ordinal)) continue;
            var info = new FileInfo(path);
            if (info.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
            FailureReport? report;
            try
            {
                report = JsonSerializer.Deserialize<FailureReport>(File.ReadAllText(path), Json);
            }
            catch (JsonException)
            {
                continue;
            }
            if (report?.Fingerprint != fingerprint) continue;
            var markdownPath = Path.ChangeExtension(path, ".md");
            if (File.Exists(markdownPath)) report.Markdown = File.ReadAllText(markdownPath);
            byte[]? png = null;
            if (report.MaskedScreenshotFile is not null)
            {
                var image = ContainedPath.Combine(directory, report.MaskedScreenshotFile);
                if (File.Exists(image)) png = File.ReadAllBytes(image);
            }
            return (report, png);
        }
        throw new ExecutorDataException(ExecutorError.ReportNotFound);
    }
}

public sealed record LinearIssue(string Id, string Title, string Fingerprint);

public sealed record LinearIssueDraft(string Title, string Body, string Fingerprint, string? TeamId, string? ProjectId, byte[]? MaskedPng);

public interface ILinearIssueClient
{
    Task<LinearIssue?> FindOpenByFingerprintAsync(string fingerprint, string? teamId, CancellationToken cancellationToken);

    Task<LinearIssue> CreateAsync(LinearIssueDraft draft, CancellationToken cancellationToken);

    Task CommentAsync(string issueId, string body, CancellationToken cancellationToken);
}

public sealed class FakeLinearIssueClient : ILinearIssueClient
{
    private readonly List<LinearIssue> _open = [];

    public int Calls { get; private set; }

    public List<string> Comments { get; } = [];

    public List<byte[]?> CreatedImages { get; } = [];

    public Task<LinearIssue?> FindOpenByFingerprintAsync(string fingerprint, string? teamId, CancellationToken cancellationToken)
    {
        Calls++;
        return Task.FromResult(_open.Find(issue => issue.Fingerprint == fingerprint));
    }

    public Task<LinearIssue> CreateAsync(LinearIssueDraft draft, CancellationToken cancellationToken)
    {
        Calls++;
        var issue = new LinearIssue("SYN-" + (_open.Count + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), draft.Title, draft.Fingerprint);
        _open.Add(issue);
        CreatedImages.Add(draft.MaskedPng);
        return Task.FromResult(issue);
    }

    public Task CommentAsync(string issueId, string body, CancellationToken cancellationToken)
    {
        Calls++;
        Comments.Add(issueId + ":" + body);
        return Task.CompletedTask;
    }
}

public sealed class LinearFailureExporter : IFailureExporter
{
    private readonly ILinearIssueClient _client;

    public LinearFailureExporter(ILinearIssueClient client) => _client = client;

    public async Task<ExportResult> ExportAsync(FailureReport report, byte[]? maskedPng, ExecutionPolicy policy, CancellationToken cancellationToken)
    {
        if (policy.LocalOnly) return new ExportResult("skipped-local-only", null);
        if (policy.Export == ExportMode.Ask) return new ExportResult("skipped-ask", null);
        if (policy.Export == ExportMode.Local) return new ExportResult("skipped-local", null);
        if (string.IsNullOrWhiteSpace(policy.LinearTeamId)) return new ExportResult("skipped-no-team", null);
        try
        {
            var title = "自动化测试失败 " + report.Fingerprint;
            var body = string.IsNullOrEmpty(report.Markdown) ? FailureReport.ToMarkdown(report) : report.Markdown;
            var existing = await _client.FindOpenByFingerprintAsync(report.Fingerprint, policy.LinearTeamId, cancellationToken);
            if (existing is not null)
            {
                await _client.CommentAsync(existing.Id, body, cancellationToken);
                return new ExportResult("commented", existing.Id);
            }
            var created = await _client.CreateAsync(new LinearIssueDraft(title, body, report.Fingerprint, policy.LinearTeamId, policy.LinearProjectId, maskedPng), cancellationToken);
            return new ExportResult("created", created.Id);
        }
        catch (ExecutorDataException error) when (error.Code == ExecutorError.CredentialMissing)
        {
            return new ExportResult("skipped-no-credential", null);
        }
        catch (ComputerUseException error) when (error.Code == ExecutorError.CredentialMissing)
        {
            return new ExportResult("skipped-no-credential", null);
        }
    }
}
