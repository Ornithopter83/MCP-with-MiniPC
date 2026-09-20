using System.IO;

namespace ProjectHub.Worker;

public enum JudgeDecision { Pass, Revise, Escalate, Error }

public sealed record JudgeRequest(
    string Goal,
    int Round,
    string WorkingDirectory,
    string CodexResult,
    IReadOnlyList<CodexCliFile> Files,
    string ReviewSource,
    string? ReviewCommitSha);

public sealed record JudgeResult(JudgeDecision Decision, string Message, string Provider, string? ExecutableOrEndpoint = null);

/// <summary>
/// Optional read-only Judge seam. The execution adapter remains inactive until a Jev CLI
/// or endpoint contract is configured; callers always fall back to GPT Web.
/// </summary>
public sealed class JevJudgeRunner
{
    public string? FindExecutable(string? manualExecutableOrEndpoint)
    {
        if (!string.IsNullOrWhiteSpace(manualExecutableOrEndpoint) && File.Exists(manualExecutableOrEndpoint))
            return Path.GetFullPath(manualExecutableOrEndpoint);

        var path = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(path))
        {
            var candidate = path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                .Select(directory => Path.Combine(directory, "jev.exe"))
                .FirstOrDefault(File.Exists);
            if (candidate is not null) return candidate;
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var candidates = new[]
        {
            Path.Combine(localAppData, "Jev", "jev.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Jev", "jev.exe")
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    public Task<JudgeResult> ReviewAsync(JudgeRequest request, JudgeSettings settings, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var executable = FindExecutable(settings.ManualExecutableOrEndpoint);
        var availability = executable is null
            ? "Jev executable/endpoint was not found"
            : "Jev execution adapter is not configured yet";
        return Task.FromResult(new JudgeResult(JudgeDecision.Error,
            availability + "; continuing with GPT Web.", settings.Provider, executable));
    }
}