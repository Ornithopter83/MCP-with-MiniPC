using System.Text.RegularExpressions;

namespace ProjectHub.Worker;

/// <summary>Evidence-bound, nonterminal HIGH => DESIGN request.</summary>
internal sealed record HighDesignHandoff(
    string Goal, string EvidencePath, string TargetPath,
    string ExpectedResult, string Reason);

internal static class HighDesignHandoffContract
{
    internal const int MaxReviewHandoffs = 2;

    internal static bool TryRead(string report, out HighDesignHandoff? handoff, out string error)
    {
        handoff = null;
        error = string.Empty;
        var parsed = RoleTextProtocol.ParseHigh(report);
        if (!parsed.IsValid || parsed.Status != "design_handoff")
        {
            error = "HIGH_NOT_VALID_HANDOFF";
            return false;
        }
        string Field(string name)
        {
            var match = Regex.Match(report, @"(?s)<" + name + @">\s*(.*?)\s*</>",
                RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value.Trim() : string.Empty;
        }
        var goal = Field("DESIGN_GOAL");
        var evidence = Field("EVIDENCE_PATH");
        var target = Field("TARGET_PATH");
        var expected = Field("EXPECTED_RESULT");
        var reason = Field("REASON");
        if (goal.Length is < 8 or > 2000 || expected.Length is < 8 or > 2000 ||
            reason.Length is < 8 or > 2000 ||
            !MilestoneDefinitionContract.IsSafeRelativePath(evidence) ||
            !MilestoneDefinitionContract.IsSafeRelativePath(target))
        {
            error = "HIGH_DESIGN_HANDOFF_FIELDS_INVALID";
            return false;
        }
        handoff = new(goal, evidence, target, expected, reason);
        return true;
    }
}
