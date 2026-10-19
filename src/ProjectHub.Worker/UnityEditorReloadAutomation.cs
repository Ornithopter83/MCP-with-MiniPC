using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;

namespace ProjectHub.Worker;

/// <summary>
/// While a user-requested Unity job is active, confirm external-change/reload
/// dialogs in that project's Editor process. Runs outside Unity's main thread,
/// so a blocking modal does not also block the responder. Never clicks a
/// different project's windows or unrelated destructive dialogs.
/// </summary>
internal sealed class UnityEditorReloadAutomation : IAsyncDisposable
{
    private const uint BmClick = 0x00F5;
    private readonly string _root;
    private readonly CancellationTokenSource _stop;
    private readonly Action<string> _report;
    private readonly HashSet<int> _projectPids = new();
    private readonly Dictionary<IntPtr, DateTime> _lastClickUtc = new();
    private readonly Task _loop;

    private UnityEditorReloadAutomation(string projectRoot,
        CancellationToken cancellationToken, Action<string> report)
    {
        _root = Path.GetFullPath(projectRoot);
        _stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _report = report;
        _loop = Task.Run(WatchAsync);
    }

    internal static UnityEditorReloadAutomation StartForJob(string projectRoot,
        CancellationToken token, Action<string> report) =>
        new(projectRoot, token, report);

    // Strictly an external file-update / reload dialog, never a Save As,
    // deletion, package-license, security or arbitrary Yes/No confirmation.
    internal static bool IsReloadDialog(string text)
    {
        var s = text.ToLowerInvariant();
        bool change = s.Contains("externally") || s.Contains("on disk") ||
            s.Contains("changed") || s.Contains("modified") ||
            s.Contains("out of date") || s.Contains("updated") ||
            s.Contains("changes detected") ||
            s.Contains("변경") || s.Contains("수정") || s.Contains("업데이트");
        bool reload = s.Contains("reload") || s.Contains("re-import") ||
            s.Contains("reimport") || s.Contains("refresh") ||
            s.Contains("import") || s.Contains("load") ||
            s.Contains("다시 로드") || s.Contains("재로드") ||
            s.Contains("새로 고침") || s.Contains("새로고침") ||
            s.Contains("갱신") || s.Contains("불러오기");
        return change && reload;
    }

    internal static bool IsReloadButton(string label)
    {
        var s = label.Trim().ToLowerInvariant().Replace("&", "");
        return s is "reload" or "reload all" or "reimport" or
            "re-import" or "refresh" or "update" or "import" or
            "yes" or "ok" or "확인" or "예" or "다시 로드" or
            "재로드" or "모두 다시 로드" or "새로고침" or "새로 고침" or
            "갱신" or "업데이트" or "불러오기";
    }

