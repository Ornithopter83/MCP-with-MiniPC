namespace ProjectHub.Worker;

internal enum WorkerWindowCloseAction
{
    Shutdown,
    CloseSettings,
    HideToTray
}

internal static class WorkerWindowClosePolicy
{
    public static WorkerWindowCloseAction Resolve(
        bool allowClose,
        bool shutdownRequested,
        bool dispatcherShutdownStarted,
        bool settingsOpen)
    {
        if (allowClose || shutdownRequested || dispatcherShutdownStarted)
            return WorkerWindowCloseAction.Shutdown;

        return settingsOpen
            ? WorkerWindowCloseAction.CloseSettings
            : WorkerWindowCloseAction.HideToTray;
    }
}
