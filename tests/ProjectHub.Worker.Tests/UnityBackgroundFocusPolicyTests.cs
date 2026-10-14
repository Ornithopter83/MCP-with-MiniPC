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
    [InlineData(true, true, 0U, false)]
    [InlineData(true, true, 350U, false)]
    [InlineData(true, true, 351U, true)]
    [InlineData(true, true, 10_000U, true)]
    [InlineData(true, false, 10_000U, false)]
    [InlineData(false, true, 10_000U, false)]
    public void NativeActivationRestoresOnlyWhenUserDidNotJustSwitchFocus(
        bool unity, bool previous, uint inputAge, bool expected)
    {
        Assert.Equal(expected,
            UnityBackgroundFocusPolicy.ShouldRestore(unity, previous, inputAge));
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
