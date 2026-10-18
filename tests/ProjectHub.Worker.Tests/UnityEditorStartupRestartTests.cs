using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class UnityEditorStartupRestartTests
{
    [Theory]
    [InlineData("Unity", "Unity3DTest - Unity 6", true)]
    [InlineData("Unity", "OtherProject - Unity 6", false)]
    [InlineData("OtherApp", "Unity3DTest - Unity 6", false)]
    [InlineData("Unity", "Unity3DTestCopy - Unity 6", false)]
    public void ExistingEditorWindowMustMatchProject(
        string name, string title, bool expected)
    {
        var root = Path.Combine(Path.GetTempPath(), "Unity3DTest");
        Assert.Equal(expected,
            UnityEditorStartupRestart.MatchesEditorWindow(name, title, root));
    }

    [Theory]
    [InlineData("C:\\Program Files\\Unity\\Editor\\Unity.exe", true)]
    [InlineData("C:\\Program Files\\Unity\\Editor\\Unity Hub.exe", false)]
    [InlineData("C:\\Program Files\\Other\\Worker.exe", false)]
    public void OnlyUnityEditorExecutableIsAllowed(string executable, bool expected) =>
        Assert.Equal(expected, UnityEditorStartupRestart.IsUnityEditorExecutable(
            executable));

    [Fact]
    public void EditorGuiLaunchUsesWindowsShellAndExactProject()
    {
        var root = Path.Combine(Path.GetTempPath(), "unity-project",
            Guid.NewGuid().ToString("N"));
        var editor = Path.Combine(root, "Hub", "Editor", "Unity.exe");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(editor)!);
            File.WriteAllBytes(editor, Array.Empty<byte>());
            // Construction only: do not start a Unity process.
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
    public void MissingExecutableCannotLaunch()
    {
        Assert.Throws<FileNotFoundException>(() =>
            UnityEditorStartupRestart.CreateLaunchStartInfo(
                Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"),
                    "Unity.exe"), Path.GetTempPath()));
    }

    [Fact]
    public void OnlyVerifiedStartupMayReachHq()
    {
        Assert.True(new UnityEditorStartupRestart.Result(
            "EDITOR_STARTED_VERIFIED", "", true).Verified);
        Assert.True(new UnityEditorStartupRestart.Result(
            "EDITOR_RESTART_VERIFIED", "", true).Verified);
        Assert.False(new UnityEditorStartupRestart.Result(
            "EDITOR_START_NOT_CONFIRMED", "", false).Verified);
        Assert.False(new UnityEditorStartupRestart.Result(
            "EDITOR_START_CLOSE_TIMEOUT", "", false).Verified);
    }
}
