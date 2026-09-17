namespace ProjectHub.Core;

public enum LargeDataOperation
{
    Provision,
    Upload,
    Download
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
    MigrationRequired
}

public sealed record LargeObjectIdentity(string Sha256, long SizeBytes);

public sealed record ProjectLargeFile(string ProjectId, string RelativePath, LargeObjectIdentity Object, LargeDataLifecycle Lifecycle, string? CheckpointCommitSha = null);

public sealed record LargeDataSet(string ProjectId, string CommitSha, LargeDataLifecycle Status, IReadOnlyList<ProjectLargeFile> Items);

public interface ILargeDataMetadataRepository
{
    Task UpsertObjectAsync(LargeObjectIdentity objectIdentity, LargeDataLifecycle lifecycle, CancellationToken cancellationToken);
    Task UpsertProjectFileAsync(ProjectLargeFile projectFile, CancellationToken cancellationToken);
    Task MarkStagedAsync(ProjectLargeFile projectFile, CancellationToken cancellationToken);
    Task CreateDataSetAsync(LargeDataSet dataSet, CancellationToken cancellationToken);
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
    string? LocationId = null);

public sealed record LargeUploadSession(
    string SessionId,
    string ProjectId,
    string WorkstationId,
    LargeObjectIdentity Object,
    string StorageScope,
    long ChunkSizeBytes,
    LargeDataLifecycle Lifecycle);

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

public sealed record UploadChunkResult(string SessionId, int ChunkIndex, long BytesReceived);

public sealed record UploadStatus(string SessionId, long BytesReceived, IReadOnlyList<int> CompletedChunks, LargeDataLifecycle Lifecycle);

public interface IResumableUploadService
{
    Task<UploadChunkResult> WriteChunkAsync(LargeDataAssertionScope scope, int chunkIndex, Stream content, CancellationToken cancellationToken);
    Task<UploadStatus> GetStatusAsync(LargeDataAssertionScope scope, CancellationToken cancellationToken);
    Task<LargeDataLifecycle> FinalizeAsync(LargeDataAssertionScope scope, CancellationToken cancellationToken);
}
