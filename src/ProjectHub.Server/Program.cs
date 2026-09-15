using ProjectHub.Infrastructure;

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

app.Run();

public partial class Program;
