namespace ProjectHub.Worker;

internal static class HqResponseRecoveryContract
{
    public static string[] RecognizablePaths(string content) =>
        System.Text.RegularExpressions.Regex.Matches(
            content ?? string.Empty,
            @"(?m)^\s*<(?:PATH|WRITE_PATH)>([^<>\r\n]+)</(?:PATH|WRITE_PATH)?>\s*$")
            .Select(x => x.Groups[1].Value.Trim()).ToArray();

    public static bool PreservesPaths(string previous, string next)
    {
        var existing = RecognizablePaths(previous);
        return existing.Length == 0 ||
            existing.SequenceEqual(RecognizablePaths(next), StringComparer.Ordinal);
    }

    public static string BuildBody(
        string originalResponse,
        string parserError)
    {
        return
            "이전 HQ 응답의 형식을 복구하기 위한 전체 응답 재요청이다." +
            Environment.NewLine +
            "이전 HQ 응답과 파싱 오류는 참고 데이터로 사용한다. 기존 WORK ID 및 PATH는 삭제하거나 변경하지 않는다." +
            Environment.NewLine +
            "처음 사용자 명령에서 받은 HQ 역할 계약을 유지하고 출력 형식만 복구한다." +
            Environment.NewLine +
            "이전 설계 의도를 참고하여 현재 HQ 계약에 맞는 완결된 전체 응답을 처음부터 한 번 다시 출력한다." +
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
