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
    internal sealed record Result(bool Ready, string Code, string Detail);

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
            return new(true, "UNITY_EDITOR_CONNECTED",
                "지정 프로젝트에 대한 Unity CLI 명령 목록 요청 성공 (MCP 호출 검증과 별개)");
        }
        catch (JsonException)
        {
            return new(false, "UNITY_EDITOR_INVALID_RESPONSE",
                "Unity CLI의 출력이 올바른 JSON이 아닙니다: " + ShortDetail(stdout));
        }
    }

    public static async Task<Result> RunAsync(string root, CancellationToken cancellationToken)
    {
        var project = ValidateProject(root);
        if (!project.Ready)
            return project;

        var projectRoot = Path.GetFullPath(root);
        using var editorLease = await UnityEditorOperationGate.EnterAsync(
            projectRoot, cancellationToken).ConfigureAwait(false);
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
