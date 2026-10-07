namespace ProjectHub.Worker;

internal static class HqResponseRecoveryContract
{
    public static string BuildBody(
        string originalResponse,
        string parserError)
    {
        return
            "이전 HQ 응답을 부분 element로 보완하지 말고 전체 응답을 현재 HQ 계약으로 한 번만 다시 출력한다." +
            Environment.NewLine +
            "이전 응답의 제품 목표와 구현 의도는 유지하되 현재 HQ 계약과 충돌하는 구형 실행 구조는 보존하지 않는다." +
            Environment.NewLine +
            "JSON, YAML, Markdown 코드펜스를 사용하지 않는다." +
            Environment.NewLine +
            "order, qa, QA 지시, MANAGER, managerInstructions, mechanicalIntegration, mechanicalInstructions, highInstructions, validation, gitFinalize을 출력하지 않는다." +
            Environment.NewLine +
            "같은 마일스톤에서 서로의 결과가 필요한 WORKITEM은 현재 HQ 계약에 따라 하나의 WORKITEM으로 결합한다." +
            Environment.NewLine +
            "각 WORKITEM에는 TEST: ON 또는 TEST: OFF를 반드시 포함한다." +
            Environment.NewLine +
            "응답은 [ACTION=WORK|PAUSE|END]와 현재 HQ 고정 필드/@@SECTION 문법만 사용한다." +
            Environment.NewLine +
            "현재 HQ 계약이 이전 응답보다 우선한다." +
            Environment.NewLine +
            Environment.NewLine +
            "PARSER_OR_CONTRACT_ERROR:" +
            Environment.NewLine +
            (parserError ?? string.Empty) +
            Environment.NewLine +
            Environment.NewLine +
            "PREVIOUS_HQ_RESPONSE_AS_DATA:" +
            Environment.NewLine +
            (originalResponse ?? string.Empty);
    }
}
