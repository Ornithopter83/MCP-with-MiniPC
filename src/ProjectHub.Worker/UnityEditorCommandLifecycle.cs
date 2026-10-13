using System.Diagnostics;
using System.IO;

namespace ProjectHub.Worker;

/// <summary>
/// Invoked only when a user command is accepted for execution or queued.
/// The "new task" dashboard navigation never opens Unity.
/// </summary>
internal static class UnityEditorCommandLifecycle
{
    internal sealed record LaunchResult(bool Success, string Detail);

    internal static string[] OpenArguments(string root) =>
        new[] { "open", Path.GetFullPath(root) };

    internal static bool CanAutoOpen(UnityToolchainPreflight.Result result) =>
        result.Code == "UNITY_EDITOR_UNAVAILABLE" &&
        (result.Detail.Contains("No Pipeline instance found", StringComparison.OrdinalIgnoreCase) ||
         result.Detail.Contains("STATUS_NO_INSTANCES", StringComparison.OrdinalIgnoreCase));

    internal static bool ProjectEditorLockHeld(string root)
    {
        // Unity protects a project against simultaneous Editors via its lock.
        // An existing active lock takes precedence over Pipeline connectivity:
        // Safe Mode and package initialization can hide a running Editor.
        foreach (var relative in new[] { "Temp/UnityLockfile", "Library/UnityLockfile" })
        {
            var file = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(file)) continue;
            try
            {
                using var stream = new FileStream(file, FileMode.Open,
                    FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) { return true; }
            catch (UnauthorizedAccessException) { return true; }
        }
        return false;
    }

    internal static Task<UnityToolchainPreflight.Result> EnsureForAcceptedCommandAsync(
        string root, CancellationToken cancellationToken) =>
        EnsureForAcceptedCommandAsync(
            root, cancellationToken, UnityToolchainPreflight.RunAsync,
            OpenEditorAsync, ProjectEditorLockHeld,
            (delay, token) => Task.Delay(delay, token),
            maxChecks: 25);

    // Injectable boundaries make the command-only trigger and no-double-open
    // logic testable without starting a real Unity Editor in CI.
    internal static async Task<UnityToolchainPreflight.Result> EnsureForAcceptedCommandAsync(
        string root,
        CancellationToken cancellationToken,
        Func<string, CancellationToken, Task<UnityToolchainPreflight.Result>> probe,
        Func<string, CancellationToken, Task<LaunchResult>> open,
        Func<string, bool> editorLockHeld,
        Func<TimeSpan, CancellationToken, Task> wait,
        int maxChecks)
    {
        var project = UnityToolchainPreflight.ValidateProject(root);
        if (!project.Ready) return project;

        var first = await probe(root, cancellationToken).ConfigureAwait(false);
        if (first.Ready || !CanAutoOpen(first)) return first;

        // Do not start a second Editor when the project is already locked.
        // Its Pipeline could be importing packages or in Safe Mode.
        var locked = editorLockHeld(root);
        if (!locked)
        {
            var launched = await open(root, cancellationToken).ConfigureAwait(false);
            if (!launched.Success)
                return new(false, "UNITY_EDITOR_AUTO_OPEN_FAILED", launched.Detail);
        }

        var last = first;
        for (var attempt = 0; attempt < maxChecks; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await wait(TimeSpan.FromSeconds(4), cancellationToken)
                .ConfigureAwait(false);
            last = await probe(root, cancellationToken).ConfigureAwait(false);
            if (last.Ready) return last;
            if (!CanAutoOpen(last)) return last;
        }

        return new(false,
            locked ? "UNITY_EDITOR_RUNNING_PIPELINE_UNAVAILABLE" :
                     "UNITY_EDITOR_AUTO_OPEN_TIMEOUT",
            "Unity Editor " + (locked ? "프로젝트 잠금이 감지되어 중복 실행하지 않았습니다. " :
                "자동 실행 후 ") +
            "Pipeline 연결을 확인하지 못했습니다. 마지막 진단: " + last.Detail);
    }

    private static async Task<LaunchResult> OpenEditorAsync(
        string root, CancellationToken cancellationToken)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "unity",
                WorkingDirectory = Path.GetFullPath(root),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            foreach (var argument in OpenArguments(root))
                psi.ArgumentList.Add(argument);

            // Unlike a worker-owned CLI Job, an opened GUI Editor must survive
            // after the short-lived 'unity open' launcher exits.
            using var process = new Process { StartInfo = psi };
            if (!process.Start())
                return new(false, "unity open 프로세스 시작 실패");

            var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, timeout.Token);
            try
            {
                await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Terminate just the CLI launcher, never the detached Editor.
                try { if (!process.HasExited) process.Kill(); } catch { }
                if (cancellationToken.IsCancellationRequested) throw;
                return new(false, "unity open 명령이 45초 안에 반환하지 않았습니다.");
            }
            var err = await stderr.ConfigureAwait(false);
            var output = await stdout.ConfigureAwait(false);
            return process.ExitCode == 0
                ? new(true, output.Length == 0 ? "unity open launched" : output)
                : new(false, "unity open exit=" + process.ExitCode + " " +
                    (err.Length == 0 ? output : err));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or
                                  System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return new(false, "unity open 실행 오류: " + e.Message);
        }
    }
}
