using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace ProjectHub.Worker;

/// <summary>
/// Read-only command availability snapshot shared by all roles in this Worker
/// process. It checks a tool's real --version exit code, not just its PATH
/// entry (Windows Store Python aliases are not Python installations).
/// </summary>
internal static class WorkerCliEnvironmentProbe
{
    private static readonly Lazy<Task<string>> Guidance =
        new(BuildGuidanceAsync, LazyThreadSafetyMode.ExecutionAndPublication);

    internal static Task<string> GetGuidanceAsync(CancellationToken token) =>
        Guidance.Value.WaitAsync(token);

    internal static bool IsWindowsAppAlias(string path) =>
        path.Replace('\\', '/').Contains("/Microsoft/WindowsApps/",
            StringComparison.OrdinalIgnoreCase);

    internal static string FormatStatus(string? executable, bool versionPassed) =>
        !string.IsNullOrWhiteSpace(executable) &&
        !IsWindowsAppAlias(executable) && versionPassed ? "READY" : "UNAVAILABLE";

    private static async Task<string> BuildGuidanceAsync()
    {
        var rg = await ProbeAsync("rg.exe", "--version").ConfigureAwait(false);
        var node = await ProbeAsync("node.exe", "--version").ConfigureAwait(false);
        var python = await ProbeAsync("python.exe", "--version").ConfigureAwait(false);
        if (python != "READY")
            python = await ProbeAsync("py.exe", "-3", "--version").ConfigureAwait(false);

        return "CLI 명령 환경(Worker 실동작 점검): rg=" + rg +
               ", python=" + python + ", node=" + node + "." +
               " UNAVAILABLE 명령을 반복 호출하지 말고 rg는 git ls-files/" +
               "Get-ChildItem/Select-String으로, Python은 사용 가능한 Node.js나" +
               " PowerShell로 대체하라. Windows PowerShell 5.1에서는 파이프/" +
               " here-string 한글 전달의 인코딩을 확인하고, JSON과 스크립트는" +
               " [IO.File]::WriteAllText(경로, 내용, [Text.UTF8Encoding]::new($false))" +
               "로 UTF-8 BOM 없이 저장하라. 실패한 명령과 대체 방법은 후속 역할에 보고하라." +
               " 이 점검은 Unity Editor 연결·실제 테스트 통과 증거가 아니다.";
    }

    private static async Task<string> ProbeAsync(string command, params string[] args)
    {
        var executable = UnityFreeDesignToolPolicy.FindExecutable(command);
        if (string.IsNullOrWhiteSpace(executable) || IsWindowsAppAlias(executable))
            return "UNAVAILABLE";

        try
        {
            var info = new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            foreach (var argument in args)
                info.ArgumentList.Add(argument);

            using var job = new WorkerChildProcessJob("UnityWorker CLI availability probe");
            using var child = job.Start(info, CancellationToken.None);
            var stdout = child.StandardOutput!.ReadToEndAsync();
            var stderr = child.StandardError!.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            try
            {
                await child.Process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
                return FormatStatus(executable, child.Process.ExitCode == 0);
            }
            catch (OperationCanceledException)
            {
                job.Dispose();
                return "UNAVAILABLE";
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                  Win32Exception or InvalidOperationException)
        {
            return "UNAVAILABLE";
        }
    }
}
