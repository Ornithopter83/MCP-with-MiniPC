namespace ProjectHub.Worker;

/// <summary>
/// Phase 1 UnityWorker safety gate. Unity Editor and AssetDatabase state are shared;
/// do not allow parallel WORK turns before the exclusive editor lane exists.
/// The configured concurrency is retained for a future source-only lane.
/// </summary>
internal static class UnityWorkSchedulingPolicy
{
    public static int MaxConcurrentWork(int configured, int runnableCount)
    {
        if (configured < 1)
            throw new ArgumentOutOfRangeException(nameof(configured));
        if (runnableCount < 0)
            throw new ArgumentOutOfRangeException(nameof(runnableCount));
        return runnableCount == 0 ? 0 : 1;
    }
}
