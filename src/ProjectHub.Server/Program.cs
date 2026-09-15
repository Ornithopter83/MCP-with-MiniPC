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
});

app.Run();

public partial class Program;

public sealed record HeartbeatRequest(
    string WorkstationId,
    string DisplayName,
    string? Hostname);
