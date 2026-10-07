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
            "원문의 의미, 경로, 상태, 작업 내용, 결과와 property를 보존하면서 문법과 JSON 구조를 정상화한다." +
            Environment.NewLine +
            "ACTION은 응답 envelope로 표현하고 JSON 본문은 해당 역할의 결과 필드를 유지한다." +
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

    private static string GetSchema(string role) =>
        role switch
        {
            "MANAGER" => """
                초기 분배:
                [ACTION=DISPATCH]
                {
                  "workItemIds": [10],
                  "mechanical": [
                    {
                      "operation": "BUILD",
                      "command": "기존 명령"
                    }
                  ]
                }

                일시정지:
                [ACTION=PAUSE]
                { "message": "기존 메시지" }

                최종 보고:
                [GOTO : HQ]
                [ACTION=REPORT]
                {
                  "status": "completed 또는 partial 또는 blocked",
                  "content": "기존 결과와 판단 의견"
                }
                """,

            "WORK" => """
                [ACTION=RESULT]
                {
                  "status": "completed 또는 blocked",
                  "summary": "기존 수행 결과",
                  "changedPaths": [],
                  "issues": []
                }
                """,

            "QA" => """
                [ACTION=RESULT]
                {
                  "status": "passed 또는 issue",
                  "summary": "기존 조사 결과",
                  "changedPaths": [],
                  "issues": []
                }
                """,

            "HIGH" => """
                [ACTION=RESULT]
                {
                  "status": "verified 또는 modified 또는 incomplete",
                  "summary": "기존 검토 결과",
                  "changedPaths": [],
                  "issues": []
                }
                """,

            _ => "[ACTION=RESULT]\n{ \"status\": \"blocked\", \"summary\": \"기존 결과\" }"
        };
}
