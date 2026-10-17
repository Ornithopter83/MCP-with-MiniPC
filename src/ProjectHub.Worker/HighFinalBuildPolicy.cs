namespace ProjectHub.Worker;

/// <summary>
/// Final build validation after HIGH directly changes integration inputs.
/// No specialist handoff or additional review round.
/// </summary>
internal static class HighFinalBuildPolicy
{
    internal static string InvalidateCompletion(string highReport, string failure)
    {
        if (!string.Equals(MilestoneDefinitionContract.ReadHighStatus(highReport),
                "completed", StringComparison.OrdinalIgnoreCase))
            return highReport;
        return RoleTextProtocol.BuildResult("blocked",
            "HIGH_REVIEW_INVALIDATED_BY_BUILD_FAILURE",
            issues: new[] { failure });
    }

    internal static bool RequiresBuild(IEnumerable<string> changedPaths) =>
        changedPaths.Any(RequiresBuild);

    private static bool RequiresBuild(string path)
    {
        var normalized = path.Replace('\\', '/');
        // Recompile only for source/assembly/build-configuration inputs.
        // Scene, prefab, material, visual kit, texture and their sidecars
        // require targeted HIGH/Unity checks, not an unrelated C# recompile.
        if (normalized.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("ProjectSettings/", StringComparison.OrdinalIgnoreCase))
            return true;
        return new[] { ".cs", ".asmdef", ".asmref", ".dll", ".rsp" }
            .Any(extension => normalized.EndsWith(extension,
                StringComparison.OrdinalIgnoreCase)) ||
            new[] { ".asmdef.meta", ".asmref.meta", ".dll.meta" }
            .Any(extension => normalized.EndsWith(extension,
                StringComparison.OrdinalIgnoreCase));
    }
}
