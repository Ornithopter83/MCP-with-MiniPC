using System.Text.Json;

namespace ProjectHub.Agent;

public sealed record AgentOptions(
    Uri ServerBaseUrl,
    string WorkstationId,
    string DisplayName,
    TimeSpan HeartbeatInterval,
    IReadOnlyList<ProjectRegistration> RegisteredProjects,
    long LargeFileThresholdBytes)
{
    public static AgentOptions Load(string[] args)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        var configPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        if (File.Exists(configPath))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(configPath));
            if (document.RootElement.TryGetProperty("Agent", out var agent))
            {
                ReadString(agent, "ServerBaseUrl", values);
                ReadString(agent, "WorkstationId", values);
                ReadString(agent, "DisplayName", values);
                ReadString(agent, "HeartbeatIntervalSeconds", values);
            }
        }

        SetFromEnvironment(values, "ServerBaseUrl", "PROJECTHUB_AGENT_SERVER_BASE_URL");
        SetFromEnvironment(values, "WorkstationId", "PROJECTHUB_AGENT_WORKSTATION_ID");
        SetFromEnvironment(values, "DisplayName", "PROJECTHUB_AGENT_DISPLAY_NAME");
        SetFromEnvironment(values, "HeartbeatIntervalSeconds", "PROJECTHUB_AGENT_HEARTBEAT_INTERVAL_SECONDS");
        SetFromEnvironment(values, "LargeFileThresholdBytes", "PROJECTHUB_AGENT_LARGE_FILE_THRESHOLD_BYTES");

        foreach (var argument in args)
        {
            if (!argument.StartsWith("--", StringComparison.Ordinal) || !argument.Contains('=')) continue;
            var separator = argument.IndexOf('=');
            values[argument[2..separator]] = argument[(separator + 1)..];
        }

        if (!Uri.TryCreate(values["ServerBaseUrl"], UriKind.Absolute, out var serverBaseUrl) ||
            serverBaseUrl.Scheme is not ("http" or "https"))
        {
            throw new InvalidOperationException("Agent ServerBaseUrl must be an absolute HTTP(S) URL.");
        }

        var workstationId = Required(values["WorkstationId"], "WorkstationId");
        var displayName = Required(values["DisplayName"], "DisplayName");
        if (!int.TryParse(values["HeartbeatIntervalSeconds"], out var intervalSeconds) || intervalSeconds < 1)
        {
            throw new InvalidOperationException("Agent HeartbeatIntervalSeconds must be at least 1.");
        }

        var threshold = long.TryParse(values["LargeFileThresholdBytes"], out var configuredThreshold) && configuredThreshold > 0 ? configuredThreshold : 1L << 30;

        return new AgentOptions(
            new Uri(serverBaseUrl.ToString().TrimEnd('/') + "/"),
            workstationId,
            displayName,
            TimeSpan.FromSeconds(intervalSeconds),
            ReadProjects(configPath), threshold);
    }

    private static void ReadString(JsonElement agent, string name, IDictionary<string, string?> values)
    {
        if (agent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
        {
            values[name] = value.GetString();
        }
    }

    private static void SetFromEnvironment(
        IDictionary<string, string?> values,
        string name,
        string environmentName)
    {
        var value = Environment.GetEnvironmentVariable(environmentName);
        if (value is not null) values[name] = value;
    }

    private static string Required(string? value, string name) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"Agent {name} is required.")
            : value.Trim();

    private static IReadOnlyList<ProjectRegistration> ReadProjects(string configPath)
    {
        if (!File.Exists(configPath)) return [];
        using var document = JsonDocument.Parse(File.ReadAllText(configPath));
        if (!document.RootElement.TryGetProperty("Agent", out var agent) ||
            !agent.TryGetProperty("RegisteredProjects", out var projects) ||
            projects.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return projects.EnumerateArray()
            .Select(project => new ProjectRegistration(
                project.GetProperty("ProjectId").GetString() ?? string.Empty,
                project.GetProperty("DisplayName").GetString() ?? string.Empty,
                project.GetProperty("LocalPath").GetString() ?? string.Empty,
                project.TryGetProperty("RepositoryUrl", out var url) ? url.GetString() : null))
            .Where(project => !string.IsNullOrWhiteSpace(project.ProjectId) && !string.IsNullOrWhiteSpace(project.LocalPath))
            .ToArray();
    }
}

public sealed record ProjectRegistration(
    string ProjectId,
    string DisplayName,
    string LocalPath,
    string? RepositoryUrl);
