namespace ProjectHub.Worker;

public static class JevRetryPromptBuilder
{
    public static string Build(string partialResult, int attempt, int maximumAttempts = 3)
    {
        var current = Math.Clamp(attempt, 1, maximumAttempts);
        return $"[JEV PARTIAL REVIEW · {current}/{maximumAttempts}]" + Environment.NewLine +
            partialResult.Trim() + Environment.NewLine + Environment.NewLine +
            "JEV threshold 미달은 그 자체로 구현 결함의 증거가 아니다. 같은 Codex session에서 아래 순서로만 처리하라." + Environment.NewLine +
            "1. 각 미통과 질문의 QID, 주장, EVIDENCE, SCOPE, COUNTEREXAMPLE, 고정 PASS threshold를 확인하고 원인을 분류한다: 확인된 모순, 증거 부족, confidence 미달만 있음, 사용자 실검증 필요." + Environment.NewLine +
            "2. 실제 코드/결정적 검증에서 주장과 충돌하는 근거가 확인된 경우에만 그 QID와 관련된 최소 범위를 수정한다. 다른 코드와 이미 통과한 질문은 건드리지 않는다." + Environment.NewLine +
            "3. 증거가 부족하면 가능한 결정적 검증을 실행하고 해당 QID의 실제 결과를 보충한다. 경로만 나열하거나 실행하지 않은 검증을 만들지 않는다." + Environment.NewLine +
            "4. confidence 미달만 있고 새 모순이나 개선된 증거가 없으면 코드를 수정하지 않는다. 같은 질문과 같은 evidence를 그대로 반복하지 않는다." + Environment.NewLine +
            "5. threshold를 낮추지 않는다. evidence 변경의 영향을 받는 원자 질문만 [NEXT : JEV]에 다시 포함하고, 각 질문 앞의 [QID:C1] 같은 ID, 주장, 중요도, 범위, 반례, PASS 기준을 유지한다. 영향받지 않은 PASS 질문은 다시 보내지 않는다." + Environment.NewLine +
            "6. 새 증거를 만들 수 없거나 사용자 확인이 필요한 경우 [NEXT : WEB]과 [REPORT]로 PARTIAL 사유와 검토 필요 항목을 보고한다. 같은 작업의 PARTIAL 재검증은 최대 3회이며 상한 뒤에는 구현 완료로 표시하지 말고 검토로 넘긴다." + Environment.NewLine + Environment.NewLine +
            "응답은 기존 Footer의 [NEXT : WEB] 또는 [NEXT : JEV] 계약을 지킨다.";
    }
}
