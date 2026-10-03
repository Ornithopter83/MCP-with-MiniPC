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
                "ProjectHub 병렬 WORK는 기존 Git 저장소를 요구합니다.");

        if (string.IsNullOrWhiteSpace(target.RepositoryUrl))
            return Fail(
                "PARALLEL_GIT_REMOTE_REQUIRED",
                "ProjectHub 병렬 WORK는 origin 원격 저장소를 필수로 사용합니다.");

        if (!GitRemoteAddressPolicy.IsNetworkRemote(target.RepositoryUrl))
            return Fail(
                "PARALLEL_GIT_NETWORK_REMOTE_REQUIRED",
                "ProjectHub 병렬 WORK는 로컬 경로가 아닌 네트워크 Git origin을 요구합니다.");

        if (string.IsNullOrWhiteSpace(target.HeadSha))
            return Fail(
                "PARALLEL_GIT_HEAD_REQUIRED",
                "WORK 기준으로 사용할 원격 동기화 Git HEAD commit을 확인할 수 없습니다.");

        if (string.IsNullOrWhiteSpace(target.Branch) ||
            string.Equals(target.Branch.Trim(), "HEAD", StringComparison.OrdinalIgnoreCase))
        {
            return Fail(
                "PARALLEL_GIT_ATTACHED_BRANCH_REQUIRED",
                "원격 기준점을 사용하려면 현재 작업공간이 branch에 연결되어 있어야 합니다.");
        }

        return new ParallelWorkGitPreflightResult(
            true,
            null,
            $"remote={target.RepositoryUrl.Trim()} branch={target.Branch.Trim()} head={target.HeadSha.Trim()}");
    }

    private static ParallelWorkGitPreflightResult Fail(string errorCode, string message)
        => new(false, errorCode, message);
}
