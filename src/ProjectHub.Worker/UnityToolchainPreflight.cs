using System.IO;
using System.Text.Json;

namespace ProjectHub.Worker;

/// <summary>
/// Non-mutating project and Editor connectivity check performed before HQ dispatch.
/// Connecting to an Editor is not proof that Codex's own MCP sandbox works.
/// </summary>
internal static class UnityToolchainPreflight
{
    // CommandCatalogJson is read from the same successful live probe, not
    // inferred from remembered CLI syntax or past Editor sessions.
    internal sealed record Result(bool Ready, string Code, string Detail,
        string? CommandCatalogJson = null);

    internal static string CatalogPath(string root, string jobId) =>
        Path.Combine(Path.GetFullPath(root), "temp", "ProjectHub", jobId,
            "unity-command-catalog.json");

    // Information-only cache, scoped to this user job. Missing/invalid data
    // never blocks an otherwise valid Editor connection or role execution.
    internal static string? SaveCatalogForJob(
        string root, string jobId, Result probe)
    {
        var path = CatalogPath(root, jobId);
        try
        {
            if (!probe.Ready || string.IsNullOrWhiteSpace(probe.CommandCatalogJson))
            {
                if (File.Exists(path)) File.Delete(path);
                return null;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, probe.CommandCatalogJson, ProjectHubJson.Utf8NoBom);
            return path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    internal static Result ValidateProject(string root)
    {
        if (!UnityCliBuildRunner.IsUnityProject(root))
            return new(false, "UNITY_PROJECT_NOT_FOUND",
                "Assets/, Packages/manifest.json, ProjectSettings/ProjectVersion.txt를 포함한 Unity 프로젝트 루트를 선택하세요.");
        try
        {
            var manifest = Path.Combine(root, "Packages", "manifest.json");
            using var document = JsonDocument.Parse(File.ReadAllText(manifest));
            if (!document.RootElement.TryGetProperty("dependencies", out var deps) ||
                deps.ValueKind != JsonValueKind.Object ||
                !deps.TryGetProperty("com.unity.pipeline", out var pipeline) ||
                pipeline.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(pipeline.GetString()))
                return new(false, "UNITY_PIPELINE_NOT_DECLARED",
                    "Packages/manifest.json에 com.unity.pipeline 패키지가 선언되어 있지 않습니다.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new(false, "UNITY_MANIFEST_UNREADABLE", ex.Message);
        }
        return new(true, "UNITY_PROJECT_READY", "Unity 프로젝트와 Pipeline 의존성 확인");
    }

    internal static string[] EditorProbeArguments(string root) =>
        new[] { "command", "--project-path", Path.GetFullPath(root), "--format", "json" };

    internal static Result InterpretEditorProbe(int exitCode, string stdout, string stderr)
    {
        if (exitCode != 0)
            return new(false, "UNITY_EDITOR_UNAVAILABLE",
                "Unity CLI exit " + exitCode + ": " + ShortDetail(stderr.Length > 0 ? stderr : stdout));
        try
        {
            using var document = JsonDocument.Parse(stdout);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("success", out var success) ||
                success.ValueKind != JsonValueKind.True)
                return new(false, "UNITY_EDITOR_UNVERIFIED",
                    "Unity CLI의 성공 JSON 응답을 확인하지 못했습니다: " + ShortDetail(stdout));
            var commandCatalogJson = ExtractCommandCatalog(document.RootElement);
            return new(true,
                commandCatalogJson is null ? "UNITY_EDITOR_CONNECTED_NO_CATALOG" :
                    "UNITY_EDITOR_CONNECTED",
                commandCatalogJson is null
                    ? "Unity CLI 요청 성공, 명령 목록은 확인되지 않음; " +
                      "실제 Editor 명령 실행 가능 여부는 별도 조회 필요"
                    : "Unity CLI 명령 목록 확인 (실제 Editor 명령 실행과 별개)",
                commandCatalogJson);
        }
        catch (JsonException)
        {
            return new(false, "UNITY_EDITOR_INVALID_RESPONSE",
                "Unity CLI의 출력이 올바른 JSON이 아닙니다: " + ShortDetail(stdout));
        }
    }

    // Only preserve explicit command collections, never guess a command from
    // an echoed request name or an unrelated status response.
    internal static string? ExtractCommandCatalog(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) return null;
        if (root.TryGetProperty("data", out var data))
        {
            if (data.ValueKind == JsonValueKind.Array)
                return data.GetArrayLength() > 0 ? data.GetRawText() : null;
            if (data.ValueKind == JsonValueKind.Object)
                foreach (var key in new[] { "commands", "items", "results" })
                    if (data.TryGetProperty(key, out var records) &&
                        records.ValueKind == JsonValueKind.Array)
                        return records.GetArrayLength() > 0
                            ? records.GetRawText() : null;
        }
        if (root.TryGetProperty("commands", out var direct) &&
            direct.ValueKind == JsonValueKind.Array)
            return direct.GetArrayLength() > 0 ? direct.GetRawText() : null;
        return null;
    }

    // A detailed command query may return an object rather than an array.
    // Success=true is insufficient: the data must identify the exact command.
    internal static bool DetailedCommandConfirmed(
        int exitCode, string stdout, string requested)
    {
        if (exitCode != 0) return false;
        try
        {
            using var json = JsonDocument.Parse(stdout);
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("success", out var success) ||
                success.ValueKind != JsonValueKind.True ||
                !root.TryGetProperty("data", out var data)) return false;
            if (data.ValueKind == JsonValueKind.Object)
            {
                foreach (var key in new[] { "name", "command" })
                    if (data.TryGetProperty(key, out var name) &&
                        name.ValueKind == JsonValueKind.String &&
                        string.Equals(name.GetString(), requested, StringComparison.Ordinal))
                        return true;
                if (data.TryGetProperty("commands", out var commands) &&
                    commands.ValueKind == JsonValueKind.Array)
                    return CatalogContainsCommand(commands.GetRawText(), requested);
            }
            if (data.ValueKind == JsonValueKind.Array)
                return CatalogContainsCommand(data.GetRawText(), requested);
        }
        catch (JsonException) { }
        return false;
    }

