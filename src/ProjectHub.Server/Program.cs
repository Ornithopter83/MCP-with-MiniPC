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
