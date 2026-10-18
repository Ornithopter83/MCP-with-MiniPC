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

    // Launch through the Windows shell rather than treating a GUI Unity Editor
    // as a redirected CLI child. A visible window makes launch failures clear.
    internal static ProcessStartInfo CreateLaunchStartInfo(string executable, string root)
    {
        if (!IsUnityEditorExecutable(executable) || !File.Exists(executable))
            throw new FileNotFoundException("UNITY_EDITOR_EXE_NOT_FOUND", executable);
        var info = new ProcessStartInfo
        {
            FileName = Path.GetFullPath(executable),
            WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(executable))!,
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Normal
        };
        info.ArgumentList.Add("-projectPath");
        info.ArgumentList.Add(Path.GetFullPath(root));
        return info;
    }

    private static string RememberedExecutablePath(string root) =>
        Path.Combine(UnityEditorDirectBridge.QueueDirectory(root),
            "last-editor-executable.txt");

    internal static void RememberExecutable(string root, string executable)
    {
        if (!IsUnityEditorExecutable(executable) || !File.Exists(executable))
            return;
        try
        {
            var record = RememberedExecutablePath(root);
            Directory.CreateDirectory(Path.GetDirectoryName(record)!);
            File.WriteAllText(record, Path.GetFullPath(executable),
                ProjectHubJson.Utf8NoBom);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    // A new task must also recover if a previous attempt closed Unity but
    // failed to start its replacement. The Unity version is from this project.
    internal static string? FindExecutableForStoppedEditor(string root)
    {
        var candidates = new List<string>();
        try
        {
            var cached = RememberedExecutablePath(root);
            if (File.Exists(cached))
                candidates.Add(File.ReadAllText(cached).Trim());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        var overridePath = Environment.GetEnvironmentVariable("UNITY_EDITOR_PATH");
        if (!string.IsNullOrWhiteSpace(overridePath))
            candidates.Add(overridePath);

        var versionFile = Path.Combine(root, "ProjectSettings", "ProjectVersion.txt");
        if (File.Exists(versionFile))
        {
            try
            {
                var line = File.ReadLines(versionFile).FirstOrDefault(x =>
                    x.StartsWith("m_EditorVersion:", StringComparison.Ordinal));
                var version = line?.Split(':', 2)[1].Trim();
                // Treat metadata only as a directory name, never as a command.
                if (!string.IsNullOrWhiteSpace(version) &&
                    version.All(x => char.IsAsciiLetterOrDigit(x) || x is '.' or '_' or '-'))
                {
                    foreach (var programs in new[]
                    {
                        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
                    })
                    {
                        candidates.Add(Path.Combine(programs, "Unity", "Hub", "Editor",
                            version, "Editor", "Unity.exe"));
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return candidates.FirstOrDefault(x =>
            IsUnityEditorExecutable(x) && File.Exists(x));
    }

    internal static async Task<Result> RunAsync(
        string projectRoot, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
            return new("EDITOR_RESTART_WINDOWS_ONLY", "Windows-only operation", false);
        if (!UnityCliBuildRunner.IsUnityProject(projectRoot))
            return new("EDITOR_RESTART_PROJECT_INVALID", "Unity project not found", false);

        using var lease = await UnityEditorOperationGate.EnterAsync(
            projectRoot, cancellationToken).ConfigureAwait(false);
        var status = await UnityCliProcessRunner.RunAsync(projectRoot,
            ["status", "--project-path", projectRoot, "--format", "json"],
            TimeSpan.FromSeconds(20), "UnityWorker startup restart discovery",
            "EDITOR_RESTART_DISCOVERY_TIMEOUT", cancellationToken).ConfigureAwait(false);
        if (status.ExitCode != 0)
            return new("EDITOR_RESTART_UNVERIFIED",
                "Cannot identify Editor: CLI_EXIT=" + status.ExitCode + " " +
                Short(status.Error) + "; EVIDENCE=" + status.EvidencePath, false);
        if (!TryIdentifyOneInstance(status.Output, projectRoot, out var instance,
                out var discovery))
            return new("EDITOR_RESTART_UNVERIFIED",
                "Cannot identify Editor: " + discovery +
                "; EVIDENCE=" + status.EvidencePath, false);

        string? executable = null;
        int? previousPid = null;
        try
        {
            if (instance is not null)
            {
                using var oldEditor = Process.GetProcessById(instance.Pid);
                if (oldEditor.HasExited)
                    return new("EDITOR_RESTART_RACE",
                        "Old Editor exited before identification; no second launch.", false);
                try { executable = oldEditor.MainModule?.FileName; }
                catch (Win32Exception)
                {
                    return new("EDITOR_RESTART_ACCESS_DENIED",
                        "Cannot inspect Unity.exe of reported PID; no close.", false);
                }
                if (!IsUnityEditorExecutable(executable) ||
                    oldEditor.MainWindowHandle == IntPtr.Zero)
                    return new("EDITOR_RESTART_PROCESS_UNVERIFIED",
                        "CLI PID is not a verifiable GUI Unity.exe; no close.", false);

                // Persist before closing so a subsequent user command can
                // recover from a failed launch, even when no Editor runs.
                RememberExecutable(projectRoot, executable!);
                previousPid = instance.Pid;
                cancellationToken.ThrowIfCancellationRequested();
                if (!oldEditor.CloseMainWindow())
                    return new("EDITOR_RESTART_CLOSE_REFUSED",
                        "Unity refused a graceful close; no duplicate launch.", false);
                using var shutdownTimeout = new CancellationTokenSource(
                    TimeSpan.FromSeconds(35));
                try
                {
                    // Finish a close/relaunch transaction even if the job's
                    // cancellation arrives after closing the window.
                    await oldEditor.WaitForExitAsync(shutdownTimeout.Token)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return new("EDITOR_RESTART_UNSAVED_OR_BUSY",
                        "Unity did not exit within 35 seconds. No force-kill.", false);
                }
            }
            else
            {
                // A missing CLI instance is not necessarily a stopped
                // Editor. An active UnityLockfile blocks launching a duplicate.
                if (UnityEditorCommandLifecycle.ProjectEditorLockHeld(projectRoot))
                    return new("EDITOR_RESTART_PROJECT_LOCKED",
                        "Editor lock held but no CLI PID; refusing a duplicate.", false);
                executable = FindExecutableForStoppedEditor(projectRoot);
                if (executable is null)
                    return new("EDITOR_START_EXE_NOT_FOUND",
                        "No Editor is running and no cached/default Unity.exe was found. " +
                        "Set UNITY_EDITOR_PATH or open the correct Editor once.", false);
            }

            // This is a desktop GUI startup. Never attach to the short-lived
            // CLI Job Object, never use -batchmode/-quit, and do not hide the window.
            using var launched = Process.Start(
                CreateLaunchStartInfo(executable!, projectRoot));
            if (launched is null)
                return new("EDITOR_RESTART_LAUNCH_FAILED",
                    "Previous Editor closed (if present), but Windows shell returned " +
                    "no Unity process. EXE=" + executable, false);
            var launchedPid = launched.Id;
            var lastDiagnostic = "NO_CLI_OBSERVATION";
            var deadline = DateTime.UtcNow.AddSeconds(120);
            while (DateTime.UtcNow < deadline)
            {
                await Task.Delay(1500).ConfigureAwait(false);
                var check = await UnityCliProcessRunner.RunAsync(projectRoot,
                    ["status", "--project-path", projectRoot, "--format", "json"],
                    TimeSpan.FromSeconds(10), "UnityWorker Editor startup readiness",
                    "EDITOR_RESTART_STATUS_TIMEOUT", CancellationToken.None)
                    .ConfigureAwait(false);
                lastDiagnostic = "STATUS_EXIT=" + check.ExitCode +
                    "; EVIDENCE=" + check.EvidencePath;
                if (check.ExitCode == 0 &&
                    TryIdentifyOneInstance(check.Output, projectRoot,
                        out var observed, out var code) &&
                    observed is not null &&
                    observed.Pid != previousPid &&
                    string.Equals(observed.State, "ready",
                        StringComparison.OrdinalIgnoreCase) &&
                    IsMatchingNewUnityEditor(observed.Pid, executable!))
                    return new(previousPid.HasValue
                            ? "EDITOR_RESTART_VERIFIED" : "EDITOR_STARTED_VERIFIED",
                        "Unity GUI launched; previous PID=" +
                        (previousPid?.ToString() ?? "NONE") +
                        "; shell PID=" + launchedPid +
                        "; Editor PID=" + observed.Pid +
                        "; " + lastDiagnostic, true);
                if (check.ExitCode == 0 &&
                    !TryIdentifyOneInstance(check.Output, projectRoot,
                        out _, out var reason))
                    lastDiagnostic += "; " + reason;
            }

            // Never spawn a second Unity merely because the shell process
            // handed control to a different PID. Report exact diagnostics.
            string exitDetail = launched.HasExited
                ? "EXIT_CODE=" + launched.ExitCode : "SHELL_PROCESS_ALIVE";
            return new("EDITOR_RESTART_READY_UNVERIFIED",
                "Unity shell startup PID=" + launchedPid + "; " + exitDetail +
                "; EXE=" + executable + "; " + lastDiagnostic +
                "; no automatic duplicate launch.", false);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or
            Win32Exception or UnauthorizedAccessException or IOException)
        {
            return new(previousPid.HasValue
                    ? "EDITOR_RESTART_LAUNCH_FAILED" : "EDITOR_START_LAUNCH_FAILED",
                "Windows Unity GUI launch failed; oldPID=" +
                (previousPid?.ToString() ?? "NONE") +
                "; EXE=" + (executable ?? "UNKNOWN") +
                "; " + ex.GetType().Name +
                (ex is Win32Exception win ? "(" + win.NativeErrorCode + ")" : "") +
                ": " + Short(ex.Message), false);
        }
    }

    private static bool IsMatchingNewUnityEditor(int pid, string executable)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited && IsUnityEditorExecutable(process.MainModule?.FileName) &&
                SameProjectExecutable(process.MainModule?.FileName, executable);
        }
        catch (Exception ex) when (ex is ArgumentException or Win32Exception or
            InvalidOperationException or UnauthorizedAccessException) { return false; }
    }

    private static bool SameProjectExecutable(string? first, string second)
    {
        if (first is null) return false;
        try
        {
            return string.Equals(Path.GetFullPath(first), Path.GetFullPath(second),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException) { return false; }
    }

    private static string Short(string text) =>
        text.Length <= 600 ? text.Trim() : text[..600].Trim();
}