    private async Task WatchAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                DiscoverProjectEditorPids();
                ConfirmDialogs();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _report("EDITOR_RELOAD_SCAN_UNVERIFIED: " +
                    ex.GetType().Name + ": " + ex.Message);
            }
            try { await Task.Delay(400, _stop.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested)
            { break; }
        }
    }

    private void DiscoverProjectEditorPids()
    {
        var visibleTitles = new Dictionary<int, List<string>>();
        EnumWindows((handle, _) =>
        {
            if (!IsWindowVisible(handle)) return true;
            GetWindowThreadProcessId(handle, out var pid);
            if (pid == 0) return true;
            if (!visibleTitles.TryGetValue((int)pid, out var titles))
                visibleTitles[(int)pid] = titles = new List<string>();
            titles.Add(WindowText(handle));
            return true;
        }, IntPtr.Zero);
        foreach (var process in Process.GetProcessesByName("Unity"))
        {
            using (process)
            {
                try
                {
                    if (process.HasExited) continue;
                    if (!visibleTitles.TryGetValue(process.Id, out var titles))
                        continue;
                    if (titles.Any(title =>
                        UnityBackgroundFocusPolicy.IsGuardedEditorForProject(
                            process.ProcessName, title, _root)))
                        _projectPids.Add(process.Id);
                }
                catch (Exception ex) when (ex is Win32Exception or
                    InvalidOperationException or UnauthorizedAccessException) { }
            }
        }
        _projectPids.RemoveWhere(pid =>
        {
            try
            {
                using var process = Process.GetProcessById(pid);
                return process.HasExited ||
                    !string.Equals(process.ProcessName, "Unity",
                        StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex) when (ex is ArgumentException or
                InvalidOperationException or Win32Exception) { return true; }
        });
    }

    private void ConfirmDialogs()
    {
        if (_projectPids.Count == 0) return;
        var windows = new List<IntPtr>();
        EnumWindows((handle, _) =>
        {
            GetWindowThreadProcessId(handle, out var pid);
            if (pid > 0 && _projectPids.Contains((int)pid) &&
                IsWindowVisible(handle))
                windows.Add(handle);
            return true;
        }, IntPtr.Zero);
        foreach (var handle in windows)
        {
            // A regular Unity project window must not be operated on by a
            // generic text matcher. Only separate top-level popups qualify.
            if (!IsWindow(handle) || IsProjectMainWindow(handle)) continue;
            if (_lastClickUtc.TryGetValue(handle, out var last) &&
                DateTime.UtcNow - last < TimeSpan.FromSeconds(4))
                continue;
            try { ConfirmDialog(handle); }
            catch (Exception ex) when (ex is COMException or
                ElementNotAvailableException or InvalidOperationException)
            {
                _report("EDITOR_RELOAD_UI_UNVERIFIED: " + ex.GetType().Name);
            }
        }
        foreach (var old in _lastClickUtc.Keys.Where(handle =>
            !IsWindow(handle)).ToArray())
            _lastClickUtc.Remove(old);
    }

    private bool IsProjectMainWindow(IntPtr handle)
    {
        var title = WindowText(handle);
        return UnityBackgroundFocusPolicy.IsProjectEditorTitle(title,
            Path.GetFileName(_root.TrimEnd(Path.DirectorySeparatorChar)));
    }

    private void ConfirmDialog(IntPtr handle)
    {
        var title = WindowText(handle);
        var parts = new List<string> { title };
        var nativeButtons = new List<(IntPtr Handle, string Name)>();
        EnumChildWindows(handle, (child, _) =>
        {
            var value = WindowText(child);
            if (value.Length > 0) parts.Add(value);
            var className = new StringBuilder(100);
            GetClassName(child, className, className.Capacity);
            if (IsWindowVisible(child) &&
                className.ToString().Equals("Button",
                    StringComparison.OrdinalIgnoreCase))
                nativeButtons.Add((child, value));
            return true;
        }, IntPtr.Zero);

        AutomationElement? automation = null;
        AutomationElementCollection? buttons = null;
        try
        {
            automation = AutomationElement.FromHandle(handle);
            if (automation is not null)
            {
                var textItems = automation.FindAll(TreeScope.Descendants,
                    new OrCondition(
                        new PropertyCondition(AutomationElement.ControlTypeProperty,
                            ControlType.Text),
                        new PropertyCondition(AutomationElement.ControlTypeProperty,
                            ControlType.Button)));
                foreach (AutomationElement item in textItems)
                {
                    if (!string.IsNullOrWhiteSpace(item.Current.Name))
                        parts.Add(item.Current.Name);
                }
                buttons = automation.FindAll(TreeScope.Descendants,
                    new PropertyCondition(AutomationElement.ControlTypeProperty,
                        ControlType.Button));
            }
        }
        catch (Exception ex) when (ex is COMException or ElementNotAvailableException)
        {
            // Standard Win32 controls may still be usable without UIAutomation.
        }

        if (!IsReloadDialog(string.Join(" ", parts)))
            return;

        foreach (var button in nativeButtons)
        {
            if (!IsReloadButton(button.Name)) continue;
            _lastClickUtc[handle] = DateTime.UtcNow;
            if (PostMessage(button.Handle, BmClick, IntPtr.Zero, IntPtr.Zero))
                _report("EDITOR_RELOAD_AUTO_CONFIRMED_UNVERIFIED: " +
                    Limit(title) + "; button=" + Limit(button.Name) +
                    "; waiting for fresh Editor Ping/scene_status");
            else
                _report("EDITOR_RELOAD_CLICK_FAILED: " +
                    Marshal.GetLastWin32Error());
            return;
        }

        if (buttons is null) return;
        foreach (AutomationElement button in buttons)
        {
            var name = button.Current.Name;
            if (!IsReloadButton(name)) continue;
            if (!button.TryGetCurrentPattern(InvokePattern.Pattern, out var pattern) ||
                pattern is not InvokePattern invoker)
                continue;
            _lastClickUtc[handle] = DateTime.UtcNow;
            invoker.Invoke();
            _report("EDITOR_RELOAD_AUTO_CONFIRMED_UNVERIFIED: " +
                Limit(title) + "; button=" + Limit(name) +
                "; waiting for fresh Editor Ping/scene_status");
            return;
        }
        _lastClickUtc[handle] = DateTime.UtcNow;
        _report("EDITOR_RELOAD_CONTROL_UNAVAILABLE: " + Limit(title));
    }

    private static string WindowText(IntPtr handle)
    {
        var buffer = new StringBuilder(1024);
        GetWindowText(handle, buffer, buffer.Capacity);
        return buffer.ToString();
    }

    private static string Limit(string text) =>
        text.Length <= 120 ? text : text[..120];

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        try { await _loop.ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        finally { _stop.Dispose(); }
    }

    private delegate bool EnumWindowCallback(IntPtr handle, IntPtr parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowCallback callback,
        IntPtr parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumChildWindows(IntPtr parent,
        EnumWindowCallback callback, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr handle, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr handle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr handle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr handle,
        StringBuilder buffer, int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr handle,
        StringBuilder buffer, int maxCount);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr handle, uint message,
        IntPtr wParam, IntPtr lParam);
}
