using System.Threading.Channels;
using ProjectHub.Core;

namespace ProjectHub.Agent;

public sealed class ProjectActivityMonitor(
    IReadOnlyList<ProjectRegistration> projects,
    string workstationId,
    GitStateCollector collector,
    ProjectStateSender sender,
    Action<string> log,
    Action<string> logError)
{
    private static readonly TimeSpan DebouncePeriod = TimeSpan.FromSeconds(1);

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        if (projects.Count == 0)
        {
            log("No registered projects configured; project activity monitoring is idle.");
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return;
        }

        var signal = Channel.CreateUnbounded<ProjectRegistration>();
        using var watchers = new CompositeDisposable();
        foreach (var project in projects)
        {
            if (!Directory.Exists(project.LocalPath))
            {
                logError($"Project path not found; skipping watcher: {project.LocalPath}");
                continue;
            }

            var watcher = new FileSystemWatcher(project.LocalPath)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName |
                               NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true
            };
            FileSystemEventHandler handler = (_, eventArgs) =>
            {
                if (!IsNoisePath(eventArgs.FullPath)) signal.Writer.TryWrite(project);
            };
            RenamedEventHandler renamedHandler = (_, eventArgs) =>
            {
                if (!IsNoisePath(eventArgs.FullPath)) signal.Writer.TryWrite(project);
            };
            watcher.Created += handler;
            watcher.Changed += handler;
            watcher.Deleted += handler;
            watcher.Renamed += renamedHandler;
            watchers.Add(watcher);
        }

        await foreach (var firstProject in signal.Reader.ReadAllAsync(cancellationToken))
        {
            var pending = new HashSet<ProjectRegistration> { firstProject };
            await Task.Delay(DebouncePeriod, cancellationToken);
            while (signal.Reader.TryRead(out var project)) pending.Add(project);

            foreach (var project in pending)
            {
                try
                {
                    var state = await collector.CollectAsync(
                        project.ProjectId, workstationId, project.LocalPath, cancellationToken);
                    await sender.SendAsync(project, state, cancellationToken);
                    log($"Project state sent: {project.ProjectId}");
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception) when (exception is GitStateException or HttpRequestException)
                {
                    logError($"Project state update failed for {project.ProjectId}; will retry on next activity: {exception.Message}");
                }
            }
        }
    }

    private static bool IsNoisePath(string path) =>
        path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(part => part is ".git" or "bin" or "obj" or "Library" or "Temp" or "Logs" or "node_modules");

    private sealed class CompositeDisposable : IDisposable
    {
        private readonly List<IDisposable> _items = [];
        public void Add(IDisposable item) => _items.Add(item);
        public void Dispose()
        {
            foreach (var item in _items) item.Dispose();
        }
    }
}
