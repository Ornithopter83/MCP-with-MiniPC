using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace ProjectHub.Agent;

public sealed class HeartbeatSender(HttpClient httpClient, AgentOptions options)
{
    public async Task SendOnceAsync(CancellationToken cancellationToken = default)
    {
        var payload = new HeartbeatPayload(
            options.WorkstationId,
            options.DisplayName,
            Environment.MachineName);

        using var response = await httpClient.PostAsJsonAsync(
            "api/agent/heartbeat", payload, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (body.Length > 500) body = body[..500];
            throw new HttpRequestException(
                $"Heartbeat failed with {(int)response.StatusCode} {response.ReasonPhrase}: {body}");
        }
    }

    private sealed record HeartbeatPayload(
        [property: JsonPropertyName("workstationId")] string WorkstationId,
        [property: JsonPropertyName("displayName")] string DisplayName,
        [property: JsonPropertyName("hostname")] string Hostname);
}
