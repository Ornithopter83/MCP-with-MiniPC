using ProjectHub.Infrastructure;
using ProjectHub.Core;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddProjectHubInfrastructure(builder.Configuration);
var app = builder.Build();

app.MapGet("/api/status", () => Results.Ok(new
{
    server = "ProjectHub",
    status = "ok",
    database = "supabase",
    time = DateTimeOffset.UtcNow
}));

app.MapPost("/api/large-data/assertions", async (
    LargeDataAssertionRequest request,
    ILargeDataAssertionIssuer issuer,
    LargeDataOptions options,
    ILargeDataMetadataRepository metadata,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.ProjectId) || string.IsNullOrWhiteSpace(request.WorkstationId) ||
        string.IsNullOrWhiteSpace(request.ObjectHash) || request.ObjectHash.Length != 64 || request.SizeBytes < 0)
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["request"] = ["projectId, workstationId, a 64-character objectHash, and non-negative sizeBytes are required."] });
    var now = DateTimeOffset.UtcNow;
    var scope = new LargeDataAssertionScope(options.Issuer, options.Audience, request.WorkstationId, request.ProjectId, request.WorkstationId, request.Operation, request.UploadSessionId ?? Guid.NewGuid().ToString("N"), new LargeObjectIdentity(request.ObjectHash.ToLowerInvariant(), request.SizeBytes), request.StorageScope ?? "default", now, now.AddMinutes(5), Guid.NewGuid().ToString("N"), RelativePath: request.RelativePath);
    try
    {
        if (request.Operation == LargeDataOperation.Upload)
        {
            await metadata.UpsertObjectAsync(scope.Object, LargeDataLifecycle.Uploading, cancellationToken);
            await metadata.UpsertUploadSessionAsync(new LargeUploadSession(scope.UploadSessionId, scope.ProjectId, scope.WorkstationId, scope.Object, scope.StorageScope, 16 * 1024 * 1024, LargeDataLifecycle.Uploading), cancellationToken);
        }
        return Results.Ok(new { assertion = await issuer.IssueAsync(scope, cancellationToken), expiresAt = scope.ExpiresAt });
    }
    catch (InvalidOperationException exception) { return Results.Problem(exception.Message, statusCode: StatusCodes.Status503ServiceUnavailable); }
    catch (HttpRequestException exception) { return Results.Problem(exception.Message, statusCode: StatusCodes.Status502BadGateway); }
});

app.MapGet("/api/large-data/resumable-session", async (
    string projectId,
    string workstationId,
    string objectHash,
    long sizeBytes,
    ILargeDataMetadataRepository metadata,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(projectId) || string.IsNullOrWhiteSpace(workstationId) || objectHash.Length != 64 || sizeBytes < 0)
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["request"] = ["projectId, workstationId, objectHash, and non-negative sizeBytes are required."] });
    try
    {
        var session = await metadata.FindResumableSessionAsync(projectId, workstationId, new LargeObjectIdentity(objectHash.ToLowerInvariant(), sizeBytes), cancellationToken);
        return session is null ? Results.NotFound() : Results.Ok(session);
    }
    catch (HttpRequestException exception) { return Results.Problem(exception.Message, statusCode: StatusCodes.Status502BadGateway); }
});

app.MapGet("/api/large-data/sessions", async (string? projectId, string? workstationId, ILargeDataMetadataRepository metadata, CancellationToken cancellationToken) =>
{
    try { return Results.Ok(await metadata.ListUploadSessionsAsync(projectId, workstationId, cancellationToken)); }
    catch (HttpRequestException exception) { return Results.Problem(exception.Message, statusCode: StatusCodes.Status502BadGateway); }
});

app.MapPost("/api/large-data/sessions/{sessionId}/cancel", async (string sessionId, ILargeDataMetadataRepository metadata, CancellationToken cancellationToken) =>
{
    try { await metadata.UpdateUploadSessionLifecycleAsync(sessionId, LargeDataLifecycle.Cancelled, cancellationToken); return Results.Ok(); }
    catch (HttpRequestException exception) { return Results.Problem(exception.Message, statusCode: StatusCodes.Status502BadGateway); }
});

app.MapPost("/api/large-data/sessions/{sessionId}/abandon", async (string sessionId, ILargeDataMetadataRepository metadata, CancellationToken cancellationToken) =>
{
    try { await metadata.UpdateUploadSessionLifecycleAsync(sessionId, LargeDataLifecycle.Abandoned, cancellationToken); return Results.Ok(); }
    catch (HttpRequestException exception) { return Results.Problem(exception.Message, statusCode: StatusCodes.Status502BadGateway); }
});

app.MapPost("/api/large-data/resumable-session/{sessionId}/complete", async (
    string sessionId,
    ILargeDataMetadataRepository metadata,
    CancellationToken cancellationToken) =>
{
    if (!Guid.TryParse(sessionId, out _)) return Results.BadRequest(new { error = "session_id_must_be_guid" });
    try { await metadata.MarkUploadSessionCompletedAsync(sessionId, cancellationToken); return Results.Ok(new { sessionId, lifecycle = LargeDataLifecycle.Staged }); }
    catch (HttpRequestException exception) { return Results.Problem(exception.Message, statusCode: StatusCodes.Status502BadGateway); }
});

