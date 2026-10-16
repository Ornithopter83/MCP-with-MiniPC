using System.IO;
using System.Text.Json;

namespace ProjectHub.Worker;

internal sealed record UnitySceneCliResult(bool Success, string Detail);

/// <summary>
/// Edits the connected Unity Editor, never serialized Unity YAML or a second
/// batchmode instance. Callers MUST own UnityEditorOperationGate for the entire
/// open -> agent edits -> save transaction. The agent discovers the runtime
/// command catalog; these idempotent Editor-side eval operations provide an
/// additional mechanical open/save boundary without guessing CLI command flags.
/// </summary>
internal static class UnitySceneCliWorkflow
{
    internal static bool TryNormalizeScenePath(string? path, out string normalized)
    {
        normalized = (path ?? "").Replace('\\', '/').Trim();
        var parts = normalized.Split('/');
        return parts.Length >= 3 &&
            parts[0].Equals("Assets", StringComparison.OrdinalIgnoreCase) &&
            parts.All(p => p.Length > 0 && p is not "." and not "..") &&
            normalized.EndsWith(".unity", StringComparison.OrdinalIgnoreCase) &&
            !Path.IsPathRooted(normalized);
    }

    internal static string DirectSceneInstructions(string path, bool readOnly = false) =>
        "연결된 기존 Unity Editor를 직접 사용하라. 먼저 unity command --project-path " +
        "<PROJECT_ROOT> --format json 으로 명령/파라미터를 확인하고 " +
        "open_scene, get_scene_hierarchy, create_gameobject, set_transform, " +
        "add_component, save_scene 등 실제 등록된 명령을 우선 사용한다. " +
        "버전에 없는 명령/인수는 추측하지 말고 명령 카탈로그를 확인한다. " +
        "대상 씬: " + path + ". " +
        (readOnly
            ? "읽기 전용이므로 Scene/Prefab/Editor 상태를 변경하거나 저장하지 마라. "
            : "기존 씬을 열어 필요한 오브젝트·컴포넌트를 실제 Editor에서 연결한 뒤 " +
              "save_scene 또는 EditorSceneManager.SaveScene으로 저장하고 " +
              "재조회하여 객체/참조와 저장 성공 증거를 기록하라. ") +
        "Unity.exe -batchmode 및 별도 프로젝트 복제, .unity YAML 수동 편집은 " +
        "공식 Editor 연결로 해결할 수 없을 때만 대안으로 HQ에 보고하라. " +
        "Unity CLI와 Unity MCP는 동일 Editor 자원을 공유하므로 명령을 병렬 호출하지 마라.";

    internal static string OpenExpression(string scenePath)
    {
        var literal = JsonSerializer.Serialize(scenePath);
        return "if (UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode) " +
            "throw new System.InvalidOperationException(\"PROJECTHUB_EDITOR_PLAYING\"); " +
            "var phScenePath = " + literal + "; " +
            "var phScene = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(phScenePath); " +
            "if (!phScene.IsValid() || !phScene.isLoaded) " +
            "phScene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(" +
            "phScenePath, UnityEditor.SceneManagement.OpenSceneMode.Additive); " +
            "if (!UnityEngine.SceneManagement.SceneManager.SetActiveScene(phScene)) " +
            "throw new System.InvalidOperationException(\"PROJECTHUB_SCENE_ACTIVATION_FAILED\"); " +
            "return \"PROJECTHUB_SCENE_OPEN_OK|\" + phScene.path;";
    }

    internal static string SaveExpression(string scenePath)
    {
        var literal = JsonSerializer.Serialize(scenePath);
        return "if (UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode) " +
            "throw new System.InvalidOperationException(\"PROJECTHUB_EDITOR_PLAYING\"); " +
            "var phScenePath = " + literal + "; " +
            "var phScene = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(phScenePath); " +
            "if (!phScene.IsValid() || !phScene.isLoaded) " +
            "throw new System.InvalidOperationException(\"PROJECTHUB_SCENE_NOT_OPEN\"); " +
            "if (!UnityEditor.SceneManagement.EditorSceneManager.SaveScene(phScene)) " +
            "throw new System.InvalidOperationException(\"PROJECTHUB_SCENE_SAVE_FAILED\"); " +
            "return \"PROJECTHUB_SCENE_SAVED|\" + phScene.path;";
    }

    internal static Task<UnitySceneCliResult> OpenAsync(
        string root, string scenePath, CancellationToken token,
        Func<IReadOnlyList<string>, CancellationToken, Task<UnityAssetCliResult>>? execute = null) =>
        InvokeAsync(root, scenePath, "PROJECTHUB_SCENE_OPEN_OK|",
            OpenExpression, token, execute);

    internal static Task<UnitySceneCliResult> SaveAsync(
        string root, string scenePath, CancellationToken token,
        Func<IReadOnlyList<string>, CancellationToken, Task<UnityAssetCliResult>>? execute = null) =>
        InvokeAsync(root, scenePath, "PROJECTHUB_SCENE_SAVED|",
            SaveExpression, token, execute);

    private static async Task<UnitySceneCliResult> InvokeAsync(
        string root, string scenePath, string marker,
        Func<string, string> expression,
        CancellationToken token,
        Func<IReadOnlyList<string>, CancellationToken, Task<UnityAssetCliResult>>? execute)
    {
        if (!TryNormalizeScenePath(scenePath, out var normalized))
            return new(false, "UNITY_SCENE_PATH_INVALID: " + scenePath);

        execute ??= (args, cancellationToken) => ExecuteAsync(root, args, cancellationToken);
        try
        {
            // The CLI talks to the already connected project's Editor.
            var result = await execute(
                UnityAssetCliImporter.ImportArguments(root, expression(normalized)),
                token).ConfigureAwait(false);
            if (result.ExitCode != 0)
                return new(false, "UNITY_SCENE_COMMAND_UNCERTAIN_EXIT_" +
                    result.ExitCode + ": " + Limit(result.Error + " " + result.Output));
            using var json = JsonDocument.Parse(result.Output);
            if (!json.RootElement.TryGetProperty("success", out var success) ||
                success.ValueKind != JsonValueKind.True ||
                !json.RootElement.TryGetProperty("data", out var data) ||
                !FindResult(data, out var actual) ||
                !string.Equals(actual, marker + normalized, StringComparison.Ordinal))
                return new(false, "UNITY_SCENE_RESULT_UNVERIFIED: " + Limit(result.Output));
            return new(true, marker + normalized);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
            JsonException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return new(false, "UNITY_SCENE_COMMAND_ERROR: " + ex.Message);
        }
    }

    private static bool FindResult(JsonElement data, out string value)
    {
        value = "";
        if (data.ValueKind == JsonValueKind.String)
        {
            value = data.GetString() ?? "";
            return true;
        }
        if (data.ValueKind != JsonValueKind.Object)
            return false;
        foreach (var key in new[] { "result", "value", "output" })
            if (data.TryGetProperty(key, out var child) &&
                FindResult(child, out value))
                return true;
        return false;
    }

    private static Task<UnityAssetCliResult> ExecuteAsync(
        string root, IReadOnlyList<string> args, CancellationToken token) =>
        UnityCliProcessRunner.RunAsync(root, args, TimeSpan.FromMinutes(4),
            "UnityWorker scene command",
            "UNITY_SCENE_CLI_TIMEOUT: 실행 여부가 불확실해 자동 재시도 금지", token);

    private static string Limit(string value) =>
        value.Length <= 500 ? value.Trim() : value[..500].Trim() + "...";
}
