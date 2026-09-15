using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ProjectHub.Server.Tests;

public sealed class StatusEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public StatusEndpointTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Status_ReturnsExpectedPayload()
    {
        using var response = await _client.GetAsync("/api/status");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<StatusPayload>();

        Assert.NotNull(payload);
        Assert.Equal("ProjectHub", payload.Server);
        Assert.Equal("ok", payload.Status);
        Assert.Equal("supabase", payload.Database);
        Assert.NotEqual(default, payload.Time);
    }

    private sealed record StatusPayload(
        string Server,
        string Status,
        string Database,
        DateTimeOffset Time);
}
