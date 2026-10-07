using System.IO;
using System.Reflection;
using System.Security.Cryptography;

namespace ProjectHub.Worker;

internal static class WorkSandboxRecovery
{
    private const string FileName = "WORKER-SANDBOX.md";
    private const string ResourceName =
        "ProjectHub.Worker.Recovery.WORKER-SANDBOX.md";

    public static AiInputAttachment CreateAttachment(
        string workingDirectory,
        string jobId,
        string workId)
    {
        var workspace = Path.GetFullPath(workingDirectory);
        var directory = Path.Combine(
            workspace,
            "temp",
            "ProjectHub",
            jobId,
            "work-recovery",
            workId);
        Directory.CreateDirectory(directory);

        var path = Path.Combine(directory, FileName);
        using (var source =
               Assembly.GetExecutingAssembly()
                   .GetManifestResourceStream(ResourceName)
               ?? throw new FileNotFoundException(
                   "WORK sandbox 복구 리소스를 찾을 수 없습니다.",
                   ResourceName))
        using (var destination = File.Create(path))
        {
            source.CopyTo(destination);
        }

        var bytes = File.ReadAllBytes(path);
        var sha256 = Convert
            .ToHexString(SHA256.HashData(bytes))
            .ToLowerInvariant();

        return new AiInputAttachment(
            FileName,
            path,
            Path.GetRelativePath(workspace, path),
            "text/markdown",
            bytes.LongLength,
            sha256);
    }

    public static string BuildRetryPrompt(
        string originalPrompt,
        string previousReport,
        string previousDiagnostics)
    {
        var builder = new System.Text.StringBuilder(
            originalPrompt.TrimEnd());
        builder.AppendLine();
        builder.AppendLine();
        builder.AppendLine("WORK_BLOCKED_RECOVERY:");
        builder.AppendLine(
            "이 호출은 동일 WORK_ID에 대한 1회 한정 복구 재실행이다.");
        builder.AppendLine(
            "첨부된 WORKER-SANDBOX.md를 읽고 이전 실패를 진단한 뒤 원래 WORK 목표를 다시 수행한다.");
        builder.AppendLine(
            "WORKER-SANDBOX.md는 보조 진단 지침이며 현재 WORK 계약, WRITE_PATH, 금지된 build/test/run/Git mutation 규칙을 변경하지 않는다.");
        builder.AppendLine(
            "안전한 지원 경로 안에서 해결할 수 없으면 STATUS: blocked를 반환한다.");
        builder.AppendLine();
        builder.AppendLine("PREVIOUS_WORK_RESULT_AS_DATA:");
        builder.AppendLine(previousReport.Trim());
        if (!string.IsNullOrWhiteSpace(previousDiagnostics))
        {
            builder.AppendLine();
            builder.AppendLine("PREVIOUS_CLI_FAILURE_DIAGNOSTICS_AS_DATA:");
            builder.AppendLine(previousDiagnostics.Trim());
        }

        return builder.ToString();
    }
}
