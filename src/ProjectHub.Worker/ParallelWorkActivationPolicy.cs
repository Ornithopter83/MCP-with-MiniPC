namespace ProjectHub.Worker;

public static class ParallelWorkActivationPolicy
{
    public static bool ShouldUseParallel(
        bool isContinuation,
        int maxConcurrentWork,
        bool hasPersistedWorkGraph)
    {
        if (maxConcurrentWork is < WorkGraph.MinimumConcurrency or > WorkGraph.MaximumConcurrency)
            throw new ArgumentOutOfRangeException(
                nameof(maxConcurrentWork),
                $"동시 WORK 수는 {WorkGraph.MinimumConcurrency}~{WorkGraph.MaximumConcurrency} 범위여야 합니다.");

        if (!isContinuation)
            return true;

        if (hasPersistedWorkGraph)
            return true;

        return maxConcurrentWork > 1;
    }
}
