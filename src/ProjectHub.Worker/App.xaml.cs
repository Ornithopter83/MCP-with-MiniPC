using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace ProjectHub.Worker;

public partial class App : System.Windows.Application
{
    internal bool ShutdownRequested { get; private set; }

    internal void RequestShutdown()
    {
        ShutdownRequested = true;
        Shutdown();
        Environment.Exit(0);
    }
    private const string InstanceMutexName = @"Local\ProjectHub.Worker.SingleInstance";
    private const string ActivateEventName = @"Local\ProjectHub.Worker.Activate";
    private Mutex? _instanceMutex;
    private EventWaitHandle? _activateEvent;
    private DispatcherTimer? _activationTimer;
    private BridgeServer? _bridgeServer;
    private ManagedWebRuntimeManager? _managedWebRuntimeManager;

    protected override void OnStartup(StartupEventArgs e)
    {
        WorkerPaths.EnsureCreated();
        var extension = ExtensionDeployment.EnsureDeployed();
        if (extension.Error is not null)
            LogStartupFailure(new InvalidOperationException("Extension deployment failed: " + extension.Error));
        _instanceMutex = new Mutex(initiallyOwned: true, InstanceMutexName, out var createdNew);
        if (!createdNew)
        {
            SignalExistingInstance();
            _instanceMutex.Dispose();
            _instanceMutex = null;
            Shutdown();
            return;
        }

        _activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);
        try
        {
            _bridgeServer = new BridgeServer();
            _bridgeServer.Start();
        }
        catch (Exception ex)
        {
            LogStartupFailure(ex);
            _bridgeServer?.Dispose();
            _bridgeServer = null;
        }

        try
        {
            _managedWebRuntimeManager = new ManagedWebRuntimeManager(WorkerPaths.Extension);
            _managedWebRuntimeManager.StartHidden(
                ManagedWebRole.Hq,
                _bridgeServer?.GetRoleConversationId("HQ"));
            _managedWebRuntimeManager.StartHidden(
                ManagedWebRole.Resource,
                _bridgeServer?.GetRoleConversationId("RESOURCE"));
        }
        catch (Exception ex)
        {
            LogStartupFailure(ex);
            _managedWebRuntimeManager?.Dispose();
            _managedWebRuntimeManager = null;
        }

        base.OnStartup(e);
        MainWindow = new MainWindow(_bridgeServer, _managedWebRuntimeManager);
        MainWindow.Show();

        _activationTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _activationTimer.Tick += (_, _) => ActivateWindowIfRequested();
        _activationTimer.Start();
}

    protected override void OnExit(ExitEventArgs e)
    {
        _activationTimer?.Stop();
        try
        {
            _managedWebRuntimeManager?.Dispose();
            _managedWebRuntimeManager = null;
            _bridgeServer?.Dispose();
            _activateEvent?.Dispose();
        }
        catch (Exception ex)
        {
            LogStartupFailure(ex);
        }
        finally
        {
            try { _instanceMutex?.ReleaseMutex(); } catch (ApplicationException) { }
            _instanceMutex?.Dispose();
        }
        base.OnExit(e);
        Environment.Exit(e.ApplicationExitCode);
    }

    private static void SignalExistingInstance()
    {
        try
        {
            using var activateEvent = EventWaitHandle.OpenExisting(ActivateEventName);
            activateEvent.Set();
        }
        catch (WaitHandleCannotBeOpenedException)
        {
        }
    }

    private void ActivateWindowIfRequested()
    {
        if (_activateEvent is null || !_activateEvent.WaitOne(0) || MainWindow is null) return;

        if (MainWindow.WindowState == WindowState.Minimized)
            MainWindow.WindowState = WindowState.Normal;

        MainWindow.Show();
        MainWindow.Activate();
        MainWindow.Topmost = true;
        MainWindow.Topmost = false;
        MainWindow.Focus();
    }

    private static void LogStartupFailure(Exception exception)
    {
        try
        {
            var directory = WorkerPaths.Logs;
            Directory.CreateDirectory(directory);
            File.AppendAllText(Path.Combine(directory, "startup-errors.log"), $"{DateTimeOffset.Now:O} {exception}{Environment.NewLine}");
        }
        catch
        {
        }
    }
}
