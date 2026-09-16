using System.Net.Http.Json;
using System.Text.Json.Serialization;
using ProjectHub.Core;

namespace ProjectHub.Agent;

public sealed class ProjectStateSender(HttpClient httpClient)
{
    public async Task SendAsync(
        ProjectRegistration project,
        ProjectState state,
        CancellationToken cancellationToken = default)
    {
        var payload = new ProjectStatePayload(
            state.WorkstationId,
            project.DisplayName,
            project.RepositoryUrl,
            state.Branch,
            state.HeadSha,
            state.Dirty,
            state.ChangedCount,
            state.UntrackedCount,
            state.DeletedCount,
            state.DiffFingerprint,
            state.LastFileActivity);

        using var response = await httpClient.PostAsJsonAsync(
            $"api/projects/{Uri.EscapeDataString(project.ProjectId)}/state",
            payload,
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (body.Length > 500) body = body[..500];
            throw new HttpRequestException(
                $"Project state failed with {(int)response.StatusCode} {response.ReasonPhrase}: {body}");
        }
    }

    private sealed record ProjectStatePayload(
        [property: JsonPropertyName("workstationId")] string WorkstationId,
        [property: JsonPropertyName("displayName")] string DisplayName,
        [property: JsonPropertyName("repositoryUrl")] string? RepositoryUrl,
        [property: JsonPropertyName("branch")] string? Branch,
        [property: JsonPropertyName("headSha")] string? HeadSha,
        [property: JsonPropertyName("dirty")] bool Dirty,
        [property: JsonPropertyName("changedCount")] int ChangedCount,
        [property: JsonPropertyName("untrackedCount")] int UntrackedCount,
        [property: JsonPropertyName("deletedCount")] int DeletedCount,
        [property: JsonPropertyName("diffFingerprint")] string? DiffFingerprint,
        [property: JsonPropertyName("lastFileActivity")] DateTimeOffset? LastFileActivity);
}
