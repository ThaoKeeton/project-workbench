using System.Text;
using System.Text.Json;

namespace Workbench.TestExecutor;

public sealed class LinearHttpIssueClient : ILinearIssueClient
{
    public const string Endpoint = "https://api.linear.app/graphql";
    private const string FindQuery = """
        query Find($title: String!, $teamId: String!) {
          issues(filter: { title: { eq: $title }, team: { id: { eq: $teamId } }, state: { type: { nin: ["completed", "canceled"] } } }) {
            nodes { id identifier title }
          }
        }
        """;
    private const string CreateQuery = """
        mutation Create($input: IssueCreateInput!) {
          issueCreate(input: $input) { success issue { id identifier } }
        }
        """;
    private const string CommentQuery = """
        mutation Comment($input: CommentCreateInput!) {
          commentCreate(input: $input) { success comment { id } }
        }
        """;
    private const string UploadQuery = """
        mutation Upload($size: Int!, $contentType: String!, $filename: String!) {
          fileUpload(size: $size, contentType: $contentType, filename: $filename) {
            success
            uploadFile { uploadUrl assetUrl }
          }
        }
        """;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly IApiKeyStore _keys;
    private readonly HttpClient _http;

    public LinearHttpIssueClient(IApiKeyStore keys, HttpMessageHandler? handler = null)
    {
        _keys = keys;
        _http = new HttpClient(handler ?? new SocketsHttpHandler(), disposeHandler: false)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
    }

    public async Task<LinearIssue?> FindOpenByFingerprintAsync(string fingerprint, string? teamId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(teamId)) return null;
        var key = RequireKey();
        using var document = await PostAsync(key, FindQuery, new { title = Title(fingerprint), teamId }, cancellationToken);
        if (!document.RootElement.TryGetProperty("data", out var data) || !data.TryGetProperty("issues", out var issues))
            return null;
        if (!issues.TryGetProperty("nodes", out var nodes) || nodes.ValueKind != JsonValueKind.Array) return null;
        foreach (var node in nodes.EnumerateArray())
        {
            var title = node.TryGetProperty("title", out var titleValue) ? titleValue.GetString() : null;
            if (title != Title(fingerprint)) continue;
            var id = node.GetProperty("id").GetString();
            if (!string.IsNullOrEmpty(id)) return new LinearIssue(id, title, fingerprint);
        }
        return null;
    }

    public async Task<LinearIssue> CreateAsync(LinearIssueDraft draft, CancellationToken cancellationToken)
    {
        var key = RequireKey();
        var body = draft.Body;
        if (draft.MaskedPng is { Length: > 0 })
        {
            var asset = await UploadMaskedAsync(key, draft.MaskedPng, cancellationToken);
            if (asset is not null) body += "\n\n![masked](" + asset + ")";
        }
        var input = new Dictionary<string, string?>
        {
            ["teamId"] = draft.TeamId,
            ["title"] = draft.Title,
            ["description"] = body
        };
        if (!string.IsNullOrWhiteSpace(draft.ProjectId)) input["projectId"] = draft.ProjectId;
        using var document = await PostAsync(key, CreateQuery, new { input }, cancellationToken);
        var issue = document.RootElement.GetProperty("data").GetProperty("issueCreate").GetProperty("issue");
        var id = issue.GetProperty("id").GetString() ?? throw new ExecutorDataException(ExecutorError.ProviderUnavailable);
        return new LinearIssue(id, draft.Title, draft.Fingerprint);
    }

    public async Task CommentAsync(string issueId, string body, CancellationToken cancellationToken)
    {
        var key = RequireKey();
        using var document = await PostAsync(key, CommentQuery, new { input = new { issueId, body } }, cancellationToken);
        _ = document;
    }

    private async Task<string?> UploadMaskedAsync(string key, byte[] png, CancellationToken cancellationToken)
    {
        try
        {
            using var document = await PostAsync(key, UploadQuery, new { size = png.Length, contentType = "image/png", filename = "masked.png" }, cancellationToken);
            var upload = document.RootElement.GetProperty("data").GetProperty("fileUpload").GetProperty("uploadFile");
            var uploadUrl = upload.GetProperty("uploadUrl").GetString();
            var assetUrl = upload.GetProperty("assetUrl").GetString();
            if (!IsHttps(uploadUrl) || !IsHttps(assetUrl)) return null;
            using var request = new HttpRequestMessage(HttpMethod.Put, uploadUrl);
            request.Content = new ByteArrayContent(png);
            request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
            using var response = await _http.SendAsync(request, cancellationToken);
            return response.IsSuccessStatusCode ? assetUrl : null;
        }
        catch (Exception error) when (error is HttpRequestException or ExecutorDataException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return null;
        }
    }

    private string RequireKey()
    {
        var key = _keys.Read(CredentialTargets.Linear);
        if (string.IsNullOrWhiteSpace(key)) throw new ExecutorDataException(ExecutorError.CredentialMissing);
        return key;
    }

    private async Task<JsonDocument> PostAsync(string key, string query, object variables, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint);
        request.Headers.TryAddWithoutValidation("Authorization", key);
        request.Content = new StringContent(JsonSerializer.Serialize(new { query, variables }, Json), Encoding.UTF8, "application/json");
        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException)
        {
            throw new ExecutorDataException(ExecutorError.ProviderUnavailable);
        }
        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode || body.Length > 1_000_000)
                throw new ExecutorDataException(ExecutorError.ProviderUnavailable);
            try
            {
                return JsonDocument.Parse(body);
            }
            catch (JsonException)
            {
                throw new ExecutorDataException(ExecutorError.ProviderUnavailable);
            }
        }
    }

    private static string Title(string fingerprint) => "自动化测试失败 " + fingerprint;

    private static bool IsHttps(string? value)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;
}
