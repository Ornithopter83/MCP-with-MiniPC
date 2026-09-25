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

        var normalized = (body ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Trim();
        if (normalized.Length == 0)
        {
            error = "RESOURCE_REQUEST_EMPTY";
            return false;
        }

        var newline = normalized.IndexOf('\n');
        var header = (newline < 0 ? normalized : normalized[..newline]).Trim();
        const string prefix = "RESOURCE_TYPE:";
        if (!header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            error = "RESOURCE_TYPE_MISSING";
            return false;
        }

        var type = header[prefix.Length..].Trim().ToUpperInvariant();
        if (!IsSupportedType(type))
        {
            error = "RESOURCE_TYPE_UNSUPPORTED";
            return false;
        }

        var prompt = newline < 0 ? string.Empty : normalized[(newline + 1)..].Trim();
        if (prompt.Length == 0)
        {
            error = "RESOURCE_REQUEST_EMPTY";
            return false;
        }

        request = new(type, prompt);
        return true;
    }
}
