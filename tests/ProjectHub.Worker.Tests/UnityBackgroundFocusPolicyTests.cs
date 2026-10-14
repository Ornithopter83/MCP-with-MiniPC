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
