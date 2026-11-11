using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Workbench.TestExecutor;

public sealed class OpenAiComputerUseProvider : IComputerUseProvider
{
    public const string Endpoint = "https://api.openai.com/v1/responses";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly IApiKeyStore _keys;
    private readonly Func<ExecutionPolicy> _policy;
    private readonly HttpClient _http;

    public OpenAiComputerUseProvider(IApiKeyStore keys, Func<ExecutionPolicy> policy, HttpMessageHandler? handler = null)
    {
        _keys = keys;
        _policy = policy;
        _http = new HttpClient(handler ?? new SocketsHttpHandler(), disposeHandler: false)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
    }

    public string Name => "openai";

    public Task<ComputerUseSession> StartAsync(ComputerUseStart start, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new ComputerUseSession(Guid.NewGuid().ToString("N")));
    }

    public async Task<ComputerUseTurn> ObserveAsync(ComputerUseSession session, ComputerUseObservation observation, CancellationToken cancellationToken)
    {
        var policy = _policy();
        if (!policy.CanUseComputer) throw new ComputerUseException(ExecutorError.ComputerUseDisabled);
        var key = _keys.Read(CredentialTargets.OpenAi);
        if (string.IsNullOrEmpty(key)) throw new ComputerUseException(ExecutorError.CredentialMissing);
        if (!TextRules.IsSafeId(policy.ComputerUseModel)) throw new ComputerUseException(ExecutorError.ProviderUnavailable);
        if (observation.Screenshot.Png.Length > 2_000_000) throw new ComputerUseException(ExecutorError.ProviderUnavailable);

        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        request.Content = new StringContent(BuildBody(policy.ComputerUseModel, observation), Encoding.UTF8, "application/json");
        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException)
        {
            throw new ComputerUseException(ExecutorError.ProviderUnavailable);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ComputerUseException(ExecutorError.Timeout);
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode || body.Length > 1_000_000)
                throw new ComputerUseException(ExecutorError.ProviderUnavailable);
            var actions = ComputerUseResponseParser.Parse(body);
            return new ComputerUseTurn(actions, actions.Count == 0 ? "empty" : "actions");
        }
    }

    public Task EndAsync(ComputerUseSession session, CancellationToken cancellationToken) => Task.CompletedTask;

    internal static string BuildBody(string model, ComputerUseObservation observation)
    {
        var payload = new
        {
            model,
            tools = new object[]
            {
                new { type = "computer_use_preview", display_width = 1280, display_height = 720, environment = "windows" }
            },
            input = new object[]
            {
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new { type = "input_text", text = TextRules.Clip(observation.Instruction, 500) },
                        new { type = "input_image", image_url = "data:image/png;base64," + Convert.ToBase64String(observation.Screenshot.Png) }
                    }
                }
            },
            truncation = "auto"
        };
        return JsonSerializer.Serialize(payload, Json);
    }
}
