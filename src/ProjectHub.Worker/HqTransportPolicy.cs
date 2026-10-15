namespace ProjectHub.Worker;

/// <summary>Keep HQ transport-specific initialization and investigation separate.</summary>
internal static class HqTransportPolicy
{
    internal static bool NeedsManagedBrowser(WorkerAiRoleSettings coordinator)
        => string.Equals(coordinator.Transport, "web",
            StringComparison.OrdinalIgnoreCase);

    internal static string BuildRepositoryGuidance(
        bool web, bool remoteAvailable, string inboundType = "USER_REQUEST")
    {
        var investigationScope = inboundType switch
        {
            "HQ_RESPONSE_RECOVERY" =>
                "응답 문법 복구: 이미 수행한 조사나 검증을 다시 실행하지 말고 기존 판단의 문법만 복구하라. ",
            "USER_REQUEST" =>
                "신규 작업 최초 호출: 관련 설계·구현·증거를 필요한 범위에서 조사하라. ",
            _ =>
                "후속 HQ 호출: 이미 확인한 전체 프로젝트를 다시 조사하지 말고 이번 보고서, 변경 파일, 미해결 항목만 확인하라. "
        };

        if (web)
            return remoteAvailable
                ? investigationScope + "Worker가 origin/main을 갱신했다. Web에서는 제공된 원격 저장소 URL의 main과 전달된 마일스톤 증거를 직접 조사하라. 로컬 파일·CLI 실행이 가능한 것으로 가정하지 마라."
                : investigationScope + "Worker가 origin/main을 검증하지 못했다. Web에서는 제공된 원격 저장소와 전달된 증거로 확인 가능한 범위만 판단하고, 불가능한 부분은 미확인으로 보고하라.";

        return remoteAvailable
            ? investigationScope + "Worker가 origin/main을 갱신했다. CLI HQ는 실제 로컬 프로젝트에서 git status, git log, git show, git diff 및 파일 검색·읽기로 현재 변경·구현 상태를 직접 조사하라. 필요할 때만 비파괴적 로컬 진단 명령이나 읽기 전용 검증을 실행하되, 같은 검사를 관례적으로 반복하지 마라. 읽기 전용 샌드박스에서 쓰기가 필요한 빌드·테스트를 우회하지 말고 WORK/HIGH에 요청하라. HQ는 프로젝트·Git·Unity Editor 상태를 수정하지 마라."
            : investigationScope + "Worker가 origin/main을 검증하지 못했다. CLI HQ는 로컬 Git·파일·Unity CLI 연결 상태를 읽기 전용 명령으로 직접 조사하되, 확인되지 않은 원격 상태를 최신으로 가정하지 마라. 비파괴적 진단으로 해결하지 못하면 PAUSE 여부를 판단하라. HQ는 프로젝트·Git·Unity Editor 상태를 수정하지 마라.";
    }
}
