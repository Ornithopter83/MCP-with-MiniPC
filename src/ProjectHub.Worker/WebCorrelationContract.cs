using System.Security.Cryptography;

namespace ProjectHub.Worker;

internal static class WebCorrelationContract
{
    public const int KeyLength = 13;
    public const string ResponseOkMarker = "[RESPONSE=OK]";
    private const string Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

    public static string CreateKey()
    {
        Span<char> buffer = stackalloc char[KeyLength];
        for (var index = 0; index < buffer.Length; index++)
            buffer[index] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        return new string(buffer);
    }

    public static bool IsValidKey(string? key)
        => key is { Length: KeyLength } && key.All(char.IsLetterOrDigit);

    public static string Marker(string key)
    {
        if (!IsValidKey(key))
            throw new ArgumentException("Web correlation KEY는 13자리 영숫자여야 합니다.", nameof(key));
        return $"[KEY={key}]";
    }

    public static string WrapPrompt(string prompt, string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        var marker = Marker(key);
        return
            marker + Environment.NewLine +
            prompt.Trim() + Environment.NewLine + Environment.NewLine +
            "Web 요청 상관 규약:" + Environment.NewLine +
            "- 최종 응답의 첫 줄에 요청의 KEY 행을 정확히 그대로 출력한다." + Environment.NewLine +
            "- KEY 바로 다음 의미 행은 필요하면 [GOTO : 역할], 그 다음은 [ACTION=...]이어야 한다." + Environment.NewLine +
            "- ACTION 이후 본문은 현재 역할 계약의 포맷을 따른다. HQ는 JSON이 아니라 고정 필드와 @@SECTION 평문 프로토콜을 사용한다." + Environment.NewLine +
            "- HQ 형식 복구에서도 JSON, order, QA 지시, MANAGER, mechanicalInstructions, highInstructions, validation 구형 필드를 사용하지 않는다." + Environment.NewLine +
            "- BODY_BEGIN/BODY_END/END_ACTION, Markdown 코드펜스를 추가하지 않는다." + Environment.NewLine +
            "- 응답을 모두 작성한 뒤 별도 마지막 줄에 " + ResponseOkMarker + "를 출력한다." + Environment.NewLine +
            "- 수신 측은 현재 KEY부터 " + ResponseOkMarker + "까지를 하나의 응답으로 상관한다." + Environment.NewLine +
            "- " + ResponseOkMarker + " 뒤에는 의미 내용을 이어 쓰지 않는다." + Environment.NewLine +
            "- KEY를 변경하거나 다른 KEY로 대체하지 않는다.";
    }

    public static bool TryExtractResponse(string? rawResponse, string key, out string response)
    {
        response = string.Empty;
        if (!IsValidKey(key) || string.IsNullOrWhiteSpace(rawResponse))
            return false;

        var marker = Marker(key);
        var index = rawResponse.LastIndexOf(marker, StringComparison.Ordinal);
        if (index < 0)
            return false;

        var correlated = rawResponse[(index + marker.Length)..]
            .TrimStart(' ', '\t', '\r', '\n');
        var lines = correlated.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');
        var completionLine = Array.FindIndex(
            lines,
            line => string.Equals(line.Trim(), ResponseOkMarker, StringComparison.Ordinal));
        if (completionLine < 0)
            return false;

        response = string.Join(Environment.NewLine, lines.Take(completionLine)).TrimEnd();
        return true;
    }
}
