using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;

namespace ProjectHub.Worker;

public enum ManagedWebRole
{
    Hq,
    Resource
}

public sealed record ManagedWebRuntimeStatus(
    ManagedWebRole Role,
    string State,
    bool Running,
    bool Hidden,
    string? ExecutablePath,
    string ProfilePath,
    int? ProcessId,
    string? Error);

public sealed class ManagedWebRuntimeManager : IDisposable
{
    private const string ChromeForTestingMetadataUrl =
        "https://googlechromelabs.github.io/chrome-for-testing/last-known-good-versions-with-downloads.json";

    private static readonly SemaphoreSlim RuntimeProvisionGate = new(1, 1);
    private static readonly HttpClient RuntimeClient = new()
    {
        Timeout = TimeSpan.FromMinutes(10)
    };

    private sealed class Slot
    {
        public Process? Process { get; set; }
        public SuspendedJobProcess? Launch { get; set; }
        public WorkerChildProcessJob? ProcessJob { get; set; }
        public bool Hidden { get; set; }
        public string? ExecutablePath { get; set; }
        public string? Error { get; set; }
        public bool Provisioning { get; set; }
    }

    private readonly object _gate = new();
    private readonly string _extensionDirectory;
    private readonly string _managedRuntimeToken;
    private readonly Dictionary<ManagedWebRole, Slot> _slots = new()
    {
        [ManagedWebRole.Hq] = new Slot(),
        [ManagedWebRole.Resource] = new Slot()
    };

    public event Action<ManagedWebRuntimeStatus>? StatusChanged;

    public ManagedWebRuntimeManager(string extensionDirectory, string managedRuntimeToken)
    {
        _extensionDirectory = Path.GetFullPath(extensionDirectory);
        _managedRuntimeToken = string.IsNullOrWhiteSpace(managedRuntimeToken)
            ? throw new ArgumentException("관리형 Web 런타임 토큰이 필요합니다.", nameof(managedRuntimeToken))
            : managedRuntimeToken.Trim();
    }

    public ManagedWebRuntimeStatus GetStatus(ManagedWebRole role)
    {
        lock (_gate)
        {
            var slot = _slots[role];
            RefreshExitedProcess(slot);
            return Snapshot(role, slot);
        }
    }

    public Task<ManagedWebRuntimeStatus> StartHiddenAsync(
        ManagedWebRole role,
        string? conversationId = null,
        CancellationToken cancellationToken = default)
        => RestartFreshAsync(role, hidden: true, conversationId, cancellationToken);

    public Task<ManagedWebRuntimeStatus> ShowForLoginAsync(
        ManagedWebRole role,
        string? conversationId = null,
        CancellationToken cancellationToken = default)
        => RestartFreshAsync(role, hidden: false, conversationId, cancellationToken);

    public Task<ManagedWebRuntimeStatus> HideAsync(
        ManagedWebRole role,
        string? conversationId = null,
        CancellationToken cancellationToken = default)
        => RestartFreshAsync(role, hidden: true, conversationId, cancellationToken);

    public async Task<ManagedWebRuntimeStatus> RestartHiddenAsync(
        ManagedWebRole role,
        string? conversationId = null,
        CancellationToken cancellationToken = default)
    {
        Stop(role);
        return await StartHiddenAsync(role, conversationId, cancellationToken);
    }

    public void Stop(ManagedWebRole role)
    {
        ManagedWebRuntimeStatus status;
        lock (_gate)
        {
            var slot = _slots[role];
            StopProcess(slot);
            slot.Error = null;
            slot.Provisioning = false;
            slot.Hidden = true;
            status = Snapshot(role, slot);
        }
        StatusChanged?.Invoke(status);
    }

    public static string ProfilePathFor(ManagedWebRole role)
        => role == ManagedWebRole.Hq ? WorkerPaths.ManagedWebHqProfile : WorkerPaths.ManagedWebResourceProfile;

    public static string RoleToken(ManagedWebRole role)
        => role == ManagedWebRole.Hq ? "HQ" : "RESOURCE";