app.MapPost("/api/large-data/provision", async (
    LargeDataProvisionRequest request,
    LargeDataAssertionVerifier verifier,
    INasGatewayProvisioner provisioner,
    CancellationToken cancellationToken) =>
{
    try
    {
        var scope = verifier.Verify(request.Assertion, LargeDataOperation.Provision);
        return Results.Ok(await provisioner.ProvisionAsync(scope, cancellationToken));
    }
    catch (UnauthorizedAccessException) { return Results.Unauthorized(); }
    catch (InvalidOperationException exception) { return Results.ValidationProblem(new Dictionary<string, string[]> { ["assertion"] = [exception.Message] }); }
});

app.MapGet("/api/large-data/uploads/{sessionId}", async (string sessionId, string assertion, LargeDataAssertionVerifier verifier, IResumableUploadService uploads, CancellationToken cancellationToken) =>
{
    try { return Results.Ok(await uploads.GetStatusAsync(verifier.Verify(assertion, LargeDataOperation.Upload), cancellationToken)); }
    catch (UnauthorizedAccessException) { return Results.Unauthorized(); }
    catch (InvalidOperationException exception) { return Results.ValidationProblem(new Dictionary<string, string[]> { ["upload"] = [exception.Message] }); }
});

app.MapPut("/api/large-data/uploads/{sessionId}/chunks/{chunkIndex:int}", async (string sessionId, int chunkIndex, string assertion, HttpRequest httpRequest, LargeDataAssertionVerifier verifier, IResumableUploadService uploads, CancellationToken cancellationToken) =>
{
    try { return Results.Ok(await uploads.WriteChunkAsync(verifier.Verify(assertion, LargeDataOperation.Upload), chunkIndex, httpRequest.Body, cancellationToken)); }
    catch (UnauthorizedAccessException) { return Results.Unauthorized(); }
    catch (InvalidOperationException exception) { return Results.ValidationProblem(new Dictionary<string, string[]> { ["upload"] = [exception.Message] }); }
});

app.MapPost("/api/large-data/uploads/{sessionId}/finalize", async (string sessionId, string assertion, LargeDataAssertionVerifier verifier, IResumableUploadService uploads, CancellationToken cancellationToken) =>
{
    try { return Results.Ok(new { lifecycle = await uploads.FinalizeAsync(verifier.Verify(assertion, LargeDataOperation.Upload), cancellationToken) }); }
    catch (UnauthorizedAccessException) { return Results.Unauthorized(); }
    catch (InvalidOperationException exception) { return Results.ValidationProblem(new Dictionary<string, string[]> { ["upload"] = [exception.Message] }); }
});

app.MapPost("/api/large-data/reconciliation/{projectId}", async (string projectId, LargeDataReconciliationRequest request, ILargeDataMetadataRepository metadata, CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(projectId) || string.IsNullOrWhiteSpace(request.WorkstationId)) return Results.ValidationProblem(new Dictionary<string, string[]> { ["request"] = ["projectId and workstationId are required."] });
    try
    {
        foreach (var file in request.Files)
        {
            var normalized = file with { ProjectId = projectId, Lifecycle = LargeDataLifecycle.LocalOnly };
            await metadata.UpsertObjectAsync(normalized.Object, normalized.Lifecycle, cancellationToken);
            await metadata.UpsertProjectFileAsync(normalized, cancellationToken);
        }
        return Results.Ok(new { projectId, workstationId = request.WorkstationId, count = request.Files.Count });
    }
    catch (HttpRequestException exception) { return Results.Problem(exception.Message, statusCode: StatusCodes.Status502BadGateway); }
});

app.MapPost("/api/large-data/staged", async (ProjectLargeFile file, ILargeDataMetadataRepository metadata, CancellationToken cancellationToken) =>
{
    try { await metadata.MarkStagedAsync(file with { Lifecycle = LargeDataLifecycle.Staged }, cancellationToken); return Results.Ok(file with { Lifecycle = LargeDataLifecycle.Staged }); }
    catch (HttpRequestException exception) { return Results.Problem(exception.Message, statusCode: StatusCodes.Status502BadGateway); }
});

app.MapPost("/api/large-data/checkpoint", async (LargeDataCheckpointRequest request, ILargeDataMetadataRepository metadata, CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.ProjectId) || string.IsNullOrWhiteSpace(request.CommitSha)) return Results.ValidationProblem(new Dictionary<string, string[]> { ["request"] = ["projectId and commitSha are required."] });
    try
    {
        var dataSet = new LargeDataSet(request.ProjectId, request.CommitSha, LargeDataLifecycle.Checkpointed, request.Items.Select(item => item with { Lifecycle = LargeDataLifecycle.Checkpointed, CheckpointCommitSha = request.CommitSha }).ToArray());
        await metadata.CreateDataSetAsync(dataSet, cancellationToken);
        return Results.Ok(dataSet);
    }
    catch (HttpRequestException exception) { return Results.Problem(exception.Message, statusCode: StatusCodes.Status502BadGateway); }
});

