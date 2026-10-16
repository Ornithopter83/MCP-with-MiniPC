using System.ComponentModel;
using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace ProjectHub.Worker;

/// <summary>
/// Best-effort native foreground recovery while Worker owns a user-requested
/// Unity job. An existing Unity Editor may self-activate on CLI/MCP calls;
/// CreateNoWindow and Codex hooks cannot prevent that native activation.
///
/// A fallback may minimize only a matched, unintentionally foregrounded
/// project window; no input is injected. User-triggered activation is
/// respected using the latest Windows input timestamp.
/// It cannot guarantee focus on other desktops/elevated processes and does
/// not claim to control external processes outside this Worker job.
/// Must be created/disposed on the WPF dispatcher thread (message loop).
/// </summary>
internal sealed class UnityBackgroundFocusGuard : IDisposable
{
    private const uint EventSystemForeground = 0x0003;
    private const uint WinEventOutOfContext = 0;
    private const int ObjIdWindow = 0;
    private const int SwMinimize = 6;
    private const int SwShowNoActivate = 4;
    private const int VkLButton = 0x01;
    private const int VkRButton = 0x02;
    private const int VkMenu = 0x12;
    private const int VkTab = 0x09;

    private readonly WinEventDelegate _callback;
    private readonly string? _projectRoot;
    private readonly IntPtr _workerWindow;
    private IntPtr _visualLeaseWindow;
    private long _visualLeaseDeadline;
    private bool _visualUserOverride;
    private int _backgroundCaptureDepth;
    private readonly DateTime _jobStartUtc;
    private readonly object _sync = new();
    private readonly HashSet<IntPtr> _parkedEditorWindows = new();
    private System.Threading.Timer? _watchdog;
    private bool _everAvailable;
    private IntPtr _hook;
    private IntPtr _previousUserWindow;
    private IntPtr _manualUnityWindow;
    private IntPtr _lastObservedForeground;
    private bool _restoring;
    private volatile bool _disposed;
    private string _unavailableReason = string.Empty;

    // The independent polling watchdog also works when the UI message pump
    // fails to deliver foreground WinEvents during synchronous UI work.
    internal bool Available => _watchdog is not null || _hook != IntPtr.Zero;
    internal int RestoreAttempts { get; private set; }
    internal int RestoreFailures { get; private set; }
    internal int MinimizeFallbacks { get; private set; }
    internal int ParkedWindowRestores { get; private set; }
    internal int UnityForegroundActivations { get; private set; }
    internal int PlayerForegroundActivations { get; private set; }
    internal int ExplicitUserUnitySelections { get; private set; }
    internal int RestoreSuccesses => RestoreAttempts - RestoreFailures;

    internal string Diagnostic => _everAvailable
        ? $"FOCUS_GUARD: restore={RestoreSuccesses}/{RestoreAttempts}; " +
          $"minimize_fallback_requests={MinimizeFallbacks}; " +
          $"parked_restored={ParkedWindowRestores}; not_restored={RestoreFailures}; " +
          $"unity_activations={UnityForegroundActivations}; " +
           $"player_activations={PlayerForegroundActivations}; " +
          $"user_selection_exempt={ExplicitUserUnitySelections}; " +
          "watchdog=on, direct Unity mouse/Alt-Tab selection exempt. " +
          "Unity may briefly flash when it self-activates."
        : "FOCUS_GUARD_UNAVAILABLE: " + _unavailableReason +
          "; Unity Editor may take focus during CLI/MCP execution.";

    private UnityBackgroundFocusGuard(string? projectRoot, IntPtr workerWindow)
    {
        _callback = OnForegroundChanged;
        _workerWindow = workerWindow;
        _projectRoot = string.IsNullOrWhiteSpace(projectRoot)
            ? null : Path.GetFullPath(projectRoot);
        _jobStartUtc = DateTime.UtcNow.AddSeconds(-5);
    }

