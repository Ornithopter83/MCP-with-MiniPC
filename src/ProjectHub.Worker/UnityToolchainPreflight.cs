using System.Diagnostics;
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
            string? commandCatalogJson = null;
            if (document.RootElement.TryGetProperty("data", out var data) &&
                data.ValueKind == JsonValueKind.Object &&
                data.TryGetProperty("commands", out var commands) &&
                commands.ValueKind == JsonValueKind.Array)
            {
                // Store the exact command/parameter records returned by this
                // Editor, without AI reconstruction or schema guessing.
                commandCatalogJson = commands.GetRawText();
            }
            return new(true, "UNITY_EDITOR_CONNECTED",
                "지정 프로젝트에 대한 Unity CLI 명령 목록 요청 성공 (MCP 호출 검증과 별개)",
                commandCatalogJson);
        }
        catch (JsonException)
        {
            return new(false, "UNITY_EDITOR_INVALID_RESPONSE",
                "Unity CLI의 출력이 올바른 JSON이 아닙니다: " + ShortDetail(stdout));
        }
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
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "unity",
                WorkingDirectory = projectRoot,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            foreach (var arg in EditorProbeArguments(projectRoot))
                psi.ArgumentList.Add(arg);
            using var job = new WorkerChildProcessJob("UnityWorker Editor preflight");
            using var child = job.Start(psi, cancellationToken);
            var stdout = child.StandardOutput!.ReadToEndAsync(cancellationToken);
            var stderr = child.StandardError!.ReadToEndAsync(cancellationToken);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, timeout.Token);
            try
            {
                await child.Process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
                return InterpretEditorProbe(child.Process.ExitCode,
                    await stdout.ConfigureAwait(false),
                    await stderr.ConfigureAwait(false));
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                job.Dispose();
                return new(false, "UNITY_EDITOR_PREFLIGHT_TIMEOUT",
                    "30초 안에 Editor 연결을 확인하지 못했습니다.");
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new(false, "UNITY_CLI_LAUNCH_FAILED",
                "Unity CLI를 실행할 수 없습니다: " + ex.GetType().Name + ": " + ShortDetail(ex.Message));
        }
    }

    private static string ShortDetail(string value)
    {
        var text = value.Trim();
        return text.Length <= 400 ? text : text[..400];
    }
}
