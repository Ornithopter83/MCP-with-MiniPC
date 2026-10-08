using System.Text.RegularExpressions;

namespace ProjectHub.Worker;

/// <summary>
/// Reads only top-level middle fields. Text inside a field is opaque:
/// nested headings, colons and prose never become machine directives.
/// </summary>
internal sealed class StructuredRoleFields
{
    private readonly Dictionary<string, List<string>> _values =
        new(StringComparer.OrdinalIgnoreCase);

    public string? Get(string name) =>
        _values.TryGetValue(name, out var items) ? items.LastOrDefault() : null;

    public IReadOnlyList<string> GetMany(string name) =>
        _values.TryGetValue(name, out var items) ? items : Array.Empty<string>();

    public IReadOnlyCollection<string> Names => _values.Keys;

    public static bool TryParse(string body, out StructuredRoleFields fields, out string error)
    {
        fields = new StructuredRoleFields();
        error = string.Empty;
        var lines = (body ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n').Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.Length == 0)
                continue;

            var inline = Regex.Match(line, @"^<([A-Z][A-Z0-9_]*)>(.*?)</>$",
                RegexOptions.CultureInvariant);
            if (inline.Success)
            {
                fields.Add(inline.Groups[1].Value, inline.Groups[2].Value);
                continue;
            }

            var opening = Regex.Match(line, @"^<([A-Z][A-Z0-9_]*)>$",
                RegexOptions.CultureInvariant);
            if (!opening.Success)
            {
                error = "MIDDLE_FIELD_INVALID: " + line;
                return false;
            }

            var parts = new List<string>();
            var closed = false;
            while (++i < lines.Length)
            {
                if (lines[i].Trim() == "</>")
                {
                    closed = true;
                    break;
                }
                parts.Add(lines[i]);
            }
            if (!closed)
            {
                error = "MIDDLE_FIELD_UNCLOSED: " + opening.Groups[1].Value;
                return false;
            }
            fields.Add(opening.Groups[1].Value, string.Join("\n", parts).Trim());
        }
        return true;
    }

    private void Add(string key, string value)
    {
        if (!_values.TryGetValue(key, out var entries))
            _values[key] = entries = new List<string>();
        entries.Add(value.Trim());
    }
}
