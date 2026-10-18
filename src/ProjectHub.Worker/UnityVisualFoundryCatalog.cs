using System.IO;
using System.Text.Json;

namespace ProjectHub.Worker;

/// <summary>
/// Read-only, model-free discovery of reusable premium visual assets already
/// owned by the target Unity project. Does not fetch or import licensed packs.
/// </summary>
internal static class UnityVisualFoundryCatalog
{
    internal sealed record Asset(string Path, string Kind);
    private static readonly string[] Extensions =
        { ".prefab", ".mat", ".shader", ".shadergraph", ".png", ".svg", ".uxml", ".uss" };

    // Mechanical inventory only: no keyword ranking, preference or asset selection.
    // ASSET MANAGER decides whether a project asset is suitable and licensed.
    internal static IReadOnlyList<Asset> Inventory(string projectRoot, int limit = 1000)
    {
        var root = Path.GetFullPath(projectRoot);
        var assetsRoot = Path.Combine(root, "Assets");
        if (!Directory.Exists(assetsRoot)) return Array.Empty<Asset>();
        var results = new List<Asset>();
        var visited = 0;
        try
        {
            foreach (var file in Directory.EnumerateFiles(assetsRoot, "*",
                SearchOption.AllDirectories))
            {
                if (++visited > 25000) break;
                var extension = Path.GetExtension(file);
                if (!Extensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
                    continue;
                var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
                if (relative.StartsWith("Assets/ProjectHub/VisualFoundry/",
                        StringComparison.OrdinalIgnoreCase))
                    continue;
                results.Add(new(relative, extension.TrimStart('.')));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Preserve only the paths actually observed.
        }
        return results.OrderBy(asset => asset.Path, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Clamp(limit, 0, 1000)).ToArray();
    }

    internal static string SaveManifest(string outputPath,
        IReadOnlyList<Asset> assets)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        File.WriteAllText(outputPath, JsonSerializer.Serialize(new
        {
            source = "LOCAL_PROJECT_ONLY",
            externalAcquisition = false,
            assets
        }, new JsonSerializerOptions { WriteIndented = true }),
            ProjectHubJson.Utf8NoBom);
        return outputPath;
    }
}
