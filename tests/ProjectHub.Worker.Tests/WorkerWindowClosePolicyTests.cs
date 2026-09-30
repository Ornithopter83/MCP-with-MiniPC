using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class WorkerWindowClosePolicyTests
{
    [Fact]
    public void ExplicitShutdownWinsEvenWhenSettingsAreOpen()
    {
        Assert.Equal(
            WorkerWindowCloseAction.Shutdown,
            WorkerWindowClosePolicy.Resolve(
                allowClose: false,
                shutdownRequested: true,
                dispatcherShutdownStarted: false,
                settingsOpen: true));
    }

    [Fact]
    public void AllowedCloseWinsEvenWhenSettingsAreOpen()
    {
        Assert.Equal(
            WorkerWindowCloseAction.Shutdown,
            WorkerWindowClosePolicy.Resolve(
                allowClose: true,
                shutdownRequested: false,
                dispatcherShutdownStarted: false,
                settingsOpen: true));
    }

    [Fact]
    public void SettingsOnlyCloseDismissesSettingsWithoutExiting()
    {
        Assert.Equal(
            WorkerWindowCloseAction.CloseSettings,
            WorkerWindowClosePolicy.Resolve(
                allowClose: false,
                shutdownRequested: false,
                dispatcherShutdownStarted: false,
                settingsOpen: true));
    }

    [Fact]
    public void OrdinaryWindowCloseHidesToTray()
    {
        Assert.Equal(
            WorkerWindowCloseAction.HideToTray,
            WorkerWindowClosePolicy.Resolve(
                allowClose: false,
                shutdownRequested: false,
                dispatcherShutdownStarted: false,
                settingsOpen: false));
    }
}
