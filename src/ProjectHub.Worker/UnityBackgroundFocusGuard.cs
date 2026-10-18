using System.ComponentModel;
using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace ProjectHub.Worker;

/// <summary>
/// Passive foreground observation throughout a user-requested Unity job.
/// Unity is NEVER refocused, unfocused, or minimized while the job is running.
/// Only after all job commands stop may the guard restore the last user window
/// once, if this project's Unity Editor/Player is still foreground and was
/// not intentionally selected by the user. No input is injected.
/// It cannot guarantee focus on other desktops/elevated processes and does
/// not claim to control external processes outside this Worker job.
/// Must be created/disposed on the WPF dispatcher thread (message loop).
/// </summary>
internal sealed class UnityBackgroundFocusGuard : IDisposable
{
    private const uint EventSystemForeground = 0x0003;
    private const uint WinEventOutOfContext = 0;
    private const int ObjIdWindow = 0;
    private const int VkLButton = 0x01;
    private const int VkRButton = 0x02;
    private const int VkMenu = 0x12;
    private const int VkTab = 0x09;

    private readonly WinEventDelegate _callback;
    private readonly string? _projectRoot;
    private int _backgroundCaptureDepth;
    private readonly DateTime _jobStartUtc;
    private readonly object _sync = new();
    private System.Threading.Timer? _watchdog;
    private bool _everAvailable;
    private IntPtr _hook;
    private IntPtr _previousUserWindow;
    private IntPtr _manualUnityWindow;
    private IntPtr _lastObservedForeground;
    private bool _restoring;
    private bool _jobEndRestoreEnabled;
    private bool _jobEndRestoreRequested;
    private volatile bool _disposed;
    private string _unavailableReason = string.Empty;

    // The independent polling watchdog also works when the UI message pump
    // fails to deliver foreground WinEvents during synchronous UI work.
    internal bool Available => _watchdog is not null || _hook != IntPtr.Zero;
    internal int RestoreAttempts { get; private set; }
    internal int RestoreFailures { get; private set; }
    internal int UnityForegroundActivations { get; private set; }
    internal int PlayerForegroundActivations { get; private set; }
    internal int ExplicitUserUnitySelections { get; private set; }
    internal int RestoreSuccesses => RestoreAttempts - RestoreFailures;

    internal string Diagnostic => _everAvailable
        ? $"FOCUS_GUARD: restore={RestoreSuccesses}/{RestoreAttempts}; " +
          $"not_restored={RestoreFailures}; " +
          $"unity_activations={UnityForegroundActivations}; " +
           $"player_activations={PlayerForegroundActivations}; " +
          $"user_selection_exempt={ExplicitUserUnitySelections}; " +
          "watchdog=passive, restore=job_end_only, " +
          "direct Unity mouse/Alt-Tab selection exempt. " +
          "Unity can remain foreground during a running job."
        : "FOCUS_GUARD_UNAVAILABLE: " + _unavailableReason +
          "; Unity Editor may take focus during CLI/MCP execution.";

    private UnityBackgroundFocusGuard(string? projectRoot)
    {
        _callback = OnForegroundChanged;
        _projectRoot = string.IsNullOrWhiteSpace(projectRoot)
            ? null : Path.GetFullPath(projectRoot);
        _jobStartUtc = DateTime.UtcNow.AddSeconds(-5);
    }

