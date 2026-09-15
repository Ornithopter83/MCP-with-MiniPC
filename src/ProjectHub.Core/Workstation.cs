using System.Text.Json.Serialization;

namespace ProjectHub.Core;

public sealed record Workstation(
    [property: JsonPropertyName("workstation_id")] string WorkstationId,
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("hostname")] string? Hostname,
    [property: JsonPropertyName("last_seen")] DateTimeOffset LastSeen,
    [property: JsonPropertyName("created_at")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    DateTimeOffset? CreatedAt = null,
    [property: JsonPropertyName("updated_at")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    DateTimeOffset? UpdatedAt = null);
