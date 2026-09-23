using System.IO;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ProjectHub.Worker;

public sealed record JudgeSettings(
    [property: JsonPropertyName("enabled")] bool Enabled = false,
    [property: JsonPropertyName("provider")] string Provider = "jev",
    [property: JsonPropertyName("manualExecutableOrEndpoint")] string? ManualExecutableOrEndpoint = null,
    [property: JsonPropertyName("timeoutSeconds")] int TimeoutSeconds = 120);

public sealed record JudgeEndpointValidation(
    [property: JsonPropertyName("configurationFingerprint")] string ConfigurationFingerprint,
    [property: JsonPropertyName("succeeded")] bool Succeeded,
    [property: JsonPropertyName("outcome")] string Outcome,
    [property: JsonPropertyName("testedAtUtc")] DateTimeOffset TestedAtUtc);

public sealed record WorkerAiRoleSettings(
    [property: JsonPropertyName("provider")] string Provider = "openai",
    [property: JsonPropertyName("model")] string Model = "",
    [property: JsonPropertyName("reasoning")] string Reasoning = "medium",
    [property: JsonPropertyName("transport")] string Transport = "web",
    [property: JsonPropertyName("threadSessionId")] string? ThreadSessionId = null,
    [property: JsonPropertyName("threadProjectPath")] string? ThreadProjectPath = null);

public sealed record WorkerTargetSettings(
    [property: JsonPropertyName("manualRepositoryUrl")] string? ManualRepositoryUrl,
    [property: JsonPropertyName("manualServerBaseUrl")] string? ManualServerBaseUrl,
    [property: JsonPropertyName("repositoryUrlSource")] string? RepositoryUrlSource,
    [property: JsonPropertyName("serverBaseUrlSource")] string? ServerBaseUrlSource,
    [property: JsonPropertyName("manualWorkingDirectory")] string? ManualWorkingDirectory = null,
    [property: JsonPropertyName("judge")] JudgeSettings? Judge = null,
    [property: JsonPropertyName("executionMode")] string ExecutionMode = "CLI_TO_CLI",
    [property: JsonPropertyName("coordinator")] WorkerAiRoleSettings? Coordinator = null,
    [property: JsonPropertyName("implementer")] WorkerAiRoleSettings? Implementer = null,
    [property: JsonPropertyName("highLevelEnabled")] bool HighLevelEnabled = false,
    [property: JsonPropertyName("highLevel")] WorkerAiRoleSettings? HighLevel = null,
    [property: JsonPropertyName("judgeEndpointValidation")] JudgeEndpointValidation? JudgeEndpointValidation = null)
{
    public JudgeSettings EffectiveJudge => Judge ?? new JudgeSettings();
    public WorkerAiRoleSettings EffectiveCoordinator => Coordinator ?? new WorkerAiRoleSettings(Model: "gpt-6-sol", Reasoning: "high");
    public WorkerAiRoleSettings EffectiveImplementer => Implementer ?? new WorkerAiRoleSettings(Model: "gpt-6-luna", Reasoning: "medium", Transport: "codex_cli");
    public WorkerAiRoleSettings EffectiveHighLevel => HighLevel ?? new WorkerAiRoleSettings(Model: "gpt-6-astra", Reasoning: "high", Transport: "codex_cli");
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

    public static void SaveJudgeEndpointValidation(JudgeEndpointValidation validation)
    {
        var settings = Load();
        Save(settings with { JudgeEndpointValidation = validation });
    }

    public static string GetJudgeEndpointFingerprint(JudgeSettings settings)
    {
        var canonical = string.Join("\n",
            settings.Provider.Trim().ToLowerInvariant(),
            settings.ManualExecutableOrEndpoint?.Trim() ?? string.Empty,
            Math.Clamp(settings.TimeoutSeconds, 10, 600).ToString(System.Globalization.CultureInfo.InvariantCulture));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    public static bool IsJudgeEndpointValidationCurrent(JudgeEndpointValidation? validation, JudgeSettings settings)
        => validation is not null && string.Equals(validation.ConfigurationFingerprint, GetJudgeEndpointFingerprint(settings), StringComparison.Ordinal);

    public static string? GetJudgeApplyWarning(JudgeSettings settings, JudgeEndpointValidation? validation)
    {
        if (!settings.Enabled) return null;
        if (!IsJudgeEndpointValidationCurrent(validation, settings))
            return "설정 테스트가 수행되지 않았습니다. 현재 설정으로 JSON 설정 테스트를 다시 실행해 주세요. 계속 적용합니다.";
        if (!validation!.Succeeded)
            return "설정 테스트가 실패했습니다. 환경을 확인한 뒤 직접 재검증해 주세요. 설정은 계속 적용합니다.";
        return null;
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
