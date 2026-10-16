using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class UnityCliProcessRunnerTests
{
    [Fact]
    public void ChildProcessArgumentsAndIsolationArePreserved()
    {
        var root = Path.Combine(Path.GetTempPath(), "unity project");
        var args = new[] { "command", "eval", "UnityEngine.Debug.Log(\\"a b\\")",
            "--project-path", root, "--format", "json" };
        var info = UnityCliProcessRunner.CreateStartInfo(root, args);

        Assert.Equal("unity", info.FileName);
        Assert.Equal(Path.GetFullPath(root), info.WorkingDirectory);
        Assert.False(info.UseShellExecute);
        Assert.True(info.CreateNoWindow);
        Assert.True(info.RedirectStandardOutput);
        Assert.True(info.RedirectStandardError);
        Assert.Equal(args, info.ArgumentList);
    }
}
