using System.Text.RegularExpressions;

namespace ProjectHub.Worker;

public enum LegacyWebActionKind { None, Continue, Pause, End, ProtocolError }
public sealed record LegacyWebAction(LegacyWebActionKind Kind, string Body, string? Error = null);

public static class LegacyWebActionContract
{
    private static readonly Regex ActionPattern = new(@"^\[ACTION\s*=\s*(CONTINUE|PAUSE|END)\s*\]$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static LegacyWebAction Parse(string? response, bool strict = false)
    {
        if (string.IsNullOrWhiteSpace(response)) return InvalidOrNone("ACTION 응답이 비어 있습니다.", strict);
        var lines = response.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var first = Array.FindIndex(lines, line => !string.IsNullOrWhiteSpace(line));
        if (first < 0) return InvalidOrNone("ACTION 응답이 비어 있습니다.", strict);
        var match = ActionPattern.Match(lines[first].Trim());
        if (!match.Success) return InvalidOrNone("첫 유효행에 유효한 ACTION이 없습니다.", strict);
        var kind = Enum.Parse<LegacyWebActionKind>(match.Groups[1].Value, true);
        var body = string.Join(Environment.NewLine, lines.Skip(first + 1)).Trim();
        if (kind == LegacyWebActionKind.Continue && body.Length == 0) return new(LegacyWebActionKind.ProtocolError, string.Empty, "CONTINUE 본문이 비어 있습니다.");
        return new(kind, body);
    }

    public static string BuildInstructions(bool judgeEnabled) =>
        "답변 첫 줄에서 다음 ACTION 중 하나를 사용하세요: [ACTION=CONTINUE], [ACTION=PAUSE], [ACTION=END]." + Environment.NewLine
        + "CONTINUE 뒤에는 Codex에 전달할 본문을 작성하세요. NEXT 경로는 [NEXT : WEB] 또는 [NEXT : JEV]만 허용됩니다." + Environment.NewLine
        + (judgeEnabled ? "JEV 검증이 필요하면 [NEXT : JEV]를 사용하세요." : "JEV 검증 경로는 현재 사용할 수 없습니다.");

    private static LegacyWebAction InvalidOrNone(string message, bool strict) => strict
        ? new(LegacyWebActionKind.ProtocolError, string.Empty, message)
        : new(LegacyWebActionKind.None, string.Empty);
}