    public static string ResolveLaunchUrl(
        ManagedWebRole role,
        string? conversationId,
        string? managedRuntimeToken = null)
    {
        var roleToken = Uri.EscapeDataString(RoleToken(role));
        var query = "projecthub-managed-role=" + roleToken;
        if (!string.IsNullOrWhiteSpace(managedRuntimeToken))
            query += "&projecthub-runtime-token=" + Uri.EscapeDataString(managedRuntimeToken.Trim());

        if (!string.IsNullOrWhiteSpace(conversationId))
            return $"https://chatgpt.com/c/{Uri.EscapeDataString(conversationId.Trim())}?{query}";
        return $"https://chatgpt.com/?{query}";
    }

    public static IReadOnlyList<string> BuildLaunchArguments(
        ManagedWebRole role,
        bool hidden,
        string extensionDirectory,
        string profilePath,
        string? conversationId,
        string? managedRuntimeToken = null)
    {
        var arguments = new List<string>
        {
            $"--user-data-dir={Path.GetFullPath(profilePath)}",
            "--profile-directory=Default",
            $"--disable-extensions-except={Path.GetFullPath(extensionDirectory)}",
            $"--load-extension={Path.GetFullPath(extensionDirectory)}",
            "--no-first-run",
            "--no-default-browser-check",
            "--disable-background-mode",
            "--disable-session-crashed-bubble",
            "--disable-background-timer-throttling",
            "--disable-renderer-backgrounding",
            "--disable-backgrounding-occluded-windows",
            "--window-size=1280,900"
        };

        if (hidden)
            arguments.Add("--window-position=-32000,-32000");

        arguments.Add("--app=" + ResolveLaunchUrl(role, conversationId, managedRuntimeToken));
        return arguments;
    }

    public int TerminateStaleOwnedBrowserProcesses()
    {
        var executable = ResolveBrowserExecutable();
        if (string.IsNullOrWhiteSpace(executable) || !IsWorkerOwnedBrowserExecutable(executable))
            return 0;

        var terminated = 0;
        var processName = Path.GetFileNameWithoutExtension(executable);
        foreach (var process in Process.GetProcessesByName(processName))
        {
            try
            {
                string? processPath = null;
                try
                {
                    processPath = process.MainModule?.FileName;
                }
                catch
                {
                }

                // 현재 선택된 chrome.exe 한 파일만 비교하지 않는다.
                // Worker 관리 BrowserRuntime의 이전/대체 경로에서 남은 프로세스도
                // 모두 Worker 소유 stale Chromium으로 취급한다.
                if (!IsWorkerOwnedBrowserExecutable(processPath))
                    continue;

                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(5000);
                    terminated++;
                }
            }
            catch
            {
            }
            finally
            {
                process.Dispose();
            }
        }

