namespace ProjectHub.Worker;

public static class WorkerUnknownErrorLog
{
    public static string CreateHandoffSummary(WorkerRoleState source, string code)
    {
        var sourceName = GetSourceName(source);
        var explanation = GetExplanation(code);
        return $"{sourceName}에서 작업을 이어가는 중 오류가 발생했습니다. {explanation} 오류 코드: {code}. 오류 원문과 상세 출력은 로그에만 보관되어 전달되지 않았습니다.";
    }

    public static string Format(WorkerRoleState source, string code, string detail)
    {
        var sourceName = GetSourceName(source);
        var explanation = GetExplanation(code);

        var original = string.IsNullOrWhiteSpace(detail) ? "(세부 내용 없음)" : detail.Trim();
        return $"{explanation}\n발생 단계: {sourceName}\n오류 코드: {code}\n세부 내용:\n{original}\n\n오류 원문과 상세 출력은 로그에만 기록하며 HQ에는 한글 요약만 전달합니다.";
    }

    private static string GetSourceName(WorkerRoleState source) => source switch
    {
        WorkerRoleState.Hq => "설계·관제 AI",
        WorkerRoleState.Work => "작업 AI",
        WorkerRoleState.Judge => "판단 AI",
        WorkerRoleState.High => "고수준 작업 AI",
        _ => "시스템"
    };

    private static string GetExplanation(string code) => code switch
    {
        "GOTO_INVALID" or "GOTO_INVALID_FIRST_LINE" or "GOTO_MISSING" => "AI 응답에서 올바른 전달 경로를 확인하지 못했습니다.",
        "ACTION_INVALID" or "ACTION_MISSING" => "AI 응답에서 올바른 작업 동작을 확인하지 못했습니다.",
        "SESSION_RESUME_FAILED" => "이전 AI 대화를 이어갈 세션을 확인하지 못했습니다.",
        "PROCESS_EXIT" => "AI 실행 프로세스가 오류와 함께 종료됐습니다.",
        "HIGH_NOT_AUTHORIZED" => "고수준 작업에 필요한 실행 허가가 없습니다.",
        "JUDGE_UNAVAILABLE" => "설정된 판단 AI를 사용할 수 없습니다.",
        "JUDGE_REQUEST_INVALID" => "판단 AI에 전달할 요청을 확인하지 못했습니다.",
        "JEV_RESPONSE_MISSING" => "판단 AI의 응답을 받지 못했습니다.",
        _ => "작업 중 오류가 발생했습니다."
    };
}
