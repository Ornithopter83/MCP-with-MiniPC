namespace ProjectHub.Core;

public sealed record ProjectState(
    string ProjectId,
    string WorkstationId,
    string? Branch,
    string? HeadSha,
    bool Dirty,
    int ChangedCount,
    int UntrackedCount,
    int DeletedCount,
    DateTimeOffset LastSeen);

public sealed record ProjectEvent(
    string ProjectId,
    string? WorkstationId,
    string EventType,
    string? Message,
    DateTimeOffset CreatedAt);
