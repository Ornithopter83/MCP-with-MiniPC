using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace ProjectHub.Worker;

/// <summary>
/// Best-effort native foreground recovery while Worker owns a user-requested
/// Unity job. An existing Unity Editor may self-activate on CLI/MCP calls;
/// CreateNoWindow and Codex hooks cannot prevent that native activation.
///
/// This guard does not hide/minimize Unity or inject input. User-triggered
/// activation is respected using the latest Windows input timestamp.
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

    private static bool IsUnityEditorWindow(IntPtr window)
    {
        if (window == IntPtr.Zero) return false;
        GetWindowThreadProcessId(window, out var processId);
        if (processId == 0) return false;
        try
        {
            using var process = Process.GetProcessById(unchecked((int)processId));
            return UnityBackgroundFocusPolicy.IsUnityEditorProcess(
                process.ProcessName);
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

    internal static bool IsUnityEditorProcess(string? processName) =>
        string.Equals(processName, "Unity", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(processName, "Unity.exe", StringComparison.OrdinalIgnoreCase);

    internal static bool IsRecentUserSelection(uint ageMilliseconds, bool inputTargetsUnity) =>
        inputTargetsUnity && ageMilliseconds <= UserInputGraceMilliseconds;

    internal static bool ShouldRestore(
        bool unityForeground, bool hasPreviousWindow, bool explicitUnitySelection) =>
        unityForeground && hasPreviousWindow && !explicitUnitySelection;
}
