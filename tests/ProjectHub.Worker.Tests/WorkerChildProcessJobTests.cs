using System.Diagnostics;
using System.Reflection;
using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class WorkerChildProcessJobTests
{
    [Fact]
    public void CodexRunnerTracksKillOnCloseJobPerActiveRun()
    {
        var field = typeof(CodexCliRunner).GetField(
            "_activeProcessJobs",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(field);
        Assert.True(field!.FieldType.IsGenericType);
        Assert.Equal(
            typeof(WorkerChildProcessJob),
            field.FieldType.GetGenericArguments()[1]);
    }

    [Fact]
    public void ProjectHubProcessIsNotStoredInRootJob()
    {
        var field = typeof(App).GetField(
            "_rootProcessJob",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.Null(field);
    }

    [Fact]
    public void ChildProcessJobExposesOnlyPreResumeStartPath()
    {
        Assert.NotNull(typeof(WorkerChildProcessJob).GetMethod(
            "Start",
            BindingFlags.Instance | BindingFlags.Public));

        Assert.Null(typeof(WorkerChildProcessJob).GetMethod(
            "Assign",
            BindingFlags.Instance | BindingFlags.Public));

        Assert.Null(typeof(WorkerChildProcessJob).GetMethod(
            "TerminateDescendants",
            BindingFlags.Static | BindingFlags.NonPublic));
    }

    [Fact]
    public void ChildProcessStartAcceptsCancellationToken()
    {
        var method = typeof(WorkerChildProcessJob).GetMethod(
            "Start",
            BindingFlags.Instance | BindingFlags.Public);

        Assert.NotNull(method);
        var parameters = method!.GetParameters();
        Assert.Equal(2, parameters.Length);
        Assert.Equal(typeof(ProcessStartInfo), parameters[0].ParameterType);
        Assert.Equal(typeof(CancellationToken), parameters[1].ParameterType);
        Assert.True(parameters[1].HasDefaultValue);
    }

    [Fact]
    public void ChildJobsAreRegisteredAndRemovedOnDispose()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var before = WorkerChildProcessJob.ActiveJobCount;
        var job = new WorkerChildProcessJob("registry-test");
        Assert.Equal(before + 1, WorkerChildProcessJob.ActiveJobCount);

        job.Dispose();

        Assert.Equal(before, WorkerChildProcessJob.ActiveJobCount);
    }

    [Fact]
    public void ChildJobTypeExposesGlobalShutdownDrain()
    {
        var method = typeof(WorkerChildProcessJob).GetMethod(
            "TerminateAllActiveJobs",
            BindingFlags.Static | BindingFlags.NonPublic);

        Assert.NotNull(method);
    }


    [Fact]
    public void BlockingDialogMonitorOnlyTargetsHeadlessRuns()
    {
        var headless = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            CreateNoWindow = true
        };
        var visible = new ProcessStartInfo
        {
            FileName = "chrome.exe",
            CreateNoWindow = false
        };

        Assert.True(BlockingDialogMonitor.ShouldMonitor("Codex CLI run", headless));
        Assert.False(BlockingDialogMonitor.ShouldMonitor("Managed Chromium HQ", headless));
        Assert.False(BlockingDialogMonitor.ShouldMonitor("Codex CLI run", visible));
    }

    [Theory]
    [InlineData("sample.exe - Application Error")]
    [InlineData("Warning")]
    [InlineData("처리 오류")]
    [InlineData("실행 실패")]
    public void BlockingDialogMonitorRecognizesFaultTitles(string title)
    {
        Assert.True(BlockingDialogMonitor.LooksLikeFaultTitle(title));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Open File")]
    [InlineData("Settings")]
    [InlineData("ProjectHub")]
    public void BlockingDialogMonitorIgnoresOrdinaryDialogTitles(string title)
    {
        Assert.False(BlockingDialogMonitor.LooksLikeFaultTitle(title));
    }

    [Fact]
    public void DisposingJobTerminatesStartedProcess()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var job = new WorkerChildProcessJob("test child");
        var startInfo = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = "/c ping 127.0.0.1 -n 30 > nul",
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var launched = job.Start(startInfo);
        var process = launched.Process;

        Assert.Contains(process.Id, job.SnapshotProcessIds());

        job.Dispose();

        Assert.True(process.WaitForExit(5000));
        Assert.True(process.HasExited);
    }
}
