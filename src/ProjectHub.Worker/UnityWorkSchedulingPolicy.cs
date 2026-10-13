namespace ProjectHub.Worker;

/// <summary>
/// Independent source-only WORKs may run up to the user-selected limit.
/// Any WORK that can touch Unity Editor, scenes, prefabs or AssetDatabase
/// uses the same exclusive lane; waiting for that lane never consumes a
/// source-worker slot.
/// </summary>
internal static class UnityWorkSchedulingPolicy
{
    private static readonly string[] EditorOperations =
    {
        "unity cli", "unity mcp", "unity editor", "unityeditor.",
        "assetdatabase", "editor api", "playmode", "editmode",
        "unity recompile", "unity command", "unity eval",
        "씬 저장", "씬 배치", "프리팹 저장", "에셋 임포트",
        "에셋 import"
    };

    public static int MaxConcurrentWork(int configured, int runnableCount)
    {
        if (configured < 1)
            throw new ArgumentOutOfRangeException(nameof(configured));
        if (runnableCount < 0)
            throw new ArgumentOutOfRangeException(nameof(runnableCount));
        return Math.Min(configured, runnableCount);
    }

    internal static bool RequiresEditorLane(MilestoneWorkDefinition work)
    {
        // No declared source scope cannot be proven safe to parallelize.
        if (work.WritePaths.Count == 0)
            return true;

        if (EditorOperations.Any(marker =>
                work.Body.Contains(marker, StringComparison.OrdinalIgnoreCase)))
            return true;

        return work.WritePaths.Any(path =>
        {
            var normalized = path.Replace('\\', '/').Trim('/');
            // Only ordinary C# gameplay source files are an unambiguous
            // editor-free lane. Folders, Editor scripts and other assets stay
            // exclusive, including read-only Editor operations.
            return !normalized.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) ||
                   !normalized.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
                   normalized.Split('/').Any(segment =>
                       segment.Equals("Editor", StringComparison.OrdinalIgnoreCase));
        });
    }

    internal static async Task<TResult[]> RunAsync<TResult>(
        IReadOnlyList<MilestoneWorkDefinition> workItems,
        int configured,
        Func<MilestoneWorkDefinition, CancellationToken, Task<TResult>> execute,
        CancellationToken cancellationToken,
        Action<int>? activeChanged = null,
        string? projectRoot = null)
    {
        ArgumentNullException.ThrowIfNull(workItems);
        ArgumentNullException.ThrowIfNull(execute);
        var parallelism = MaxConcurrentWork(configured, workItems.Count);
        if (parallelism == 0)
            return Array.Empty<TResult>();

        using var workers = new SemaphoreSlim(parallelism);
        var active = 0;

        async Task<TResult> RunOneAsync(MilestoneWorkDefinition work)
        {
            // Take the shared project-wide Editor lease before a worker slot:
            // waiting Editor WORKs must not block independent source WORKs.
            using var editorLease = RequiresEditorLane(work)
                ? await UnityEditorOperationGate.EnterAsync(
                    projectRoot ?? ".", cancellationToken).ConfigureAwait(false)
                : null;
            var workerSlot = false;
            try
            {
                // Acquire Editor first: an Editor waiter must not occupy a
                // concurrency slot needed by an independent C# source WORK.
                await workers.WaitAsync(cancellationToken).ConfigureAwait(false);
                workerSlot = true;
                activeChanged?.Invoke(Interlocked.Increment(ref active));
                try
                {
                    return await execute(work, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    activeChanged?.Invoke(Interlocked.Decrement(ref active));
                }
            }
            finally
            {
                if (workerSlot)
                    workers.Release();
            }
        }

        return await Task.WhenAll(workItems.Select(RunOneAsync)).ConfigureAwait(false);
    }
}
