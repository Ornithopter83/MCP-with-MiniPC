namespace ProjectHub.Worker;

// Compile-time asset: no runtime manifest-resource lookup or external sidecar.
internal static class UnityEditorBridgeTemplate
{
    internal const string Source = """
// PROJECTHUB_EDITOR_BRIDGE_V1
// Unity Editor-only extension. Runs in the existing Editor process.
// The local per-user queue is transport, not an elevated permission service.
#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
internal static class ProjectHubEditorBridge
{
    [Serializable]
    private sealed class Request
    {
        public string id;
        public string action;
        public string type;
        public string method;
        public string menu;
        public string scene;
        public long deadlineUtcTicks;
    }

    [Serializable]
    private sealed class Response
    {
        public string id;
        public bool success;
        public string code;
        public string value;
        public string detail;
    }

    private static readonly string QueueDirectory = MakeQueueDirectory();
    private static double _nextTick;

    static ProjectHubEditorBridge()
    {
        // Update callbacks run on the Editor's main thread, including in the
        // background while the Unity Editor is responsive.
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        AssemblyReloadEvents.beforeAssemblyReload += () =>
            EditorApplication.update -= Tick;
    }

    private static string MakeQueueDirectory()
    {
        string root = Directory.GetParent(Application.dataPath).FullName;
        string canonical = Path.GetFullPath(root).Replace('\\', '/')
            .TrimEnd('/').ToUpperInvariant();
        byte[] hash;
        using (var sha = SHA256.Create())
            hash = sha.ComputeHash(Encoding.UTF8.GetBytes(canonical));
        string key = BitConverter.ToString(hash).Replace("-", "").Substring(0, 16);
        return Path.Combine(Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "ProjectHub", "UnityWorker", "EditorBridge", key);
    }

    private static void Tick()
    {
        if (EditorApplication.timeSinceStartup < _nextTick)
            return;
        _nextTick = EditorApplication.timeSinceStartup + 0.2d;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating ||
            !Directory.Exists(QueueDirectory))
            return;
        try
        {
            string[] pending = Directory.GetFiles(QueueDirectory, "*.req.json");
            Array.Sort(pending, StringComparer.Ordinal);
            // Never run more than one mutation per Editor update.
            if (pending.Length > 0)
                Process(pending[0]);
        }
        catch (Exception e)
        {
            Debug.LogWarning("PROJECTHUB_EDITOR_BRIDGE_QUEUE_ERROR: " +
                e.GetType().Name + ": " + e.Message);
        }
    }

    private static void Process(string requestPath)
    {
        string basename = Path.GetFileName(requestPath);
        string id = basename.Substring(0, basename.Length - ".req.json".Length);
        if (!Guid.TryParseExact(id, "N", out _))
            return;
        string active = Path.Combine(QueueDirectory, id + ".processing");
        try { File.Move(requestPath, active); }
        catch (IOException) { return; }

        var reply = new Response
        {
            id = id, success = false, code = "BRIDGE_UNVERIFIED",
            value = "", detail = ""
        };
        try
        {
            if (new FileInfo(active).Length > 128 * 1024)
                throw new InvalidOperationException("REQUEST_TOO_LARGE");
            var request = JsonUtility.FromJson<Request>(File.ReadAllText(active));
            if (request == null || request.id != id)
                throw new InvalidOperationException("REQUEST_ID_INVALID");
            if (request.deadlineUtcTicks <= DateTime.UtcNow.Ticks)
                throw new InvalidOperationException("REQUEST_EXPIRED_UNEXECUTED");
            reply = Execute(request);
        }
        catch (Exception e)
        {
            reply.code = "EDITOR_COMMAND_FAILED";
            reply.detail = e.GetType().Name + ": " + e.Message;
        }

        try
        {
            string target = Path.Combine(QueueDirectory, id + ".resp.json");
            string staging = target + ".tmp";
            File.WriteAllText(staging, JsonUtility.ToJson(reply));
            File.Move(staging, target);
        }
        finally
        {
            try { File.Delete(active); }
            catch (IOException) { }
        }
    }

    private static Response Execute(Request request)
    {
        var reply = new Response
        {
            id = request.id, success = false, code = "UNKNOWN_ACTION",
            value = "", detail = ""
        };
        if (request.action == "scene_status")
            return ReadSceneStatus(reply);
        if (request.action == "sync_scene")
            return ReloadActiveSceneWithBackup(reply, request.scene);
        if (request.action == "refresh_assets")
            return RefreshAndInspectAssets(reply);
        if (request.action == "ping")
        {
            reply.success = true;
            reply.code = "EDITOR_RESPONDED";
            reply.value = Application.unityVersion;
            reply.detail = "Editor main thread responded; project=" +
                Directory.GetParent(Application.dataPath).FullName;
            return reply;
        }

        if (request.action == "menu")
        {
            if (String.IsNullOrWhiteSpace(request.menu))
                throw new ArgumentException("MENU_PATH_REQUIRED");
            bool handled = EditorApplication.ExecuteMenuItem(request.menu);
            reply.success = handled;
            reply.code = handled ? "EDITOR_MENU_INVOKED" : "EDITOR_MENU_NOT_FOUND";
            reply.value = handled ? "True" : "False";
            return reply;
        }
        if (request.action != "invoke")
            throw new InvalidOperationException("BRIDGE_ACTION_NOT_SUPPORTED");
        if (String.IsNullOrWhiteSpace(request.type) ||
            String.IsNullOrWhiteSpace(request.method))
            throw new ArgumentException("EDITOR_TYPE_AND_METHOD_REQUIRED");

        Type target = null;
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type found = assembly.GetType(request.type, false);
            if (found == null) continue;
            if (target != null)
                throw new InvalidOperationException("EDITOR_TYPE_AMBIGUOUS");
            target = found;
        }
        if (target == null)
            throw new InvalidOperationException("EDITOR_TYPE_NOT_FOUND");

        MethodInfo method = null;
        foreach (MethodInfo candidate in target.GetMethods(
            BindingFlags.Static | BindingFlags.Public))
        {
            if (candidate.Name != request.method ||
                candidate.GetParameters().Length != 0 ||
                candidate.ContainsGenericParameters)
                continue;
            if (method != null)
                throw new InvalidOperationException("EDITOR_METHOD_AMBIGUOUS");
            method = candidate;
        }
        if (method == null)
            throw new InvalidOperationException("EDITOR_PUBLIC_STATIC_NOARG_METHOD_NOT_FOUND");

        object result;
        try { result = method.Invoke(null, null); }
        catch (TargetInvocationException e)
        {
            throw new InvalidOperationException("EDITOR_METHOD_THREW: " +
                (e.InnerException == null ? e.Message : e.InnerException.Message));
        }

        BuildReport build = result as BuildReport;
        if (build != null)
        {
            reply.value = build.summary.result.ToString();
            reply.detail = "BuildResult=" + reply.value + "; Errors=" +
                build.summary.totalErrors + "; Warnings=" +
                build.summary.totalWarnings + "; OutputPath=" +
                build.summary.outputPath;
        }
        else
        {
            reply.value = result == null ? "VOID" : result.ToString();
        }
        reply.success = true; // An invoked method still needs expected-value verification.
        reply.code = "EDITOR_METHOD_RETURNED";
        return reply;
    }
    private static Response ReadSceneStatus(Response reply)
    {
        Scene active = SceneManager.GetActiveScene();
        var detail = new StringBuilder();
        var total = 0;
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            if (!scene.isLoaded) continue;
            var roots = scene.GetRootGameObjects();
            total += roots.Length;
            detail.Append("[scene=").Append(scene.path)
                .Append(";loaded=").Append(scene.isLoaded)
                .Append(";dirty=").Append(scene.isDirty)
                .Append(";roots=").Append(roots.Length).Append(";names=");
            foreach (var root in roots)
                detail.Append(root.name).Append(",");
            detail.Append("] ");
        }
        reply.success = true;
        reply.code = "EDITOR_SCENE_STATE";
        reply.value = "ACTIVE=" + active.path +
            ";ACTIVE_LOADED=" + active.isLoaded +
            ";ACTIVE_DIRTY=" + (active.IsValid() && active.isLoaded && active.isDirty) +
            ";ACTIVE_ROOTS=" + (active.IsValid() && active.isLoaded
                ? active.GetRootGameObjects().Length : -1) +
            ";LOADED_ROOTS=" + total +
            ";PLAYING=" + EditorApplication.isPlaying +
            ";COMPILING=" + EditorApplication.isCompiling +
            ";UPDATING=" + EditorApplication.isUpdating;
        reply.detail = detail.ToString();
        return reply;
    }

    private static string ResolveScenePath(string relative)
    {
        if (String.IsNullOrWhiteSpace(relative) ||
            !relative.StartsWith("Assets/", StringComparison.Ordinal) ||
            !relative.EndsWith(".unity", StringComparison.OrdinalIgnoreCase) ||
            relative.Contains("\\") || relative.Contains("..") ||
            Path.IsPathRooted(relative) || relative.Contains(":"))
            throw new InvalidOperationException("SYNC_SCENE_PATH_NOT_ALLOWED");
        string root = Directory.GetParent(Application.dataPath).FullName;
        string full = Path.GetFullPath(Path.Combine(root, relative));
        string assets = Path.GetFullPath(Application.dataPath) +
            Path.DirectorySeparatorChar;
        if (!full.StartsWith(assets, StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(full))
            throw new InvalidOperationException("SYNC_SCENE_FILE_MISSING_OR_OUTSIDE_ASSETS");
        return full;
    }

    private static Response ReloadActiveSceneWithBackup(Response reply, string requestedScene)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode ||
            EditorApplication.isCompiling || EditorApplication.isUpdating)
            throw new InvalidOperationException("SYNC_SCENE_BUSY_EDITOR");
        var file = ResolveScenePath(requestedScene);
        Scene old = SceneManager.GetActiveScene();
        if (!old.IsValid() || !old.isLoaded ||
            !String.Equals(old.path, requestedScene, StringComparison.Ordinal))
            throw new InvalidOperationException(
                "SYNC_SCENE_NOT_ACTIVE: " + old.path);
        // The original on-disk version is always snapshotted outside Assets
        // before touching the in-memory scene.
        string project = Directory.GetParent(Application.dataPath).FullName;
        string backupFolder = Path.Combine(project, "Library", "ProjectHub",
            "SceneBackups");
        Directory.CreateDirectory(backupFolder);
        string unique = DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff") +
            "-" + Guid.NewGuid().ToString("N");
        string diskBackup = Path.Combine(backupFolder, unique + ".unity");
        File.Copy(file, diskBackup, false);

        string dirtyBackup = "NONE";
        if (old.isDirty)
        {
            // Unity SaveScene requires an Assets-relative destination.
            // SaveAsCopy keeps the original Scene path and does not accept
            // the pending changes as an implicit save of that original.
            const string baseFolder = "Assets/Editor/ProjectHub/SceneBackups";
            Directory.CreateDirectory(Path.Combine(project,
                baseFolder.Replace('/', Path.DirectorySeparatorChar)));
            dirtyBackup = baseFolder + "/" + unique + ".unity";
            if (!EditorSceneManager.SaveScene(old, dirtyBackup, true) ||
                !File.Exists(Path.Combine(project,
                    dirtyBackup.Replace('/', Path.DirectorySeparatorChar))))
                throw new InvalidOperationException(
                    "SYNC_SCENE_DIRTY_BACKUP_FAILED: aborting without reload");
        }

        // Do not call OpenSceneMode.Single: it can discard other loaded
        // scenes and their unrelated unsaved state.
        if (!EditorSceneManager.CloseScene(old, true))
            throw new InvalidOperationException("SYNC_SCENE_CLOSE_FAILED");
        Scene reopened = EditorSceneManager.OpenScene(
            requestedScene, OpenSceneMode.Additive);
        if (!reopened.IsValid() || !reopened.isLoaded ||
            !SceneManager.SetActiveScene(reopened))
            throw new InvalidOperationException(
                "SYNC_SCENE_REOPEN_FAILED: diskBackup=" + diskBackup +
                "; dirtyBackup=" + dirtyBackup);

        reply = ReadSceneStatus(reply);
        reply.code = "EDITOR_SCENE_RELOADED_FROM_DISK";
        reply.detail = "DISK_BACKUP=" + diskBackup +
            ";DIRTY_BACKUP=" + dirtyBackup + "; " + reply.detail;
        return reply;
    }

    private static Response RefreshAndInspectAssets(Response reply)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode ||
            EditorApplication.isCompiling || EditorApplication.isUpdating)
            throw new InvalidOperationException("REFRESH_ASSETS_BUSY_EDITOR");
        // May trigger an Editor domain reload on changed scripts. Such a
        // request is UNVERIFIED if no final response is received.
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        reply = ReadSceneStatus(reply);
        reply.code = "EDITOR_ASSET_REFRESH_RETURNED";
        reply.detail = "IMPORT_OR_COMPILE_MAY_CONTINUE_AFTER_RETURN; " +
            reply.detail;
        return reply;
    }


}
#endif
""";
}
