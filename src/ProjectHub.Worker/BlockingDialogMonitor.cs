using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace ProjectHub.Worker;

internal sealed record BlockingDialogObservation(
    IntPtr WindowHandle,
    int ProcessId,
    string Title,
    bool ExternalOwner,
    IReadOnlyList<int> RelatedProcessIds);

internal sealed class BlockingDialogMonitor : IDisposable
{
    internal const string ExternalScopeEnabledEnvironment = "PROJECTHUB_BLOCKING_DIALOG_EXTERNAL";
    internal const string ExternalScopeRootEnvironment = "PROJECTHUB_BLOCKING_DIALOG_SCOPE_ROOT";

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);
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
        "응용 프로그램 오류",
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
    private readonly bool _allowExternalWorkspaceDialogs;
    private readonly string? _externalScopeRoot;
    private readonly CancellationTokenSource _cancellation = new();
    private Task? _monitorTask;
    private int _started;
    private int _disposed;

    public BlockingDialogMonitor(
        string ownerLabel,
        Func<IReadOnlyList<int>> snapshotProcessIds,
        Action<BlockingDialogObservation> onConfirmed,
        bool allowExternalWorkspaceDialogs = false,
        string? externalScopeRoot = null)
    {
        _ownerLabel = ownerLabel;
        _snapshotProcessIds = snapshotProcessIds;
        _onConfirmed = onConfirmed;
        _allowExternalWorkspaceDialogs = allowExternalWorkspaceDialogs;
        _externalScopeRoot = NormalizeScopeRoot(externalScopeRoot);
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

    internal static bool TryGetExternalWorkspaceScope(
        ProcessStartInfo startInfo,
        out string? scopeRoot)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        scopeRoot = null;

        if (!startInfo.Environment.TryGetValue(
                ExternalScopeEnabledEnvironment,
                out var enabled) ||
            !string.Equals(enabled, "1", StringComparison.Ordinal))
            return false;

        if (!startInfo.Environment.TryGetValue(
                ExternalScopeRootEnvironment,
                out var configuredRoot) ||
            string.IsNullOrWhiteSpace(configuredRoot))
            return false;

        scopeRoot = NormalizeScopeRoot(configuredRoot);
        return scopeRoot is not null;
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
                var observations = ScanFaultDialogs(
                    _snapshotProcessIds(),
                    _allowExternalWorkspaceDialogs,
                    _externalScopeRoot);
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
                    if (observation.ExternalOwner)
                        RemediateExternalDialog(observation);

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
        => ScanFaultDialogs(
            processIds,
            allowExternalWorkspaceDialogs: false,
            externalScopeRoot: null);

    internal static IReadOnlyList<BlockingDialogObservation> ScanFaultDialogs(
        IReadOnlyList<int> processIds,
        bool allowExternalWorkspaceDialogs,
        string? externalScopeRoot)
    {
        if (!OperatingSystem.IsWindows())
            return Array.Empty<BlockingDialogObservation>();
        if (processIds.Count == 0 && !allowExternalWorkspaceDialogs)
            return Array.Empty<BlockingDialogObservation>();

        var owned = processIds.ToHashSet();
        var normalizedScopeRoot = allowExternalWorkspaceDialogs
            ? NormalizeScopeRoot(externalScopeRoot)
            : null;
        var result = new List<BlockingDialogObservation>();

        EnumWindows(
            (windowHandle, _) =>
            {
                if (!IsWindowVisible(windowHandle))
                    return true;

                var title = ReadWindowTitle(windowHandle);
                if (!LooksLikeFaultTitle(title))
                    return true;

                GetWindowThreadProcessId(windowHandle, out var rawProcessId);
                if (rawProcessId == 0)
                    return true;

                var processId = unchecked((int)rawProcessId);
                var className = ReadClassName(windowHandle);

                if (owned.Contains(processId))
                {
                    if (!IsDialogLikeClass(className) &&
                        !TryExtractExecutableNameFromFaultTitle(title, out _))
                        return true;

                    result.Add(new BlockingDialogObservation(
                        windowHandle,
                        processId,
                        title,
                        ExternalOwner: false,
                        RelatedProcessIds: new[] { processId }));
                    return true;
                }

                if (normalizedScopeRoot is null ||
                    !TryExtractExecutableNameFromFaultTitle(title, out var executableName))
                    return true;

                var relatedProcessIds = FindWorkspaceProcesses(
                    executableName,
                    normalizedScopeRoot);
                if (relatedProcessIds.Count == 0)
                    return true;

                if (!IsDialogLikeClass(className) &&
                    !LooksLikeApplicationErrorTitle(title, executableName))
                    return true;

                result.Add(new BlockingDialogObservation(
                    windowHandle,
                    processId,
                    title,
                    ExternalOwner: true,
                    RelatedProcessIds: relatedProcessIds));
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

    internal static bool TryExtractExecutableNameFromFaultTitle(
        string? title,
        out string executableName)
    {
        executableName = string.Empty;
        if (string.IsNullOrWhiteSpace(title))
            return false;

        foreach (var separator in new[] { " - ", " – ", " — " })
        {
            var separatorIndex = title.IndexOf(
                separator,
                StringComparison.Ordinal);
            if (separatorIndex <= 0)
                continue;

            var candidate = title[..separatorIndex].Trim();
            if (!candidate.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                continue;
            if (candidate.IndexOfAny(
                    new[]
                    {
                        Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar,
                        ':'
                    }) >= 0)
                continue;
            if (candidate.Length > 260)
                continue;

            executableName = candidate;
            return true;
        }

        return false;
    }

    private static bool LooksLikeApplicationErrorTitle(
        string title,
        string executableName)
    {
        if (!title.StartsWith(executableName, StringComparison.OrdinalIgnoreCase))
            return false;

        var suffix = title[executableName.Length..].TrimStart();
        return suffix.StartsWith("-", StringComparison.Ordinal) ||
               suffix.StartsWith("–", StringComparison.Ordinal) ||
               suffix.StartsWith("—", StringComparison.Ordinal);
    }

    private static IReadOnlyList<int> FindWorkspaceProcesses(
        string executableName,
        string scopeRoot)
    {
        var processName = Path.GetFileNameWithoutExtension(executableName);
        if (string.IsNullOrWhiteSpace(processName))
            return Array.Empty<int>();

        var result = new List<int>();
        Process[] processes;
        try
        {
            processes = Process.GetProcessesByName(processName);
        }
        catch
        {
            return result;
        }

        foreach (var process in processes)
        {
            using (process)
            {
                try
                {
                    var executablePath = process.MainModule?.FileName;
                    if (string.IsNullOrWhiteSpace(executablePath) ||
                        !IsPathWithinRoot(scopeRoot, executablePath))
                        continue;

                    result.Add(process.Id);
                }
                catch (Exception exception) when (
                    exception is InvalidOperationException or
                    Win32Exception or
                    NotSupportedException)
                {
                }
            }
        }

        return result;
    }

    internal static bool IsPathWithinRoot(string root, string path)
    {
        try
        {
            var normalizedRoot = Path.GetFullPath(root)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            var normalizedPath = Path.GetFullPath(path);

            return normalizedPath.StartsWith(
                normalizedRoot,
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (
            exception is ArgumentException or
            NotSupportedException or
            PathTooLongException)
        {
            return false;
        }
    }

    private static bool IsDialogLikeClass(string className)
        => string.Equals(className, "#32770", StringComparison.Ordinal) ||
           className.Contains("dialog", StringComparison.OrdinalIgnoreCase);

    private static string? NormalizeScopeRoot(string? scopeRoot)
    {
        if (string.IsNullOrWhiteSpace(scopeRoot))
            return null;

        try
        {
            return Path.GetFullPath(scopeRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch (Exception exception) when (
            exception is ArgumentException or
            NotSupportedException or
            PathTooLongException)
        {
            return null;
        }
    }

    private static void RemediateExternalDialog(
        BlockingDialogObservation observation)
    {
        foreach (var processId in observation.RelatedProcessIds.Distinct())
        {
            try
            {
                using var process = Process.GetProcessById(processId);
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch (Exception exception) when (
                exception is ArgumentException or
                InvalidOperationException or
                NotSupportedException or
                Win32Exception)
            {
            }
        }

        try
        {
            PostMessageW(
                observation.WindowHandle,
                WmClose,
                IntPtr.Zero,
                IntPtr.Zero);
        }
        catch
        {
        }
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

    private const uint WmClose = 0x0010;

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

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessageW(
        IntPtr windowHandle,
        uint message,
        IntPtr wParam,
        IntPtr lParam);
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
            $"\texternalOwner={observation.ExternalOwner}" +
            $"\trelatedPids={string.Join(",", observation.RelatedProcessIds)}" +
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
