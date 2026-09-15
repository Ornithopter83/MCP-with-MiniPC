using System.Net.Http.Json;
using ProjectHub.Core;

namespace ProjectHub.Infrastructure;

public sealed class SupabaseWorkstationRepository(
    IHttpClientFactory httpClientFactory,
    SupabaseOptions options) : IWorkstationRepository
{
    public async Task<Workstation> UpsertAsync(
        Workstation workstation,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var client = httpClientFactory.CreateClient("Supabase");
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "workstations?on_conflict=workstation_id");
        request.Headers.Add("Prefer", "resolution=merge-duplicates,return=representation");
        request.Content = JsonContent.Create(workstation);

        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var rows = await response.Content.ReadFromJsonAsync<List<Workstation>>(cancellationToken);
        return rows is { Count: > 0 } ? rows[0] : workstation;
    }

    public async Task<IReadOnlyList<Workstation>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var client = httpClientFactory.CreateClient("Supabase");
        var rows = await client.GetFromJsonAsync<List<Workstation>>(
            "workstations?select=workstation_id,display_name,hostname,last_seen,created_at,updated_at&order=display_name.asc",
            cancellationToken);

        return rows ?? [];
    }

    private void EnsureConfigured()
    {
        if (!options.IsConfigured)
        {
            throw new InvalidOperationException(
                "Supabase is not configured. Set the server environment variables before using workstation storage.");
        }
    }
}
