namespace ProjectHub.Worker;

public sealed record ParallelWorkGitPreflightResult(
    bool Success,
    string? ErrorCode,
    string Message);

public static class ParallelWorkGitPreflight
{
    public static ParallelWorkGitPreflightResult Validate(GitTargetSnapshot target)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (!target.IsRepository)
            return Fail(
                "PARALLEL_GIT_REPOSITORY_REQUIRED",
                "병렬 WORK는 WorkItem별 Git worktree를 사용하므로 Git 저장소가 필요합니다.");

        if (string.IsNullOrWhiteSpace(target.HeadSha))
            return Fail(
                "PARALLEL_GIT_HEAD_REQUIRED",
                "병렬 WORK 기준으로 사용할 Git HEAD commit을 확인할 수 없습니다.");

        if (string.IsNullOrWhiteSpace(target.Branch) ||
            string.Equals(target.Branch.Trim(), "HEAD", StringComparison.OrdinalIgnoreCase))
        {
            return Fail(
                "PARALLEL_GIT_ATTACHED_BRANCH_REQUIRED",
                "Integration 결과를 안전하게 반영하려면 주 작업공간이 detached HEAD가 아닌 branch에 있어야 합니다.");
        }

        return new ParallelWorkGitPreflightResult(
            true,
            null,
            $"branch={target.Branch.Trim()} head={target.HeadSha.Trim()}");
    }

    private static ParallelWorkGitPreflightResult Fail(string errorCode, string message)
        => new(false, errorCode, message);
}
