using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace ProjectHub.Worker;

public enum WorkerCommandRiskLevel { Normal, Caution, Warning, Danger }

internal sealed record WorkerCommandRiskAssessment(
    WorkerCommandRiskLevel Level, string Reason)
{
    internal string Tag => Level switch
    {
        WorkerCommandRiskLevel.Caution => "[주의]",
        WorkerCommandRiskLevel.Warning => "[경고]",
        WorkerCommandRiskLevel.Danger => "[위험]",
        _ => "[일반]"
    };
}

/// <summary>
/// Advisory risk labels for observed shell commands. Classification never
/// denies, delays or grants execution; the operating system remains authoritative.
/// Unobserved tool actions cannot be labeled reliably.
/// </summary>
internal static class WorkerCommandRisk
{
    private static bool Has(string value, string pattern) =>
        Regex.IsMatch(value, pattern, RegexOptions.IgnoreCase |
            RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250));

    internal static WorkerCommandRiskAssessment Classify(string? command)
    {
        if (string.IsNullOrWhiteSpace(command))
            return new(WorkerCommandRiskLevel.Normal, "empty");
        var cmd = command.Length > 20000 ? command[..20000] : command;
        try
        {
            if (Has(cmd, @"\bgit(?:\.exe)?\s+(?:-C\s+\S+\s+)?reset\b[^\r\n]*--hard\b") ||
                Has(cmd, @"\bgit(?:\.exe)?\s+(?:-C\s+\S+\s+)?clean\s+-\S*f") ||
                Has(cmd, @"\bgit(?:\.exe)?\s+(?:-C\s+\S+\s+)?push\b[^\r\n]*--force\b") ||
                Has(cmd, @"\bRemove-Item\b(?=[^\r\n]*\s-Recurse\b)(?=[^\r\n]*\s-Force\b)") ||
                Has(cmd, @"\b(?:rm|rmdir)\s+-(?:rf|fr)\b|\b(?:rmdir|rd)\s+/s\b") ||
                Has(cmd, @"\b(?:Clear-Disk|Format-Volume|Initialize-Disk|Remove-Partition|diskpart|format\.com)\b") ||
                Has(cmd, @"\b(?:Set-MpPreference|Remove-MpPreference)\b[^\r\n]*Disable") ||
                Has(cmd, @"\b(?:reg(?:\.exe)?\s+delete|shutdown|Stop-Computer|Restart-Computer)\b"))
                return new(WorkerCommandRiskLevel.Danger, "파괴적 삭제·Git 강제 변경·시스템 조작 가능");

            if (Has(cmd, @"\b(?:Invoke-Expression|iex|Set-ExecutionPolicy|takeown|icacls)\b") ||
                Has(cmd, @"\b(?:winget|choco|scoop|pip|pip3|npm|pnpm|yarn)\b[^\r\n]*\b(?:install|uninstall|remove)\b") ||
                Has(cmd, @"\bdotnet\s+tool\s+(?:install|uninstall|update)\b") ||
                Has(cmd, @"\bgit(?:\.exe)?\s+(?:-C\s+\S+\s+)?(?:push|rebase|merge|checkout|switch)\b") ||
                Has(cmd, @"\b(?:Invoke-WebRequest|Invoke-RestMethod|curl(?:\.exe)?|wget|Start-BitsTransfer)\b") ||
                Has(cmd, @"\b(?:Start-Process)\b[^\r\n]*-Verb\s+RunAs\b") ||
                Has(cmd, @"\b(?:reg(?:\.exe)?\s+add|sc(?:\.exe)?\s+(?:stop|delete|config))\b"))
                return new(WorkerCommandRiskLevel.Warning, "외부 통신·설치·관리 권한 또는 원격 상태 변경 가능");

            if (Has(cmd, @"\bgit(?:\.exe)?\s+(?:-C\s+\S+\s+)?(?:add|commit|restore|rm|mv)\b") ||
                Has(cmd, @"\b(?:Copy-Item|Move-Item|Set-Content|New-Item|Remove-Item|Out-File|Add-Content|mkdir)\b"))
                return new(WorkerCommandRiskLevel.Caution, "로컬 파일 또는 Git 상태 변경 가능");
            return new(WorkerCommandRiskLevel.Normal, "일반 실행");
        }
        catch (RegexMatchTimeoutException)
        {
            return new(WorkerCommandRiskLevel.Warning, "명령 분석 시간초과; 실행은 유지");
        }
    }

    internal static bool TryReadProgress(string? message, out string command,
        out string phase, out WorkerCommandRiskAssessment assessment)
    {
        command = string.Empty;
        phase = string.Empty;
        assessment = new(WorkerCommandRiskLevel.Normal, "일반 실행");
        if (string.IsNullOrWhiteSpace(message)) return false;

        const string start = "명령 실행 시작 · ";
        const string complete = "명령 실행 종료";
        if (message.StartsWith(start, StringComparison.Ordinal))
        {
            command = message[start.Length..].Trim();
            phase = "STARTED";
        }
        else if (message.StartsWith(complete, StringComparison.Ordinal))
        {
            var i = message.IndexOf(" · ", StringComparison.Ordinal);
            if (i < 0) return false;
            // Codex reports "명령 실행 종료 · exit N · <command>".
            var exitDelimiter = message.IndexOf(" · ", i + 3, StringComparison.Ordinal);
            var commandDelimiter = exitDelimiter >= 0 ? exitDelimiter : i;
            command = message[(commandDelimiter + 3)..].Trim();
            phase = message[..commandDelimiter].Trim();
        }
        if (command.Length == 0) return false;
        assessment = Classify(command);
        return assessment.Level != WorkerCommandRiskLevel.Normal;
    }

    internal static string SafePreview(string command, int maxLength = 700)
    {
        var preview = command.Length > maxLength
            ? command[..maxLength] + "…" : command;
        return Regex.Replace(preview,
            @"(?i)(--(?:token|password|api-key|secret)\s*(?:=|\s)\s*)(?:""[^""]*""|'[^']*'|\S+)",
            "$1[REDACTED]");
    }
}

internal static class WorkerRiskAuditLog
{
    private static readonly object Sync = new();

    internal static string Format(string jobId, string role,
        WorkerCommandRiskAssessment assessment, string command, string phase) =>
        DateTimeOffset.Now.ToString("yyyy-MM-ddTHH:mm:ss.fffzzz") + " " +
        assessment.Tag + " job=" + jobId + " role=" + role +
        " phase=" + phase + " reason=" + assessment.Reason +
        Environment.NewLine + "command=" + WorkerCommandRisk.SafePreview(command);

    internal static bool TryAppend(string directory, string logRecord)
    {
        try
        {
            lock (Sync)
            {
                Directory.CreateDirectory(directory);
                File.AppendAllText(Path.Combine(directory, "risk-audit.log"),
                    logRecord + Environment.NewLine + Environment.NewLine,
                    new UTF8Encoding(false));
            }
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Trace.WriteLine("RISK_AUDIT_LOG_FAILED: " + ex.Message);
            return false; // Advisory: do not block executed commands.
        }
    }
}
