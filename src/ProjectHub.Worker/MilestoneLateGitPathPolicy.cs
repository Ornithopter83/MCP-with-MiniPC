namespace ProjectHub.Worker;

/// <summary>
/// Restricts late Unity-generated Git additions to sidecars of milestone-owned files.
/// Roles still report their own source changes through the normal milestone path journal.
/// </summary>
internal static class MilestoneLateGitPathPolicy
{
    internal static IReadOnlyList<string> SelectSidecars(
        IEnumerable<string> dirtyPaths,
        IReadOnlySet<string> initiallyDirtyPaths,
        IReadOnlySet<string> milestonePaths)
    {
        return dirtyPaths
            .Where(path =>
                !initiallyDirtyPaths.Contains(path) &&
                path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase) &&
                milestonePaths.Contains(path[..^5]))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
