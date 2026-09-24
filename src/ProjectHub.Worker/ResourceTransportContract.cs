namespace ProjectHub.Worker;

public sealed record ResourceTransportRequest(string Prompt);

/// <summary>
/// Mechanical WORK -> RESOURCE validation. The body is natural language and is forwarded verbatim to ChatGPT Web.
/// Worker does not infer resource meaning, style, target component, or file naming from the text.
/// </summary>
public static class ResourceTransportContract
{
    public static bool TryParse(string? body, out ResourceTransportRequest? request, out string? error)
    {
        request = null;
        error = null;
        var prompt = body?.Trim() ?? string.Empty;
        if (prompt.Length == 0)
        {
            error = "RESOURCE_REQUEST_EMPTY";
            return false;
        }

        request = new(prompt);
        return true;
    }
}
