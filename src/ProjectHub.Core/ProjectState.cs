using System.Text.Json.Serialization;

namespace ProjectHub.Core;

public sealed record Project(
    [property: JsonPropertyName("project_id")] string ProjectId,
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("repository_url")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? RepositoryUrl,
    [property: JsonPropertyName("created_at")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    DateTimeOffset? CreatedAt = null,
    [property: JsonPropertyName("updated_at")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    DateTimeOffset? UpdatedAt = null);

public sealed record ProjectState(
    [property: JsonPropertyName("project_id")] string ProjectId,
    [property: JsonPropertyName("workstation_id")] string WorkstationId,
    [property: JsonPropertyName("branch")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Branch,
    [property: JsonPropertyName("head_sha")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? HeadSha,
    [property: JsonPropertyName("dirty")] bool Dirty,
    [property: JsonPropertyName("changed_count")] int ChangedCount,
    [property: JsonPropertyName("untracked_count")] int UntrackedCount,
    [property: JsonPropertyName("deleted_count")] int DeletedCount,
    [property: JsonPropertyName("diff_fingerprint")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? DiffFingerprint,
    [property: JsonPropertyName("last_file_activity")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    DateTimeOffset? LastFileActivity,
    [property: JsonPropertyName("last_seen")] DateTimeOffset LastSeen);

public sealed record ProjectEvent(
    string ProjectId,
    string? WorkstationId,
    string EventType,
    string? Message,
    DateTimeOffset CreatedAt);
