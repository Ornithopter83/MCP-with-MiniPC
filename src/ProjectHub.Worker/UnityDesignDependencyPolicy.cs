namespace ProjectHub.Worker;

/// <summary>
/// Failure of DESIGN cannot silently validate an output-dependent WORK.
/// Independent implementation and investigation may still proceed.
/// </summary>
internal static class UnityDesignDependencyPolicy
{
    internal static IReadOnlySet<string> BlockedWorkIds(
        IEnumerable<MilestoneWorkDefinition> workItems, bool designCompleted) =>
        designCompleted ? new HashSet<string>(StringComparer.OrdinalIgnoreCase) :
        workItems.Where(work => work.DependsOnDesign)
            .Select(work => work.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
}
