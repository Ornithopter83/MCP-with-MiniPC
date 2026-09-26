using System.Diagnostics;
using System.IO;

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
    private sealed class Slot
    {
        public Process? Process { get; set; }
        public bool Hidden { get; set; }
        public string? ExecutablePath { get; set; }
        public string? Error { get; set; }
    }

    private readonly object _gate = new();
    private readonly string _extensionDirectory;
    private readonly Dictionary<ManagedWebRole, Slot> _slots = new()
    {
        [ManagedWebRole.Hq] = new Slot(),
        [ManagedWebRole.Resource] = new Slot()
    };

    public event Action<ManagedWebRuntimeStatus>? StatusChanged;

    public ManagedWebRuntimeManager(string extensionDirectory)
    {
        _extensionDirectory = Path.GetFullPath(extensionDirectory);
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

    public ManagedWebRuntimeStatus StartHidden(ManagedWebRole role, string? conversationId = null)
        => Start(role, hidden: true, conversationId);

    public ManagedWebRuntimeStatus ShowForLogin(ManagedWebRole role, string? conversationId = null)
        => Start(role, hidden: false, conversationId);

    public ManagedWebRuntimeStatus RestartHidden(ManagedWebRole role, string? conversationId = null)
    {
        Stop(role);
        return StartHidden(role, conversationId);
    }

    public void Stop(ManagedWebRole role)
    {
        ManagedWebRuntimeStatus status;
        lock (_gate)
        {
            var slot = _slots[role];
            StopProcess(slot);
            slot.Error = null;
            slot.Hidden = true;
            status = Snapshot(role, slot);
        }
        StatusChanged?.Invoke(status);
    }

    public static string ProfilePathFor(ManagedWebRole role)
        => role == ManagedWebRole.Hq ? WorkerPaths.ManagedWebHqProfile : WorkerPaths.ManagedWebResourceProfile;

    public static string RoleToken(ManagedWebRole role)
        => role == ManagedWebRole.Hq ? "HQ" : "RESOURCE";

    public static string ResolveLaunchUrl(ManagedWebRole role, string? conversationId)
    {
        var roleToken = Uri.EscapeDataString(RoleToken(role));
        if (!string.IsNullOrWhiteSpace(conversationId))
            return $"https://chatgpt.com/c/{Uri.EscapeDataString(conversationId.Trim())}?projecthub-managed-role={roleToken}";
        return $"https://chatgpt.com/?projecthub-managed-role={roleToken}";
    }

    public static IReadOnlyList<string> BuildLaunchArguments(
        ManagedWebRole role,
        bool hidden,
        string extensionDirectory,
        string profilePath,
        string? conversationId)
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
            "--window-size=1280,900"
        };

        if (hidden)
        {
            arguments.Add("--window-position=-32000,-32000");
            arguments.Add("--start-minimized");
        }

        arguments.Add(ResolveLaunchUrl(role, conversationId));
        return arguments;
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
            Path.Combine(WorkerPaths.ManagedWebBrowserRuntime, "chrome-win64", "chrome.exe"),
            RuntimeDiagnostics.FindChrome()
        };

        return candidates
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.GetFullPath(path!))
            .FirstOrDefault(File.Exists);
    }

    private ManagedWebRuntimeStatus Start(ManagedWebRole role, bool hidden, string? conversationId)
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
                    ?? throw new FileNotFoundException(
                        "관리형 Chromium 런타임을 찾을 수 없습니다. BrowserRuntime 폴더 또는 PROJECTHUB_CHROMIUM_PATH를 확인하세요.");

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
                    conversationId))
                {
                    startInfo.ArgumentList.Add(argument);
                }

                var process = Process.Start(startInfo)
                    ?? throw new InvalidOperationException("Chromium 프로세스를 시작하지 못했습니다.");
                process.EnableRaisingEvents = true;
                process.Exited += (_, _) => OnProcessExited(role, process);

                slot.Process = process;
                slot.ExecutablePath = executable;
            }
            catch (Exception exception)
            {
                slot.Process = null;
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
        lock (_gate)
        {
            var slot = _slots[role];
            if (!ReferenceEquals(slot.Process, process))
                return;

            slot.Process = null;
            status = Snapshot(role, slot);
        }

        process.Dispose();
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

        slot.Process.Dispose();
        slot.Process = null;
    }

    private static void StopProcess(Slot slot)
    {
        var process = slot.Process;
        slot.Process = null;
        if (process is null)
            return;

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
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

    private static ManagedWebRuntimeStatus Snapshot(ManagedWebRole role, Slot slot)
    {
        var running = slot.Process is not null;
        return new ManagedWebRuntimeStatus(
            role,
            slot.Error is not null ? "ERROR" : running ? slot.Hidden ? "HIDDEN" : "VISIBLE" : "STOPPED",
            running,
            running && slot.Hidden,
            slot.ExecutablePath,
            ProfilePathFor(role),
            running ? slot.Process?.Id : null,
            slot.Error);
    }

    public void Dispose()
    {
        Stop(ManagedWebRole.Hq);
        Stop(ManagedWebRole.Resource);
    }
}
