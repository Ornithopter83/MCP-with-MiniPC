using System.IO;
using System.Text.RegularExpressions;

namespace ProjectHub.Worker;

/// <summary>
/// Tolerant HIGH => DESIGN handoff. GOTO is routing; image evidence is
/// supplemental. Unsafe write targets never become executable.
/// </summary>
internal sealed record HighDesignHandoff(
    string Goal, string EvidencePath, string TargetPath,
    string ExpectedResult, string Reason)
{
    internal IReadOnlyList<string> EvidencePaths { get; init; } = Array.Empty<string>();
    internal IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}

internal static class HighDesignHandoffContract
{
    internal const int MaxReviewHandoffs = 2;

    /// <summary>
    /// Read only existing, inside-root evidence. Invalid paths and inaccessible
    /// files are omitted, not retried or interpreted as verified images.
    /// </summary>
    internal static IReadOnlyList<string> ExistingEvidencePaths(
        string projectRoot, IEnumerable<string> paths)
    {
        var valid = new List<string>();
        foreach (var path in paths)
        {
            if (path.Any(char.IsControl) ||
                !MilestoneDefinitionContract.IsSafeRelativePath(path))
                continue;
            try
            {
                var full = Path.GetFullPath(Path.Combine(projectRoot,
                    path.Replace('/', Path.DirectorySeparatorChar)));
                if (MilestoneDefinitionContract.IsPathInsideRoot(projectRoot, full) &&
                    File.Exists(full))
                    valid.Add(path);
            }
            catch (Exception e) when (e is ArgumentException or
                IOException or UnauthorizedAccessException or NotSupportedException)
            {
                // Preserve missing evidence as a warning, never promote it.
            }
        }
        return valid.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    internal static bool TryRead(string report, out HighDesignHandoff? handoff,
        out string error)
    {
        handoff = null;
        error = string.Empty;
        if (MilestoneDefinitionContract.ReadHighStatus(report) != "design_handoff")
        {
            error = "HIGH_NOT_VALID_HANDOFF";
            return false;
        }

        string Field(string name)
        {
            var match = Regex.Match(report,
                @"(?ims)^[ \t]*<" + name +
                @">\s*(.*?)(?:</>|</" + name +
                @">|(?=^[ \t]*<[A-Z_]+>))",
                RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value.Trim() : "";
        }

        var parsed = RoleTextProtocol.ParseHigh(report);
        var goal = Field("DESIGN_GOAL");
        if (goal.Length == 0) goal = parsed.Summary.Trim();
        var target = Field("TARGET_PATH").Replace('\\', '/').Trim();
        var expected = Field("EXPECTED_RESULT");
        if (expected.Length == 0) expected = goal;
        var reason = Field("REASON");
        if (reason.Length == 0) reason = "HIGH 인계 이유 미기재";
        if (goal.Length is < 4 or > 2000 ||
            expected.Length > 2000 || reason.Length > 2000 ||
            target.Contains(';') || target.Any(char.IsControl) ||
            !MilestoneDefinitionContract.IsSafeRelativePath(target))
        {
            error = "HIGH_DESIGN_HANDOFF_FIELDS_INVALID";
            return false;
        }

        var warnings = new List<string>();
        if (Field("DESIGN_GOAL").Length == 0)
            warnings.Add("DESIGN_GOAL_RECOVERED_FROM_SUMMARY");
        if (Field("EXPECTED_RESULT").Length == 0)
            warnings.Add("EXPECTED_RESULT_DEFAULTED_TO_GOAL");
        if (Field("REASON").Length == 0)
            warnings.Add("REASON_MISSING");

        var evidenceText = Field("EVIDENCE_PATH");
        var extraEvidence = Field("EVIDENCE_PATHS");
        if (extraEvidence.Length > 0)
            evidenceText += "\n" + extraEvidence;
        var parts = evidenceText.Split(new[] { ';', '\n', '\r', '|' },
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var valid = new List<string>();
        foreach (var part in parts)
        {
            var candidate = part.Trim().Trim('"', '\'', (char)96)
                .Replace('\\', '/');
            if (candidate.StartsWith("- ", StringComparison.Ordinal))
                candidate = candidate[2..].Trim();
            if (!candidate.Any(char.IsControl) &&
                MilestoneDefinitionContract.IsSafeRelativePath(candidate))
                valid.Add(candidate);
            else
                warnings.Add("EVIDENCE_UNSAFE_OR_INVALID_OMITTED");
        }
        var paths = valid.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (paths.Length == 0)
            warnings.Add("EVIDENCE_UNVERIFIED_NO_SAFE_PATH");
        handoff = new(goal, paths.FirstOrDefault() ?? "", target,
            expected, reason)
        {
            EvidencePaths = paths,
            Warnings = warnings
        };
        return true;
    }
}
