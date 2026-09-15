var builder = WebApplication.CreateBuilder(args);
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
