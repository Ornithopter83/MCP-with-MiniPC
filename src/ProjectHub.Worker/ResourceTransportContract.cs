using System.IO;
using System.Text.Json;

namespace ProjectHub.Worker;

public sealed record ResourceTransportRequest(string Type, string Prompt, string TargetDirectory, string TargetFileName);

/// <summary>Mechanical schema/path validation for WORK -> RESOURCE transport. It does not judge resource meaning or quality.</summary>
public static class ResourceTransportContract
{
    public static bool TryParse(string? body, out ResourceTransportRequest? request, out string? error)
    {
        request = null;
        error = null;
        if (string.IsNullOrWhiteSpace(body)) { error = "RESOURCE_REQUEST_EMPTY"; return false; }

        try
        {
            using var document = JsonDocument.Parse(body.Trim());
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) { error = "RESOURCE_REQUEST_NOT_OBJECT"; return false; }

            var type = Read(root, "type").ToUpperInvariant();
            var prompt = Read(root, "prompt");
            var targetDirectory = Read(root, "targetDirectory").Replace('\\', '/').Trim();
            var targetFileName = Read(root, "targetFileName").Trim();

            if (type is not ("IMAGE" or "SOUND")) { error = "RESOURCE_TYPE_INVALID"; return false; }
            if (string.IsNullOrWhiteSpace(prompt)) { error = "RESOURCE_PROMPT_REQUIRED"; return false; }
            if (string.IsNullOrWhiteSpace(targetDirectory)) targetDirectory = ".";
            if (Path.IsPathRooted(targetDirectory) || targetDirectory.Split('/', StringSplitOptions.RemoveEmptyEntries).Any(part => part == ".."))
            { error = "RESOURCE_TARGET_DIRECTORY_INVALID"; return false; }
            if (string.IsNullOrWhiteSpace(targetFileName) || Path.GetFileName(targetFileName) != targetFileName ||
                targetFileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            { error = "RESOURCE_TARGET_FILENAME_INVALID"; return false; }

            request = new(type, prompt, targetDirectory, targetFileName);
            return true;
        }
        catch (JsonException)
        {
            error = "RESOURCE_REQUEST_INVALID_JSON";
            return false;
        }
    }

    private static string Read(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
}
