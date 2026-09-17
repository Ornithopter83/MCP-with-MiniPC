using System.Net.Http.Json;
using System.Text.Json.Serialization;
using ProjectHub.Core;

namespace ProjectHub.Infrastructure;

public sealed class SupabaseLargeDataMetadataRepository(IHttpClientFactory clientFactory) : ILargeDataMetadataRepository
{
    public async Task<LargeUploadSession?> FindResumableSessionAsync(string projectId, string workstationId, LargeObjectIdentity objectIdentity, CancellationToken cancellationToken)
    {
        var client = clientFactory.CreateClient("Supabase");
        var url = "large_upload_sessions?project_id=eq." + Uri.EscapeDataString(projectId) +
                  "&workstation_id=eq." + Uri.EscapeDataString(workstationId) +
                  "&sha256=eq." + Uri.EscapeDataString(objectIdentity.Sha256) +
                  "&size_bytes=eq." + objectIdentity.SizeBytes +
                  "&lifecycle=eq.UPLOADING&order=updated_at.desc&limit=1";
        var rows = await client.GetFromJsonAsync<List<UploadSessionRow>>(url, cancellationToken);
        var row = rows?.FirstOrDefault();
        return row is null ? null : new LargeUploadSession(row.Id, row.ProjectId, row.WorkstationId, new LargeObjectIdentity(row.Sha256, row.SizeBytes), row.StorageScope, row.ChunkSizeBytes, ParseLifecycle(row.Lifecycle));
    }

    public Task UpsertUploadSessionAsync(LargeUploadSession session, CancellationToken cancellationToken) =>
        SendAsync("large_upload_sessions", new { id = session.SessionId, project_id = session.ProjectId, workstation_id = session.WorkstationId, sha256 = session.Object.Sha256, size_bytes = session.Object.SizeBytes, chunk_size_bytes = session.ChunkSizeBytes, storage_scope = session.StorageScope, lifecycle = session.Lifecycle.ToString().ToUpperInvariant() }, "id", cancellationToken);

    public Task MarkUploadSessionCompletedAsync(string sessionId, CancellationToken cancellationToken) =>
        PatchAsync("large_upload_sessions?id=eq." + Uri.EscapeDataString(sessionId), new { lifecycle = LargeDataLifecycle.Staged.ToString().ToUpperInvariant() }, cancellationToken);

    public Task UpsertObjectAsync(LargeObjectIdentity objectIdentity, LargeDataLifecycle lifecycle, CancellationToken cancellationToken) =>
        SendAsync("large_objects", new { sha256 = objectIdentity.Sha256, size_bytes = objectIdentity.SizeBytes, lifecycle = lifecycle.ToString().ToUpperInvariant() }, "sha256", cancellationToken);

    public Task UpsertProjectFileAsync(ProjectLargeFile projectFile, CancellationToken cancellationToken) =>
        SendAsync("project_large_files", new { project_id = projectFile.ProjectId, relative_path = projectFile.RelativePath, sha256 = projectFile.Object.Sha256, size_bytes = projectFile.Object.SizeBytes, lifecycle = projectFile.Lifecycle.ToString().ToUpperInvariant(), checkpoint_commit_sha = projectFile.CheckpointCommitSha }, "project_id,relative_path", cancellationToken);

    public Task MarkStagedAsync(ProjectLargeFile projectFile, CancellationToken cancellationToken) =>
        SendAsync("project_large_files", new { project_id = projectFile.ProjectId, relative_path = projectFile.RelativePath, sha256 = projectFile.Object.Sha256, size_bytes = projectFile.Object.SizeBytes, lifecycle = LargeDataLifecycle.Staged.ToString().ToUpperInvariant() }, "project_id,relative_path", cancellationToken);

    public async Task CreateDataSetAsync(LargeDataSet dataSet, CancellationToken cancellationToken)
    {
        var client = clientFactory.CreateClient("Supabase");
        using var request = new HttpRequestMessage(HttpMethod.Post, "large_data_sets");
        request.Headers.Add("Prefer", "return=representation");
        request.Content = JsonContent.Create(new { project_id = dataSet.ProjectId, commit_sha = dataSet.CommitSha, status = dataSet.Status.ToString().ToUpperInvariant() });
        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var rows = await response.Content.ReadFromJsonAsync<List<DataSetRow>>(cancellationToken: cancellationToken);
        if (rows is null || rows.Count == 0) throw new InvalidOperationException("Supabase did not return a dataset id.");
        foreach (var item in dataSet.Items)
            await client.PostAsJsonAsync("large_data_set_items", new { data_set_id = rows[0].Id, sha256 = item.Object.Sha256, relative_path = item.RelativePath, size_bytes = item.Object.SizeBytes }, cancellationToken);
    }

    private sealed record DataSetRow(Guid Id);

    private sealed record UploadSessionRow(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("project_id")] string ProjectId,
        [property: JsonPropertyName("workstation_id")] string WorkstationId,
        [property: JsonPropertyName("sha256")] string Sha256,
        [property: JsonPropertyName("size_bytes")] long SizeBytes,
        [property: JsonPropertyName("chunk_size_bytes")] long ChunkSizeBytes,
        [property: JsonPropertyName("storage_scope")] string StorageScope,
        [property: JsonPropertyName("lifecycle")] string Lifecycle);

    private static LargeDataLifecycle ParseLifecycle(string value) =>
        Enum.TryParse<LargeDataLifecycle>(value, true, out var lifecycle) ? lifecycle : LargeDataLifecycle.Orphaned;

    private async Task SendAsync(string table, object value, string filter, CancellationToken cancellationToken)
    {
        var client = clientFactory.CreateClient("Supabase");
        using var request = new HttpRequestMessage(HttpMethod.Post, table + "?on_conflict=" + filter);
        request.Headers.Add("Prefer", "resolution=merge-duplicates");
        request.Content = JsonContent.Create(value);
        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private async Task PatchAsync(string path, object value, CancellationToken cancellationToken)
    {
        var client = clientFactory.CreateClient("Supabase");
        using var request = new HttpRequestMessage(HttpMethod.Patch, path);
        request.Content = JsonContent.Create(value);
        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}
