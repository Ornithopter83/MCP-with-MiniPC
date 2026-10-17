namespace ProjectHub.Worker;

/// <summary>
/// Observation only. Does not change scheduling, validation, HQ decisions,
/// checkpoint state or Git finalization. A path in the finalization set is
/// not necessarily a new/committed file, so we label it a candidate.
/// </summary>
internal static class MilestoneProgressFacts
{
    // HQ receives a compact view of actual changed-path candidates, not merely
    // a commit SHA. These path classes are *not* validated gameplay progress.
    internal static string FormatGoalProgressCandidates(
        IEnumerable<string> candidatePaths)
    {
        var paths = candidatePaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path.Replace('\\', '/'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        static bool In(string path, string prefix) =>
            path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);

        var meta = paths.Count(path => path.EndsWith(
            ".meta", StringComparison.OrdinalIgnoreCase));
        var product = paths.Count(path =>
            !path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase) &&
            ((In(path, "Assets/") &&
              !In(path, "Assets/Tests/") &&
              !In(path, "Assets/ProjectHub/VisualFoundry/") &&
              !In(path, "Assets/_Recovery/")) ||
             In(path, "ProjectSettings/") || In(path, "Packages/")));
        var tests = paths.Count(path => In(path, "Assets/Tests/") &&
            !path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase));
        var visualKits = paths.Count(path =>
            In(path, "Assets/ProjectHub/VisualFoundry/") &&
            !path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase));
        var evidence = paths.Count(path =>
            In(path, "docs/") || In(path, "tools/"));

        return "GOAL_PROGRESS_CANDIDATES: total=" + paths.Length +
               " product_or_config=" + product +
               " tests=" + tests +
               " visual_kit_unverified=" + visualKits +
               " docs_or_tools=" + evidence +
               " meta=" + meta + "\n" +
               "GOAL_PROGRESS_CAUTION: 변경 후보 경로의 수는 실제 적용·" +
               "검증 성공·커밋 여부를 증명하지 않는다. 기존 목표/실패 조건과 " +
               "HIGH 증거를 비교해 실질 진전이 없으면 HQ가 PAUSE를 판단한다.";
    }

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
        // Legacy callers may still supply QA status; new observations never
        // display or schedule a separate QA stage.
        _ = qaReportedStatus;
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
               "MECHANICAL_VERIFICATION_OWNER: WORKER" + "\n" +
               "HIGH_REPORTED_STATUS: " + (string.IsNullOrWhiteSpace(highReportedStatus)
                   ? "NOT_RECORDED" : highReportedStatus) + "\n" +
               "TEST_EXECUTED_COUNT: NOT_MACHINE_RECORDED" + "\n" +
               "NOTE: candidate paths and reported statuses are observations, " +
               "not proof of gameplay completion or test passage.";
    }
}