    internal static UnityBackgroundFocusGuard Start(string? projectRoot = null)
    {
        var guard = new UnityBackgroundFocusGuard(projectRoot);
        if (!OperatingSystem.IsWindows())
        {
            guard._unavailableReason = "Windows-only";
            return guard;
        }

        try
        {
            var current = GetForegroundWindow();
            if (current != IntPtr.Zero && !guard.IsWatchedUnityWindow(current))
                guard._previousUserWindow = current;
            // WPF already supplies the message pump required by out-of-context
            // WinEvent delivery. Do not install a global keyboard/mouse hook.
            guard._hook = SetWinEventHook(EventSystemForeground,
                EventSystemForeground, IntPtr.Zero, guard._callback,
                0, 0, WinEventOutOfContext);
            if (guard._hook == IntPtr.Zero)
                guard._unavailableReason =
                    "SetWinEventHook failed (" + Marshal.GetLastWin32Error() + ")";
            // Never rely solely on WPF WinEvent callbacks. Unity itself may
            // activate while the user is typing, exactly when the previous
            // input-grace implementation incorrectly ignored the hijack.
            guard._watchdog = new System.Threading.Timer(
                static state => ((UnityBackgroundFocusGuard)state!).WatchdogTick(),
                guard, TimeSpan.FromMilliseconds(120), TimeSpan.FromMilliseconds(120));
        }
        catch (Exception error) when (error is Win32Exception or
            InvalidOperationException or DllNotFoundException or EntryPointNotFoundException)
        {
            guard._unavailableReason = error.GetType().Name + ": " + error.Message;
        }
        guard._everAvailable = guard.Available;
        return guard;
    }

    /// <summary>
    /// Tracks a background screenshot interval. The guard never restores
    /// foreground during this interval or anywhere else in a running job.
    /// Only RestoreAfterJob may trigger a foreground restoration.
    /// </summary>
    internal BackgroundCaptureScope BeginBackgroundCapture()
    {
        lock (_sync)
        {
            if (_disposed || !Available)
                return new(null, _disposed
                    ? "BACKGROUND_CAPTURE_GUARD_DISPOSED"
                    : "BACKGROUND_CAPTURE_GUARD_UNAVAILABLE");
            _backgroundCaptureDepth++;
            return new(this, "BACKGROUND_CAPTURE_NO_ACTIVATION");
        }
    }

    internal sealed class BackgroundCaptureScope : IDisposable
    {
        private UnityBackgroundFocusGuard? _owner;
        internal string Diagnostic { get; private set; }

        internal BackgroundCaptureScope(UnityBackgroundFocusGuard? owner, string diagnostic)
        {
            _owner = owner;
            Diagnostic = diagnostic;
        }

        public void Dispose()
        {
            var owner = Interlocked.Exchange(ref _owner, null);
            if (owner is not null)
                Diagnostic = owner.EndBackgroundCapture();
        }
    }

    private string EndBackgroundCapture()
    {
        lock (_sync)
        {
            if (_backgroundCaptureDepth == 0)
                return "BACKGROUND_CAPTURE_SCOPE_ALREADY_FINISHED";
            _backgroundCaptureDepth--;
            if (_backgroundCaptureDepth != 0)
                return "BACKGROUND_CAPTURE_NESTED_SCOPE_FINISHED";
            // Passive observation only; even a self-activated Unity Editor
            // must keep focus until the entire user job finishes.
            CheckForeground(GetForegroundWindow());
            return "BACKGROUND_CAPTURE_COMPLETE_NO_FOCUS_CHANGE";
        }
    }

