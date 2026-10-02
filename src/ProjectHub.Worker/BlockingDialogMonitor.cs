using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace ProjectHub.Worker;

internal sealed record BlockingDialogObservation(
    IntPtr WindowHandle,
    int ProcessId,
    string Title);

internal sealed class BlockingDialogMonitor : IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ConfirmationWindow = TimeSpan.FromSeconds(3);
    private static readonly string[] FaultTitleMarkers =
    {
        "error",
        "warning",
        "failed",
        "failure",
        "exception",
        "crash",
        "fatal",
        "assertion",
        "not responding",
        "runtime library",
        "오류",
        "에러",
        "경고",
        "실패",
        "예외",
        "중단",
        "응답 없음"
    };

    private readonly string _ownerLabel;
    private readonly Func<IReadOnlyList<int>> _snapshotProcessIds;
    private readonly Action<BlockingDialogObservation> _onConfirmed;
    private readonly CancellationTokenSource _cancellation = new();
    private Task? _monitorTask;
    private int _started;
    private int _disposed;

    public BlockingDialogMonitor(
        string ownerLabel,
        Func<IReadOnlyList<int>> snapshotProcessIds,
        Action<BlockingDialogObservation> onConfirmed)
    {
        _ownerLabel = ownerLabel;
        _snapshotProcessIds = snapshotProcessIds;
        _onConfirmed = onConfirmed;
    }

    internal static bool ShouldMonitor(string ownerLabel, ProcessStartInfo startInfo)
    {
        ArgumentNullException.ThrowIfNull(startInfo);

        if (!startInfo.CreateNoWindow)
            return false;

        return !ownerLabel.StartsWith(
            "Managed Chromium",
            StringComparison.OrdinalIgnoreCase);
    }

    public void Start()
    {
        if (!OperatingSystem.IsWindows())
            return;
        if (Interlocked.Exchange(ref _started, 1) != 0)
            return;

        _monitorTask = Task.Run(() => MonitorAsync(_cancellation.Token));
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        try { _cancellation.Cancel(); } catch { }
        _cancellation.Dispose();
    }

    private async Task MonitorAsync(CancellationToken cancellationToken)
    {
        var firstSeen = new Dictionary<IntPtr, DateTimeOffset>();

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var now = DateTimeOffset.UtcNow;
                var observations = ScanOwnedFaultDialogs(_snapshotProcessIds());
                var visibleHandles = observations
                    .Select(observation => observation.WindowHandle)
                    .ToHashSet();

                foreach (var stale in firstSeen.Keys
                             .Where(handle => !visibleHandles.Contains(handle))
                             .ToArray())
                {
                    firstSeen.Remove(stale);
                }

                foreach (var observation in observations)
                {
                    if (!firstSeen.TryGetValue(observation.WindowHandle, out var since))
                    {
                        firstSeen[observation.WindowHandle] = now;
                        continue;
                    }

                    if (now - since < ConfirmationWindow)
                        continue;

                    BlockingDialogLog.Write(_ownerLabel, observation, now - since);
                    _onConfirmed(observation);
                    return;
                }

                await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            BlockingDialogLog.WriteMonitorFailure(_ownerLabel, exception);
        }
    }

    internal static IReadOnlyList<BlockingDialogObservation> ScanOwnedFaultDialogs(
        IReadOnlyList<int> processIds)
    {
        if (!OperatingSystem.IsWindows() || processIds.Count == 0)
            return Array.Empty<BlockingDialogObservation>();

        var owned = processIds.ToHashSet();
        var result = new List<BlockingDialogObservation>();

        EnumWindows(
            (windowHandle, _) =>
            {
                if (!IsWindowVisible(windowHandle))
                    return true;

                GetWindowThreadProcessId(windowHandle, out var processId);
                if (processId == 0 || !owned.Contains(unchecked((int)processId)))
                    return true;

                var className = ReadClassName(windowHandle);
                if (!string.Equals(className, "#32770", StringComparison.Ordinal))
                    return true;

                var title = ReadWindowTitle(windowHandle);
                if (!LooksLikeFaultTitle(title))
                    return true;

                result.Add(new BlockingDialogObservation(
                    windowHandle,
                    unchecked((int)processId),
                    title));
                return true;
            },
            IntPtr.Zero);

        return result;
    }

    internal static bool LooksLikeFaultTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return false;

        return FaultTitleMarkers.Any(marker =>
            title.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    private static string ReadClassName(IntPtr windowHandle)
    {
        var buffer = new StringBuilder(256);
        return GetClassNameW(windowHandle, buffer, buffer.Capacity) > 0
            ? buffer.ToString()
            : string.Empty;
    }

    private static string ReadWindowTitle(IntPtr windowHandle)
    {
        var length = GetWindowTextLengthW(windowHandle);
        if (length <= 0)
            return string.Empty;

        var buffer = new StringBuilder(Math.Min(length + 1, 1024));
        return GetWindowTextW(windowHandle, buffer, buffer.Capacity) > 0
            ? buffer.ToString()
            : string.Empty;
    }

    private delegate bool EnumWindowsCallback(IntPtr windowHandle, IntPtr parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(
        EnumWindowsCallback callback,
        IntPtr parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr windowHandle);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(
        IntPtr windowHandle,
        out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassNameW(
        IntPtr windowHandle,
        StringBuilder className,
        int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLengthW(IntPtr windowHandle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextW(
        IntPtr windowHandle,
        StringBuilder text,
        int maxCount);
}

internal static class BlockingDialogLog
{
    private static readonly object Gate = new();

    public static void Write(
        string ownerLabel,
        BlockingDialogObservation observation,
        TimeSpan visibleFor)
    {
        WriteLine(
            $"{DateTimeOffset.UtcNow:O}\tBLOCKING_DIALOG_DETECTED" +
            $"\towner={Sanitize(ownerLabel)}" +
            $"\tpid={observation.ProcessId}" +
            $"\tvisibleMs={(long)visibleFor.TotalMilliseconds}" +
            $"\ttitle={Sanitize(observation.Title)}");
    }

    public static void WriteMonitorFailure(string ownerLabel, Exception exception)
    {
        WriteLine(
            $"{DateTimeOffset.UtcNow:O}\tBLOCKING_DIALOG_MONITOR_ERROR" +
            $"\towner={Sanitize(ownerLabel)}" +
            $"\ttype={exception.GetType().Name}");
    }

    private static void WriteLine(string line)
    {
        try
        {
            Directory.CreateDirectory(WorkerPaths.Logs);
            var path = Path.Combine(WorkerPaths.Logs, "blocking-dialogs.log");
            lock (Gate)
                File.AppendAllText(path, line + Environment.NewLine, Encoding.UTF8);
        }
        catch
        {
        }
    }

    private static string Sanitize(string? value)
    {
        var normalized = (value ?? string.Empty)
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Replace('\t', ' ')
            .Trim();

        return normalized.Length <= 240
            ? normalized
            : normalized[..240];
    }
}
