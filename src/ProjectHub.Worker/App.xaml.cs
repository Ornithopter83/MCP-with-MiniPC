using System.Diagnostics;
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
    }
    private const string InstanceMutexName = @"Local\ProjectHub.Worker.SingleInstance";
    private const string ActivateEventName = @"Local\ProjectHub.Worker.Activate";
    private Mutex? _instanceMutex;
    private EventWaitHandle? _activateEvent;
    private DispatcherTimer? _activationTimer;
    private BridgeServer? _bridgeServer;
    private ManagedWebRuntimeManager? _managedWebRuntimeManager;
    private IDisposable? _werPolicyLease;

    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        WorkerPaths.EnsureCreated();
        _instanceMutex = new Mutex(initiallyOwned: true, InstanceMutexName, out var createdNew);
        if (!createdNew)
        {
            SignalExistingInstance();
            _instanceMutex.Dispose();
            _instanceMutex = null;
            Shutdown();
            return;
        }

        try
        {
            TemporaryWerPolicyLease.RecoverStaleLease();
            _werPolicyLease = TemporaryWerPolicyLease.Acquire("Worker process");
        }
        catch (Exception ex)
        {
            LogStartupFailure(ex);
            try { _instanceMutex.ReleaseMutex(); } catch (ApplicationException) { }
            _instanceMutex.Dispose();
            _instanceMutex = null;
            Shutdown();
            return;
        }

        if (!WorkerPaths.TryResetEphemeralDirectories(out var startupCleanupError))
            LogStartupFailure(new IOException("Startup ephemeral cleanup failed: " + (startupCleanupError ?? "unknown")));
        var extension = ExtensionDeployment.EnsureDeployed();
        if (extension.Error is not null)
            LogStartupFailure(new InvalidOperationException("Extension deployment failed: " + extension.Error));

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
            _managedWebRuntimeManager = new ManagedWebRuntimeManager(
                WorkerPaths.Extension,
                _bridgeServer?.ManagedRuntimeToken
                    ?? throw new InvalidOperationException("관리형 Web bridge 토큰을 생성하지 못했습니다."));
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
        DispatcherUnhandledException -= OnDispatcherUnhandledException;
        _activationTimer?.Stop();
        WriteShutdownProcessAudit("before-dispose");
        WorkerChildProcessJob.TerminateAllActiveJobs();
        try
        {
            _werPolicyLease?.Dispose();
            _werPolicyLease = null;
            TemporaryWerPolicyLease.RestoreActiveLeaseForShutdown();
        }
        catch (Exception ex)
        {
            LogStartupFailure(ex);
            try
            {
                TemporaryWerPolicyLease.RestoreActiveLeaseForShutdown();
            }
            catch (Exception restoreException)
            {
                LogStartupFailure(restoreException);
            }
            finally
            {
                _werPolicyLease = null;
            }
        }

        try
        {
            _managedWebRuntimeManager?.Dispose();
            _managedWebRuntimeManager = null;
            _bridgeServer?.Dispose();
            _activateEvent?.Dispose();
            WriteShutdownProcessAudit("after-dispose");
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
    }

    private void WriteShutdownProcessAudit(string stage)
    {
        try
        {
            Directory.CreateDirectory(WorkerPaths.Logs);
            var line =
                $"{DateTimeOffset.Now:O}\tstage={stage}\tworkerPid={Environment.ProcessId}";
            File.AppendAllText(
                Path.Combine(WorkerPaths.Logs, "shutdown-process-audit.log"),
                line + Environment.NewLine);
        }
        catch
        {
        }
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

    private void OnDispatcherUnhandledException(
        object sender,
        DispatcherUnhandledExceptionEventArgs e)
    {
        LogRuntimeFailure("WPF_DISPATCHER_UNHANDLED", e.Exception);
    }

    internal static void LogRuntimeFailure(
        string source,
        Exception exception)
    {
        try
        {
            var directory = WorkerPaths.Logs;
            Directory.CreateDirectory(directory);
            File.AppendAllText(
                Path.Combine(directory, "runtime-errors.log"),
                $"{DateTimeOffset.Now:O}\tsource={source}\tworkerPid={Environment.ProcessId}{Environment.NewLine}{exception}{Environment.NewLine}");
        }
        catch
        {
        }
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
