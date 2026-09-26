namespace ProjectHub.Worker;

public enum WorkerRoleState { Hq, Work, Judge, Resource, Unknown }
public enum WorkerAction { Continue, Pause, End }
public sealed record WorkerGotoRoute(WorkerRoleState? Target, string Body, WorkerAction? Action = null, string? Error = null);

/// <summary>Parses only control lines and enforces the configured state graph. Body content remains opaque except at dedicated transport boundaries.</summary>
public static class WorkerGotoContract
{
    public static WorkerGotoRoute Parse(WorkerRoleState source, string? response)
    {
        var lines = (response ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var first = Array.FindIndex(lines, line => !string.IsNullOrWhiteSpace(line));
        if (first < 0) return Invalid("CONTROL_MISSING");
        var control = lines[first].Trim().TrimStart('\uFEFF');

        if (source == WorkerRoleState.Hq)
        {
            if (!IsActionCandidate(control))
                return IsGotoCandidate(control) ? Invalid("ACTION_MISSING") : Invalid("CONTROL_INVALID_FIRST_LINE");
            if (!TryParseAction(control, out var action, out var actionEnd)) return Invalid("ACTION_INVALID");

            var nextIndex = NextContentLine(lines, first + 1);
            if (action is WorkerAction.Pause or WorkerAction.End)
            {
                if (nextIndex >= 0 && IsGotoCandidate(lines[nextIndex].Trim())) return Invalid("GOTO_NOT_ALLOWED_WITH_ACTION");
                return new(null, JoinBody(control, actionEnd, lines, first), action);
            }

            if (nextIndex < 0) return Invalid("GOTO_MISSING");
            var gotoLine = lines[nextIndex].Trim();
            if (!IsGotoCandidate(gotoLine)) return Invalid("GOTO_INVALID");
            if (!TryParseTarget(gotoLine, out var target, out var gotoEnd)) return Invalid("GOTO_INVALID");
            if (target != WorkerRoleState.Work) return Invalid("GOTO_NOT_ALLOWED");
            return new(target, JoinBody(gotoLine, gotoEnd, lines, nextIndex), action);
        }

        if (IsActionCandidate(control))
            return TryParseAction(control, out _, out _) ? Invalid("ACTION_NOT_ALLOWED") : Invalid("ACTION_INVALID");
        if (!IsGotoCandidate(control)) return Invalid("GOTO_INVALID_FIRST_LINE");
        if (!TryParseTarget(control, out var destination, out var controlEnd)) return Invalid("GOTO_INVALID");
        var allowed = source switch
        {
            WorkerRoleState.Work => destination is WorkerRoleState.Hq or WorkerRoleState.Judge or WorkerRoleState.Resource,
            WorkerRoleState.Resource => destination == WorkerRoleState.Work,
            _ => false
        };
        if (!allowed) return Invalid("GOTO_NOT_ALLOWED");
        return new(destination, JoinBody(control, controlEnd, lines, first));
    }

    private static int NextContentLine(string[] lines, int start)
    {
        for (var i = start; i < lines.Length; i++) if (!string.IsNullOrWhiteSpace(lines[i])) return i;
        return -1;
    }

    private static bool IsActionCandidate(string line) => line.StartsWith("[ACTION", StringComparison.OrdinalIgnoreCase);
    private static bool IsGotoCandidate(string line) => line.StartsWith("[GOTO", StringComparison.OrdinalIgnoreCase);

    private static bool TryParseAction(string line, out WorkerAction action, out int controlEnd)
    {
        action = default;
        controlEnd = -1;
        var close = line.IndexOf(']');
        if (close < 0) return false;
        var control = line[..(close + 1)];
        var matches = new (string Token, WorkerAction Action)[]
        {
            ("CONTINUE", WorkerAction.Continue), ("PAUSE", WorkerAction.Pause), ("END", WorkerAction.End)
        }.Where(item => ContainsKeywordBeforeClose(control, item.Token)).ToArray();
        if (matches.Length != 1) return false;
        action = matches[0].Action;
        controlEnd = close + 1;
        return true;
    }

    private static bool TryParseTarget(string line, out WorkerRoleState target, out int controlEnd)
    {
        target = WorkerRoleState.Unknown;
        controlEnd = -1;
        var close = line.IndexOf(']');
        if (close < 0) return false;
        var control = line[..(close + 1)];
        var matches = new[] { "HQ", "WORK", "JUDGE", "RESOURCE", "UNKNOWN" }
            .Where(token => ContainsKeywordBeforeClose(control, token)).ToArray();
        if (matches.Length != 1) return false;
        target = ParseTarget(matches[0]);
        controlEnd = close + 1;
        return true;
    }

    private static bool ContainsKeywordBeforeClose(string control, string keyword)
    {
        var index = control.IndexOf(keyword + "]", StringComparison.OrdinalIgnoreCase);
        if (index < 0) return false;
        var before = index == 0 ? '\0' : control[index - 1];
        return index == 0 || !(char.IsLetterOrDigit(before) || before == '_');
    }

    private static string JoinBody(string controlLine, int controlEnd, string[] lines, int controlLineIndex)
    {
        var parts = new List<string>();
        var inlineBody = controlLine[controlEnd..].Trim();
        if (inlineBody.Length > 0) parts.Add(inlineBody);
        parts.AddRange(lines.Skip(controlLineIndex + 1));
        return string.Join(Environment.NewLine, parts).Trim();
    }

    private static WorkerRoleState ParseTarget(string target) => target.ToUpperInvariant() switch
    {
        "HQ" => WorkerRoleState.Hq,
        "WORK" => WorkerRoleState.Work,
        "JUDGE" => WorkerRoleState.Judge,
        "RESOURCE" => WorkerRoleState.Resource,
        _ => WorkerRoleState.Unknown
    };

    private static WorkerGotoRoute Invalid(string error) => new(null, string.Empty, null, error);
}
