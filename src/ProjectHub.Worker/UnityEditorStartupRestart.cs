using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace ProjectHub.Worker;

/// <summary>
/// First action of a new Unity job: gracefully restart the single matching
/// GUI Editor or start one if stopped. Never require a working Pipeline CLI.
/// Never force-terminate, guess between windows, or start a duplicate.
/// </summary>
internal static class UnityEditorStartupRestart
{
    internal sealed record Result(string Code, string Detail, bool Restarted)
    {
        internal bool Verified => Code is "EDITOR_RESTART_VERIFIED" or
            "EDITOR_STARTED_VERIFIED";
    }

    internal static bool IsUnityEditorExecutable(string? file) =>
        !string.IsNullOrWhiteSpace(file) &&
        string.Equals(Path.GetFileName(file), "Unity.exe",
            StringComparison.OrdinalIgnoreCase);

    internal static bool MatchesEditorWindow(string? processName, string? title,
        string root) => UnityBackgroundFocusPolicy.IsGuardedEditorForProject(
            processName, title, root);

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


    // Window title identifies the project name. The project's held lock is
    // required as an independent signal before closing an existing Editor.
    private static int[] FindProjectWindowPids(string root)
    {
        var ids = new List<int>();
        foreach (var process in Process.GetProcessesByName("Unity"))
        {
            using (process)
            {
                try
                {
                    if (!process.HasExited && process.MainWindowHandle != IntPtr.Zero &&
                        MatchesEditorWindow(process.ProcessName,
                            process.MainWindowTitle, root))
                        ids.Add(process.Id);
                }
                catch (Exception ex) when (ex is InvalidOperationException or
                    Win32Exception or UnauthorizedAccessException) { }
            }
        }
        return ids.Distinct().ToArray();
    }

    private static bool SameExecutable(string? actual, string expected)
    {
        try
        {
            return IsUnityEditorExecutable(actual) &&
                string.Equals(Path.GetFullPath(actual!), Path.GetFullPath(expected),
                    StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    internal static async Task<Result> RunAsync(
        string projectRoot, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
            return new("EDITOR_START_WINDOWS_ONLY", "Windows-only", false);
        if (!UnityCliBuildRunner.IsUnityProject(projectRoot))
            return new("EDITOR_START_INVALID_PROJECT", "Unity project not found", false);

        using var lease = await UnityEditorOperationGate.EnterAsync(
            projectRoot, cancellationToken).ConfigureAwait(false);
        var oldPids = FindProjectWindowPids(projectRoot);
        if (oldPids.Length > 1)
            return new("EDITOR_START_AMBIGUOUS",
                "Multiple matching Unity Editor windows; no process closed.", false);
        // With a visible project-matching Unity window, the GUI is the
        // identity signal. Unity versions can place their lock elsewhere.
        // Without a matching window, a held lock blocks duplicate startup.
        if (oldPids.Length == 0 &&
            UnityEditorCommandLifecycle.ProjectEditorLockHeld(projectRoot))
            return new("EDITOR_START_PROJECT_LOCKED",
                "No matching GUI, but this project is locked; no duplicate launch.",
                false);

        string? executable = null;
        var oldPid = oldPids.FirstOrDefault();
        try
        {
            if (oldPid != 0)
            {
                using var old = Process.GetProcessById(oldPid);
                if (old.HasExited)
                    return new("EDITOR_START_RACE", "Old Editor just exited.", false);
                executable = old.MainModule?.FileName;
                if (!IsUnityEditorExecutable(executable))
                    return new("EDITOR_START_UNVERIFIED_EXE",
                        "Matching window is not a verified Unity.exe.", false);
                RememberExecutable(projectRoot, executable!);
                cancellationToken.ThrowIfCancellationRequested();
                if (!old.CloseMainWindow())
                    return new("EDITOR_START_CLOSE_REFUSED",
                        "Unity refused graceful close.", false);
                using var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(40));
                try
                {
                    // Once close is requested, finish the bounded restart even
                    // when the user's job cancellation arrives.
                    await old.WaitForExitAsync(shutdown.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return new("EDITOR_START_CLOSE_TIMEOUT",
                        "Old Editor did not exit. Unsaved changes may need attention; " +
                        "no force-kill and no duplicate launch.", false);
                }
            }
            else
            {
                executable = FindExecutableForStoppedEditor(projectRoot);
                if (executable is null)
                    return new("EDITOR_START_EXE_NOT_FOUND",
                        "Unity.exe not found. Set UNITY_EDITOR_PATH or open " +
                        "this Unity version once to record its path.", false);
            }

            // Wait until Unity releases its project lock after shutdown.
            for (var i = 0; i < 20 &&
                UnityEditorCommandLifecycle.ProjectEditorLockHeld(projectRoot); i++)
                await Task.Delay(250).ConfigureAwait(false);
            if (UnityEditorCommandLifecycle.ProjectEditorLockHeld(projectRoot) ||
                FindProjectWindowPids(projectRoot).Length != 0)
                return new("EDITOR_START_OLD_EDITOR_STILL_PRESENT",
                    "Project lock or Editor window still present; no duplicate started.", false);

            using var launched = Process.Start(
                CreateLaunchStartInfo(executable!, projectRoot));
            if (launched is null)
                return new("EDITOR_START_LAUNCH_FAILED",
                    "Windows did not return a Unity process.", false);

            var deadline = DateTime.UtcNow.AddSeconds(600);
            while (DateTime.UtcNow < deadline)
            {
                await Task.Delay(1200).ConfigureAwait(false);
                var matches = FindProjectWindowPids(projectRoot);
                if (matches.Length > 1)
                    return new("EDITOR_START_AMBIGUOUS",
                        "Multiple matching Editor windows after launch.", false);
                if (matches.Length == 1 && matches[0] != oldPid)
                {
                    using var opened = Process.GetProcessById(matches[0]);
                    if (SameExecutable(opened.MainModule?.FileName, executable!))
                        return new(oldPid == 0 ? "EDITOR_STARTED_VERIFIED" :
                            "EDITOR_RESTART_VERIFIED",
                            "Project Editor GUI running. oldPID=" + oldPid +
                            "; newPID=" + matches[0], true);
                }
            }
            return new("EDITOR_START_NOT_CONFIRMED",
                "Unity launched but no matching project GUI + lock confirmed " +
                "within 600 seconds; no retry.", false);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or
            ArgumentException or IOException or UnauthorizedAccessException)
        {
            return new("EDITOR_START_FAILED",
                ex.GetType().Name + ": " + ex.Message, false);
        }
    }
}
