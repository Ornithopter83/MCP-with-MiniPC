namespace ProjectHub.Worker;

/// <summary>
/// Deterministic Worker transitions after a HIGH-requested DESIGN revision.
/// A failed no-op does not consume another HIGH review; changed integration
/// inputs must be rebuilt before claims of successful verification.
/// </summary>
internal static class HighDesignContinuationPolicy
{
    internal static bool ShouldResumeHigh(
        string designReport, IReadOnlyCollection<string> changedPaths)
    {
        var parsed = RoleTextProtocol.ParseWork(designReport);
        return parsed.IsValid && parsed.Status == "completed" ||
               changedPaths.Count > 0;
    }

    internal static bool RequiresBuild(IEnumerable<string> changedPaths) =>
        changedPaths.Any(RequiresBuild);

    private static bool RequiresBuild(string path)
    {
        var normalized = path.Replace('\\', '/');
        return normalized.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith("ProjectSettings/", StringComparison.OrdinalIgnoreCase) ||
               new[] { ".cs", ".asmdef", ".asmref", ".dll", ".unity",
                   ".prefab", ".shader", ".shadergraph", ".compute",
                   ".mat", ".meta", ".uxml", ".uss", ".controller", ".anim" }
               .Any(extension => normalized.EndsWith(extension,
                   StringComparison.OrdinalIgnoreCase));
    }
}
