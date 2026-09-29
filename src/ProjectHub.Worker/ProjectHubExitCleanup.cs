using System.Diagnostics;
using System.IO;
using System.Threading;

namespace ProjectHub.Worker;

public static class ProjectHubExitCleanup
{
    public const string HelperSwitch = "--projecthub-cleanup-after-exit";
    private static readonly TimeSpan ImmediateRetryDelay = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan HelperRetryDelay = TimeSpan.FromMilliseconds(500);

    public static bool IsHelperInvocation(IReadOnlyList<string> args)
        => args.Count >= 3 &&
           string.Equals(args[0], HelperSwitch, StringComparison.Ordinal);

    public static int RunHelper(IReadOnlyList<string> args)
    {
        if (!IsHelperInvocation(args) ||
            !int.TryParse(args[1], out var parentProcessId))
            return 2;

        string projectHubRoot;
        try
        {
            projectHubRoot = NormalizeAndValidateProjectHubRoot(args[2]);
        }
        catch
        {
            return 2;
        }

        WaitForParentExit(parentProcessId, TimeSpan.FromSeconds(30));

        return TryDeleteProjectHubRoot(
            projectHubRoot,
            attempts: 120,
            HelperRetryDelay,
            out _)
            ? 0
            : 3;
    }

    public static string GetProjectHubRoot(string workspace)
    {
        if (string.IsNullOrWhiteSpace(workspace))
            throw new ArgumentException("작업공간이 비어 있습니다.", nameof(workspace));

        return NormalizeAndValidateProjectHubRoot(
            ProjectWorkspacePersistence.RootDirectory(workspace));
    }

    public static bool TryDeleteWorkspaceProjectHubRoot(
        string workspace,
        out string? error)
    {
        try
        {
            var root = GetProjectHubRoot(workspace);
            return TryDeleteProjectHubRoot(
                root,
                attempts: 8,
                ImmediateRetryDelay,
                out error);
        }
        catch (Exception exception)
        {
            error = exception.GetType().Name + ": " + exception.Message;
            return false;
        }
    }

    public static bool TryScheduleAfterExit(
        string workspace,
        int parentProcessId,
        out string? error)
    {
        try
        {
            var executable = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable))
            {
                error = "현재 Worker 실행 파일 경로를 확인할 수 없습니다.";
                return false;
            }

            var projectHubRoot = GetProjectHubRoot(workspace);
            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                WorkingDirectory = AppContext.BaseDirectory,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add(HelperSwitch);
            startInfo.ArgumentList.Add(parentProcessId.ToString());
            startInfo.ArgumentList.Add(projectHubRoot);

            using var helper = Process.Start(startInfo);
            if (helper is null)
            {
                error = "종료 후 ProjectHub 정리 helper를 시작하지 못했습니다.";
                return false;
            }

            error = null;
            return true;
        }
        catch (Exception exception)
        {
            error = exception.GetType().Name + ": " + exception.Message;
            return false;
        }
    }

    private static void WaitForParentExit(
        int parentProcessId,
        TimeSpan timeout)
    {
        if (parentProcessId <= 0 ||
            parentProcessId == Environment.ProcessId)
            return;

        try
        {
            using var parent = Process.GetProcessById(parentProcessId);
            parent.WaitForExit((int)Math.Clamp(
                timeout.TotalMilliseconds,
                0,
                int.MaxValue));
        }
        catch (ArgumentException)
        {
            // 이미 종료된 프로세스다.
        }
        catch
        {
            // helper의 핵심 책임은 정리 재시도이므로 부모 wait 실패만으로 중단하지 않는다.
        }
    }

    private static bool TryDeleteProjectHubRoot(
        string projectHubRoot,
        int attempts,
        TimeSpan delay,
        out string? error)
    {
        if (attempts < 1)
            throw new ArgumentOutOfRangeException(nameof(attempts));

        projectHubRoot = NormalizeAndValidateProjectHubRoot(projectHubRoot);
        if (!Directory.Exists(projectHubRoot))
        {
            error = null;
            return true;
        }

        Exception? lastException = null;
        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                ClearDeleteBlockingAttributes(new DirectoryInfo(projectHubRoot));
                Directory.Delete(projectHubRoot, recursive: true);
                error = null;
                return true;
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                lastException = exception;
                if (attempt < attempts)
                    Thread.Sleep(delay);
            }
        }

        error = lastException is null
            ? "ProjectHub 작업 폴더 삭제에 실패했습니다."
            : lastException.GetType().Name + ": " + lastException.Message;
        return false;
    }

    private static string NormalizeAndValidateProjectHubRoot(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("ProjectHub 경로가 비어 있습니다.", nameof(path));

        var fullPath = Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var directory = new DirectoryInfo(fullPath);
        if (!string.Equals(
                directory.Name,
                ".projecthub",
                StringComparison.OrdinalIgnoreCase) ||
            directory.Parent is null)
        {
            throw new InvalidOperationException(
                "종료 정리 대상은 target workspace 바로 아래 .projecthub 폴더여야 합니다.");
        }

        return fullPath;
    }

    private static void ClearDeleteBlockingAttributes(DirectoryInfo directory)
    {
        if (!directory.Exists)
            return;

        foreach (var entry in directory.EnumerateFileSystemInfos())
        {
            if (entry is DirectoryInfo child &&
                !entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                ClearDeleteBlockingAttributes(child);
            }

            entry.Attributes &= ~(FileAttributes.ReadOnly | FileAttributes.System);
        }

        directory.Attributes &= ~(FileAttributes.ReadOnly | FileAttributes.System);
    }
}
