using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace ProjectHub.Worker;

/// <summary>
/// Single bounded restart at a user command boundary, never between milestones.
/// Uses the current project's CLI-reported Editor PID; refuses ambiguous or
/// foreign processes. No Kill(), taskkill, forced process termination or
/// automated response to Unity's unsaved-work dialog.
/// </summary>
internal static class UnityEditorStartupRestart
{
    internal sealed record Result(string Code, string Detail, bool Restarted)
    {
        internal bool Verified => Code is "EDITOR_RESTART_VERIFIED" or
            "EDITOR_RESTART_NOT_RUNNING";
    }

    internal sealed record Instance(int Pid, string Project, string State);

    internal static bool TryIdentifyOneInstance(
        string stdout, string projectRoot, out Instance? match, out string code)
    {
        match = null;
        code = "EDITOR_RESTART_STATUS_UNVERIFIED";
        try
        {
            using var json = JsonDocument.Parse(stdout);
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("success", out var ok) ||
                ok.ValueKind != JsonValueKind.True ||
                !root.TryGetProperty("data", out var data) ||
                data.ValueKind != JsonValueKind.Object ||
                !data.TryGetProperty("instances", out var items) ||
                items.ValueKind != JsonValueKind.Array)
                return false;
            var matches = new List<Instance>();
            foreach (var item in items.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object ||
                    !item.TryGetProperty("project", out var project) ||
                    project.ValueKind != JsonValueKind.String ||
                    !item.TryGetProperty("pid", out var pid) ||
                    !pid.TryGetInt32(out var processId))
                    continue;
                if (processId > 0 &&
                    SameProject(project.GetString(), projectRoot))
                    matches.Add(new(processId, project.GetString()!,
                        item.TryGetProperty("state", out var state) &&
                        state.ValueKind == JsonValueKind.String
                            ? state.GetString() ?? "" : ""));
            }
            if (matches.Count == 0)
            {
                code = "EDITOR_RESTART_NOT_RUNNING";
                return true;
            }
            if (matches.Count != 1)
            {
                code = "EDITOR_RESTART_AMBIGUOUS_PROJECT_INSTANCES";
                return false;
            }
            match = matches[0];
            code = "EDITOR_RESTART_INSTANCE_IDENTIFIED";
            return true;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException
            or ArgumentException) { return false; }
    }

    internal static bool SameProject(string? candidate, string root)
    {
        if (string.IsNullOrWhiteSpace(candidate)) return false;
        try
        {
            static string Canonical(string path) =>
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            return string.Equals(Canonical(candidate), Canonical(root),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException
            or PathTooLongException) { return false; }
    }

    internal static bool IsUnityEditorExecutable(string? file)
        => !string.IsNullOrWhiteSpace(file) &&
           string.Equals(Path.GetFileName(file), "Unity.exe",
               StringComparison.OrdinalIgnoreCase);

    internal static async Task<Result> RunAsync(
        string projectRoot, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
            return new("EDITOR_RESTART_WINDOWS_ONLY", "Windows-only operation", false);
        if (!UnityCliBuildRunner.IsUnityProject(projectRoot))
            return new("EDITOR_RESTART_PROJECT_INVALID", "Unity project not found", false);

        // No second process can be started or stopped on this project while
        // another Worker Editor command owns the gate.
        using var lease = await UnityEditorOperationGate.EnterAsync(
            projectRoot, cancellationToken).ConfigureAwait(false);
        var status = await UnityCliProcessRunner.RunAsync(projectRoot,
            ["status", "--project-path", projectRoot, "--format", "json"],
            TimeSpan.FromSeconds(20), "UnityWorker startup restart discovery",
            "EDITOR_RESTART_DISCOVERY_TIMEOUT", cancellationToken).ConfigureAwait(false);
        if (status.ExitCode != 0 ||
            !TryIdentifyOneInstance(status.Output, projectRoot, out var instance,
                out var discovery))
            return new("EDITOR_RESTART_UNVERIFIED",
                "Cannot safely identify a single Editor for this project: " +
                (status.ExitCode != 0
                    ? "CLI_EXIT=" + status.ExitCode + " " + Short(status.Error)
                    : discovery) + "; CLI_EVIDENCE=" + status.EvidencePath, false);
        if (instance is null)
            return new("EDITOR_RESTART_NOT_RUNNING",
                "No running Editor for the selected project; did not launch one.", false);

        try
        {
            using var oldEditor = Process.GetProcessById(instance.Pid);
            if (oldEditor.HasExited)
                return new("EDITOR_RESTART_RACE",
                    "Editor exited before it could be identified; no launch attempted.", false);
            string? executable;
            try { executable = oldEditor.MainModule?.FileName; }
            catch (Win32Exception)
            {
                return new("EDITOR_RESTART_ACCESS_DENIED",
                    "Cannot inspect selected Editor executable; no close attempted.", false);
            }
            if (!IsUnityEditorExecutable(executable) ||
                oldEditor.MainWindowHandle == IntPtr.Zero)
                return new("EDITOR_RESTART_PROCESS_UNVERIFIED",
                    "CLI-reported PID is not a verifiable GUI Unity.exe; no close attempted.",
                    false);

            // CloseMainWindow lets Unity prompt for unsaved scenes or other
            // user changes. Never answer such prompts or terminate forcibly.
            if (!oldEditor.CloseMainWindow())
                return new("EDITOR_RESTART_CLOSE_REFUSED",
                    "Unity refused a graceful close; no restart.", false);
            // After requesting closure, complete the bounded shutdown/relaunch
            // even if the task is canceled; otherwise cancellation could leave
            // the user's Editor closed without its replacement.
            using (var shutdownTimeout = new CancellationTokenSource(
                TimeSpan.FromSeconds(35)))
            {
                try
                {
                    await oldEditor.WaitForExitAsync(shutdownTimeout.Token)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return new("EDITOR_RESTART_UNSAVED_OR_BUSY",
                        "Unity did not exit in 35 s (unsaved-work dialog or busy Editor). " +
                        "No force-close and no duplicate Editor launched.", false);
                }
            }
            // The original process is gone. If cancellation occurred during
            // graceful close, still attempt to restore the user's Editor.
            var start = new ProcessStartInfo
            {
                FileName = executable!,
                WorkingDirectory = projectRoot,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Minimized
            };
            start.ArgumentList.Add("-projectPath");
            start.ArgumentList.Add(Path.GetFullPath(projectRoot));
            using var launched = Process.Start(start);
            if (launched is null)
                return new("EDITOR_RESTART_LAUNCH_FAILED",
                    "Old Editor closed but Process.Start did not return a new process.", false);
            var newPid = launched.Id;
            // A new PID is evidence of launch, NOT of Editor readiness. The
            // underlying CLI status must report the same project and PID.
            var readinessDeadline = DateTime.UtcNow.AddSeconds(105);
            while (DateTime.UtcNow < readinessDeadline)
            {
                await Task.Delay(1500).ConfigureAwait(false);
                var check = await UnityCliProcessRunner.RunAsync(projectRoot,
                    ["status", "--project-path", projectRoot, "--format", "json"],
                    TimeSpan.FromSeconds(10), "UnityWorker startup restart readiness",
                    "EDITOR_RESTART_STATUS_TIMEOUT", CancellationToken.None)
                    .ConfigureAwait(false);
                if (check.ExitCode == 0 &&
                    TryIdentifyOneInstance(check.Output, projectRoot,
                        out var observed, out _) &&
                    observed?.Pid == newPid &&
                    string.Equals(observed.State, "ready",
                        StringComparison.OrdinalIgnoreCase))
                {
                    return new("EDITOR_RESTART_VERIFIED",
                        "Previous Editor PID " + instance.Pid +
                        " exited; same-project Editor PID " + newPid +
                        " detected in CLI status; EVIDENCE=" + check.EvidencePath, true);
                }
                // Unity Hub wrappers can hand the project to another child
                // process. Never launch another Editor merely because the
                // initial process exited; wait for a verifiable status.
            }
            return new("EDITOR_RESTART_READY_UNVERIFIED",
                "Unity launched with PID " + newPid +
                " but CLI did not confirm readiness within 105 s.", false);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or
            Win32Exception or UnauthorizedAccessException or IOException)
        {
            return new("EDITOR_RESTART_FAILED",
                ex.GetType().Name + ": " + Short(ex.Message) +
                "; do not force-kill or automatically retry.", false);
        }
    }

    private static string Short(string text) =>
        text.Length <= 600 ? text.Trim() : text[..600].Trim();
}
