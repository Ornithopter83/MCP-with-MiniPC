using System.IO;
using System.Security.Cryptography;

namespace ProjectHub.Worker;

internal sealed record AssetFileEvidence(string RelativePath, long Length, string Sha256);
internal sealed record AssetFolderEvidence(
    bool Ready, string Detail, IReadOnlyList<AssetFileEvidence> Files, int ModelCount);

/// <summary>
/// Records immutable evidence for downloaded/importable files, separately
/// from Unity-generated .meta files and user-readable LICENSES.md.
/// This allows reimport without repeating network downloads.
/// </summary>
internal static class AssetManagerFileEvidence
{
    private static readonly HashSet<string> ModelExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".fbx", ".obj", ".gltf", ".glb" };
    private static readonly HashSet<string> ImportableExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".fbx", ".obj", ".gltf", ".glb", ".png", ".jpg", ".jpeg",
            ".tga", ".tif", ".tiff", ".exr", ".hdr", ".mat", ".mtl"
        };

    internal static AssetFolderEvidence Inspect(string root, string targetPath,
        bool requireCc0Manifest, IReadOnlyList<AssetFileEvidence>? expected = null)
    {
        try
        {
            var project = Path.GetFullPath(root);
            var assets = Path.Combine(project, "Assets") + Path.DirectorySeparatorChar;
            var target = Path.GetFullPath(Path.Combine(project,
                targetPath.Replace('/', Path.DirectorySeparatorChar)));
            if (!target.StartsWith(assets, StringComparison.OrdinalIgnoreCase))
                return new(false, "ASSET_TARGET_OUTSIDE_ASSETS", Array.Empty<AssetFileEvidence>(), 0);
            if (!Directory.Exists(target))
                return new(false, "ASSET_TARGET_MISSING", Array.Empty<AssetFileEvidence>(), 0);
            if (requireCc0Manifest)
            {
                var license = Path.Combine(target, "LICENSES.md");
                if (!File.Exists(license) ||
                    !File.ReadAllText(license).Contains("CC0", StringComparison.OrdinalIgnoreCase))
                    return new(false, "ASSET_CC0_MANIFEST_MISSING", Array.Empty<AssetFileEvidence>(), 0);
            }

            var list = new List<AssetFileEvidence>();
            var models = 0;
            foreach (var path in Directory.EnumerateFiles(target, "*", SearchOption.AllDirectories))
            {
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                    return new(false, "ASSET_REPARSE_POINT", Array.Empty<AssetFileEvidence>(), 0);
                var ext = Path.GetExtension(path);
                if (ext.Equals(".meta", StringComparison.OrdinalIgnoreCase) ||
                    ext.Equals(".md", StringComparison.OrdinalIgnoreCase)) continue;
                if (!ImportableExtensions.Contains(ext))
                    return new(false, "ASSET_UNRECOGNIZED_FILE: " +
                        Path.GetRelativePath(project, path), Array.Empty<AssetFileEvidence>(), 0);
                using var file = File.OpenRead(path);
                var hash = Convert.ToHexString(SHA256.HashData(file));
                var relative = Path.GetRelativePath(project, path).Replace('\\', '/');
                list.Add(new(relative, file.Length, hash));
                if (ModelExtensions.Contains(ext)) models++;
            }
            list.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(
                a.RelativePath, b.RelativePath));
            if (models == 0)
                return new(false, "ASSET_MODEL_NOT_FOUND", list, 0);
            if (expected is not null)
            {
                var previous = expected.OrderBy(x => x.RelativePath,
                    StringComparer.OrdinalIgnoreCase).ToArray();
                if (previous.Length != list.Count ||
                    previous.Where((item, i) =>
                        !string.Equals(item.RelativePath, list[i].RelativePath,
                            StringComparison.OrdinalIgnoreCase) ||
                        item.Length != list[i].Length ||
                        !string.Equals(item.Sha256, list[i].Sha256,
                            StringComparison.OrdinalIgnoreCase)).Any())
                    return new(false, "ASSET_FILES_CHANGED_SINCE_CHECKPOINT", list, models);
            }
            return new(true, "ASSET_FILES_VERIFIED: " + list.Count +
                " files, " + models + " models", list, models);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                 or ArgumentException or NotSupportedException)
        {
            return new(false, "ASSET_FILES_UNREADABLE: " + ex.Message,
                Array.Empty<AssetFileEvidence>(), 0);
        }
    }
}