    /// <summary>
    /// Called exactly once after the job has stopped all Unity work. The
    /// watchdog remains passive; only this explicit call can restore focus.
    /// Do not interrupt a user who switched applications or chose Unity.
    /// </summary>
    internal string RestoreAfterJob()
    {
        lock (_sync)
        {
            if (_jobEndRestoreRequested)
                return "FOCUS_JOB_END_ALREADY_HANDLED";
            _jobEndRestoreRequested = true;
            if (_disposed || !Available)
                return "FOCUS_JOB_END_GUARD_UNAVAILABLE";

            var foreground = GetForegroundWindow();
            CheckForeground(foreground); // Final passive observation.
            if (foreground == IntPtr.Zero || !IsWatchedUnityWindow(foreground))
                return "FOCUS_JOB_END_OTHER_APP_ACTIVE";
            if (_manualUnityWindow == foreground ||
                UnityBackgroundFocusPolicy.IsRecentUserSelection(
                    MillisecondsSinceUserInput(), IsExplicitUnitySelection(foreground)))
                return "FOCUS_JOB_END_USER_SELECTED_UNITY";
            if (_previousUserWindow == IntPtr.Zero ||
                !IsWindow(_previousUserWindow) ||
                !IsWindowVisible(_previousUserWindow) ||
                IsIconic(_previousUserWindow) ||
                _previousUserWindow == foreground)
                return "FOCUS_JOB_END_NO_VALID_USER_WINDOW";

            var earlierAttempts = RestoreAttempts;
            _jobEndRestoreEnabled = true;
            try
            {
                CheckForeground(foreground);
            }
            finally
            {
                _jobEndRestoreEnabled = false;
            }
            if (RestoreAttempts == earlierAttempts)
                return "FOCUS_JOB_END_RESTORE_SKIPPED";
            return GetForegroundWindow() == _previousUserWindow
                ? "FOCUS_JOB_END_USER_WINDOW_RESTORED"
                : "FOCUS_JOB_END_RESTORE_UNVERIFIED";
        }
    }

    private void OnForegroundChanged(
        IntPtr eventHook, uint eventType, IntPtr window,
        int objectId, int childId, uint eventThread, uint eventTime)
    {
        if (objectId != ObjIdWindow || childId != 0)
            return;
        CheckForeground(window);
    }

    private void WatchdogTick()
    {
        if (_disposed) return;
        // SetWinEventHook reports normal changes rapidly, but the independent
        // timer also handles missed UI-dispatcher messages.
        CheckForeground(GetForegroundWindow());
    }

    private void CheckForeground(IntPtr window)
    {
        if (_disposed || window == IntPtr.Zero) return;
        lock (_sync)
        {
            if (_disposed || _restoring) return;
            try
            {
                var isUnityEditor = IsUnityEditorWindow(window);
                var isProjectPlayer = !isUnityEditor && IsProjectPlayerWindow(window);
                var guardedWindow = isUnityEditor || isProjectPlayer;
                if (_lastObservedForeground != window)
                {
                    if (guardedWindow) UnityForegroundActivations++;
                    if (isProjectPlayer) PlayerForegroundActivations++;
                    _lastObservedForeground = window;
                }
                if (!guardedWindow)
                {
                    // Switches to a regular application reset any explicit
                    // Unity focus exemption.
                    if (IsWindow(window) && IsWindowVisible(window))
                    {
                        _previousUserWindow = window;
                        _manualUnityWindow = IntPtr.Zero;
                    }
                    return;
                }
                if (guardedWindow && _backgroundCaptureDepth > 0)
                {
                    // Keep the rendered frame undisturbed. This is NOT an
                    // instruction to bring the Editor into the foreground.
                    if (UnityBackgroundFocusPolicy.IsRecentUserSelection(
                            MillisecondsSinceUserInput(), IsExplicitUnitySelection(window)))
                    {
                        _manualUnityWindow = window;
                        ExplicitUserUnitySelections++;
                    }
                    return;
                }
                // During the entire job, only observe focus. Automatic Unity
                // activation is not canceled, so Editor refresh/compilation
                // and screenshots have a chance to run in the foreground.
                if (!_jobEndRestoreEnabled)
                {
                    if (_manualUnityWindow != window &&
                        UnityBackgroundFocusPolicy.IsRecentUserSelection(
                            MillisecondsSinceUserInput(), IsExplicitUnitySelection(window)))
                    {
                        _manualUnityWindow = window;
                        ExplicitUserUnitySelections++;
                    }
                    return;
                }
                if (_manualUnityWindow == window)
                    return;
                if (_previousUserWindow == IntPtr.Zero ||
                    !IsWindow(_previousUserWindow) ||
                    !IsWindowVisible(_previousUserWindow) ||
                    IsIconic(_previousUserWindow) ||
                    _previousUserWindow == window)
                    return;

                // Fresh keyboard input in ANOTHER app is not an intentional
                // selection of Unity. Only a mouse interaction targeting
                // Unity or an Alt+Tab gesture exempts direct user activation.
                if (UnityBackgroundFocusPolicy.IsRecentUserSelection(
                        MillisecondsSinceUserInput(), IsExplicitUnitySelection(window)))
                {
                    _manualUnityWindow = window;
                    ExplicitUserUnitySelections++;
                    return;
                }
                if (!UnityBackgroundFocusPolicy.ShouldRestoreAfterJob(
                        jobFinished: _jobEndRestoreEnabled,
                        unityForeground: true,
                        hasPreviousWindow: true,
                        explicitUnitySelection: false))
                    return;
                if (GetForegroundWindow() != window)
                    return;
                _restoring = true;
                RestoreAttempts++;
                var restored = SetForegroundWindow(_previousUserWindow);
                if (!restored && GetForegroundWindow() == window)
                {
                    // Windows can reject a foreground request from Worker;
                    // temporarily attach input queues as a bounded fallback.
                    var unityThread = GetWindowThreadProcessId(window, out _);
                    var callerThread = GetCurrentThreadId();
                    // Do not attach to the previous user's (possibly WPF)
                    // thread: synchronous WinEvent callbacks may be queued
                    // on that thread, causing a circular GUI wait.
                    var attachedUnity = unityThread != 0 && callerThread != unityThread &&
                        AttachThreadInput(callerThread, unityThread, true);
                    try
                    {
                        if (GetForegroundWindow() == window)
                            restored = SetForegroundWindow(_previousUserWindow);
                    }
                    finally
                    {
                        if (attachedUnity)
                            AttachThreadInput(callerThread, unityThread, false);
                    }
                }
                // Never minimize Unity, even if Windows denies the final
                // foreground request. The caller receives UNVERIFIED.
                if (GetForegroundWindow() == window)
                    RestoreFailures++;
            }
            catch (Exception ex) when (ex is InvalidOperationException or
                ArgumentException or Win32Exception or DllNotFoundException or
                EntryPointNotFoundException)
            {
                RestoreFailures++;
            }
            finally
            {
                _restoring = false;
            }
        }
    }

