using System.Reflection;
using System.IO;
using System.Text.RegularExpressions;

namespace ProjectHub.Worker;

public enum NextRoute { Invalid, Web, Jev }
public sealed record NextDirective(NextRoute Route, string Body, string? Error = null);

/// <summary>Legacy GPT Web public wire only. New role routing uses ACTION/GOTO contracts.</summary>
public static class LegacyWebJevContract
{
    private static readonly Regex NextPattern = new(@"^\[NEXT\s*:\s*(WEB|JEV)\]$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static NextDirective ParseNext(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return new(NextRoute.Invalid, string.Empty, "CODEX_EMPTY");
        var lines = Normalize(text).Split('\n');
        var first = lines.Select((value, index) => (Value: value.Trim(), Index: index)).FirstOrDefault(item => item.Value.Length > 0);
        if (string.IsNullOrWhiteSpace(first.Value)) return new(NextRoute.Invalid, string.Empty, "NEXT_MISSING");
        var match = NextPattern.Match(first.Value);
        if (!match.Success) return new(NextRoute.Invalid, string.Empty, "NEXT_INVALID");
        var route = match.Groups[1].Value.Equals("WEB", StringComparison.OrdinalIgnoreCase) ? NextRoute.Web : NextRoute.Jev;
        var body = string.Join(Environment.NewLine, lines.Skip(first.Index + 1)).Trim();
        if (Normalize(body).Split('\n').Any(line => NextPattern.IsMatch(line.Trim()))) return new(NextRoute.Invalid, string.Empty, "NEXT_DUPLICATE");
        return new(route, body);
    }

    public static string? ValidateStructure(NextDirective directive)
    {
        if (directive.Route == NextRoute.Invalid) return directive.Error ?? "NEXT_INVALID";
        var body = Normalize(directive.Body);
        if (directive.Route == NextRoute.Web)
            return ValidateUniqueBodyMarker(body, "[REPORT]", "REPORT_MISSING", "REPORT_DUPLICATE");
        if (directive.Route == NextRoute.Jev)
            return ValidateUniqueBodyMarker(body, "[VALIDATION REQUEST]", "VALIDATION_REQUEST_MISSING", "VALIDATION_REQUEST_DUPLICATE");
        return null;
    }

    private static string? ValidateUniqueBodyMarker(
        string body,
        string marker,
        string missingError,
        string duplicateError)
    {
        var matches = body
            .Split('\n')
            .Count(line => line.Trim().Equals(marker, StringComparison.OrdinalIgnoreCase));
        if (matches == 0) return missingError;
        if (matches > 1) return duplicateError;
        return null;
    }

    public static string LoadFooter()
    {
        const string name = "ProjectHub.Worker.Legacy.LEGACY-WEB-JEV-FOOTER-CONTRACT.md";
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name) ?? throw new FileNotFoundException($"Legacy Web JEV footer not found: {name}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Trim();
    }

    private static string Normalize(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n');
}
