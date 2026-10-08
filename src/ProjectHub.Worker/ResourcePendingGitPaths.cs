using System.IO;
using System.Text.Json;

namespace ProjectHub.Worker;

/// <summary>
/// Durable, workspace-scoped paths of completed RESOURCE output. A resource
/// never waits for Git. Snapshot generations prevent acknowledging an output
/// replaced while Git finalize was already in flight.
/// </summary>
internal sealed class ResourcePendingGitPaths
{
    private sealed class WorkspaceState
    {
        public long Generation { get; set; }
        public Dictionary<string, long> Paths { get; set; } =
            new(StringComparer.OrdinalIgnoreCase);
    }

    private readonly object _gate = new();
    private readonly Dictionary<string, WorkspaceState> _states =
        new(StringComparer.OrdinalIgnoreCase);
    private const string LedgerFolder = ".projecthub";
    private const string LedgerFile = "resource-pending-git.json";

    public void Register(string workingDirectory, IEnumerable<string> paths)
    {
        lock (_gate)
        {
            var root = Path.GetFullPath(workingDirectory);
            var state = GetState(root);
            var changed = false;
            foreach (var candidate in paths)
            {
                var path = (candidate ?? string.Empty).Replace('\\', '/').Trim();
                if (!MilestoneDefinitionContract.IsSafeRelativePath(path))
                    continue;
                state.Paths[path] = ++state.Generation;
                changed = true;
            }
            if (changed)
                Save(root, state);
        }
    }

    public IReadOnlyDictionary<string, long> Snapshot(string workingDirectory)
    {
        lock (_gate)
            return new Dictionary<string, long>(
                GetState(Path.GetFullPath(workingDirectory)).Paths,
                StringComparer.OrdinalIgnoreCase);
    }

    public void Acknowledge(
        string workingDirectory,
        IReadOnlyDictionary<string, long> committedSnapshot)
    {
        lock (_gate)
        {
            var root = Path.GetFullPath(workingDirectory);
            var state = GetState(root);
            var changed = false;
            foreach (var entry in committedSnapshot)
            {
                if (state.Paths.TryGetValue(entry.Key, out var version) &&
                    version == entry.Value)
                    changed |= state.Paths.Remove(entry.Key);
            }
            if (changed)
                Save(root, state);
        }
    }

    private WorkspaceState GetState(string root)
    {
        if (_states.TryGetValue(root, out var cached))
            return cached;

        var file = Path.Combine(root, LedgerFolder, LedgerFile);
        WorkspaceState state;
        if (File.Exists(file))
        {
            state = JsonSerializer.Deserialize<WorkspaceState>(
                File.ReadAllText(file)) ?? new WorkspaceState();
            state.Paths = new Dictionary<string, long>(
                state.Paths, StringComparer.OrdinalIgnoreCase);
        }
        else
        {
            state = new WorkspaceState();
        }
        _states[root] = state;
        return state;
    }

    private static void Save(string root, WorkspaceState state)
    {
        var folder = Path.Combine(root, LedgerFolder);
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, LedgerFile);
        var tempFile = Path.Combine(folder, LedgerFile + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            File.WriteAllText(tempFile, JsonSerializer.Serialize(state));
            File.Move(tempFile, file, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }
}
