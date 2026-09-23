using System.IO;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ProjectHub.Worker;

public sealed record JudgeSettings(
    [property: JsonPropertyName("enabled")] bool Enabled = false,
    [property: JsonPropertyName("provider")] string Provider = "jev",
    [property: JsonPropertyName("manualExecutableOrEndpoint")] string? ManualExecutableOrEndpoint = null,
    [property: JsonPropertyName("timeoutSeconds")] int TimeoutSeconds = 120);

public sealed record WorkerAiRoleSettings(
    [property: JsonPropertyName("provider")] string Provider = "openai",
    [property: JsonPropertyName("model")] string Model = "",
    [property: JsonPropertyName("reasoning")] string Reasoning = "medium");

public sealed record WorkerTargetSettings(
    [property: JsonPropertyName("manualRepositoryUrl")] string? ManualRepositoryUrl,
    [property: JsonPropertyName("manualServerBaseUrl")] string? ManualServerBaseUrl,
    [property: JsonPropertyName("repositoryUrlSource")] string? RepositoryUrlSource,
    [property: JsonPropertyName("serverBaseUrlSource")] string? ServerBaseUrlSource,
    [property: JsonPropertyName("manualWorkingDirectory")] string? ManualWorkingDirectory = null,
    [property: JsonPropertyName("judge")] JudgeSettings? Judge = null,
    [property: JsonPropertyName("executionMode")] string ExecutionMode = "CLI_TO_CLI",
    [property: JsonPropertyName("coordinator")] WorkerAiRoleSettings? Coordinator = null,
    [property: JsonPropertyName("implementer")] WorkerAiRoleSettings? Implementer = null)
{
    public JudgeSettings EffectiveJudge => Judge ?? new JudgeSettings();
    public WorkerAiRoleSettings EffectiveCoordinator => Coordinator ?? new WorkerAiRoleSettings(Model: "gpt-6-sol", Reasoning: "high");
    public WorkerAiRoleSettings EffectiveImplementer => Implementer ?? new WorkerAiRoleSettings(Model: "gpt-6-luna", Reasoning: "medium");
    public bool IsCoordinatorFirst => string.Equals(ExecutionMode, "CLI_TO_CLI", StringComparison.OrdinalIgnoreCase);
}
public sealed record GitTargetSnapshot(
    string ProjectPath,
    string? RepositoryUrl,
    string? Branch,
    string? HeadSha,
    string Source,
    bool IsRepository);

public static class WorkerTargetConfiguration
{
    public const string DefaultServerBaseUrl = "https://projecthub.ornithopter.bid";

    public static string SettingsPath => Path.Combine(WorkerPaths.Config, "target-settings.json");

    public static WorkerTargetSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
                return new(null, null, null, null);

            return JsonSerializer.Deserialize<WorkerTargetSettings>(File.ReadAllText(SettingsPath))
                ?? new(null, null, null, null);
        }
        catch
        {
            return new(null, null, null, null);
        }
    }

    public static void Save(WorkerTargetSettings settings)
    {
        Directory.CreateDirectory(WorkerPaths.Config);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static (string Url, string Source) ResolveServer(WorkerTargetSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.ManualServerBaseUrl))
            return (settings.ManualServerBaseUrl.Trim(), "MANUAL");

        var environment = Environment.GetEnvironmentVariable("PROJECTHUB_AGENT_SERVER_BASE_URL");
        if (!string.IsNullOrWhiteSpace(environment))
            return (environment.Trim(), "AUTO_ENV");


        return (DefaultServerBaseUrl, "DEFAULT");
    }

    public static GitTargetSnapshot ResolveGit(string projectPath, WorkerTargetSettings settings)
    {
        if (string.IsNullOrWhiteSpace(projectPath) || !Directory.Exists(projectPath))
            return new(projectPath ?? string.Empty, null, null, null, "UNCONFIGURED", false);

        var path = Path.GetFullPath(projectPath);
        var root = FindRepositoryRoot(path);
        if (root is null)
            return new(path, null, null, null, "UNCONFIGURED", false);

        var remote = RunGit(root, "remote", "get-url", "origin");
        var branch = RunGit(root, "rev-parse", "--abbrev-ref", "HEAD");
        var head = RunGit(root, "rev-parse", "HEAD");
        var source = !string.IsNullOrWhiteSpace(remote) ? "AUTO_GIT_REMOTE" : "UNCONFIGURED";
        return new(root, SanitizeRemote(remote), branch, head, source, true);
    }

    private static string? FindRepositoryRoot(string path)
    {
        var directory = new DirectoryInfo(path);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, ".git")) || File.Exists(Path.Combine(directory.FullName, ".git")))
                return directory.FullName;
            directory = directory.Parent;
        }

        return null;
    }

    private static string? RunGit(string workingDirectory, params string[] arguments)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "git",
                    WorkingDirectory = workingDirectory,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };
            foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);
            if (!process.Start()) return null;
            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(5000);
            return process.ExitCode == 0 ? output : null;
        }
        catch
        {
            return null;
        }
    }

    private static string? SanitizeRemote(string? remote)
    {
        if (string.IsNullOrWhiteSpace(remote)) return null;
        if (!Uri.TryCreate(remote, UriKind.Absolute, out var uri) || string.IsNullOrWhiteSpace(uri.Host))
            return remote.Contains('@') ? remote[(remote.IndexOf('@') + 1)..] : remote;

        var builder = new UriBuilder(uri) { UserName = string.Empty, Password = string.Empty };
        return builder.Uri.ToString().TrimEnd('/');
    }
}