app.MapPost("/api/agent/heartbeat", async (
    HeartbeatRequest request,
    IWorkstationRepository repository,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.WorkstationId) ||
        string.IsNullOrWhiteSpace(request.DisplayName))
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["workstationId"] = ["workstationId is required."],
            ["displayName"] = ["displayName is required."]
        });
    }

    try
    {
        var workstation = new Workstation(
            request.WorkstationId.Trim(),
            request.DisplayName.Trim(),
            string.IsNullOrWhiteSpace(request.Hostname) ? null : request.Hostname.Trim(),
            DateTimeOffset.UtcNow);

        var saved = await repository.UpsertAsync(workstation, cancellationToken);
        return Results.Ok(saved);
    }
    catch (InvalidOperationException exception)
    {
        return Results.Problem(
            detail: exception.Message,
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
    catch (HttpRequestException exception)
    {
        return Results.Problem(
            detail: exception.Message,
            statusCode: StatusCodes.Status502BadGateway);
    }
});

app.MapGet("/api/workstations", async (
    IWorkstationRepository repository,
    CancellationToken cancellationToken) =>
{
    try
    {
        return Results.Ok(await repository.ListAsync(cancellationToken));
    }
    catch (InvalidOperationException exception)
    {
        return Results.Problem(
            detail: exception.Message,
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
    catch (HttpRequestException exception)
    {
        return Results.Problem(
            detail: exception.Message,
            statusCode: StatusCodes.Status502BadGateway);
    }
});

app.MapPost("/api/projects/{projectId}/state", async (
    string projectId,
    ProjectStateRequest request,
    IProjectService service,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(projectId) || string.IsNullOrWhiteSpace(request.WorkstationId))
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["projectId"] = ["projectId and workstationId are required."]
        });
    }

    var state = new ProjectState(
        projectId.Trim(), request.WorkstationId.Trim(), request.Branch, request.HeadSha,
        request.Dirty, request.ChangedCount, request.UntrackedCount, request.DeletedCount,
        request.DiffFingerprint, request.LastFileActivity, DateTimeOffset.UtcNow);

    try
    {
        await service.UpdateProjectStateAsync(
            new Project(projectId.Trim(), request.DisplayName?.Trim() ?? projectId.Trim(), request.RepositoryUrl),
            state, cancellationToken);
        return Results.Ok(state);
    }
    catch (InvalidOperationException exception)
    {
        return Results.Problem(detail: exception.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
    catch (HttpRequestException exception)
    {
        return Results.Problem(detail: exception.Message, statusCode: StatusCodes.Status502BadGateway);
    }
});

app.MapGet("/api/projects/{projectId}/states", async (
    string projectId,
    IProjectService service,
    CancellationToken cancellationToken) =>
{
    try
    {
        return Results.Ok(await service.GetProjectStatesAsync(projectId, cancellationToken));
    }
    catch (InvalidOperationException exception)
    {
        return Results.Problem(detail: exception.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
    catch (HttpRequestException exception)
    {
        return Results.Problem(detail: exception.Message, statusCode: StatusCodes.Status502BadGateway);
    }
});

app.MapGet("/api/projects/{projectId}/states/{workstationId}", async (
    string projectId,
    string workstationId,
    IProjectService service,
    CancellationToken cancellationToken) =>
{
    try
    {
        var state = await service.GetProjectStateAsync(projectId, workstationId, cancellationToken);
        return state is null ? Results.NotFound() : Results.Ok(state);
    }
    catch (InvalidOperationException exception)
    {
        return Results.Problem(detail: exception.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
    catch (HttpRequestException exception)
    {
        return Results.Problem(detail: exception.Message, statusCode: StatusCodes.Status502BadGateway);
    }
});

app.Run();

public partial class Program;

public sealed record HeartbeatRequest(
    string WorkstationId,
    string DisplayName,
    string? Hostname);

public sealed record ProjectStateRequest(
    string WorkstationId,
    string? DisplayName,
    string? RepositoryUrl,
    string? Branch,
    string? HeadSha,
    bool Dirty,
    int ChangedCount,
    int UntrackedCount,
    int DeletedCount,
    string? DiffFingerprint,
    DateTimeOffset? LastFileActivity);

public sealed record LargeDataAssertionRequest(
    string ProjectId,
    string WorkstationId,
    string ObjectHash,
    long SizeBytes,
    LargeDataOperation Operation,
    string? UploadSessionId,
    string? StorageScope,
    string? RelativePath);

public sealed record LargeDataProvisionRequest(string Assertion);

public sealed record LargeDataReconciliationRequest(string WorkstationId, IReadOnlyList<ProjectLargeFile> Files);

public sealed record LargeDataCheckpointRequest(string ProjectId, string CommitSha, IReadOnlyList<ProjectLargeFile> Items);