        return terminated;
    }

    public static bool IsWorkerOwnedBrowserExecutable(string? executable)
    {
        if (string.IsNullOrWhiteSpace(executable))
            return false;

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(executable);
        }
        catch
        {
            return false;
        }

        var managedRoot = Path.GetFullPath(WorkerPaths.ManagedWebBrowserRuntime)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var bundledRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "BrowserRuntime"))
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        return fullPath.StartsWith(managedRoot, StringComparison.OrdinalIgnoreCase) ||
               fullPath.StartsWith(bundledRoot, StringComparison.OrdinalIgnoreCase);
    }

    public static string? ResolveBrowserExecutable()
    {
        var configured = Environment.GetEnvironmentVariable("PROJECTHUB_CHROMIUM_PATH");
        var candidates = new[]
        {
            configured,
            Path.Combine(AppContext.BaseDirectory, "BrowserRuntime", "chrome.exe"),
            Path.Combine(AppContext.BaseDirectory, "BrowserRuntime", "chrome-win64", "chrome.exe"),
            Path.Combine(WorkerPaths.ManagedWebBrowserRuntime, "chrome.exe"),
            Path.Combine(WorkerPaths.ManagedWebBrowserRuntime, "chrome-win64", "chrome.exe")
        };

        return candidates
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.GetFullPath(path!))
            .FirstOrDefault(File.Exists);
    }

    public static async Task<string> EnsureBrowserRuntimeAsync(
        CancellationToken cancellationToken = default)
    {
        var existing = ResolveBrowserExecutable();
        if (!string.IsNullOrWhiteSpace(existing))
            return existing;

        await RuntimeProvisionGate.WaitAsync(cancellationToken);
        try
        {
            existing = ResolveBrowserExecutable();
            if (!string.IsNullOrWhiteSpace(existing))
                return existing;

            WorkerPaths.EnsureCreated();

            using var metadataResponse = await RuntimeClient.GetAsync(
                ChromeForTestingMetadataUrl,
                cancellationToken);
            metadataResponse.EnsureSuccessStatusCode();

            var metadataJson = await metadataResponse.Content.ReadAsStringAsync(cancellationToken);
            using var metadata = JsonDocument.Parse(metadataJson);
            var stable = metadata.RootElement
                .GetProperty("channels")
                .GetProperty("Stable");
            var version = stable.GetProperty("version").GetString()
                ?? throw new InvalidOperationException("Chrome for Testing Stable version을 확인할 수 없습니다.");
            var download = stable
                .GetProperty("downloads")
                .GetProperty("chrome")
                .EnumerateArray()
                .FirstOrDefault(item =>
                    string.Equals(
                        item.GetProperty("platform").GetString(),
                        "win64",
                        StringComparison.OrdinalIgnoreCase));
            if (download.ValueKind == JsonValueKind.Undefined)
                throw new InvalidOperationException("Chrome for Testing win64 다운로드를 찾을 수 없습니다.");

            var downloadUrl = download.GetProperty("url").GetString()
                ?? throw new InvalidOperationException("Chrome for Testing 다운로드 URL이 없습니다.");
            if (!Uri.TryCreate(downloadUrl, UriKind.Absolute, out var uri) ||
                uri.Scheme != Uri.UriSchemeHttps ||
                !uri.Host.Equals("storage.googleapis.com", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Chrome for Testing 다운로드 URL이 허용된 공식 호스트가 아닙니다.");

            var installParent = WorkerPaths.ManagedWebRoot;
            var tempRoot = Path.Combine(
                installParent,
                "BrowserRuntime.installing." + Guid.NewGuid().ToString("N"));
            var zipPath = Path.Combine(
                installParent,
                "chrome-for-testing." + Guid.NewGuid().ToString("N") + ".zip");

            Directory.CreateDirectory(tempRoot);
            try
            {
                using (var response = await RuntimeClient.GetAsync(
                           uri,
                           HttpCompletionOption.ResponseHeadersRead,
                           cancellationToken))
                {
                    response.EnsureSuccessStatusCode();
                    await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
                    await using var target = new FileStream(
                        zipPath,
                        FileMode.CreateNew,
                        FileAccess.Write,
                        FileShare.None,
                        1024 * 1024,
                        useAsync: true);
                    await source.CopyToAsync(target, cancellationToken);
                }

                var zipLength = new FileInfo(zipPath).Length;
                if (zipLength < 1024 * 1024)
                    throw new InvalidOperationException("Chrome for Testing 다운로드 파일이 비정상적으로 작습니다.");

                ZipFile.ExtractToDirectory(zipPath, tempRoot);
                var extractedExecutable = Path.Combine(tempRoot, "chrome-win64", "chrome.exe");
                if (!File.Exists(extractedExecutable))
                    throw new InvalidOperationException("Chrome for Testing chrome.exe를 추출하지 못했습니다.");

                if (Directory.Exists(WorkerPaths.ManagedWebBrowserRuntime))
                    Directory.Delete(WorkerPaths.ManagedWebBrowserRuntime, recursive: true);
                Directory.Move(tempRoot, WorkerPaths.ManagedWebBrowserRuntime);

                File.WriteAllText(
                    Path.Combine(WorkerPaths.ManagedWebBrowserRuntime, "runtime.json"),
                    JsonSerializer.Serialize(
                        new
                        {
                            product = "Chrome for Testing",
                            version,
                            platform = "win64",
                            source = downloadUrl,
                            installedAtUtc = DateTimeOffset.UtcNow
                        },
                        new JsonSerializerOptions { WriteIndented = true }));

                return ResolveBrowserExecutable()
                    ?? throw new InvalidOperationException("설치한 Chrome for Testing 실행 파일을 찾을 수 없습니다.");
            }
            finally
            {
                try
                {
                    if (File.Exists(zipPath))
                        File.Delete(zipPath);
                }
                catch
                {
                }

                try
                {
                    if (Directory.Exists(tempRoot))
                        Directory.Delete(tempRoot, recursive: true);
                }
                catch
                {
                }
            }
        }
        finally
        {
            RuntimeProvisionGate.Release();
        }
    }

    private async Task<ManagedWebRuntimeStatus> RestartFreshAsync(
        ManagedWebRole role,
        bool hidden,
        string? conversationId,
        CancellationToken cancellationToken)
    {
        await Task.Run(() => Stop(role), cancellationToken);
        ClearBrowserSessionState(ProfilePathFor(role));
        return await StartAsync(role, hidden, conversationId, cancellationToken);
    }

    public static void ClearBrowserSessionState(string profilePath)
    {
        var defaultProfile = Path.Combine(Path.GetFullPath(profilePath), "Default");
        var sessionsDirectory = Path.Combine(defaultProfile, "Sessions");

        try
        {
            if (Directory.Exists(sessionsDirectory))
                Directory.Delete(sessionsDirectory, recursive: true);
        }
        catch
        {
        }

        foreach (var fileName in new[] { "Current Session", "Current Tabs", "Last Session", "Last Tabs" })
        {
            try
            {
                var path = Path.Combine(defaultProfile, fileName);
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
            }
        }
    }

    private async Task<ManagedWebRuntimeStatus> StartAsync(
        ManagedWebRole role,
        bool hidden,
        string? conversationId,
        CancellationToken cancellationToken)
    {
        ManagedWebRuntimeStatus provisioningStatus;
        lock (_gate)
        {
            var slot = _slots[role];
            slot.Hidden = hidden;
            slot.Error = null;
            slot.Provisioning = ResolveBrowserExecutable() is null;
            provisioningStatus = Snapshot(role, slot);
        }
        StatusChanged?.Invoke(provisioningStatus);

        try
        {
            await EnsureBrowserRuntimeAsync(cancellationToken);
            ClearBrowserSessionState(ProfilePathFor(role));
            lock (_gate)
                _slots[role].Provisioning = false;
            return Start(role, hidden, conversationId);
        }
        catch (Exception exception)
        {
            ManagedWebRuntimeStatus status;
            lock (_gate)
            {
                var slot = _slots[role];
                StopProcess(slot);
                slot.Provisioning = false;
                slot.Hidden = hidden;
                slot.ExecutablePath = null;
                slot.Error = exception.Message;
                status = Snapshot(role, slot);
            }

            StatusChanged?.Invoke(status);
            return status;
        }
    }

    private ManagedWebRuntimeStatus Start(
        ManagedWebRole role,
        bool hidden,
        string? conversationId)
    {
        ManagedWebRuntimeStatus status;
        lock (_gate)
        {
            var slot = _slots[role];
            StopProcess(slot);
            slot.Hidden = hidden;
            slot.Error = null;

            try
            {
                WorkerPaths.EnsureCreated();
                Directory.CreateDirectory(ProfilePathFor(role));

                if (!Directory.Exists(_extensionDirectory))
                    throw new DirectoryNotFoundException("GPTWeb-Hub 확장 배포 폴더를 찾을 수 없습니다.");

                var executable = ResolveBrowserExecutable()
                    ?? throw new FileNotFoundException("관리형 Chrome for Testing 런타임을 찾을 수 없습니다.");

                var startInfo = new ProcessStartInfo
                {
                    FileName = executable,
                    UseShellExecute = false,
                    CreateNoWindow = false,
                    WorkingDirectory = Path.GetDirectoryName(executable) ?? AppContext.BaseDirectory
                };
                foreach (var argument in BuildLaunchArguments(
                    role,
                    hidden,
                    _extensionDirectory,
                    ProfilePathFor(role),
                    conversationId,
                    _managedRuntimeToken))
                {
                    startInfo.ArgumentList.Add(argument);
                }

                var processJob = new WorkerChildProcessJob("Managed Chromium " + RoleToken(role));
                SuspendedJobProcess? launch = null;
                try
                {
                    launch = processJob.Start(startInfo);
                    var startedProcess = launch.Process;

                    startedProcess.Exited += (_, _) => OnProcessExited(role, startedProcess);
                    slot.Process = startedProcess;
                    slot.Launch = launch;
                    slot.ProcessJob = processJob;
                    slot.ExecutablePath = executable;
                    startedProcess.EnableRaisingEvents = true;
                }
                catch
                {
                    try { processJob.Dispose(); }
                    catch { }
                    try { launch?.Dispose(); }
                    catch { }
                    throw;
                }
            }
            catch (Exception exception)
            {
                slot.Process = null;
                slot.Launch = null;
                slot.ProcessJob = null;
                slot.ExecutablePath = null;
                slot.Error = exception.Message;
            }

            status = Snapshot(role, slot);
        }

        StatusChanged?.Invoke(status);
        return status;
    }

    private void OnProcessExited(ManagedWebRole role, Process process)
    {
        ManagedWebRuntimeStatus? status = null;
        WorkerChildProcessJob? processJob = null;
        SuspendedJobProcess? launch = null;
        lock (_gate)
        {
            var slot = _slots[role];
            if (!ReferenceEquals(slot.Process, process))
                return;

            slot.Process = null;
            launch = slot.Launch;
            slot.Launch = null;
            processJob = slot.ProcessJob;
            slot.ProcessJob = null;
            status = Snapshot(role, slot);
        }

        // launcher가 끝났을 때 role Job을 닫아 renderer/helper를 함께 끝낸다.
        try { processJob?.Dispose(); }
        catch { }
        try { launch?.Dispose(); }
        catch { }

        if (status is not null)
            StatusChanged?.Invoke(status);
    }

    private static void RefreshExitedProcess(Slot slot)
    {
        if (slot.Process is null)
            return;

        try
        {
            if (!slot.Process.HasExited)
                return;
        }
        catch
        {
        }

        var launch = slot.Launch;
        var processJob = slot.ProcessJob;
        slot.Process = null;
        slot.Launch = null;
        slot.ProcessJob = null;

        try { processJob?.Dispose(); }
        catch { }
        try { launch?.Dispose(); }
        catch { }
    }

    private static void StopProcess(Slot slot)
    {
        var process = slot.Process;
        var launch = slot.Launch;
        var processJob = slot.ProcessJob;
        slot.Process = null;
        slot.Launch = null;
        slot.ProcessJob = null;

        // 역할 종료/재시작 명령의 경계는 Job close다.
        // 브라우저 UI의 graceful close를 기다리지 않고 renderer/helper까지 즉시 종료한다.
        try { processJob?.Dispose(); }
        catch { }

        if (process is not null)
        {
            try
            {
                if (!process.HasExited && !process.WaitForExit(2000))
                    process.Kill(entireProcessTree: true);
            }
            catch
            {
                try { process.Kill(entireProcessTree: true); }
                catch { }
            }
        }

        try { launch?.Dispose(); }
        catch { }
    }

    private static ManagedWebRuntimeStatus Snapshot(ManagedWebRole role, Slot slot)
    {
        var running = slot.Process is not null;
        return new ManagedWebRuntimeStatus(
            role,
            slot.Error is not null ? "ERROR" : slot.Provisioning ? "PROVISIONING" : running ? slot.Hidden ? "HIDDEN" : "VISIBLE" : "STOPPED",
            running,
            running && slot.Hidden,
            slot.ExecutablePath,
            ProfilePathFor(role),
            running ? slot.Process?.Id : null,
            slot.Error);
    }

    public void Dispose()
    {
        (Process? Process, SuspendedJobProcess? Launch, WorkerChildProcessJob? ProcessJob)[] active;
        lock (_gate)
        {
            active = _slots.Values
                .Select(slot => (slot.Process, slot.Launch, slot.ProcessJob))
                .ToArray();

            foreach (var slot in _slots.Values)
            {
                slot.Process = null;
                slot.Launch = null;
                slot.ProcessJob = null;
                slot.Provisioning = false;
            }
        }

        foreach (var item in active)
        {
            try { item.ProcessJob?.Dispose(); }
            catch { }
        }

        foreach (var item in active)
        {
            try
            {
                if (item.Process is not null &&
                    !item.Process.HasExited &&
                    !item.Process.WaitForExit(2000))
                {
                    item.Process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                try { item.Process?.Kill(entireProcessTree: true); }
                catch { }
            }
            finally
            {
                try { item.Launch?.Dispose(); }
                catch { }
            }
        }
    }

}
