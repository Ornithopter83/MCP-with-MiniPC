using System.Text.RegularExpressions;

namespace ProjectHub.Worker;

/// <summary>
/// Shared parser for model-authored routing. Recognizes [GOTO=DESIGN] and
/// [GOTO : DESIGN] as equivalent; rejects unknown and conflicting targets.
/// Only standalone directives preceding ACTION are control instructions.
/// </summary>
internal static class RoleGotoContract
{
    private static readonly Regex GotoLine = new(
        @"^\[\s*GOTO\s*[:=]\s*(?<target>[^\]\r\n]+?)\s*\]$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    internal static bool IsGotoLine(string? value) =>
        Regex.IsMatch((value ?? "").Trim(), @"^\[\s*GOTO\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    internal static bool TryReadLine(string line, out string target)
    {
        target = "";
        var match = GotoLine.Match(line.Trim());
        if (!match.Success) return false;
        var normalized = Regex.Replace(match.Groups["target"].Value.Trim(),
            @"[\s-]+", "_").ToUpperInvariant();
        if (normalized is not ("HQ" or "WORKER" or "WORK" or "HIGH" or
            "DESIGN" or "ASSET_MANAGER" or "MANAGER"))
            return false;
        target = normalized;
        return true;
    }

    internal static bool TryReadLeading(string? raw, out string? destination,
        out string error)
    {
        destination = null;
        error = "";
        foreach (var line in (raw ?? "").Replace("\r\n", "\n",
                     StringComparison.Ordinal).Replace('\r', '\n').Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 ||
                trimmed.StartsWith("[KEY=", StringComparison.OrdinalIgnoreCase))
                continue;
            if (trimmed.StartsWith("[ACTION=", StringComparison.OrdinalIgnoreCase) ||
                trimmed == "@@REPORT" ||
                trimmed.StartsWith("STATUS:", StringComparison.OrdinalIgnoreCase))
                break;
            if (!IsGotoLine(trimmed)) break;
            if (!TryReadLine(trimmed, out var candidate))
            {
                error = "GOTO_INVALID";
                return false;
            }
            if (destination is not null && destination != candidate)
            {
                error = "GOTO_CONFLICT";
                return false;
            }
            destination = candidate;
        }
        return true;
    }

    internal static string Format(string destination)
    {
        if (!TryReadLine("[GOTO=" + destination + "]", out var normalized))
            throw new ArgumentException("Unsupported GOTO target", nameof(destination));
        return "[GOTO=" + normalized + "]";
    }
}
