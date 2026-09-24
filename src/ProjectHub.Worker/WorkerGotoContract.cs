namespace ProjectHub.Worker;

public enum WorkerRoleState { Hq, Work, Judge, High, Unknown }
public enum WorkerAction { Continue, Pause, End }
public sealed record WorkerGotoRoute(WorkerRoleState? Target, string Body, WorkerAction? Action = null, string? Error = null);

/// <summary>Parses only control lines and enforces the configured state graph. Body content remains opaque.</summary>
public static class WorkerGotoContract
{
    public static WorkerGotoRoute Parse(WorkerRoleState source, string? response, bool highPermitAvailable = false)
    {
        var lines = (response ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var first = Array.FindIndex(lines, line => !string.IsNullOrWhiteSpace(line));
        if (first < 0) return Invalid("CONTROL_MISSING");
        var control = lines[first].Trim().TrimStart('\uFEFF');

        if (source == WorkerRoleState.Hq)
        {
            if (!IsActionCandidate(control))
                return IsGotoCandidate(control) ? Invalid("ACTION_MISSING") : Invalid("CONTROL_INVALID_FIRST_LINE");
            if (!TryParseAction(control, out var action)) return Invalid("ACTION_INVALID");

            var nextIndex = NextContentLine(lines, first + 1);
            if (action is WorkerAction.Pause or WorkerAction.End)
            {
                if (nextIndex >= 0 && IsGotoCandidate(lines[nextIndex].Trim())) return Invalid("GOTO_NOT_ALLOWED_WITH_ACTION");
                return new(null, string.Join(Environment.NewLine, lines.Skip(first + 1)).Trim(), action);
            }

            if (nextIndex < 0) return Invalid("GOTO_MISSING");
            var gotoLine = lines[nextIndex].Trim();
            if (!IsGotoCandidate(gotoLine)) return Invalid("GOTO_MISSING");
            if (!TryParseTarget(gotoLine, out var target)) return Invalid("GOTO_INVALID");
            if (target is not (WorkerRoleState.Work or WorkerRoleState.High)) return Invalid("GOTO_NOT_ALLOWED");
            if (target == WorkerRoleState.High && !highPermitAvailable) return Invalid("HIGH_NOT_AUTHORIZED");
            return new(target, string.Join(Environment.NewLine, lines.Skip(nextIndex + 1)).Trim(), action);
        }

        if (IsActionCandidate(control))
            return TryParseAction(control, out _) ? Invalid("ACTION_NOT_ALLOWED") : Invalid("ACTION_INVALID");
        if (!IsGotoCandidate(control)) return Invalid("GOTO_INVALID_FIRST_LINE");
        if (!TryParseTarget(control, out var destination)) return Invalid("GOTO_INVALID");
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

    private static bool IsActionCandidate(string line) => line.StartsWith("[ACTION", StringComparison.OrdinalIgnoreCase);
    private static bool IsGotoCandidate(string line) => line.StartsWith("[GOTO", StringComparison.OrdinalIgnoreCase);

    private static bool TryParseAction(string line, out WorkerAction action)
    {
        action = default;
        if (!HasSingleClosingBracket(line)) return false;
        var matches = new (string Token, WorkerAction Action)[]
        {
            ("CONTINUE", WorkerAction.Continue), ("PAUSE", WorkerAction.Pause), ("END", WorkerAction.End)
        }.Where(item => line.Contains(item.Token, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length != 1) return false;
        action = matches[0].Action;
        return true;
    }

    private static bool TryParseTarget(string line, out WorkerRoleState target)
    {
        target = WorkerRoleState.Unknown;
        if (!HasSingleClosingBracket(line)) return false;
        var matches = new[] { "HQ", "WORK", "JUDGE", "HIGH", "UNKNOWN" }
            .Where(token => line.Contains(token, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length != 1) return false;
        target = ParseTarget(matches[0]);
        return true;
    }

    private static bool HasSingleClosingBracket(string line)
        => line.StartsWith("[", StringComparison.Ordinal)
            && line.EndsWith("]", StringComparison.Ordinal)
            && line.IndexOf('[', 1) < 0
            && line.IndexOf(']') == line.Length - 1;

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
