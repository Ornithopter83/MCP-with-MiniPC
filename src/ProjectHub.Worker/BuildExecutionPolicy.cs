using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace ProjectHub.Worker;

/// <summary>
/// GENERAL WORK가 build/run/publish 및 Git mutation을 직접 실행하지 않도록 하는 기계 정책입니다.
/// build/run/publish와 Git finalize는 중간관리자/Worker의 별도 단계에서 수행합니다.
/// legacy WorkItem API는 제거 마이그레이션 동안 호환을 위해 유지합니다.
/// </summary>
public static class BuildExecutionPolicy
{
    public const string BuildCommandForbiddenError = "WORK_BUILD_COMMAND_FORBIDDEN";
    public const string GeneralWorkCommandForbiddenError = "WORK_COMMAND_FORBIDDEN";

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

    private static readonly Regex GitMutationCommandRegex =
        new(
            @"(?ix)(?<![\w.-])git(?:\.exe)?\s+(?:add|commit|push|fetch|pull|clone|reset|checkout|switch|restore|merge|rebase|cherry-pick|revert|clean|rm|mv|tag)\b",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static IReadOnlyList<string> OpenCodeDeniedCommandPatterns { get; } =
        new[]
        {
            "*dotnet*restore*",
            "*dotnet*build*",
            "*dotnet*test*",
            "*dotnet*run*",
            "*dotnet*publish*",
            "*dotnet*pack*",
            "*dotnet*msbuild*",
            "*dotnet*vstest*",
            "*msbuild*",
            "*csc*",
            "*vbc*",
            "*clang*",
            "*gcc*",
            "*g++*",
            "*rustc*",
            "*javac*",
            "*cmake*--build*",
            "*ninja*",
            "*make*",
            "*cargo*build*",
            "*cargo*test*",
            "*cargo*run*",
            "*go*build*",
            "*go*test*",
            "*go*run*",
            "*npm*build*",
            "*npm*test*",
            "*pnpm*build*",
            "*pnpm*test*",
            "*yarn*build*",
            "*yarn*test*",
            "*bun*build*",
            "*bun*test*",
            "*gradle*",
            "*gradlew*",
            "*mvn*",
            "*mvnw*",
            "*python*-m*build*"
        };

    private static readonly Regex BuildInstructionRegex =
        new(
            @"(?ix)(?:" +
            @"\bdotnet(?:\.exe)?\s+(?:restore|build|test|run|publish|pack|msbuild|vstest)\b|" +
            @"\bcmake(?:\.exe)?\s+--build\b|" +
            @"(?:release|debug)\s*(?:build|빌드).{0,24}(?:실행|수행|성공|통과|검증|시도|재시도|완료)|" +
            @"(?:솔루션|프로젝트|전체).{0,24}(?:build|빌드).{0,24}(?:실행|수행|성공|통과|검증|시도|재시도|완료)|" +
            @"(?:build|빌드).{0,24}(?:실행|수행|성공|통과|검증|시도|재시도|완료)|" +
            @"(?:restore|복원).{0,24}(?:실행|수행|성공|통과|검증|시도|재시도|완료)|" +
            @"(?:publish|게시).{0,24}(?:실행|수행|성공|통과|검증|시도|재시도|완료)" +
            @")",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ExplicitProhibitionRegex =
        new(
            @"(?ix)(?:" +
            @"(?:do\s+not|must\s+not|never)\s+.{0,40}(?:build|restore|compile|publish)|" +
            @"(?:build|빌드|restore|복원|compile|컴파일|publish|게시).{0,30}(?:하지\s*않|하지\s*말|금지|않는다|금한다)" +
            @")",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static bool IsBuildCommand(string? commandLine)
        => !string.IsNullOrWhiteSpace(commandLine) && BuildCommandRegex.IsMatch(commandLine);

    public static bool IsGeneralWorkForbiddenCommand(string? commandLine)
        => !string.IsNullOrWhiteSpace(commandLine) &&
           (BuildCommandRegex.IsMatch(commandLine) ||
            GitMutationCommandRegex.IsMatch(commandLine));

    public static string CreateCodexPreToolHookScript()
        => """
           $ErrorActionPreference = 'Stop'
           $raw = [Console]::In.ReadToEnd()
           try { $event = $raw | ConvertFrom-Json -Depth 32 } catch { exit 0 }
           if ([string]$event.tool_name -ne 'Bash') { exit 0 }
           $command = [string]$event.tool_input.command
           if ([string]::IsNullOrWhiteSpace($command)) { exit 0 }
           $pattern = '(?ix)(?:(?<![\w.-])dotnet(?:\.exe)?\s+(?:restore|build|test|run|publish|pack|msbuild|vstest)\b|(?<![\w.-])msbuild(?:\.exe)?\b|(?<![\w.-])(?:csc|vbc|cl|clang|clang\+\+|gcc|g\+\+|rustc|javac)(?:\.exe)?\b|(?<![\w.-])cmake(?:\.exe)?\s+--build\b|(?<![\w.-])(?:ninja|make)(?:\.exe)?\b|(?<![\w.-])cargo(?:\.exe)?\s+(?:build|test|run)\b|(?<![\w.-])go(?:\.exe)?\s+(?:build|test|run)\b|(?<![\w.-])(?:npm|pnpm|yarn|bun)(?:\.cmd|\.exe)?\s+(?:(?:run)\s+)?(?:build|test)\b|(?<![\w.-])(?:gradle|gradlew|mvn|mvnw)(?:\.bat|\.cmd|\.exe)?\b|(?<![\w.-])python(?:\.exe)?\s+-m\s+build\b|(?<![\w.-])git(?:\.exe)?\s+(?:add|commit|push|fetch|pull|clone|reset|checkout|switch|restore|merge|rebase|cherry-pick|revert|clean|rm|mv|tag)\b)'
           if ($command -match $pattern) {
             $payload = @{ hookSpecificOutput = @{ hookEventName = 'PreToolUse'; permissionDecision = 'deny'; permissionDecisionReason = 'ProjectHub: GENERAL WORK cannot run build/run/publish or Git mutation commands. Use the manager/Worker stages.' } } | ConvertTo-Json -Compress -Depth 6
             [Console]::Out.WriteLine($payload)
           }
           exit 0
           """;

    public static string CreateQaCodexPreToolHookScript()
        => CreateCodexPreToolHookScript().Replace(
            "ProjectHub: GENERAL WORK cannot run build/run/publish or Git mutation commands. Use the manager/Worker stages.",
            "ProjectHub: QA cannot run build/run/publish or Git mutation commands. Use the prepared execution target.",
            StringComparison.Ordinal);

    public static string BuildCodexPreToolHookOverride(
        string hookPath,
        string statusMessage = "ProjectHub WORK command gate")
    {
        if (string.IsNullOrWhiteSpace(hookPath))
            throw new ArgumentException("Hook path가 비어 있습니다.", nameof(hookPath));
        var normalizedPath = Path.GetFullPath(hookPath).Replace('\\', '/');
        var command = "powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File '" +
                      normalizedPath.Replace("'", "''", StringComparison.Ordinal) + "'";
        return "hooks.PreToolUse=[{matcher=\"^Bash$\",hooks=[{type=\"command\",command=\"" +
               EscapeTomlBasicString(command) +
               "\",timeout=5,statusMessage=\"" +
               EscapeTomlBasicString(statusMessage) +
               "\"}]}]";
    }

    private static string EscapeTomlBasicString(string value)
        => value.Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("\"", "\\\"", StringComparison.Ordinal)
                .Replace("\r", "\\r", StringComparison.Ordinal)
                .Replace("\n", "\\n", StringComparison.Ordinal);
}
