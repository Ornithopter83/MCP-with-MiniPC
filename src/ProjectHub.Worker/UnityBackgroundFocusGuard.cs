using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

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

    private readonly WinEventDelegate _callback;
    private IntPtr _hook;
    private IntPtr _previousUserWindow;
    private bool _restoring;
    private bool _disposed;
    private string _unavailableReason = string.Empty;

    internal bool Available => _hook != IntPtr.Zero;
    internal int RestoreAttempts { get; private set; }
    internal int RestoreFailures { get; private set; }
    internal int RestoreSuccesses => RestoreAttempts - RestoreFailures;

    internal string Diagnostic => Available
        ? $"FOCUS_GUARD: restore={RestoreSuccesses}/{RestoreAttempts}; " +
          $"not_restored={RestoreFailures}; direct user activation is preserved. " +
          "Windows may reject programmatic foreground recovery."
        : "FOCUS_GUARD_UNAVAILABLE: " + _unavailableReason +
          "; Unity Editor may take focus during CLI/MCP execution.";

    private UnityBackgroundFocusGuard()
    {
        _callback = OnForegroundChanged;
    }

    internal static UnityBackgroundFocusGuard Start()
    {
        var guard = new UnityBackgroundFocusGuard();
        if (!OperatingSystem.IsWindows())
        {
            guard._unavailableReason = "Windows-only";
            return guard;
        }

        try
        {
            var current = GetForegroundWindow();
            if (current != IntPtr.Zero && !IsUnityEditorWindow(current))
                guard._previousUserWindow = current;
            // WPF already supplies the message pump required by out-of-context
            // WinEvent delivery. Do not install a global keyboard/mouse hook.
            guard._hook = SetWinEventHook(EventSystemForeground,
                EventSystemForeground, IntPtr.Zero, guard._callback,
                0, 0, WinEventOutOfContext);
            if (guard._hook == IntPtr.Zero)
                guard._unavailableReason =
                    "SetWinEventHook failed (" + Marshal.GetLastWin32Error() + ")";
        }
        catch (Exception error) when (error is Win32Exception or
            InvalidOperationException or DllNotFoundException or EntryPointNotFoundException)
        {
            guard._unavailableReason = error.GetType().Name + ": " + error.Message;
        }
        return guard;
    }

    private void OnForegroundChanged(
        IntPtr eventHook, uint eventType, IntPtr window,
        int objectId, int childId, uint eventThread, uint eventTime)
    {
        if (_disposed || _restoring || objectId != ObjIdWindow ||
            childId != 0 || window == IntPtr.Zero)
            return;

        try
        {
            if (!IsUnityEditorWindow(window))
            {
                // Always follow the user's most recently chosen non-Unity
                // window, not the ProjectHub window that initially launched us.
                _previousUserWindow = window;
                return;
            }

            if (_previousUserWindow == IntPtr.Zero ||
                !IsWindow(_previousUserWindow) ||
                !IsWindowVisible(_previousUserWindow) ||
                IsIconic(_previousUserWindow))
                return;

            var inputAge = MillisecondsSinceUserInput();
            if (!UnityBackgroundFocusPolicy.ShouldRestore(
                    unityForeground: true, hasPreviousWindow: true, inputAge))
                return;

            // Recheck immediately to avoid overriding a user window switch
            // queued between the event callback and the restoration attempt.
            if (GetForegroundWindow() != window)
                return;

            _restoring = true;
            RestoreAttempts++;
            var restored = SetForegroundWindow(_previousUserWindow);
            if (!restored && GetForegroundWindow() == window)
            {
                // Windows normally blocks background focus requests. Briefly
                // attach to the current foreground thread only as fallback.
                var unityThread = GetWindowThreadProcessId(window, out _);
                var callerThread = GetCurrentThreadId();
                var attached = unityThread != 0 && callerThread != unityThread &&
                               AttachThreadInput(callerThread, unityThread, true);
                try
                {
                    if (attached && GetForegroundWindow() == window &&
                        UnityBackgroundFocusPolicy.ShouldRestore(
                            true, true, MillisecondsSinceUserInput()))
                        restored = SetForegroundWindow(_previousUserWindow);
                }
                finally
                {
                    if (attached)
                        AttachThreadInput(callerThread, unityThread, false);
                }
            }
            if (!restored || GetForegroundWindow() != _previousUserWindow)
                RestoreFailures++;
        }
        catch (Exception error) when (error is InvalidOperationException or
            ArgumentException or Win32Exception)
        {
            RestoreFailures++;
        }
        finally
        {
            _restoring = false;
        }
    }

    private static uint MillisecondsSinceUserInput()
    {
        var input = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
        if (!GetLastInputInfo(ref input))
            return 0; // Unknown intent: do not override user focus.
        return unchecked((uint)Environment.TickCount - input.TickCount);
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

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLastInputInfo(ref LastInputInfo info);
}

internal static class UnityBackgroundFocusPolicy
{
    // Explicit user activation (click, Alt+Tab, keyboard) must win.
    internal const uint UserInputGraceMilliseconds = 350;

    internal static bool IsUnityEditorProcess(string? processName) =>
        string.Equals(processName, "Unity", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(processName, "Unity.exe", StringComparison.OrdinalIgnoreCase);

    internal static bool ShouldRestore(
        bool unityForeground, bool hasPreviousWindow, uint millisecondsSinceInput) =>
        unityForeground && hasPreviousWindow &&
        millisecondsSinceInput > UserInputGraceMilliseconds;
}
