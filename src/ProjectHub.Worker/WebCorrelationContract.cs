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
            "- 최종 응답 전체에 요청의 KEY 행을 정확히 그대로 포함한다." + Environment.NewLine +
            "- 기본 출력 순서는 KEY → ACTION → GOTO(필요한 경우) → 본문이다." + Environment.NewLine +
            "- KEY의 앞에 다른 출력이 생겨도 되지만 실제 의미 응답은 KEY 뒤에 둔다." + Environment.NewLine +
            "- 응답을 모두 작성한 뒤 별도 줄에 " + ResponseOkMarker + "를 출력한다." + Environment.NewLine +
            "- 수신 측은 " + ResponseOkMarker + "가 보이는 줄까지를 현재 응답으로 읽으므로 완료 표시 뒤에 의미 본문을 이어 쓰지 않는다." + Environment.NewLine +
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
