using System.Text.RegularExpressions;

namespace ProjectHub.Worker;

public enum WorkerAction { Continue, Pause, End, Hq }
public enum WorkerNextRole { Coordinator, Implementer, HighLevel, Judge, Web }
public sealed record WorkerRoute(WorkerAction? Action, WorkerNextRole? Next, string Body, string? Error = null);

/// <summary>Parses only the first control line and optional destination. The body is opaque to Worker.</summary>
public static class WorkerRouteContract
{
    public static WorkerRoute Parse(string? response, bool actionRequired)
    {
        var lines = (response ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var first = Array.FindIndex(lines, line => !string.IsNullOrWhiteSpace(line));
        if (first < 0) return new(null, null, string.Empty, "CONTROL_MISSING");
        var control = lines[first].Trim().TrimStart('\uFEFF');
        WorkerAction? action = null;
        var actionMatch = Regex.Match(control, @"^\[ACTION\s*=\s*(CONTINUE|PAUSE|END|HQ)\s*\]$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var nextMatch = Regex.Match(control, @"^\[NEXT\s*:\s*(COORDINATOR|IMPLEMENTER|HIGH_LEVEL|JUDGE|WEB)\s*\]$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (actionMatch.Success)
            action = actionMatch.Groups[1].Value.ToUpperInvariant() switch
            {
                "CONTINUE" => WorkerAction.Continue, "PAUSE" => WorkerAction.Pause,
                "END" => WorkerAction.End, _ => WorkerAction.Hq
            };
        else if (!nextMatch.Success || actionRequired)
            return new(null, null, string.Empty, "CONTROL_INVALID_FIRST_LINE");

        WorkerNextRole? next = null;
        var bodyStart = first + 1;
        if (action.HasValue)
        {
            while (bodyStart < lines.Length && string.IsNullOrWhiteSpace(lines[bodyStart])) bodyStart++;
            if (bodyStart < lines.Length)
            {
                var candidate = lines[bodyStart].Trim();
                nextMatch = Regex.Match(candidate, @"^\[NEXT\s*:\s*(COORDINATOR|IMPLEMENTER|HIGH_LEVEL|JUDGE|WEB)\s*\]$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                if (nextMatch.Success) bodyStart++;
            }
        }
        else if (nextMatch.Success) { bodyStart = first + 1; }
        if (nextMatch.Success)
            next = nextMatch.Groups[1].Value.ToUpperInvariant() switch
            {
                "COORDINATOR" => WorkerNextRole.Coordinator, "IMPLEMENTER" => WorkerNextRole.Implementer,
                "HIGH_LEVEL" => WorkerNextRole.HighLevel, "JUDGE" => WorkerNextRole.Judge, _ => WorkerNextRole.Web
            };
        var body = string.Join(Environment.NewLine, lines.Skip(bodyStart)).Trim();
        if (action == WorkerAction.Hq) next = WorkerNextRole.Coordinator;
        if (actionRequired && action is null) return new(null, next, body, "ACTION_MISSING");
        if (action == WorkerAction.Continue && next is null) return new(action, null, body, "NEXT_MISSING");
        if (action == WorkerAction.Continue && string.IsNullOrWhiteSpace(body)) return new(action, next, string.Empty, "BODY_MISSING");
        return new(action, next, body);
    }
}