    internal static UnityBackgroundFocusGuard Start(string? projectRoot = null,
        IntPtr workerWindow = default)
    {
        var guard = new UnityBackgroundFocusGuard(projectRoot, workerWindow);
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
    /// A narrowly scoped foreground window lease for Worker-owned Native Game View
    /// verification. Skips activation when the user is working in another app.
    /// Never changes another project's Editor and never minimizes the Editor.
    /// </summary>
    internal VisualVerificationLease BeginVisualVerification()
    {
        lock (_sync)
        {
            if (_disposed || !Available || _projectRoot is null ||
                _workerWindow == IntPtr.Zero || !IsWindow(_workerWindow) ||
                !IsWindowVisible(_workerWindow) || IsIconic(_workerWindow))
                return new(null, IntPtr.Zero, "FOCUS_LEASE_UNAVAILABLE");
            if (_visualLeaseWindow != IntPtr.Zero)
                return new(null, IntPtr.Zero, "FOCUS_LEASE_ALREADY_ACTIVE");
            if (GetForegroundWindow() != _workerWindow)
                return new(null, IntPtr.Zero, "FOCUS_LEASE_SKIPPED_USER_IN_OTHER_APP");

            var projectName = Path.GetFileName(_projectRoot.TrimEnd(
                Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            IntPtr editorWindow = IntPtr.Zero;
            foreach (var process in Process.GetProcessesByName("Unity"))
            {
                using (process)
                {
                    try
                    {
                        if (!UnityBackgroundFocusPolicy.IsProjectEditorTitle(
                                process.MainWindowTitle, projectName))
                            continue;
                        var candidate = process.MainWindowHandle;
                        if (candidate == IntPtr.Zero || !IsWindow(candidate) ||
                            !IsWindowVisible(candidate) || IsIconic(candidate))
                            continue;
                        if (editorWindow != IntPtr.Zero)
                            return new(null, IntPtr.Zero,
                                "FOCUS_LEASE_AMBIGUOUS_PROJECT_WINDOWS");
                        editorWindow = candidate;
                    }
                    catch (Exception ex) when (ex is InvalidOperationException or
                        Win32Exception or ArgumentException) { }
                }
            }
            if (editorWindow == IntPtr.Zero)
                return new(null, IntPtr.Zero,
                    "FOCUS_LEASE_EDITOR_NOT_UNIQUELY_LOCATED_BY_PROJECT_TITLE");
            _visualLeaseWindow = editorWindow;
            _visualLeaseDeadline = Environment.TickCount64 + 50_000;
            _visualUserOverride = false;
            _previousUserWindow = _workerWindow;

            if (!SetForegroundWindow(editorWindow) ||
                GetForegroundWindow() != editorWindow)
            {
                _visualLeaseWindow = IntPtr.Zero;
                return new(null, IntPtr.Zero, "FOCUS_LEASE_ACTIVATION_DENIED");
            }
            return new(this, editorWindow, "FOCUS_LEASE_ACTIVE");
        }
    }

    /// <summary>
    /// Allows native screenshots to finish without automatic focus recovery
    /// mid-frame. This scope NEVER foregrounds Unity. If Unity activates on
    /// its own, the normal user-window restoration occurs after capture ends.
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
            // A user-selected Unity window remains exempt. Only an
            // incidental activation of this project's Editor may be restored.
            var priorAttempts = RestoreAttempts;
            CheckForeground(GetForegroundWindow());
            return RestoreAttempts == priorAttempts
                ? "BACKGROUND_CAPTURE_COMPLETE_NO_FOCUS_CHANGE"
                : GetForegroundWindow() == _previousUserWindow
                    ? "BACKGROUND_CAPTURE_COMPLETE_USER_FOCUS_RESTORED"
                    : "BACKGROUND_CAPTURE_COMPLETE_RESTORE_UNVERIFIED";
        }
    }

    internal sealed class VisualVerificationLease : IDisposable
    {
        private UnityBackgroundFocusGuard? _owner;
        private readonly IntPtr _window;
        internal bool Active => _owner is not null;
        internal string Diagnostic { get; private set; }

        internal VisualVerificationLease(UnityBackgroundFocusGuard? owner,
            IntPtr window, string diagnostic)
        {
            _owner = owner;
            _window = window;
            Diagnostic = diagnostic;
        }

        public void Dispose()
        {
            var owner = Interlocked.Exchange(ref _owner, null);
            if (owner is not null)
                Diagnostic = owner.EndVisualVerification(_window);
        }
    }

    private string EndVisualVerification(IntPtr unityWindow)
    {
        lock (_sync)
        {
            if (_visualLeaseWindow != unityWindow)
                return "FOCUS_LEASE_ALREADY_EXPIRED";
            _visualLeaseWindow = IntPtr.Zero;
            var userOverride = _visualUserOverride;
            _visualUserOverride = false;
            if (userOverride || GetForegroundWindow() != unityWindow)
                return "FOCUS_LEASE_USER_SWITCH_RESPECTED";
            if (_workerWindow == IntPtr.Zero || !IsWindow(_workerWindow) ||
                !IsWindowVisible(_workerWindow) || IsIconic(_workerWindow))
                return "FOCUS_LEASE_WORKER_WINDOW_UNAVAILABLE";
            var restored = SetForegroundWindow(_workerWindow);
            if (!restored && GetForegroundWindow() == unityWindow)
            {
                var unityThread = GetWindowThreadProcessId(unityWindow, out _);
                var callerThread = GetCurrentThreadId();
                var attached = unityThread != 0 && unityThread != callerThread &&
                    AttachThreadInput(callerThread, unityThread, true);
                try
                {
                    if (GetForegroundWindow() == unityWindow)
                        restored = SetForegroundWindow(_workerWindow);
                }
                finally
                {
                    if (attached)
                        AttachThreadInput(callerThread, unityThread, false);
                }
            }
            return (restored && GetForegroundWindow() == _workerWindow) ||
                GetForegroundWindow() == _workerWindow
                ? "FOCUS_LEASE_WORKER_RESTORED"
                : "FOCUS_LEASE_WORKER_RESTORE_DENIED";
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
                if (_visualLeaseWindow != IntPtr.Zero)
                {
                    if (Environment.TickCount64 >= _visualLeaseDeadline)
                    {
                        EndVisualVerification(_visualLeaseWindow);
                    }
                    else if (window == _visualLeaseWindow)
                    {
                        if (UnityBackgroundFocusPolicy.IsRecentUserSelection(
                                MillisecondsSinceUserInput(), IsExplicitUnitySelection(window)))
                            _visualUserOverride = true;
                        return;
                    }
                    else if (window != _workerWindow && !IsWatchedUnityWindow(window))
                    {
                        // User switched to a different application during capture.
                        // Do not take focus back when the capture later finishes.
                        _visualUserOverride = true;
                    }
                }
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
                if (!UnityBackgroundFocusPolicy.ShouldRestore(
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
                if ((!restored || GetForegroundWindow() == window) &&
                    GetForegroundWindow() == window)
                {
                    // Last resort: park only the foreground Unity window.
                    // Hidden/minimized Unity may not render identically; the
                    // internal RenderTexture evidence must remain unverified.
                    // Record only windows actually parked by this guard,
                    // so Dispose can return them to their original visible
                    // size without activating Unity over the user's app.
                    ShowWindowAsync(window, SwMinimize);
                    _parkedEditorWindows.Add(window);
                    MinimizeFallbacks++;
                }
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
            if (_visualLeaseWindow != IntPtr.Zero)
                EndVisualVerification(_visualLeaseWindow);
        }
        _disposed = true;
        var watchdog = Interlocked.Exchange(ref _watchdog, null);
        watchdog?.Dispose();
        if (_hook != IntPtr.Zero)
        {
            UnhookWinEvent(_hook);
            _hook = IntPtr.Zero;
        }
        lock (_sync)
        {
            foreach (var window in _parkedEditorWindows)
            {
                if (!IsWindow(window) || !IsIconic(window))
                    continue;
                // SW_SHOWNOACTIVATE preserves the user's current foreground.
                ShowWindowAsync(window, SwShowNoActivate);
                ParkedWindowRestores++;
            }
            _parkedEditorWindows.Clear();
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
    private static extern bool ShowWindowAsync(IntPtr window, int command);

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
}
