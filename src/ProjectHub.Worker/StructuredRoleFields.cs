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
        var buffer = new List<string>();
        string? current = null;
        var inFence = false;

        void Finish()
        {
            if (current is null) return;
            fields.Add(current, string.Join("\n", buffer).Trim());
            buffer.Clear();
            current = null;
        }

        foreach (var original in lines)
        {
            var line = original.Trim();
            if (line.StartsWith("```", StringComparison.Ordinal))
            {
                inFence = !inFence;
                if (current is not null) buffer.Add(original);
                continue;
            }
            if (inFence)
            {
                if (current is not null) buffer.Add(original);
                continue;
            }

            // Only top-level field boundaries count. Quoted examples and
            // unmatched middle-field markers never invalidate the envelope.
            var open = Regex.Match(line, @"^<([A-Z][A-Z0-9_]*)>(.*)$",
                RegexOptions.CultureInvariant);
            if (open.Success)
            {
                var name = open.Groups[1].Value;
                // Markers inside an instruction block can be examples.
                // Switch only for the known HQ field vocabulary.
                if (name is "ID" or "TEXT" or "INSTRUCTIONS" or "TYPE" or
                    "TARGET_PATH" or "WIDTH" or "HEIGHT" or "COLUMNS" or
                    "ROWS" or "ALPHA")
                {
                    Finish();
                    var remainder = open.Groups[2].Value;
                    var close = Regex.Match(remainder,
                        @"^(.*?)(?:</>|</" + name + @">)\s*$",
                        RegexOptions.CultureInvariant);
                    if (close.Success)
                        fields.Add(name, close.Groups[1].Value);
                    else
                    {
                        current = name;
                        if (remainder.Length > 0) buffer.Add(remainder);
                    }
                    continue;
                }
            }

            if (current is null) continue; // Free prose is not a grammar error.
            if (line == "</>" || string.Equals(line, "</" + current + ">",
                    StringComparison.OrdinalIgnoreCase))
            {
                Finish();
                continue;
            }
            buffer.Add(original);
        }
        Finish(); // Missing middle-field close is tolerated.
        return true;
    }

    private void Add(string key, string value)
    {
        if (!_values.TryGetValue(key, out var entries))
            _values[key] = entries = new List<string>();
        entries.Add(value.Trim());
    }
}
