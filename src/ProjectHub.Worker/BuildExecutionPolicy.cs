using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace ProjectHub.Worker;

/// <summary>
/// #9 BUILD/PUBLISH만 프로젝트 빌드 계열 명령을 실행하도록 강제하는 기계 정책입니다.
/// 일반 WorkItem은 소스 구현은 할 수 있지만 restore/compile/build/test-run/package/publish는 실행하지 않습니다.
/// </summary>
public static class BuildExecutionPolicy
{
    public const string BuildSlotRequiredError = "WORK_BUILD_SLOT_REQUIRED";
    public const string BuildCommandForbiddenError = "WORK_BUILD_COMMAND_FORBIDDEN";

    private const string BuildCommandPattern =
        @"(?ix)(?:" +
        @"(?<![\w.-])dotnet(?:\.exe)?\s+(?:restore|build|test|run|publish|pack|msbuild|vstest)\b|" +
        @"(?<![\w.-])msbuild(?:\.exe)?\b|" +
        @"(?<![\w.-])(?:csc|vbc|cl|clang|clang\+\+|gcc|g\+\+|rustc|javac)(?:\.exe)?\b|" +
        @"(?<![\w.-])cmake(?:\.exe)?\s+--build\b|" +
        @"(?<![\w.-])(?:ninja|make)(?:\.exe)?\b|" +
        @"(?<![\w.-])cargo(?:\.exe)?\s+(?:build|test|run)\b|" +
        @"(?<![\w.-])go(?:\.exe)?\s+(?:build|test|run)\b|" +
        @"(?<![\w.-])(?:npm|pnpm|yarn|bun)(?:\.cmd|\.exe)?\s+(?:(?:run)\s+)?(?:build|test)\b|" +
        @"(?<![\w.-])(?:gradle|gradlew|mvn|mvnw)(?:\.bat|\.cmd|\.exe)?\b|" +
        @"(?<![\w.-])python(?:\.exe)?\s+-m\s+build\b" +
        @")";

    private static readonly Regex BuildCommandRegex =
        new(BuildCommandPattern, RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex BuildInstructionRegex =
        new(
            @"(?ix)(?:" +
            @"\bdotnet(?:\.exe)?\s+(?:restore|build|test|run|publish|pack|msbuild|vstest)\b|" +
            @"\bcmake(?:\.exe)?\s+--build\b|" +
            @"(?:release|debug)\s*(?:build|빌드)\b|" +
            @"(?:솔루션|프로젝트|전체).{0,24}(?:build|빌드)(?:를|을)?\s*(?:실행|수행|성공|통과|검증|확인|시도|재시도|완료)?|" +
            @"(?:build|빌드)(?:를|을)?\s*(?:실행|수행|성공|통과|검증|확인|시도|재시도|완료)|" +
            @"(?:restore|복원).{0,20}(?:build|빌드)|(?:build|빌드).{0,20}(?:restore|복원)" +
            @")",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ExplicitProhibitionRegex =
        new(
            @"(?ix)(?:" +
            @"(?:do\s+not|must\s+not|never)\s+.{0,40}(?:build|restore|compile|publish)|" +
            @"(?:build|빌드|restore|복원|compile|컴파일|publish|게시).{0,30}(?:하지\s*않|하지\s*말|금지|않는다|금한다)" +
            @")",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static bool AllowsBuildExecution(string? workItemId)
        => string.Equals(workItemId?.Trim(), FixedWorkItemSlots.BuildPublish, StringComparison.Ordinal);

    public static bool ContainsBuildExecutionInstruction(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;
        var normalized = text.Trim();
        if (ExplicitProhibitionRegex.IsMatch(normalized))
            return false;
        return BuildInstructionRegex.IsMatch(normalized) || BuildCommandRegex.IsMatch(normalized);
    }

    public static bool IsForbiddenForWorkItem(string? workItemId, string? command, IEnumerable<string>? arguments = null)
    {
        if (AllowsBuildExecution(workItemId))
            return false;
        var combined = string.Join(" ", new[] { command ?? string.Empty }.Concat(arguments ?? Array.Empty<string>()));
        return IsBuildCommand(combined);
    }

    public static bool IsBuildCommand(string? commandLine)
        => !string.IsNullOrWhiteSpace(commandLine) && BuildCommandRegex.IsMatch(commandLine);

    public static string CreateCodexPreToolHookScript()
        => """
           $ErrorActionPreference = 'Stop'
           $raw = [Console]::In.ReadToEnd()
           try { $event = $raw | ConvertFrom-Json -Depth 32 } catch { exit 0 }
           if ([string]$event.tool_name -ne 'Bash') { exit 0 }
           $command = [string]$event.tool_input.command
           if ([string]::IsNullOrWhiteSpace($command)) { exit 0 }
           $pattern = '(?ix)(?:(?<![\w.-])dotnet(?:\.exe)?\s+(?:restore|build|test|run|publish|pack|msbuild|vstest)\b|(?<![\w.-])msbuild(?:\.exe)?\b|(?<![\w.-])(?:csc|vbc|cl|clang|clang\+\+|gcc|g\+\+|rustc|javac)(?:\.exe)?\b|(?<![\w.-])cmake(?:\.exe)?\s+--build\b|(?<![\w.-])(?:ninja|make)(?:\.exe)?\b|(?<![\w.-])cargo(?:\.exe)?\s+(?:build|test|run)\b|(?<![\w.-])go(?:\.exe)?\s+(?:build|test|run)\b|(?<![\w.-])(?:npm|pnpm|yarn|bun)(?:\.cmd|\.exe)?\s+(?:(?:run)\s+)?(?:build|test)\b|(?<![\w.-])(?:gradle|gradlew|mvn|mvnw)(?:\.bat|\.cmd|\.exe)?\b|(?<![\w.-])python(?:\.exe)?\s+-m\s+build\b)'
           if ($command -match $pattern) {
             $payload = @{ hookSpecificOutput = @{ hookEventName = 'PreToolUse'; permissionDecision = 'deny'; permissionDecisionReason = 'ProjectHub: restore/compile/build/test-run/package/publish execution is reserved for WorkItem #9 BUILD/PUBLISH.' } } | ConvertTo-Json -Compress -Depth 6
             [Console]::Out.WriteLine($payload)
           }
           exit 0
           """;

    public static string BuildCodexPreToolHookOverride(string hookPath)
    {
        if (string.IsNullOrWhiteSpace(hookPath))
            throw new ArgumentException("Hook path가 비어 있습니다.", nameof(hookPath));
        var normalizedPath = Path.GetFullPath(hookPath).Replace('\\', '/');
        var command = "powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File '" +
                      normalizedPath.Replace("'", "''", StringComparison.Ordinal) + "'";
        return "hooks.PreToolUse=[{matcher=\"^Bash$\",hooks=[{type=\"command\",command=\"" +
               EscapeTomlBasicString(command) +
               "\",timeout=5,statusMessage=\"ProjectHub build gate\"}]}]";
    }

    private static string EscapeTomlBasicString(string value)
        => value.Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("\"", "\\\"", StringComparison.Ordinal)
                .Replace("\r", "\\r", StringComparison.Ordinal)
                .Replace("\n", "\\n", StringComparison.Ordinal);
}
