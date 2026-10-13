using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ProjectHub.Worker;

internal sealed record AssetManagerCheckpoint(
    string JobId,
    string RequestId,
    string Fingerprint,
    string Provider,
    string Model,
    string State,
    string? SessionId,
    string Evidence,
    IReadOnlyList<string> ChangedPaths,
    IReadOnlyList<AssetFileEvidence>? AcquiredFiles = null,
    string? BlockedPhase = null);

/// <summary>
/// Per-job, per-request durable state. A crashed mutating phase never
/// silently repeats: a new HQ decision is required to reconcile it.
/// </summary>
internal static class AssetManagerExecutionJournal
{
    internal static string Fingerprint(AssetManagerDefinition definition)
    {
        var raw = string.Join("\n", definition.RequestId, definition.Goal,
            definition.ScenePath, definition.TargetPath, definition.SourcePolicy,
            definition.Instructions);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
    }

    internal static string FilePath(string root, string jobId, string requestId)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(jobId, @"^[a-zA-Z0-9_-]{1,80}$") ||
            !System.Text.RegularExpressions.Regex.IsMatch(requestId, @"^[a-zA-Z0-9_-]{1,64}$"))
            throw new ArgumentException("ASSET_MANAGER_ID_INVALID");
        return Path.Combine(Path.GetFullPath(root), ".projecthub", "asset-manager",
            jobId, requestId + ".json");
    }

    internal static AssetManagerCheckpoint Open(string root, string jobId,
        AssetManagerDefinition definition, WorkerAiRoleSettings role)
    {
        var file = FilePath(root, jobId, definition.RequestId);
        var fingerprint = Fingerprint(definition);
        if (File.Exists(file))
        {
            var saved = JsonSerializer.Deserialize<AssetManagerCheckpoint>(
                File.ReadAllText(file)) ?? throw new InvalidDataException("ASSET_MANAGER_CHECKPOINT_INVALID");
            if (saved.JobId != jobId || saved.RequestId != definition.RequestId ||
                saved.Fingerprint != fingerprint ||
                !string.Equals(saved.Provider, role.Provider, StringComparison.OrdinalIgnoreCase) ||
                saved.Model != role.Model)
                throw new InvalidOperationException("ASSET_MANAGER_CHECKPOINT_SCOPE_OR_MODEL_CHANGED");
            return saved;
        }
        var checkpoint = new AssetManagerCheckpoint(jobId, definition.RequestId,
            fingerprint, role.Provider, role.Model, "READY", null,
            string.Empty, Array.Empty<string>());
        Save(root, checkpoint);
        return checkpoint;
    }

    internal static void Save(string root, AssetManagerCheckpoint checkpoint)
    {
        var file = FilePath(root, checkpoint.JobId, checkpoint.RequestId);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var temp = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(checkpoint));
            File.Move(temp, file, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }
}

/// <summary>
/// Safe write-scopes for Unity imports and scene edits, including parent .meta
/// files created when a new nested asset folder is imported.
/// </summary>
internal static class AssetManagerPathPolicy
{
    internal static bool IsAllowed(string path, AssetManagerDefinition request, string phase)
    {
        var changed = (path ?? string.Empty).Replace('\\', '/');
        var folder = request.TargetPath.TrimEnd('/');
        var scene = request.ScenePath;
        if (changed.Equals(scene, StringComparison.OrdinalIgnoreCase) ||
            changed.Equals(scene + ".meta", StringComparison.OrdinalIgnoreCase))
            return phase == "INTEGRATE";

        if (changed.StartsWith(folder + "/", StringComparison.OrdinalIgnoreCase) ||
            changed.Equals(folder + ".meta", StringComparison.OrdinalIgnoreCase))
            return phase is "ACQUIRE" or "INTEGRATE";

        var parent = folder;
        while (parent.Contains('/'))
        {
            parent = parent[..parent.LastIndexOf('/')];
            if (parent.Equals("Assets", StringComparison.OrdinalIgnoreCase))
                break;
            if (changed.Equals(parent + ".meta", StringComparison.OrdinalIgnoreCase))
                return phase is "ACQUIRE" or "INTEGRATE";
        }
        return false;
    }
}
