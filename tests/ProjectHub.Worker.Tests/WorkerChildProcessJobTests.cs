using System.Diagnostics;
using System.Reflection;
using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class WorkerChildProcessJobTests
{
    [Fact]
    public void CodexRunnerTracksKillOnCloseJobPerActiveRun()
    {
        Assert.True(typeof(IDisposable).IsAssignableFrom(typeof(CodexCliRunner)));

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
    public void ChildProcessJobProvidesPostExitDescendantCleanupFallback()
    {
        var terminateDescendants = typeof(WorkerChildProcessJob).GetMethod(
            "TerminateDescendants",
            BindingFlags.Static | BindingFlags.NonPublic);

        Assert.NotNull(terminateDescendants);
        Assert.Equal(typeof(int), terminateDescendants!.ReturnType);

        var parameters = terminateDescendants.GetParameters();
        Assert.Equal(2, parameters.Length);
        Assert.Equal(typeof(int), parameters[0].ParameterType);
        Assert.Equal(typeof(DateTimeOffset), parameters[1].ParameterType);
    }

    [Fact]
    public void DisposingJobTerminatesAssignedProcess()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c ping 127.0.0.1 -n 30 > nul",
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        using var job = new WorkerChildProcessJob("test child");

        Assert.True(process.Start());
        job.Assign(process);
        job.Dispose();

        Assert.True(process.WaitForExit(5000));
        Assert.True(process.HasExited);
    }
}