    // A command is executable only if the current Editor declared it.
    // Do not infer capabilities from remembered Unity command names.
    internal static bool CatalogContainsCommand(string? catalogJson, string command)
    {
        if (string.IsNullOrWhiteSpace(catalogJson) ||
            string.IsNullOrWhiteSpace(command)) return false;
        try
        {
            using var document = JsonDocument.Parse(catalogJson);
            if (document.RootElement.ValueKind != JsonValueKind.Array) return false;
            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String &&
                    string.Equals(item.GetString(), command, StringComparison.Ordinal))
                    return true;
                if (item.ValueKind != JsonValueKind.Object) continue;
                foreach (var key in new[] { "name", "command" })
                    if (item.TryGetProperty(key, out var value) &&
                        value.ValueKind == JsonValueKind.String &&
                        string.Equals(value.GetString(), command, StringComparison.Ordinal))
                        return true;
            }
        }
        catch (JsonException) { }
        return false;
    }

    public static Task<Result> RunAsync(string root, CancellationToken cancellationToken) =>
        RunProbeAsync(root, cancellationToken, useEditorGate: true);

    // Read-only connectivity probe for the job-scoped recovery supervisor.
    // AI roles can own the cooperative Editor lane for their entire run;
    // monitoring must not wait for that lane to become free.
    internal static Task<Result> RunUncoordinatedAsync(
        string root, CancellationToken cancellationToken) =>
        RunProbeAsync(root, cancellationToken, useEditorGate: false);

    private static async Task<Result> RunProbeAsync(
        string root, CancellationToken cancellationToken, bool useEditorGate)
    {
        var project = ValidateProject(root);
        if (!project.Ready)
            return project;

        var projectRoot = Path.GetFullPath(root);
        using var editorLease = useEditorGate
            ? await UnityEditorOperationGate.EnterAsync(
                projectRoot, cancellationToken).ConfigureAwait(false)
            : null;
        var execution = await UnityCliProcessRunner.RunAsync(
            projectRoot, EditorProbeArguments(projectRoot),
            TimeSpan.FromSeconds(30), "UnityWorker Editor preflight",
            "UNITY_EDITOR_PREFLIGHT_TIMEOUT", cancellationToken).ConfigureAwait(false);
        var parsed = InterpretEditorProbe(
            execution.ExitCode, execution.Output, execution.Error);
        return parsed with
        {
            Detail = parsed.Detail + "; CLI_EVIDENCE=" + execution.EvidencePath
        };
    }

    private static string ShortDetail(string value)
    {
        var text = value.Trim();
        return text.Length <= 400 ? text : text[..400];
    }
}
