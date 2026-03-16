using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace STS2AIAgent.Desktop;

internal sealed class AgentApiClient
{
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public AgentApiClient(string baseAddress)
    {
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(baseAddress, UriKind.Absolute),
            Timeout = TimeSpan.FromSeconds(10)
        };
    }

    public Task<ServiceHealthPayload> GetHealthAsync(CancellationToken cancellationToken) =>
        GetAsync<ServiceHealthPayload>("health", cancellationToken);

    public Task<AgentSnapshot> GetSnapshotAsync(CancellationToken cancellationToken) =>
        GetAsync<AgentSnapshot>("agent/snapshot", cancellationToken);

    public Task<AgentConfig> SaveConfigAsync(AgentConfig config, CancellationToken cancellationToken) =>
        PostAsync<AgentConfig>("agent/config", config, cancellationToken);

    public Task<ConnectivityProbeResult> TestLlmAsync(AgentConfig config, CancellationToken cancellationToken) =>
        PostAsync<ConnectivityProbeResult>("agent/test-llm", config, cancellationToken);

    public Task StartAsync(CancellationToken cancellationToken) =>
        PostWithoutBodyAsync("agent/start", cancellationToken);

    public Task PauseAsync(CancellationToken cancellationToken) =>
        PostWithoutBodyAsync("agent/pause", cancellationToken);

    public Task StepAsync(CancellationToken cancellationToken) =>
        PostWithoutBodyAsync("agent/request-step", cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) =>
        PostWithoutBodyAsync("agent/stop", cancellationToken);

    private async Task<T> GetAsync<T>(string relativePath, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(relativePath, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        EnsureSuccess(response, body);
        return ReadEnvelope<T>(body);
    }

    private async Task<T> PostAsync<T>(string relativePath, object payload, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(payload, _jsonOptions);
        using var response = await _httpClient.PostAsync(
            relativePath,
            new StringContent(json, Encoding.UTF8, "application/json"),
            cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        EnsureSuccess(response, body);
        return ReadEnvelope<T>(body);
    }

    private async Task PostWithoutBodyAsync(string relativePath, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsync(relativePath, new StringContent(string.Empty), cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        EnsureSuccess(response, body);

        var envelope = JsonSerializer.Deserialize<ApiEnvelope<JsonElement>>(body, _jsonOptions);
        if (envelope?.ok != true)
        {
            throw new InvalidOperationException(envelope?.error?.message ?? "Request failed.");
        }
    }

    private T ReadEnvelope<T>(string body)
    {
        var envelope = JsonSerializer.Deserialize<ApiEnvelope<T>>(body, _jsonOptions);
        if (envelope?.ok != true || envelope.data == null)
        {
            throw new InvalidOperationException(envelope?.error?.message ?? "Server returned an invalid response.");
        }

        return envelope.data;
    }

    private void EnsureSuccess(HttpResponseMessage response, string body)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        try
        {
            var envelope = JsonSerializer.Deserialize<ApiEnvelope<JsonElement>>(body, _jsonOptions);
            if (!string.IsNullOrWhiteSpace(envelope?.error?.message))
            {
                throw new InvalidOperationException(envelope.error.message);
            }
        }
        catch (JsonException)
        {
        }

        response.EnsureSuccessStatusCode();
    }
}
