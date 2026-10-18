using System.Text.Json;
using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class UnityEditorStartupRestartTests
{
    private static string Status(params (int pid, string project, string state)[] instances) =>
        JsonSerializer.Serialize(new
        {
            success = true,
            data = new
            {
                count = instances.Length,
                instances = instances.Select(x => new
                {
                    pid = x.pid,
                    project = x.project,
                    state = x.state
                })
            }
        });

    [Fact]
    public void OnlyExactProjectEditorCanBeSelected()
    {
        var root = Path.Combine(Path.GetTempPath(), "unity-project-restart");
        var other = Path.Combine(Path.GetTempPath(), "other-unity-project");
        var payload = Status((101, other, "ready"), (202, root, "ready"));
        Assert.True(UnityEditorStartupRestart.TryIdentifyOneInstance(
            payload, root, out var match, out var code));
        Assert.Equal("EDITOR_RESTART_INSTANCE_IDENTIFIED", code);
        Assert.Equal(202, match!.Pid);
        Assert.Equal("ready", match.State);
    }

    [Fact]
    public void MultipleEditorsForSameProjectMustNeverBeClosedByGuessing()
    {
        var root = Path.Combine(Path.GetTempPath(), "unity-project-restart");
        var payload = Status((101, root, "ready"), (202, root, "ready"));
        Assert.False(UnityEditorStartupRestart.TryIdentifyOneInstance(
            payload, root, out var match, out var code));
        Assert.Null(match);
        Assert.Equal("EDITOR_RESTART_AMBIGUOUS_PROJECT_INSTANCES", code);
    }

    [Fact]
    public void MissingOrMalformedStatusDoesNotSelectArbitraryPid()
    {
        var root = Path.Combine(Path.GetTempPath(), "unity-project-restart");
        Assert.True(UnityEditorStartupRestart.TryIdentifyOneInstance(
            Status((42, Path.Combine(root, "..", "other"), "ready")), root,
            out var none, out var notRunning));
        Assert.Null(none);
        Assert.Equal("EDITOR_RESTART_NOT_RUNNING", notRunning);
        Assert.False(UnityEditorStartupRestart.TryIdentifyOneInstance(
            """{"success":true,"data":{"count":1}}""",
            root, out _, out _));
    }

    [Theory]
    [InlineData("C:\\Program Files\\Unity\\Editor\\Unity.exe", true)]
    [InlineData("C:\\Program Files\\Unity\\Editor\\Unity Hub.exe", false)]
    [InlineData("C:\\Program Files\\Other\\Worker.exe", false)]
    public void OnlyUnityEditorExecutableIsAllowed(string executable, bool expected) =>
        Assert.Equal(expected, UnityEditorStartupRestart.IsUnityEditorExecutable(
            executable));
    [Fact]
    public void EditorGuiLaunchUsesWindowsShellAndVisibleWindow()
    {
        var root = Path.Combine(Path.GetTempPath(), "unity-project",
            Guid.NewGuid().ToString("N"));
        var editor = Path.Combine(root, "Hub", "Editor", "Unity.exe");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(editor)!);
            File.WriteAllBytes(editor, Array.Empty<byte>());
            // Construction only: never launch the fake executable.
            var launch = UnityEditorStartupRestart.CreateLaunchStartInfo(editor, root);
            Assert.True(launch.UseShellExecute);
            Assert.Equal(System.Diagnostics.ProcessWindowStyle.Normal,
                launch.WindowStyle);
            Assert.Equal(Path.GetFullPath(editor), launch.FileName);
            Assert.Equal(Path.GetDirectoryName(editor), launch.WorkingDirectory);
            Assert.Equal(new[] { "-projectPath", Path.GetFullPath(root) },
                launch.ArgumentList.ToArray());
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void MissingUnityExecutableIsRejectedBeforeShellStart()
    {
        Assert.Throws<FileNotFoundException>(() =>
            UnityEditorStartupRestart.CreateLaunchStartInfo(
                Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"),
                    "Unity.exe"), Path.GetTempPath()));
    }

}
