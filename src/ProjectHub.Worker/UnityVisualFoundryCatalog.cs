using System.IO;
using System.Text.Json;

namespace ProjectHub.Worker;

/// <summary>
/// Read-only, model-free discovery of reusable premium visual assets already
/// owned by the target Unity project. Does not fetch or import licensed packs.
/// </summary>
internal static class UnityVisualFoundryCatalog
{
    internal sealed record Asset(string Path, string Kind, int Rank);
    private static readonly string[] Extensions =
        { ".prefab", ".mat", ".shader", ".shadergraph", ".png", ".svg", ".uxml", ".uss" };
    private static readonly string[] PositiveTokens =
        { "premium", "ui", "kit", "hud", "screen", "menu", "panel", "glow",
            "interface", "button", "visual", "theme", "vfx", "gui", "sprite" };

    internal static IReadOnlyList<Asset> Discover(string projectRoot,
        UnityVisualFoundry.Style style, int limit = 16)
    {
        var assetsRoot = Path.Combine(Path.GetFullPath(projectRoot), "Assets");
        if (!Directory.Exists(assetsRoot)) return Array.Empty<Asset>();
        var hints = style.Id switch
        {
            "arcane" => new[] { "fantasy", "rpg", "medieval", "magic", "ornate" },
            "luxury" => new[] { "luxury", "minimal", "noir", "elegant", "gold" },
            "playful" => new[] { "casual", "cartoon", "cute", "colorful", "toy" },
            _ => new[] { "sci", "cyber", "future", "neon", "tech", "space" }
        };
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
                var relative = Path.GetRelativePath(Path.GetFullPath(projectRoot),file)
                    .Replace('\\','/');
                if (relative.StartsWith("Assets/ProjectHub/VisualFoundry/",
                        StringComparison.OrdinalIgnoreCase))
                    continue;
                if (relative.Contains("/Editor/", StringComparison.OrdinalIgnoreCase) &&
                    extension.Equals(".prefab", StringComparison.OrdinalIgnoreCase))
                    continue;
                var rank = PositiveTokens.Count(w => relative.Contains(w,
                    StringComparison.OrdinalIgnoreCase)) * 2 +
                    hints.Count(w => relative.Contains(w,
                        StringComparison.OrdinalIgnoreCase)) * 4;
                if (rank == 0) continue;
                if (extension.Equals(".prefab",StringComparison.OrdinalIgnoreCase))
                    rank += 5;
                results.Add(new(relative, extension.TrimStart('.'), rank));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Keep the viable references already collected; discovery is optional.
        }
        return results.OrderByDescending(x => x.Rank)
            .ThenBy(x => x.Path, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Clamp(limit, 0, 100)).ToArray();
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
