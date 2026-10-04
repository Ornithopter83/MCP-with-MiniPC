using System.IO;

namespace ProjectHub.Worker;

public sealed record AiRoleRunRequest(
    string Prompt,
    WorkerAiRoleSettings Role,
    string WorkingDirectory,
    string? SessionId,
    CodexSandboxMode Sandbox,
    CancellationToken CancellationToken,
    string? OutputSchemaJson = null,
    Action<string>? Progress = null,
    Action<string>? SessionStarted = null,
    IReadOnlyList<string>? AdditionalWritableDirectories = null,
    bool IgnoreProjectInstructions = false,
    IReadOnlyList<AiInputAttachment>? InputAttachments = null,
    IReadOnlyDictionary<string, string>? EnvironmentVariables = null,
    bool DisableComputerUse = false,
    bool IncludeAppBaseWritable = true,
    IReadOnlyList<string>? CodexConfigOverrides = null,
    bool BypassHookTrust = false,
    bool BuildExecutionAllowed = false);

public sealed record AiRoleRunResult(
    string Provider,
    string Model,
    string Reasoning,
    string? SessionId,
    int ExitCode,
    string StandardOutput,
    string StandardError,
    string FinalMessage,
    IReadOnlyList<CodexCliFile> Files,
    CodexUsage Usage,
    IReadOnlyList<CodexCommandExecution> CommandExecutions);

public interface IAiRoleRunner
{
    AiServiceProvider Provider { get; }
    bool SupportsSessions { get; }
    string? GetPreflightError(WorkerAiRoleSettings role, string workingDirectory, bool openAiAuthenticated);
    Task<AiRoleRunResult> RunAsync(AiRoleRunRequest request);
}

public sealed class OpenAiCodexRoleRunner(CodexCliRunner codexRunner) : IAiRoleRunner
{
    public AiServiceProvider Provider => AiServiceProvider.OpenAI;
    public bool SupportsSessions => true;

    public string? GetPreflightError(WorkerAiRoleSettings role, string workingDirectory, bool openAiAuthenticated)
    {
        if (!Directory.Exists(workingDirectory)) return "Working Folder가 없거나 접근할 수 없습니다.";
        if (role.ProviderKind != AiServiceProvider.OpenAI) return "OPENAI_PROVIDER_MISMATCH";
        if (!string.Equals(role.Transport, "codex_cli", StringComparison.OrdinalIgnoreCase))
            return "OpenAI 역할의 실행 방식이 Codex CLI가 아닙니다.";
        if (!openAiAuthenticated) return "Codex CLI 인증을 확인할 수 없습니다. codex login status를 확인하세요.";
        if (!CodexModelRequest.TryCreate(role.Model, role.Reasoning, out _))
            return $"OpenAI 모델/추론 조합을 지원하지 않습니다: {role.Model} / {role.Reasoning}";
        return null;
    }

    public async Task<AiRoleRunResult> RunAsync(AiRoleRunRequest request)
    {
        var effectivePrompt = UserAttachmentTransport.AppendPrompt(
            request.Prompt,
            request.InputAttachments);
        var additionalWritableDirectories = (request.AdditionalWritableDirectories ??
                Array.Empty<string>())
            .Concat(
                (request.InputAttachments ?? Array.Empty<AiInputAttachment>())
                .Select(attachment => Path.GetDirectoryName(attachment.Path))
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => path!))
            .Distinct(OperatingSystem.IsWindows()
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal)
            .ToArray();

        var result = await codexRunner.RunAsync(
            effectivePrompt,
            request.Role.Model,
            request.Role.Reasoning,
            request.WorkingDirectory,
            request.SessionId,
            request.Sandbox == CodexSandboxMode.ReadOnly,
            request.CancellationToken,
            request.OutputSchemaJson,
            request.Sandbox,
            request.Progress,
            request.SessionStarted,
            additionalWritableDirectories,
            request.IgnoreProjectInstructions,
            request.EnvironmentVariables,
            request.DisableComputerUse,
            request.IncludeAppBaseWritable,
            request.CodexConfigOverrides,
            request.BypassHookTrust);

        return new(
            AiProviderCatalog.ToWireId(Provider),
            result.Model,
            result.Reasoning,
            result.SessionId,
            result.ExitCode,
            result.StandardOutput,
            result.StandardError,
            result.FinalMessage,
            result.Files,
            result.Usage,
            result.CommandExecutions ?? Array.Empty<CodexCommandExecution>());
    }
}

