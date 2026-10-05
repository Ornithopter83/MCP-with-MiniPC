using System.IO;
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
    [property: JsonPropertyName("transport")] string Transport = "codex_cli",
    [property: JsonPropertyName("threadSessionId")] string? ThreadSessionId = null,
    [property: JsonPropertyName("threadProjectPath")] string? ThreadProjectPath = null)
{
    [JsonIgnore]
    public AiServiceProvider? ProviderKind =>
        AiProviderCatalog.TryParse(Provider, out var provider) ? provider : null;
}

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
    [property: JsonPropertyName("judgeEndpointValidation")] JudgeEndpointValidation? JudgeEndpointValidation = null,
    [property: JsonPropertyName("maxConcurrentWork")] int MaxConcurrentWork = 1,
    [property: JsonPropertyName("highLevel")] WorkerAiRoleSettings? HighLevel = null,
    [property: JsonPropertyName("manager")] WorkerAiRoleSettings? Manager = null,
    [property: JsonPropertyName("qa")] WorkerAiRoleSettings? Qa = null)
{
    public JudgeSettings EffectiveJudge => new(false);
    public WorkerAiRoleSettings EffectiveCoordinator => Coordinator ?? new WorkerAiRoleSettings(Model: "gpt-6-sol", Reasoning: "high");
    public WorkerAiRoleSettings EffectiveImplementer => Implementer ?? new WorkerAiRoleSettings(Model: "gpt-6-luna", Reasoning: "medium", Transport: "codex_cli");
    public WorkerAiRoleSettings EffectiveManager => Manager ?? new WorkerAiRoleSettings(Model: "gpt-6-luna", Reasoning: "medium", Transport: "codex_cli");
    public WorkerAiRoleSettings EffectiveQa => Qa ?? new WorkerAiRoleSettings(Model: "gpt-6-luna", Reasoning: "medium", Transport: "codex_cli");
    public WorkerAiRoleSettings EffectiveHighLevel => HighLevel ?? new WorkerAiRoleSettings(Model: "gpt-6-astra", Reasoning: "high", Transport: "codex_cli");
    public int EffectiveMaxConcurrentWork =>
        MaxConcurrentWork is >= WorkerTargetConfiguration.MinimumConcurrentWork and <= WorkerTargetConfiguration.MaximumConcurrentWork
            ? MaxConcurrentWork
            : 1;
    public bool IsCoordinatorFirst => true;
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
    public const int MinimumConcurrentWork = 1;
    public const int MaximumConcurrentWork = 8;
    public const string DefaultServerBaseUrl = "https://projecthub.ornithopter.bid";

    public static string SettingsPath => Path.Combine(WorkerPaths.Config, "target-settings.json");

    public static WorkerTargetSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
                return new(null, null, null, null);

            var settings = JsonSerializer.Deserialize<WorkerTargetSettings>(File.ReadAllText(SettingsPath))
                ?? new(null, null, null, null);
            return NormalizeForRuntime(settings);
        }
        catch
        {
            return new(null, null, null, null);
        }
    }

    public static WorkerTargetSettings NormalizeForRuntime(WorkerTargetSettings settings)
    {
        WorkerAiRoleSettings? NormalizeRole(WorkerAiRoleSettings? role)
        {
            if (role is null) return null;
            var descriptor = AiProviderCatalog.Find(role.Provider);
            if (descriptor is null) return role;
            var transport = role.Transport;
            if (string.IsNullOrWhiteSpace(transport) ||
                (descriptor.Provider == AiServiceProvider.Muse &&
                 string.Equals(
                     transport,
                     "muse_cli",
                     StringComparison.OrdinalIgnoreCase)))
            {
                transport = descriptor.DefaultTransport;
            }

            if (descriptor.Provider == AiServiceProvider.Muse &&
                descriptor.Models.Count > 0)
            {
                var model = descriptor.FindModel(role.Model) ?? descriptor.Models[0];
                var reasoning = model.SupportsReasoning(role.Reasoning)
                    ? role.Reasoning
                    : model.DefaultReasoning;
                return role with
                {
                    Model = model.Id,
                    Reasoning = reasoning,
                    Transport = transport,
                    ThreadSessionId = null,
                    ThreadProjectPath = null
                };
            }

            return role with { Transport = transport };
        }

        return settings with
        {
            ExecutionMode = "CLI_TO_CLI",
            Judge = null,
            JudgeEndpointValidation = null,
            Coordinator = NormalizeRole(settings.Coordinator),
            Implementer = NormalizeRole(settings.Implementer),
            Manager = NormalizeRole(settings.Manager),
            Qa = NormalizeRole(settings.Qa),
            HighLevel = NormalizeRole(settings.HighLevel)
        };
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

    public static GitTargetSnapshot ResolveGit(
        string projectPath,
        WorkerTargetSettings settings,
        bool requireExactRoot = true)
    {
        if (string.IsNullOrWhiteSpace(projectPath) || !Directory.Exists(projectPath))
            return new(projectPath ?? string.Empty, null, null, null, "UNCONFIGURED", false);

        var path = Path.GetFullPath(projectPath);
        var root = HasGitMetadata(path) ? path : null;
        if (root is null)
            return new(path, null, null, null, "UNCONFIGURED", false);

        var remote = ResolvePreferredRemote(root);
        var branch = RunGit(root, "rev-parse", "--abbrev-ref", "HEAD");
        var head = RunGit(root, "rev-parse", "HEAD");
        var source = !string.IsNullOrWhiteSpace(remote) ? "AUTO_GIT_REMOTE" : "UNCONFIGURED";
        return new(root, SanitizeRemote(remote), branch, head, source, true);
    }

    private static bool HasGitMetadata(string path)
        => Directory.Exists(Path.Combine(path, ".git")) ||
           File.Exists(Path.Combine(path, ".git"));

    private static string? ResolvePreferredRemote(string workingDirectory)
    {
        var verbose = RunGit(workingDirectory, "remote", "-v");
        var origin = ParseOriginRemote(verbose);
        if (!string.IsNullOrWhiteSpace(origin))
            return origin;

        return ReadOriginRemoteFromGitConfig(workingDirectory);
    }

    private static string? ParseOriginRemote(string? verbose)
    {
        if (string.IsNullOrWhiteSpace(verbose))
            return null;

        foreach (var rawLine in verbose.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = rawLine.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 ||
                !string.Equals(parts[0], "origin", StringComparison.OrdinalIgnoreCase))
                continue;

            var isFetch = parts.Length < 3 ||
                          string.Equals(parts[^1], "(fetch)", StringComparison.OrdinalIgnoreCase);
            if (isFetch)
                return parts[1];
        }

        return null;
    }

    private static string? ReadOriginRemoteFromGitConfig(string workingDirectory)
    {
        try
        {
            var gitDirectory = Path.Combine(workingDirectory, ".git");
            if (!Directory.Exists(gitDirectory))
                return null;

            var configPath = Path.Combine(gitDirectory, "config");
            if (!File.Exists(configPath))
                return null;

            var inOrigin = false;
            foreach (var rawLine in File.ReadLines(configPath))
            {
                var line = rawLine.Trim();
                if (line.StartsWith("[remote \"", StringComparison.OrdinalIgnoreCase) &&
                    line.EndsWith("\"]", StringComparison.Ordinal))
                {
                    inOrigin = string.Equals(
                        line[9..^2],
                        "origin",
                        StringComparison.OrdinalIgnoreCase);
                    continue;
                }

                if (!inOrigin)
                    continue;

                var equals = line.IndexOf('=');
                if (equals <= 0)
                    continue;

                var key = line[..equals].Trim();
                if (!string.Equals(key, "url", StringComparison.OrdinalIgnoreCase))
                    continue;

                var url = line[(equals + 1)..].Trim();
                return string.IsNullOrWhiteSpace(url) ? null : url;
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private static string? RunGit(string workingDirectory, params string[] arguments)
    {
        try
        {
            var result = new ProcessGitCommandRunner()
                .RunAsync(
                    workingDirectory,
                    arguments,
                    TimeSpan.FromSeconds(5),
                    CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            return result.ExitCode == 0 ? result.StandardOutput.Trim() : null;
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
