using System.Net;

namespace Agent.Mobile.Services;

public sealed record AgentApiHealthResult(
    bool IsReachable,
    bool IsReady,
    HttpStatusCode? LiveStatusCode,
    HttpStatusCode? ReadyStatusCode,
    string Message);

public interface IAgentApiClient
{
    Task<AgentApiHealthResult> CheckHealthAsync(string baseUrl, CancellationToken cancellationToken = default);
}

/// <summary>Anonymous liveness/readiness probes only; protected user data is never sent without an auth provider.</summary>
public sealed class AgentApiClient : IAgentApiClient
{
    private readonly HttpClient _http;

    public AgentApiClient(HttpClient http) => _http = http ?? throw new ArgumentNullException(nameof(http));

    public async Task<AgentApiHealthResult> CheckHealthAsync(
        string baseUrl, CancellationToken cancellationToken = default)
    {
        var normalized = AgentSettingsStore.NormalizeApiBaseUrl(baseUrl);
        if (normalized.Length == 0)
            throw new ArgumentException("Save an HTTPS API address before testing the connection.", nameof(baseUrl));

        using var live = await ProbeAsync(new Uri(normalized + "/health/live"), cancellationToken).ConfigureAwait(false);
        if (!live.IsSuccessStatusCode)
            return new(false, false, live.StatusCode, null,
                $"A API não respondeu como esperado (liveness: {(int)live.StatusCode}).");

        using var ready = await ProbeAsync(new Uri(normalized + "/health/ready"), cancellationToken).ConfigureAwait(false);
        if (!ready.IsSuccessStatusCode)
            return new(true, false, live.StatusCode, ready.StatusCode,
                $"A API está acessível, mas não está pronta (readiness: {(int)ready.StatusCode}). Verifique PostgreSQL e a configuração do servidor.");

        return new(true, true, live.StatusCode, ready.StatusCode,
            "API acessível e serviço pronto. Isso não significa que a conta Microsoft ou o Codex já estejam conectados.");
    }

    private async Task<HttpResponseMessage> ProbeAsync(Uri uri, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        return await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
            .ConfigureAwait(false);
    }
}
