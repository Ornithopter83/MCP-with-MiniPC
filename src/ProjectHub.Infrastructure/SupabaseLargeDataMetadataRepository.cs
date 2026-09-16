using System.Net.Http.Json;
using ProjectHub.Core;

namespace ProjectHub.Infrastructure;

public sealed class SupabaseLargeDataMetadataRepository(IHttpClientFactory clientFactory) : ILargeDataMetadataRepository
{
    public Task UpsertObjectAsync(LargeObjectIdentity objectIdentity, LargeDataLifecycle lifecycle, CancellationToken cancellationToken) =>
        SendAsync("large_objects", new { sha256 = objectIdentity.Sha256, size_bytes = objectIdentity.SizeBytes, lifecycle = lifecycle.ToString().ToUpperInvariant() }, "sha256", cancellationToken);

    public Task UpsertProjectFileAsync(ProjectLargeFile projectFile, CancellationToken cancellationToken) =>
        SendAsync("project_large_files", new { project_id = projectFile.ProjectId, relative_path = projectFile.RelativePath, sha256 = projectFile.Object.Sha256, size_bytes = projectFile.Object.SizeBytes, lifecycle = projectFile.Lifecycle.ToString().ToUpperInvariant(), checkpoint_commit_sha = projectFile.CheckpointCommitSha }, "project_id,relative_path", cancellationToken);

    public async Task CreateDataSetAsync(LargeDataSet dataSet, CancellationToken cancellationToken)
    {
        var client = clientFactory.CreateClient("Supabase");
        using var response = await client.PostAsJsonAsync("large_data_sets", new { project_id = dataSet.ProjectId, commit_sha = dataSet.CommitSha, status = dataSet.Status.ToString().ToUpperInvariant() }, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private async Task SendAsync(string table, object value, string filter, CancellationToken cancellationToken)
    {
        var client = clientFactory.CreateClient("Supabase");
        using var request = new HttpRequestMessage(HttpMethod.Post, table + "?on_conflict=" + filter);
        request.Headers.Add("Prefer", "resolution=merge-duplicates");
        request.Content = JsonContent.Create(value);
        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}
