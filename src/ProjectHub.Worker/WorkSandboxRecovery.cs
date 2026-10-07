using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace ProjectHub.Worker;

internal sealed record WorkSandboxRecoveryPreparation(
    string Prompt,
    AiInputAttachment? Attachment,
    string? AttachmentWarning);

internal static class WorkSandboxRecovery
{
    private const string FileName = "WORKER-SANDBOX.md";
    private const string ResourceName =
        "ProjectHub.Worker.Recovery.WORKER-SANDBOX.md";

    public static bool TryPrepare(
        string workingDirectory,
        string jobId,
        string workId,
        string originalPrompt,
        string previousReport,
        string previousDiagnostics,
        out WorkSandboxRecoveryPreparation? preparation,
        out string? error)
    {
        preparation = null;
        error = null;

        try
        {
            var guidance = LoadGuidance();

            AiInputAttachment? attachment = null;
            string? attachmentWarning = null;
            try
            {
                attachment = CreateAttachment(
                    workingDirectory,
                    jobId,
                    workId,
                    guidance);
            }
            catch (Exception exception)
            {
                attachmentWarning =
                    exception.GetType().Name +
                    ": " +
                    exception.Message;
            }

            preparation = new WorkSandboxRecoveryPreparation(
                BuildRetryPrompt(
                    originalPrompt,
                    previousReport,
                    previousDiagnostics,
                    guidance,
                    attachmentWarning),
                attachment,
                attachmentWarning);
            return true;
        }
        catch (Exception exception)
        {
            error =
                exception.GetType().Name +
                ": " +
                exception.Message;
            return false;
        }
    }

    private static string LoadGuidance()
    {
        using var stream =
            Assembly.GetExecutingAssembly()
                .GetManifestResourceStream(ResourceName)
            ?? throw new FileNotFoundException(
                "WORK sandbox 복구 리소스를 찾을 수 없습니다.",
                ResourceName);
        using var reader = new StreamReader(
            stream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd().Trim();
    }

    private static AiInputAttachment CreateAttachment(
        string workingDirectory,
        string jobId,
        string workId,
        string guidance)
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
        File.WriteAllText(
            path,
            guidance + Environment.NewLine,
            new UTF8Encoding(false));

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

    private static string BuildRetryPrompt(
        string originalPrompt,
        string previousReport,
        string previousDiagnostics,
        string guidance,
        string? attachmentWarning)
    {
        var builder = new StringBuilder(
            originalPrompt.TrimEnd());
        builder.AppendLine();
        builder.AppendLine();
        builder.AppendLine("WORK_BLOCKED_RECOVERY:");
        builder.AppendLine(
            "이 호출은 동일 WORK_ID에 대한 1회 한정 복구 재실행이다.");
        builder.AppendLine(
            "아래 WORKER-SANDBOX.md 본문을 직접 읽고 이전 실패를 진단한 뒤 원래 WORK 목표를 다시 수행한다.");
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
            builder.AppendLine(
                "PREVIOUS_CLI_FAILURE_DIAGNOSTICS_AS_DATA:");
            builder.AppendLine(previousDiagnostics.Trim());
        }

        if (!string.IsNullOrWhiteSpace(attachmentWarning))
        {
            builder.AppendLine();
            builder.AppendLine(
                "WORKER_SANDBOX_ATTACHMENT_NOTE:");
            builder.AppendLine(
                "파일 attachment 준비는 실패했다. 아래 직접 주입된 본문을 사용한다.");
            builder.AppendLine(attachmentWarning.Trim());
        }

        builder.AppendLine();
        builder.AppendLine(
            "WORKER_SANDBOX_GUIDANCE_BEGIN");
        builder.AppendLine(guidance);
        builder.AppendLine(
            "WORKER_SANDBOX_GUIDANCE_END");
        return builder.ToString();
    }
}
