namespace ProjectHub.Worker;

// One bounded self-review for an unfinished WORKITEM, not an extra role or a
// new report schema. The role's own parsed report is the only eligibility data.
internal static class WorkReResolutionPolicy
{
    private static readonly string[] OutstandingImplementationMarkers =
    {
        "미해결", "미완료", "미구현", "미수행", "미실행",
        "실행하지 않았", "실행하지 못", "테스트하지 않았",
        "검증하지 않았", "검증하지 못", "확인하지 못",
        "수정하지 못", "구현하지 못", "아직 구현",
        "아직 해결", "테스트 실패", "검증 실패",
        "not run", "not executed", "not implemented",
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
        "하드웨어 미확인", "qa에서 별도", "qa 단계에서 별도"
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

        if (report.Status == "completed")
        {
            // A clean completion, a warning about a recovered environment,
            // and an independent QA/manual acceptance gate must not retry.
            return issues.Any(issue => !IsNonActionable(issue) &&
                                       IsOutstanding(issue));
        }

        if (issues.Length > 0)
            return issues.Any(issue => !IsNonActionable(issue));

        // Explicit blocked/in_progress without a reason still describes an
        // unresolved implementation. Do not inspect summary for hidden new fields.
        return !IsNonActionable(report.Summary);
    }

    internal static string BuildPrompt(
        string originalMission,
        string firstReport,
        string workItemId)
    {
        return "동일 WORKITEM #" + workItemId +
               "의 작업 마무리 재해결이다. 새로운 WORKITEM이 아니다. " +
               "원래 임무와 첫 번째 마무리 보고서에서 실제로 미해결이거나 " +
               "검증하지 못한 부분만 재검토해 해결하라. " +
               "단순히 완료 문구만 바꾸지 말고, 필요하면 허용된 기존 WRITE_PATH 안에서 " +
               "구현을 보완하고 실제 실행 가능한 검증을 수행하라. " +
               "같은 모델·세션·WORK_ID·WRITE_PATH를 유지한다. " +
               "환경 권한 오류나 사람의 수동 승인 사항은 억지로 반복하지 말고 명확히 보고하라. " +
               "이번 재해결은 최대 1회이며 미해결이면 blocked 또는 in_progress로 정직하게 보고하라. " +
               "새 보고서 폼을 만들지 말고 기존 [ACTION=RESULT] @@REPORT 형식으로 " +
               "마무리 보고서를 갱신하라.\n\n" +
               "=== 원래 임무 ===\n" +
               originalMission.Trim() + "\n\n" +
               "=== 첫 번째 마무리 보고서 ===\n" +
               firstReport.Trim() + "\n";
    }
}
