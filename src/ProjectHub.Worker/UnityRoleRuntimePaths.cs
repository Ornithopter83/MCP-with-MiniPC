using System.IO;

namespace ProjectHub.Worker;

/// <summary>
/// Private CLI runtime files for Unity roles. Unity can delete its Temp
/// directory during Editor/package operations; Codex's --output-last-message
/// and pre-tool hooks must not live underneath the Unity project.
/// This resolver is deliberately used only by Unity CLI role dispatch.
/// HQ WEB, generic Worker roles, and project checkpoint locations are unchanged.
/// </summary>
internal static class UnityRoleRuntimePaths
{
    internal static string Resolve(string jobId, string role)
    {
        var dataRoot = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(dataRoot))
            dataRoot = Path.GetTempPath();
        return Path.Combine(dataRoot, "ProjectHub", "UnityWorker", "role-runtime",
            SafePart(jobId), "role-" + SafePart(role).ToLowerInvariant());
    }

    private static string SafePart(string value)
    {
        var sanitized = new string(value
            .Where(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_')
            .Take(96).ToArray());
        if (sanitized.Length == 0)
            throw new ArgumentException("Unity role runtime identifier is missing.");
        return sanitized;
    }
}
