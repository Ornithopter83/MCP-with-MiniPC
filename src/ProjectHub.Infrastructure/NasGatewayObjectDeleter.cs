using System.Net.Http.Headers;
using System.Net.Http.Json;
using ProjectHub.Core;

namespace ProjectHub.Infrastructure;

public sealed class NasGatewayObjectDeleter(IHttpClientFactory clientFactory, LargeDataOptions options) : INasGatewayObjectDeleter
{
    public async Task<NasDeleteResult> DeleteAsync(string assertion, LargeDataAssertionScope scope, bool deleteCanonical, CancellationToken cancellationToken)
    {
        var client = clientFactory.CreateClient("NasGateway");
        using var request = new HttpRequestMessage(HttpMethod.Post, options.GatewayUrl.TrimEnd('/') + "/delete-object.php");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", assertion);
        request.Content = JsonContent.Create(new { delete_canonical = deleteCanonical });
        using var response = await client.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"NAS delete failed ({(int)response.StatusCode}): {body}");
        return System.Text.Json.JsonSerializer.Deserialize<NasDeleteResult>(body, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("NAS delete returned an empty response.");
    }

}
