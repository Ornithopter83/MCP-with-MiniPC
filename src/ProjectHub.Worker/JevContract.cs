using System.IO;
using System.Text.RegularExpressions;

namespace ProjectHub.Worker;

public enum NextRoute { Invalid, Web, Jev }
public sealed record NextDirective(NextRoute Route, string Body, string? Error = null);

public static class JevContract
{
    private static readonly Regex NextPattern = new(@"^\[NEXT\s*:\s*(WEB|JEV)\]$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static NextDirective ParseNext(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return new(NextRoute.Invalid, string.Empty, "Codex 결과가 비어 있습니다.");
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var first = lines.Select((value, index) => (value.Trim(), index)).FirstOrDefault(item => item.Item1.Length > 0);
        if (string.IsNullOrWhiteSpace(first.Item1)) return new(NextRoute.Invalid, string.Empty, "Codex 결과에 유효한 NEXT 행이 없습니다.");
        var match = NextPattern.Match(first.Item1);
        if (!match.Success) return new(NextRoute.Invalid, string.Empty, "Codex 결과 첫 유효행이 [NEXT : WEB] 또는 [NEXT : JEV]가 아닙니다.");
        var body = string.Join(Environment.NewLine, lines.Skip(first.index + 1)).Trim();
        return match.Groups[1].Value.Equals("WEB", StringComparison.OrdinalIgnoreCase)
            ? new(NextRoute.Web, body)
            : new(NextRoute.Jev, body);
    }

    public static string ExtractValidationRequest(string body)
    {
        var marker = body.IndexOf("[VALIDATION REQUEST]", StringComparison.OrdinalIgnoreCase);
        return marker < 0 ? string.Empty : body[(marker + "[VALIDATION REQUEST]".Length)..].Trim();
    }

    public static string LoadFooter()
    {
        using var stream = typeof(JevContract).Assembly.GetManifestResourceStream("ProjectHub.Worker.JEV-FOOTER-CONTRACT.md")
            ?? throw new FileNotFoundException("Embedded JEV footer contract was not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Trim();
    }
}