public sealed class MuseOpenCodeRoleRunner(OpenCodeCliRunner openCodeRunner) : IAiRoleRunner
{
    public AiServiceProvider Provider => AiServiceProvider.Muse;
    public bool SupportsSessions => true;

    public string? GetPreflightError(
        WorkerAiRoleSettings role,
        string workingDirectory,
        bool openAiAuthenticated)
    {
        if (!Directory.Exists(workingDirectory))
            return "Working Folder가 없거나 접근할 수 없습니다.";
        if (role.ProviderKind != AiServiceProvider.Muse)
            return "MUSE_PROVIDER_MISMATCH";
        if (!string.Equals(
                role.Transport,
                "opencode_cli",
                StringComparison.OrdinalIgnoreCase))
        {
            return "Muse 역할의 실행 방식이 OpenCode CLI가 아닙니다.";
        }

        var descriptor = AiProviderCatalog.Get(AiServiceProvider.Muse);
        var model = descriptor.FindModel(role.Model);
        if (model is null || !model.SupportsReasoning(role.Reasoning))
        {
            return $"Muse 모델/추론 조합을 지원하지 않습니다: {role.Model} / {role.Reasoning}";
        }

        if (openCodeRunner.FindExecutable() is null)
        {
            return "OpenCode CLI를 찾을 수 없습니다. Windows에 OpenCode를 설치하고 PATH를 갱신한 뒤 ProjectHub를 다시 시작하세요.";
        }

        return null;
    }

    public async Task<AiRoleRunResult> RunAsync(AiRoleRunRequest request)
    {
        var effectivePrompt = UserAttachmentTransport.AppendPrompt(
            request.Prompt,
            request.InputAttachments);
        var result = await openCodeRunner
            .RunAsync(request, effectivePrompt)
            .ConfigureAwait(false);

        return new AiRoleRunResult(
            AiProviderCatalog.ToWireId(Provider),
            result.Model,
            result.Reasoning,
            result.SessionId,
            result.ExitCode,
            result.StandardOutput,
            result.StandardError,
            result.FinalMessage,
            result.Files,
            result.Usage,
            result.CommandExecutions);
    }
}

public sealed class UnconfiguredAiRoleRunner(AiServiceProvider provider) : IAiRoleRunner
{
    public AiServiceProvider Provider { get; } = provider;
    public bool SupportsSessions => false;

    public string? GetPreflightError(WorkerAiRoleSettings role, string workingDirectory, bool openAiAuthenticated)
    {
        var descriptor = AiProviderCatalog.Get(Provider);
        return $"{descriptor.DisplayName} 실행 환경이 아직 연결되지 않았습니다 ({descriptor.WireId.ToUpperInvariant()}_NOT_CONFIGURED).";
    }

    public Task<AiRoleRunResult> RunAsync(AiRoleRunRequest request) =>
        throw new InvalidOperationException($"{AiProviderCatalog.Get(Provider).WireId.ToUpperInvariant()}_NOT_CONFIGURED");
}

public sealed class AiRoleRunnerRegistry
{
    private readonly IReadOnlyDictionary<AiServiceProvider, IAiRoleRunner> _runners;

    public AiRoleRunnerRegistry(IEnumerable<IAiRoleRunner> runners)
    {
        _runners = runners.ToDictionary(runner => runner.Provider);
    }

    public static AiRoleRunnerRegistry CreateDefault(
        CodexCliRunner codexRunner,
        OpenCodeCliRunner? openCodeRunner = null) =>
        new(new IAiRoleRunner[]
        {
            new OpenAiCodexRoleRunner(codexRunner),
            new UnconfiguredAiRoleRunner(AiServiceProvider.Claude),
            new MuseOpenCodeRoleRunner(openCodeRunner ?? new OpenCodeCliRunner())
        });

    public IAiRoleRunner? Resolve(WorkerAiRoleSettings role) =>
        role.ProviderKind is { } provider && _runners.TryGetValue(provider, out var runner) ? runner : null;

    public string? GetPreflightError(WorkerAiRoleSettings role, string workingDirectory, bool openAiAuthenticated)
    {
        if (role.ProviderKind is null)
            return $"지원되지 않는 AI Provider입니다: {role.Provider}";
        var runner = Resolve(role);
        return runner is null
            ? $"Provider runner가 등록되지 않았습니다: {role.Provider}"
            : runner.GetPreflightError(role, workingDirectory, openAiAuthenticated);
    }
}
