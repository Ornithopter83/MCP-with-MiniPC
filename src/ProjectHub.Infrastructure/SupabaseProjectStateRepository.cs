using System.Net.Http.Json;
using ProjectHub.Core;

namespace ProjectHub.Infrastructure;

public sealed class SupabaseProjectStateRepository(
    IHttpClientFactory httpClientFactory,
    SupabaseOptions options) : IProjectStateRepository
{
    public async Task UpsertProjectAsync(Project project, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        await SendUpsertAsync("projects?on_conflict=project_id", project, cancellationToken);
    }

    public async Task UpsertProjectStateAsync(ProjectState state, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        await SendUpsertAsync("project_states?on_conflict=project_id,workstation_id", state, cancellationToken);
    }

    public async Task<ProjectState?> GetProjectStateAsync(
        string projectId,
        string workstationId,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        var client = httpClientFactory.CreateClient("Supabase");
        var rows = await client.GetFromJsonAsync<List<ProjectState>>(
            $"project_states?select=project_id,workstation_id,branch,head_sha,dirty,changed_count,untracked_count,deleted_count,diff_fingerprint,last_file_activity,last_seen&project_id=eq.{Uri.EscapeDataString(projectId)}&workstation_id=eq.{Uri.EscapeDataString(workstationId)}&limit=1",
            cancellationToken);
        return rows is { Count: > 0 } ? rows[0] : null;
    }

    public async Task<IReadOnlyList<ProjectState>> GetProjectStatesAsync(
        string projectId,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        var client = httpClientFactory.CreateClient("Supabase");
        var rows = await client.GetFromJsonAsync<List<ProjectState>>(
            $"project_states?select=project_id,workstation_id,branch,head_sha,dirty,changed_count,untracked_count,deleted_count,diff_fingerprint,last_file_activity,last_seen&project_id=eq.{Uri.EscapeDataString(projectId)}&order=workstation_id.asc",
            cancellationToken);
        return rows ?? [];
    }

    public Task AddEventAsync(ProjectEvent projectEvent, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Project events are outside the 04-G minimum storage path.");

    private async Task SendUpsertAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient("Supabase");
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.Add("Prefer", "resolution=merge-duplicates,return=minimal");
        request.Content = JsonContent.Create(value);
        using var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            if (errorBody.Length > 2000) errorBody = errorBody[..2000];
            throw new HttpRequestException(
                $"Supabase returned {(int)response.StatusCode} {response.ReasonPhrase}: {errorBody}");
        }
    }

    private void EnsureConfigured()
    {
        if (!options.IsConfigured)
        {
            throw new InvalidOperationException(
                "Supabase is not configured. Set the server environment variables before using project-state storage.");
        }
    }
}
