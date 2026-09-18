using System.Text.Json.Serialization;

namespace ProjectHub.Core;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum LargeDataOperation
{
    Provision,
    Upload,
    Download,
    Cleanup,
    Delete
}

public enum LargeDataLifecycle
{
    LocalOnly,
    Hashing,
    Uploading,
    Staged,
    Checkpointed,
    Orphaned,
    Missing,
    Removed,
    MigrationRequired,
    Completed,
    Cancelled,
    Abandoned
}

public sealed record LargeObjectIdentity(string Sha256, long SizeBytes);

public sealed record ProjectLargeFile(string ProjectId, string RelativePath, LargeObjectIdentity Object, LargeDataLifecycle Lifecycle, string? CheckpointCommitSha = null);

public sealed record LargeDataSet(string ProjectId, string CommitSha, LargeDataLifecycle Status, IReadOnlyList<ProjectLargeFile> Items);

public interface ILargeDataMetadataRepository
{
    Task<LargeUploadSession?> FindResumableSessionAsync(string projectId, string workstationId, LargeObjectIdentity objectIdentity, CancellationToken cancellationToken);
    Task UpsertUploadSessionAsync(LargeUploadSession session, CancellationToken cancellationToken);
    Task MarkUploadSessionCompletedAsync(string sessionId, CancellationToken cancellationToken);
    Task<IReadOnlyList<LargeUploadSession>> ListUploadSessionsAsync(string? projectId, string? workstationId, CancellationToken cancellationToken);
    Task UpdateUploadSessionLifecycleAsync(string sessionId, LargeDataLifecycle lifecycle, CancellationToken cancellationToken);
    Task UpsertObjectAsync(LargeObjectIdentity objectIdentity, LargeDataLifecycle lifecycle, CancellationToken cancellationToken);
    Task UpsertProjectFileAsync(ProjectLargeFile projectFile, CancellationToken cancellationToken);
    Task MarkStagedAsync(ProjectLargeFile projectFile, CancellationToken cancellationToken);
    Task CreateDataSetAsync(LargeDataSet dataSet, CancellationToken cancellationToken);
    Task<IReadOnlyList<LargeDataSet>> ListDataSetsAsync(string projectId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ProjectLargeFile>> ListProjectFilesAsync(string projectId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ProjectLargeFile>> ListActiveProjectFilesByObjectAsync(LargeObjectIdentity objectIdentity, CancellationToken cancellationToken);
    Task MarkProjectFileRemovedAsync(ProjectLargeFile projectFile, CancellationToken cancellationToken);
}

public sealed record LargeDataAssertionScope(
    string Issuer,
    string Audience,
    string Subject,
    string ProjectId,
    string WorkstationId,
    LargeDataOperation Operation,
    string UploadSessionId,
    LargeObjectIdentity Object,
    string StorageScope,
    DateTimeOffset IssuedAt,
    DateTimeOffset ExpiresAt,
    string Jti,
    string? KeyId = null,
    string? GatewayId = null,
    string? LocationId = null,
    string? RelativePath = null);

public sealed record LargeUploadSession(
    string SessionId,
    string ProjectId,
    string WorkstationId,
    LargeObjectIdentity Object,
    string StorageScope,
    long ChunkSizeBytes,
    LargeDataLifecycle Lifecycle,
    DateTimeOffset? LastActivityAt = null);

public sealed record ProvisionResult(
    string SessionId,
    string RelativePath,
    bool AlreadyPresent,
    LargeDataLifecycle Lifecycle);

public interface ILargeDataAssertionIssuer
{
    Task<string> IssueAsync(LargeDataAssertionScope scope, CancellationToken cancellationToken);
}

public interface IContentAddressedStorage
{
    Task<bool> ExistsAsync(LargeObjectIdentity objectIdentity, CancellationToken cancellationToken);

    Task<Stream> OpenReadAsync(LargeObjectIdentity objectIdentity, CancellationToken cancellationToken);
}

public interface INasGatewayProvisioner
{
    Task<ProvisionResult> ProvisionAsync(
        LargeDataAssertionScope scope,
        CancellationToken cancellationToken);
}

public sealed record NasDeleteResult(
    [property: JsonPropertyName("alias_deleted")] bool AliasDeleted,
    [property: JsonPropertyName("canonical_deleted")] bool CanonicalDeleted,
    [property: JsonPropertyName("already_deleted")] bool AlreadyDeleted,
    [property: JsonPropertyName("named_path")] string? NamedPath,
    [property: JsonPropertyName("object_path")] string? ObjectPath);

public interface INasGatewayObjectDeleter
{
    Task<NasDeleteResult> DeleteAsync(string assertion, LargeDataAssertionScope scope, bool deleteCanonical, CancellationToken cancellationToken);
}

public sealed record UploadChunkResult(string SessionId, int ChunkIndex, long BytesReceived);

public sealed record UploadStatus(string SessionId, long BytesReceived, IReadOnlyList<int> CompletedChunks, LargeDataLifecycle Lifecycle);

public interface IResumableUploadService
{
    Task<UploadChunkResult> WriteChunkAsync(LargeDataAssertionScope scope, int chunkIndex, Stream content, CancellationToken cancellationToken);
    Task<UploadStatus> GetStatusAsync(LargeDataAssertionScope scope, CancellationToken cancellationToken);
    Task<LargeDataLifecycle> FinalizeAsync(LargeDataAssertionScope scope, CancellationToken cancellationToken);
}
