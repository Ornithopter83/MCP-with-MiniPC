using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ProjectHub.Worker;

/// <summary>
/// Explicit, same-user local IPC for an already running Unity Editor.
/// The Editor imports the embedded [InitializeOnLoad] script through normal
/// AssetDatabase refresh; no Unity CLI catalog, GUI launch or elevation.
/// The Editor main thread executes exactly one requested method/menu action.
/// </summary>
internal static class UnityEditorDirectBridge
{
    internal const string EditorSourceRelativePath =
        "Assets/Editor/ProjectHub/ProjectHubEditorBridge.cs";
    private const string Marker = "// PROJECTHUB_EDITOR_BRIDGE_V1";

    internal static string EditorSourcePath(string root) =>
        Path.Combine(Path.GetFullPath(root),
            EditorSourceRelativePath.Replace('/', Path.DirectorySeparatorChar));

    internal static string QueueDirectory(string root)
    {
        string canonical = Path.GetFullPath(root).Replace('\\', '/')
            .TrimEnd('/').ToUpperInvariant();
        string hash = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(canonical)))[..16];
        return Path.Combine(Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "ProjectHub", "UnityWorker", "EditorBridge", hash);
    }

    internal static string EmbeddedSource() =>
        UnityEditorBridgeTemplate.Source + Environment.NewLine;

    internal static string StartupSignalPath(string root) =>
        Path.Combine(QueueDirectory(root), "bridge-active.json");

    internal static string LegacyEmbeddedSource() =>
        UnityEditorBridgeTemplate.LegacySource + Environment.NewLine;

    internal static string Install(string root)
    {
        if (!UnityCliBuildRunner.IsUnityProject(root))
            throw new InvalidOperationException("EDITOR_BRIDGE_NOT_UNITY_PROJECT");
        var target = EditorSourcePath(root);
        var source = EmbeddedSource();
        if (!source.StartsWith(Marker, StringComparison.Ordinal))
            throw new InvalidOperationException("EDITOR_BRIDGE_SCRIPT_INVALID");
        if (File.Exists(target))
        {
            var existing = File.ReadAllText(target);
            if (string.Equals(existing, source, StringComparison.Ordinal))
                return "EDITOR_BRIDGE_ALREADY_INSTALLED: " + EditorSourceRelativePath;
            // Exact previously shipped receiver is Worker-owned and can be
            // upgraded to emit startup diagnostics. User-edited files cannot.
            if (string.Equals(existing, LegacyEmbeddedSource(), StringComparison.Ordinal))
            {
                File.WriteAllText(target, source, ProjectHubJson.Utf8NoBom);
                return "EDITOR_BRIDGE_APPROVED_UPGRADE_WAIT_FOR_UNITY_IMPORT: " +
                    EditorSourceRelativePath;
            }
            throw new InvalidOperationException(
                "EDITOR_BRIDGE_SOURCE_CONFLICT: existing file was not overwritten: " +
                EditorSourceRelativePath);
        }
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        // Never modify Packages, .git, scene files or the current window focus.
        // Unity may import this script only at its next automatic Asset refresh.
        using (var file = new FileStream(target, FileMode.CreateNew, FileAccess.Write,
            FileShare.None))
        using (var writer = new StreamWriter(file, ProjectHubJson.Utf8NoBom))
            writer.Write(source);
        return "EDITOR_BRIDGE_INSTALLED_WAIT_FOR_UNITY_IMPORT: " +
            EditorSourceRelativePath;
    }

    internal static async Task<string> ExecuteAsync(string root, string action,
        string? type, string? method, string? menu, string? expected,
        TimeSpan timeout, CancellationToken cancellationToken,
        string? scenePath = null)
    {
        var installed = EditorSourcePath(root);
        if (!File.Exists(installed))
            throw new InvalidOperationException(
                "EDITOR_BRIDGE_NOT_INSTALLED: call editor_bridge_install first");
        if (!string.Equals(File.ReadAllText(installed), EmbeddedSource(),
            StringComparison.Ordinal))
            throw new InvalidOperationException(
                "EDITOR_BRIDGE_INSTALLED_SOURCE_CHANGED: do not run an unknown script");
        if (action is not ("ping" or "scene_status" or "sync_scene" or
            "refresh_assets" or "invoke" or "menu"))
            throw new ArgumentException("EDITOR_BRIDGE_ACTION_INVALID", nameof(action));
        if (action == "invoke" &&
            (string.IsNullOrWhiteSpace(type) || string.IsNullOrWhiteSpace(method)))
            throw new ArgumentException("EDITOR_BRIDGE_METHOD_REQUIRED");
        if (action == "menu" && string.IsNullOrWhiteSpace(menu))
            throw new ArgumentException("EDITOR_BRIDGE_MENU_REQUIRED");
        if (action == "invoke" && string.IsNullOrWhiteSpace(expected))
            throw new ArgumentException("EDITOR_BRIDGE_EXPECTED_RESULT_REQUIRED");
        // ExecuteMenuItem returns whether the menu was invoked, not whether
        // the menu's BuildPipeline/async operation completed successfully.
        // Never compare a menu result with a BuildReport such as "Succeeded".
        if (action == "menu")
            expected = "True";
        if (action == "sync_scene" &&
            (string.IsNullOrWhiteSpace(scenePath) || scenePath.Length > 300 ||
             !IsValidScenePath(scenePath)))
            throw new ArgumentException("EDITOR_BRIDGE_SCENE_PATH_REQUIRED", nameof(scenePath));
        if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromMinutes(10))
            throw new ArgumentOutOfRangeException(nameof(timeout));

        var startedUtc = DateTimeOffset.UtcNow;
        var directory = QueueDirectory(root);
        Directory.CreateDirectory(directory);
        var id = Guid.NewGuid().ToString("N");
        var pending = Path.Combine(directory, id + ".req.json");
        var staging = pending + ".tmp";
        var resultPath = Path.Combine(directory, id + ".resp.json");
        var deadline = DateTime.UtcNow.Add(timeout);
        var request = JsonSerializer.Serialize(new
        {
            id, action, type = type ?? "", method = method ?? "", menu = menu ?? "",
            scene = scenePath ?? "", deadlineUtcTicks = deadline.Ticks
        });
        try
        {
            // Atomic rename: Editor never reads a partially written request.
            await File.WriteAllTextAsync(staging, request,
                ProjectHubJson.Utf8NoBom, cancellationToken).ConfigureAwait(false);
            File.Move(staging, pending);
            while (DateTime.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (File.Exists(resultPath))
                {
                    var json = await File.ReadAllTextAsync(
                        resultPath, cancellationToken).ConfigureAwait(false);
                    using var reply = JsonDocument.Parse(json);
                    var rootReply = reply.RootElement;
                    string ReadString(string key) =>
                        rootReply.TryGetProperty(key, out var element) &&
                        element.ValueKind == JsonValueKind.String
                            ? element.GetString() ?? "" : "";
                    if (ReadString("id") != id)
                        throw new InvalidOperationException("EDITOR_BRIDGE_RESPONSE_ID_MISMATCH");
                    var code = ReadString("code");
                    var value = ReadString("value");
                    var detail = ReadString("detail");
                    if (!rootReply.TryGetProperty("success", out var success) ||
                        success.ValueKind != JsonValueKind.True)
                        throw new InvalidOperationException("EDITOR_BRIDGE_FAILED: " +
                            code + "; " + Limit(detail));
                    if (expected is not null &&
                        !string.Equals(value, expected, StringComparison.Ordinal))
                        throw new InvalidOperationException(
                            "EDITOR_BRIDGE_RESULT_MISMATCH: actual=" + Limit(value) +
                            "; expected=" + Limit(expected) + "; " + Limit(detail));
                    return "EDITOR_BRIDGE_CONFIRMED: " + code + "; VALUE=" +
                        Limit(value) + "; " + Limit(detail) +
                        "; EVIDENCE=" + EvidencePath(root, id) +
                        (action == "menu"
                            ? "; MENU_TRIGGERED_ONLY: verify BuildReport and build outputs separately"
                            : "");
                }
                await Task.Delay(150, cancellationToken).ConfigureAwait(false);
            }
            // An Editor might already have claimed the request: never
            // automatically retry an uncertain mutation.
            throw new TimeoutException(
                "EDITOR_BRIDGE_TIMEOUT_UNVERIFIED: phase=" +
                (File.Exists(Path.Combine(directory, id + ".processing"))
                    ? "CLAIMED_BY_EDITOR" : File.Exists(pending)
                        ? "PENDING_NOT_CLAIMED" : "UNKNOWN") +
                "; request=" + id + "; elapsed=" +
                (DateTimeOffset.UtcNow - startedUtc).TotalMilliseconds.ToString("F0") +
                "ms; evidence=" + EvidencePath(root, id));
        }
        finally
        {
            // Persist complete bounded original response outside the Unity
            // project before deleting transport files; never retry mutations.
            PersistEvidence(root, id, action, startedUtc, resultPath,
                File.Exists(Path.Combine(directory, id + ".processing")));
            // If pending it has not been claimed; if .processing exists,
            // execution may have occurred and requires separate verification.
            TryDelete(pending);
            TryDelete(staging);
            TryDelete(resultPath);
        }
    }

    private static string EvidencePath(string root, string id)
    {
        var key = Path.GetFileName(QueueDirectory(root));
        return Path.Combine(Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "ProjectHub", "UnityWorker", "editor-bridge-evidence",
            key, id + ".json");
    }

    private static void PersistEvidence(string root, string id,
        string action, DateTimeOffset startedUtc, string responsePath, bool claimed)
    {
        try
        {
            var path = EvidencePath(root, id);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var response = File.Exists(responsePath)
                ? File.ReadAllText(responsePath) : null;
            if (response is { Length: > 2_000_000 })
                response = response[..2_000_000];
            File.WriteAllText(path, JsonSerializer.Serialize(new
            {
                id, action, startedUtc, completedUtc = DateTimeOffset.UtcNow,
                claimed, response, status = response is null
                    ? "NO_RESPONSE_UNVERIFIED" : "EDITOR_RESPONSE_RECEIVED"
            }), ProjectHubJson.Utf8NoBom);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or ArgumentException) { }
    }

    internal static bool IsValidScenePath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Length > 300 || value.Contains('\\') ||
            value.Contains(':') || value.Contains("//", StringComparison.Ordinal) ||
            value.Split('/').Any(x => x is "." or ".."))
            return false;
        return value.StartsWith("Assets/", StringComparison.Ordinal) &&
            value.EndsWith(".unity", StringComparison.OrdinalIgnoreCase) &&
            value.Split('/').Length >= 3;
    }

    internal const string DirtySceneBackupScope =
        "Assets/Editor/ProjectHub/SceneBackups/";

    private static void TryDelete(string file)
    {
        try { File.Delete(file); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static string Limit(string text) =>
        text.Length <= 1800 ? text.Trim() : text[..1800].Trim();
}
