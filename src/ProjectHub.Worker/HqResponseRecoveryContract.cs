namespace ProjectHub.Worker;

internal static class HqResponseRecoveryContract
{
    public static bool TryRepairClosingTags(string original, out string repaired)
    {
        repaired = System.Text.RegularExpressions.Regex.Replace(
            original ?? string.Empty,
            @"(?m)^([ \t]*<([A-Z][A-Z0-9_]*)>[^\r\n<>]+)</([A-Z][A-Z0-9_]*)>([ \t]*)$",
            match => string.Equals(match.Groups[2].Value, match.Groups[3].Value,
                StringComparison.Ordinal)
                ? match.Groups[1].Value + "</>" + match.Groups[4].Value
                : match.Value);
        return !string.Equals(original, repaired, StringComparison.Ordinal);
    }

    public static string[] RecognizablePaths(string content) =>
        System.Text.RegularExpressions.Regex.Matches(
            content ?? string.Empty,
            @"(?m)^\s*<PATH>([^<>\r\n]+)</(?:PATH)?>\s*$")
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
            "이 요청 뒤에 함께 주입되는 현재 HQ 역할 계약 전문을 출력 기준으로 사용한다." +
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
