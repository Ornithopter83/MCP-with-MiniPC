namespace ProjectHub.Worker;

public enum JudgeDecision { Pass, Fail, Error }
public sealed record JudgeRequest(string Goal, int Round, string WorkingDirectory, string CodexResult, string ValidationRequest, IReadOnlyList<CodexCliFile> Files, string ReviewSource, string? ReviewCommitSha);
public sealed record JudgeResult(JudgeDecision Decision, string Message, string Provider, string? ExecutableOrEndpoint = null);

/// <summary>Read-only JEV seam. Until a concrete approved provider adapter is configured, routing falls back to GPT Web.</summary>
public sealed class JevJudgeRunner
{
    public string? FindExecutable(string? manualExecutableOrEndpoint) => string.IsNullOrWhiteSpace(manualExecutableOrEndpoint) ? null : manualExecutableOrEndpoint;
    public Task<JudgeResult> ReviewAsync(JudgeRequest request, JudgeSettings settings, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var endpoint = settings.ManualExecutableOrEndpoint;
        var message = string.IsNullOrWhiteSpace(endpoint)
            ? "JEV provider가 설정되지 않아 GPT Web fallback을 사용합니다."
            : "설정된 JEV provider adapter가 없어 GPT Web fallback을 사용합니다.";
        return Task.FromResult(new JudgeResult(JudgeDecision.Error, message, settings.Provider, endpoint));
    }
}