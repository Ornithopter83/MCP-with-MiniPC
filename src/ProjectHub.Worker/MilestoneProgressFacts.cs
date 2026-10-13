namespace ProjectHub.Worker;

/// <summary>
/// Observation only. Does not change scheduling, validation, HQ decisions,
/// checkpoint state or Git finalization. A path in the finalization set is
/// not necessarily a new/committed file, so we label it a candidate.
/// </summary>
internal static class MilestoneProgressFacts
{
    internal static string Format(
        string milestoneId,
        TimeSpan elapsed,
        IReadOnlyCollection<string> scheduledWorkIds,
        IReadOnlyDictionary<string, string> checkpointStates,
        IReadOnlyCollection<string> gitCandidatePaths,
        bool gitSucceeded,
        string? qaReportedStatus,
        string? highReportedStatus)
    {
        var statuses = scheduledWorkIds
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(id => checkpointStates.TryGetValue(id, out var state)
                ? state : "NOT_RECORDED")
            .ToArray();
        var paths = gitCandidatePaths
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        static int HasExtension(IEnumerable<string> candidates, string extension) =>
            candidates.Count(path => path.EndsWith(extension,
                StringComparison.OrdinalIgnoreCase));

        return "MILESTONE_ID: " + milestoneId + "\n" +
               "OBSERVED_DURATION_SECONDS: " +
               Math.Max(0, (long)elapsed.TotalSeconds) + "\n" +
               "SCHEDULED_WORK_COUNT: " + statuses.Length + "\n" +
               "WORK_CHECKPOINT_COMPLETED: " +
               statuses.Count(state => state == "COMPLETED") + "\n" +
               "WORK_CHECKPOINT_IN_PROGRESS: " +
               statuses.Count(state => state is "RUNNING" or "IN_PROGRESS") + "\n" +
               "WORK_CHECKPOINT_BLOCKED: " +
               statuses.Count(state => state is "BLOCKED" or "CANCELED") + "\n" +
               "WORK_CHECKPOINT_NOT_RECORDED: " +
               statuses.Count(state => state == "NOT_RECORDED") + "\n" +
               "GIT_FINALIZATION_SUCCEEDED: " + gitSucceeded + "\n" +
               "GIT_CANDIDATE_PATH_COUNT: " + paths.Length + "\n" +
               "GIT_CANDIDATE_SCENE_COUNT: " + HasExtension(paths, ".unity") + "\n" +
               "GIT_CANDIDATE_CS_COUNT: " + HasExtension(paths, ".cs") + "\n" +
               "GIT_CANDIDATE_META_COUNT: " + HasExtension(paths, ".meta") + "\n" +
               "QA_REPORTED_STATUS: " + (string.IsNullOrWhiteSpace(qaReportedStatus)
                   ? "NOT_RECORDED" : qaReportedStatus) + "\n" +
               "HIGH_REPORTED_STATUS: " + (string.IsNullOrWhiteSpace(highReportedStatus)
                   ? "NOT_RECORDED" : highReportedStatus) + "\n" +
               "TEST_EXECUTED_COUNT: NOT_MACHINE_RECORDED" + "\n" +
               "NOTE: candidate paths and reported statuses are observations, " +
               "not proof of gameplay completion or test passage.";
    }
}
