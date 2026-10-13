using System.IO;
using System.Text;

namespace ProjectHub.Worker;

/// <summary>Read-only Unity asset candidates; never claims an import succeeded.</summary>
internal static class AssetManagerLocalInventory
{
    private static readonly HashSet<string> ModelExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".fbx", ".obj", ".blend", ".glb", ".gltf", ".dae", ".3ds",
            ".prefab", ".unitypackage"
        };
    internal sealed record Snapshot(bool SceneExists, bool TargetExists,
        int FilesScanned, int Candidates, bool Truncated,
        IReadOnlyList<string> Examples, string? Error);

    internal static Snapshot Scan(string root, string scene, string target,
        CancellationToken cancellationToken = default)
    {
        var basePath = Path.GetFullPath(root);
        var assets = Path.Combine(basePath, "Assets");
        var sceneExists = File.Exists(Path.Combine(basePath,
            scene.Replace('/', Path.DirectorySeparatorChar)));
        var targetExists = Directory.Exists(Path.Combine(basePath,
            target.Replace('/', Path.DirectorySeparatorChar)));
        if (!Directory.Exists(assets))
            return new(sceneExists, targetExists, 0, 0, false,
                Array.Empty<string>(), "ASSETS_MISSING");

        var filesScanned = 0;
        var candidates = 0;
        var truncated = false;
        var examples = new List<string>();
        try
        {
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = false,
                AttributesToSkip = FileAttributes.ReparsePoint
            };
            foreach (var file in Directory.EnumerateFiles(assets, "*", options))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (filesScanned >= 100000)
                {
                    truncated = true;
                    break;
                }
                filesScanned++;
                if (!ModelExtensions.Contains(Path.GetExtension(file))) continue;
                candidates++;
                if (examples.Count < 32)
                    examples.Add(Path.GetRelativePath(basePath, file)
                        .Replace('\\', '/'));
            }
            examples.Sort(StringComparer.OrdinalIgnoreCase);
            return new(sceneExists, targetExists, filesScanned, candidates,
                truncated, examples, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new(sceneExists, targetExists, filesScanned, candidates,
                truncated, examples, ex.GetType().Name + ": " + ex.Message);
        }
    }

    internal static string Format(Snapshot value)
    {
        var report = new StringBuilder();
        report.AppendLine("SOURCE=WORKER_FILESYSTEM_READ_ONLY");
        report.AppendLine("SCENE_EXISTS=" + value.SceneExists);
        report.AppendLine("TARGET_EXISTS=" + value.TargetExists);
        report.AppendLine("FILES_SCANNED=" + value.FilesScanned);
        report.AppendLine("MODEL_CANDIDATES=" + value.Candidates);
        report.AppendLine("TRUNCATED=" + value.Truncated);
        report.AppendLine("SCAN_ERROR=" + (value.Error ?? "none"));
        foreach (var path in value.Examples)
            report.AppendLine("MODEL_CANDIDATE_PATH=" + path);
        report.AppendLine("File candidates do not prove Importer/GUID or license. " +
            "Missing TARGET_PATH before ACQUIRE is expected. " +
            "Truncated or failed scans cannot establish complete absence.");
        return report.ToString();
    }
}
