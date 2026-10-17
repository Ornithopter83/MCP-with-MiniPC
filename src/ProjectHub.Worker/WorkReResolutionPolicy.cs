namespace ProjectHub.Worker;

// One bounded self-review for an unfinished WORKITEM, not an extra role or a
// new report schema. The role's own parsed report is the only eligibility data.
internal static class WorkReResolutionPolicy
{
    private static readonly string[] OutstandingImplementationMarkers =
    {
        "미해결", "미완료", "미구현", "동작하지 않", "잔여 코드",
        "수정하지 못", "구현하지 못", "아직 구현",
        "아직 해결", "테스트 실패", "검증 실패",
        "not implemented", "not working",
        "unresolved", "incomplete implementation",
        "test failed", "tests failed", "not verified"
    };

    // An environment failure or a human approval is not fixed by replaying
    // the same sandbox command. Report it promptly as before.
    private static readonly string[] NonActionableMarkers =
    {
        "eperm", "eacces", "permission denied", "access denied",
        "index.lock", "권한 거부", "접근 거부",
        "환경 오류", "환경 장애", "인증서 저장소",
        "root certificate", "command not found",
        "실제 키보드", "실제 os 입력", "물리 입력",
        "사람 승인", "사람 검수", "육안 승인", "인수 승인",
        "human approval", "manual approval", "physical keyboard",
        "하드웨어 미확인",
        // Missing test infrastructure is reported to HIGH/HQ, not retried
        // as an unfinished implementation by the same WORKITEM.
        "미검증", "playmode 실행", "unity editor", "pipeline 인스턴스",
        "연결 도구가 없어", "도구를 찾을 수 없", "rg 부재",
        "검증하지 못", "테스트하지 못", "실행하지 못"
    };

    internal static bool IsEligible(int exitCode, RoleTextResult report)
    {
        if (exitCode != 0 || !report.IsValid)
            return false;

        if (report.Status is not ("blocked" or "in_progress" or "completed"))
            return false;

        var issues = report.Issues.Where(issue =>
            !string.IsNullOrWhiteSpace(issue) &&
            !string.Equals(issue.Trim(), "없음", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(issue.Trim(), "none", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(issue.Trim(), "no issues", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        bool IsNonActionable(string text) => NonActionableMarkers.Any(marker =>
            text.Contains(marker, StringComparison.OrdinalIgnoreCase));
        bool IsOutstanding(string text) => OutstandingImplementationMarkers.Any(marker =>
            text.Contains(marker, StringComparison.OrdinalIgnoreCase));

        // The AI's BLOCKED judgment belongs to HQ, never to an automatic
        // WORK replay. In-progress WORK with no source progress is likewise
        // returned to HQ. Only clearly unfinished implementation qualifies.
        if (report.Status == "blocked" || issues.Length == 0)
            return false;

        return issues.Any(issue => !IsNonActionable(issue) &&
                                   IsOutstanding(issue));
    }

    internal static string BuildPrompt(
        string firstReport,
        string workItemId)
    {
        return "동일 WORKITEM #" + workItemId +
               "의 작업 마무리 재해결이다. 새로운 WORKITEM이 아니다. " +
               "같은 세션의 기존 임무와 첫 번째 마무리 보고서에서 미해결이거나 " +
               "검증하지 못한 부분만 재검토해 해결하라. " +
               "단순히 완료 문구만 바꾸지 말고, 필요하면 허용된 기존 WRITE_PATH 안에서 " +
               "구현을 보완하고 실제 실행 가능한 검증을 수행하라. " +
               "같은 모델·세션·WORK_ID·WRITE_PATH를 유지한다. " +
               "환경 권한 오류나 사람의 수동 승인 사항은 억지로 반복하지 말고 명확히 보고하라. " +
               "이번 재해결은 최대 1회이며 미해결이면 blocked 또는 in_progress로 정직하게 보고하라. " +
               "결과는 기존 역할 계약의 [GOTO=WORKER] → @@REPORT → " +
               "<STATUS>/<SUMMARY>/<CHANGED_PATH>/<ISSUES> → " +
               "[RESPONSE=OK] 순서로만 갱신하라. " +
               "각 대괄호 표식과 @@REPORT는 반드시 독립된 줄에 적어라.\n\n" +
               "=== 첫 번째 마무리 보고서 ===\n" +
               firstReport.Trim() + "\n";
    }
}
