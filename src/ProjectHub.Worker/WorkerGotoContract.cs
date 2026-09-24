using System.Text.RegularExpressions;

namespace ProjectHub.Worker;

public enum WorkerRoleState { Hq, Work, Judge, High, Unknown }
public enum WorkerAction { Continue, Pause, End }
public sealed record WorkerGotoRoute(WorkerRoleState? Target, string Body, WorkerAction? Action = null, string? Error = null);

/// <summary>Parses only control lines and enforces the configured state graph. Body content remains opaque.</summary>
public static class WorkerGotoContract
{
    private static readonly Regex ActionPattern = new(@"^\[ACTION\s*=\s*(CONTINUE|PAUSE|END)\s*\]$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex GotoPattern = new(@"^\[GOTO\s*:\s*(HQ|WORK|JUDGE|HIGH|UNKNOWN)\s*\]$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static WorkerGotoRoute Parse(WorkerRoleState source, string? response, bool highPermitAvailable = false)
    {
        var lines = (response ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var first = Array.FindIndex(lines, line => !string.IsNullOrWhiteSpace(line));
        if (first < 0) return Invalid("CONTROL_MISSING");
        var control = lines[first].Trim().TrimStart('\uFEFF');

        if (source == WorkerRoleState.Hq)
        {
            var actionMatch = ActionPattern.Match(control);
            if (!actionMatch.Success)
                return GotoPattern.IsMatch(control) ? Invalid("ACTION_MISSING") : Invalid("CONTROL_INVALID_FIRST_LINE");

            var action = Enum.Parse<WorkerAction>(actionMatch.Groups[1].Value, ignoreCase: true);
            var nextIndex = NextContentLine(lines, first + 1);
            if (action is WorkerAction.Pause or WorkerAction.End)
            {
                if (nextIndex >= 0 && GotoPattern.IsMatch(lines[nextIndex].Trim())) return Invalid("GOTO_NOT_ALLOWED_WITH_ACTION");
                return new(null, string.Join(Environment.NewLine, lines.Skip(first + 1)).Trim(), action);
            }

            if (nextIndex < 0) return Invalid("GOTO_MISSING");
            var targetMatch = GotoPattern.Match(lines[nextIndex].Trim());
            if (!targetMatch.Success) return Invalid("GOTO_INVALID");
            var target = ParseTarget(targetMatch.Groups[1].Value);
            if (target is not (WorkerRoleState.Work or WorkerRoleState.High)) return Invalid("GOTO_NOT_ALLOWED");
            if (target == WorkerRoleState.High && !highPermitAvailable) return Invalid("HIGH_NOT_AUTHORIZED");
            return new(target, string.Join(Environment.NewLine, lines.Skip(nextIndex + 1)).Trim(), action);
        }

        if (ActionPattern.IsMatch(control)) return Invalid("ACTION_NOT_ALLOWED");
        var gotoMatch = GotoPattern.Match(control);
        if (!gotoMatch.Success) return Invalid("GOTO_INVALID_FIRST_LINE");
        var destination = ParseTarget(gotoMatch.Groups[1].Value);
        var allowed = source switch
        {
            WorkerRoleState.Work => destination is WorkerRoleState.Hq or WorkerRoleState.Judge,
            WorkerRoleState.Judge => destination == WorkerRoleState.Work,
            WorkerRoleState.High => destination == WorkerRoleState.Hq,
            _ => false
        };
        if (!allowed) return Invalid("GOTO_NOT_ALLOWED");
        return new(destination, string.Join(Environment.NewLine, lines.Skip(first + 1)).Trim());
    }

    private static int NextContentLine(string[] lines, int start)
    {
        for (var i = start; i < lines.Length; i++) if (!string.IsNullOrWhiteSpace(lines[i])) return i;
        return -1;
    }

    private static WorkerRoleState ParseTarget(string target) => target.ToUpperInvariant() switch
    {
        "HQ" => WorkerRoleState.Hq,
        "WORK" => WorkerRoleState.Work,
        "JUDGE" => WorkerRoleState.Judge,
        "HIGH" => WorkerRoleState.High,
        _ => WorkerRoleState.Unknown
    };

    private static WorkerGotoRoute Invalid(string error) => new(null, string.Empty, null, error);
}
