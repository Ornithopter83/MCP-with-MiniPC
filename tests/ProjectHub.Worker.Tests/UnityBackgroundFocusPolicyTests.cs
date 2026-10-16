using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class UnityBackgroundFocusPolicyTests
{
    [Theory]
    [InlineData("Unity", true)]
    [InlineData("Unity.exe", true)]
    [InlineData("unity", true)]
    [InlineData("Unity Hub", false)]
    [InlineData("UnityCrashHandler64", false)]
    [InlineData("ProjectHub.Worker", false)]
    public void OnlyUnityEditorForegroundQualifies(string name, bool matches)
    {
        Assert.Equal(matches,
            UnityBackgroundFocusPolicy.IsUnityEditorProcess(name));
    }

    [Theory]
    [InlineData(true, true, false, true)]
    [InlineData(true, true, true, false)]
    [InlineData(true, false, false, false)]
    [InlineData(false, true, false, false)]
    public void UnityHijackIsRestoredRegardlessOfRecentTypingInOtherApp(
        bool unity, bool previous, bool explicitUnitySelection, bool expected)
    {
        Assert.Equal(expected, UnityBackgroundFocusPolicy.ShouldRestore(
            unity, previous, explicitUnitySelection));
    }

    [Theory]
    [InlineData(0U, true, true)]
    [InlineData(350U, true, true)]
    [InlineData(351U, true, false)]
    [InlineData(0U, false, false)]
    [InlineData(100U, false, false)]
    public void GraceIsReservedForActualUnityMouseOrAltTabSelection(
        uint age, bool inputTargetsUnity, bool expected)
    {
        Assert.Equal(expected, UnityBackgroundFocusPolicy.IsRecentUserSelection(
            age, inputTargetsUnity));
    }

    [Theory]
    [InlineData("Unity 6 - Unity3DSample - Windows", "Unity3DSample", true)]
    [InlineData("Unity 6 - DifferentProject", "Unity3DSample", false)]
    [InlineData("", "Unity3DSample", false)]
    public void ForegroundLeaseMustMatchEditorProjectTitle(
        string title, string project, bool expected)
    {
        Assert.Equal(expected,
            UnityBackgroundFocusPolicy.IsProjectEditorTitle(title, project));
    }

    [Theory]
    [InlineData("Unity", "Unity 6 - Unity3DSample - Windows", @"C:\projects\Unity3DSample", true)]
    [InlineData("Unity", "Unity 6 - DifferentProject - Windows", @"C:\projects\Unity3DSample", false)]
    [InlineData("Unity Hub", "Unity 6 - Unity3DSample - Windows", @"C:\projects\Unity3DSample", false)]
    [InlineData("Unity", "Unity 6 - Unity3DSample - Windows", null, false)]
    public void BackgroundGuardDoesNotTargetOtherProjectEditors(
        string process, string title, string? root, bool expected)
    {
        Assert.Equal(expected,
            UnityBackgroundFocusPolicy.IsGuardedEditorForProject(process, title, root));
    }

    [Fact]
    public void CaptureScopeNeverRequiresAnOSForegroundWindowOnHeadlessPlatforms()
    {
        if (OperatingSystem.IsWindows()) return;
        using var guard = UnityBackgroundFocusGuard.Start();
        var scope = guard.BeginBackgroundCapture();
        Assert.Contains("BACKGROUND_CAPTURE", scope.Diagnostic);
        scope.Dispose();
        Assert.Equal(0, guard.RestoreAttempts);
    }

    [Fact]
    public void GuardMatchesOnlyCurrentProjectPlayerOutput()
    {
        var root = Path.Combine(Path.GetTempPath(), "UnityPlayerFocusTest");
        Assert.True(UnityBackgroundFocusPolicy.IsProjectPlayerExecutable(
            root, Path.Combine(root, "Builds", "Windows", "Quiz.exe")));
        Assert.False(UnityBackgroundFocusPolicy.IsProjectPlayerExecutable(
            root, Path.Combine(root, "Assets", "Editor.exe")));
        Assert.False(UnityBackgroundFocusPolicy.IsProjectPlayerExecutable(
            root + "-other", Path.Combine(root, "Builds", "Quiz.exe")));
        Assert.False(UnityBackgroundFocusPolicy.IsProjectPlayerExecutable(
            root, Path.Combine(root, "Builds", "notes.txt")));
    }

    [Fact]
    public void GuardCanBeSafelyDisposedTwiceOnHeadlessPlatforms()
    {
        if (OperatingSystem.IsWindows())
            return; // Actual WinEvent registration requires a desktop session.
        using var guard = UnityBackgroundFocusGuard.Start();
        Assert.False(guard.Available);
        Assert.Contains("FOCUS_GUARD_UNAVAILABLE", guard.Diagnostic);
        guard.Dispose();
        guard.Dispose();
    }
}