    private bool IsExplicitUnitySelection(IntPtr unityWindow)
    {
        var mouseDown = (GetAsyncKeyState(VkLButton) & 0x8000) != 0 ||
                        (GetAsyncKeyState(VkRButton) & 0x8000) != 0;
        if (mouseDown && GetCursorPos(out var cursor))
        {
            var underMouse = WindowFromPoint(cursor);
            if (underMouse != IntPtr.Zero &&
                IsWatchedUnityWindow(underMouse))
                return true;
        }
        return (GetAsyncKeyState(VkMenu) & 0x8000) != 0 &&
               (GetAsyncKeyState(VkTab) & 0x8000) != 0;
    }

    private static uint MillisecondsSinceUserInput()
    {
        var input = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
        if (!GetLastInputInfo(ref input))
            return 0; // Unknown intent: do not override user focus.
        return unchecked((uint)Environment.TickCount - input.TickCount);
    }

    private bool IsWatchedUnityWindow(IntPtr window) =>
        IsUnityEditorWindow(window) || IsProjectPlayerWindow(window);

    private bool IsProjectPlayerWindow(IntPtr window)
    {
        if (_projectRoot is null || window == IntPtr.Zero) return false;
        GetWindowThreadProcessId(window, out var pid);
        if (pid == 0) return false;
        try
        {
            using var process = Process.GetProcessById(unchecked((int)pid));
            return process.StartTime.ToUniversalTime() >= _jobStartUtc &&
                UnityBackgroundFocusPolicy.IsProjectPlayerExecutable(
                    _projectRoot, process.MainModule?.FileName);
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException
            or Win32Exception or UnauthorizedAccessException) { return false; }
    }

