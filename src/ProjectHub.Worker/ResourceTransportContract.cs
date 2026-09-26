namespace ProjectHub.Worker;

public sealed record ResourceTransportRequest(string Type, string Prompt);

/// <summary>
/// Mechanical WORK -> RESOURCE validation. WORK declares the resource type explicitly.
/// Worker validates the type token, strips the header, and forwards only the natural-language prompt to ChatGPT Web.
/// Worker does not infer resource meaning, style, target component, or file naming from the prompt.
/// </summary>
public static class ResourceTransportContract
{
    private static readonly HashSet<string> SupportedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "IMAGE",
        "AUDIO",
        "VIDEO",
        "DOCUMENT",
        "FILE"
    };

    public static bool IsSupportedType(string? type)
        => !string.IsNullOrWhiteSpace(type) && SupportedTypes.Contains(type.Trim());

    public static bool TryParse(string? body, out ResourceTransportRequest? request, out string? error)
    {
        request = null;
        error = null;

        var normalized = (body ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        var lines = normalized.Split('\n');
        if (!lines.Any(line => !string.IsNullOrWhiteSpace(line)))
        {
            error = "RESOURCE_REQUEST_EMPTY";
            return false;
        }

        const string prefix = "RESOURCE_TYPE:";
        var headerIndexes = lines
            .Select((line, index) => (Line: line.Trim(), Index: index))
            .Where(item => item.Line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(item => item.Index)
            .ToArray();

        if (headerIndexes.Length == 0)
        {
            error = "RESOURCE_TYPE_MISSING";
            return false;
        }

        if (headerIndexes.Length > 1)
        {
            error = "RESOURCE_TYPE_DUPLICATE";
            return false;
        }

        var headerIndex = headerIndexes[0];
        var header = lines[headerIndex].Trim();
        var type = header[prefix.Length..].Trim().ToUpperInvariant();
        if (!IsSupportedType(type))
        {
            error = "RESOURCE_TYPE_UNSUPPORTED";
            return false;
        }

        var prompt = string.Join("\n", lines.Skip(headerIndex + 1)).Trim();
        if (prompt.Length == 0)
        {
            error = "RESOURCE_REQUEST_EMPTY";
            return false;
        }

        request = new(type, prompt);
        return true;
    }
}
