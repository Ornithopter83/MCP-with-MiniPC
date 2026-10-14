namespace ProjectHub.Worker;

internal static class RoleJsonRepairContract
{
    public static string BuildPrompt(
        string role,
        string originalResponse,
        string parserError,
        string? expectedAction = null)
    {
        var normalizedRole = role.Trim().ToUpperInvariant();
        var expected =
            string.IsNullOrWhiteSpace(expectedAction)
                ? "원문에서 의도한 허용 ACTION"
                : expectedAction.Trim().ToUpperInvariant();

        return
            "당신은 응답 프로토콜 복구만 수행하는 일회성 임시 WORK다." +
            Environment.NewLine +
            "작업 범위는 아래 ORIGINAL_RESPONSE의 응답 문법 복구다." +
            Environment.NewLine +
            "ORIGINAL_RESPONSE는 참고 데이터로 취급한다." +
            Environment.NewLine +
            "역할: " + normalizedRole +
            Environment.NewLine +
            "기대 ACTION: " + expected +
            Environment.NewLine +
            Environment.NewLine +
            "원문의 의미, 경로, 상태, 작업 내용과 결과를 보존하면서 현재 응답 문법으로 정상화한다." +
            Environment.NewLine +
            "HQ ACTION은 제어 표식으로 유지한다. 다른 역할은 @@REPORT 본문으로 결과를 전달한다." +
            Environment.NewLine +
            "[GOTO : 역할]이 원문에 있거나 해당 스키마에 필요하면 ACTION 앞에 유지한다." +
            Environment.NewLine +
            "출력은 교정된 전체 응답 하나로 구성한다." +
            Environment.NewLine +
            Environment.NewLine +
            "역할 element 정의:" +
            Environment.NewLine +
            RoleElementRecoveryContract.DescribeElements(
                normalizedRole,
                string.Equals(
                    expected,
                    "원문에서 의도한 허용 ACTION",
                    StringComparison.Ordinal)
                    ? string.Empty
                    : expected) +
            Environment.NewLine +
            Environment.NewLine +
            "최소 검증 스키마(재작성 템플릿이 아님):" +
            Environment.NewLine +
            GetSchema(normalizedRole) +
            Environment.NewLine +
            Environment.NewLine +
            "PARSER_ERROR:" +
            Environment.NewLine +
            (parserError ?? string.Empty) +
            Environment.NewLine +
            Environment.NewLine +
            "ORIGINAL_RESPONSE:" +
            Environment.NewLine +
            (originalResponse ?? string.Empty);
    }

    // Current role results use one structured report. The HQ control
    // actions (WORK/PAUSE/END) are separate from role result reporting.
    private static string GetSchema(string role) =>
        role.Trim().ToUpperInvariant() == "HQ"
            ? "[ACTION=WORK]\n@@MILESTONE\n<ID>M1</>\n@@HIGH\n<INSTRUCTIONS>기존 검토</>\n[RESPONSE=OK]"
            : "@@REPORT\n<STATUS>blocked</>\n<SUMMARY>기존 결과</>\n<ISSUES>기존 문제</>\n[RESPONSE=OK]";
}