    private bool IsUnityEditorWindow(IntPtr window)
    {
        if (_projectRoot is null || window == IntPtr.Zero) return false;
        GetWindowThreadProcessId(window, out var processId);
        if (processId == 0) return false;
        try
        {
            using var process = Process.GetProcessById(unchecked((int)processId));
            return UnityBackgroundFocusPolicy.IsGuardedEditorForProject(
                process.ProcessName, process.MainWindowTitle, _projectRoot);
        }
        catch (Exception error) when (error is ArgumentException or
            InvalidOperationException or Win32Exception)
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        lock (_sync)
        {
        }
        _disposed = true;
        var watchdog = Interlocked.Exchange(ref _watchdog, null);
        watchdog?.Dispose();
        if (_hook != IntPtr.Zero)
        {
            UnhookWinEvent(_hook);
            _hook = IntPtr.Zero;
        }
        GC.KeepAlive(_callback);
    }

    private delegate void WinEventDelegate(IntPtr eventHook, uint eventType,
        IntPtr window, int objectId, int childId,
        uint eventThread, uint eventTime);

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint Size;
        public uint TickCount;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax,
        IntPtr module, WinEventDelegate callback, uint processId,
        uint threadId, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWinEvent(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(
        IntPtr window, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachThreadInput(
        uint attachThread, uint targetThread, [MarshalAs(UnmanagedType.Bool)] bool attach);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [StructLayout(LayoutKind.Sequential)]
    private struct Point { public int X; public int Y; }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(Point point);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLastInputInfo(ref LastInputInfo info);
}

internal static class UnityBackgroundFocusPolicy
{
    internal const uint UserInputGraceMilliseconds = 350;

    internal static bool IsGuardedEditorForProject(
        string? processName, string? title, string? root)
    {
        if (!IsUnityEditorProcess(processName) || string.IsNullOrWhiteSpace(root))
            return false;
        var projectName = Path.GetFileName(root.TrimEnd(
            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return IsProjectEditorTitle(title, projectName);
    }

    internal static bool IsProjectPlayerExecutable(
        string projectRoot, string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(projectRoot) ||
            string.IsNullOrWhiteSpace(executablePath) ||
            !executablePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            return false;
        try
        {
            var outputRoot = Path.GetFullPath(
                Path.Combine(projectRoot, "Builds")).TrimEnd(
                    Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                Path.DirectorySeparatorChar;
            return Path.GetFullPath(executablePath).StartsWith(
                outputRoot, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception e) when (e is IOException or ArgumentException
            or NotSupportedException) { return false; }
    }

    internal static bool IsProjectEditorTitle(string? title, string? projectName)
    {
        if (string.IsNullOrWhiteSpace(title) ||
            string.IsNullOrWhiteSpace(projectName))
            return false;
        var index = title.IndexOf(projectName, StringComparison.OrdinalIgnoreCase);
        while (index >= 0)
        {
            var end = index + projectName.Length;
            var before = index == 0 || !char.IsLetterOrDigit(title[index - 1]);
            var after = end == title.Length || !char.IsLetterOrDigit(title[end]);
            if (before && after) return true;
            index = title.IndexOf(projectName, index + 1,
                StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }

    internal static bool IsUnityEditorProcess(string? processName) =>
        string.Equals(processName, "Unity", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(processName, "Unity.exe", StringComparison.OrdinalIgnoreCase);

    internal static bool IsRecentUserSelection(uint ageMilliseconds, bool inputTargetsUnity) =>
        inputTargetsUnity && ageMilliseconds <= UserInputGraceMilliseconds;

    internal static bool ShouldRestore(
        bool unityForeground, bool hasPreviousWindow, bool explicitUnitySelection) =>
        unityForeground && hasPreviousWindow && !explicitUnitySelection;

    // Foreground changes are only allowed at the explicit job-end boundary.
    internal static bool ShouldRestoreAfterJob(
        bool jobFinished, bool unityForeground, bool hasPreviousWindow,
        bool explicitUnitySelection) =>
        jobFinished && ShouldRestore(
            unityForeground, hasPreviousWindow, explicitUnitySelection);
}
