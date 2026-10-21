using System.IO;

namespace ProjectHub.Worker;

/// <summary>
/// Atomic reservation of project-relative write scopes; different calls may run
/// concurrently only when their declared paths do not overlap.
/// </summary>
internal sealed class TemporaryWorkScopeRegistry
{
    private readonly object _gate = new();
    private readonly Dictionary<string, (string Root, string[] Paths)> _leases =
        new(StringComparer.Ordinal);
    // Keep completed parallel scopes until their entire overlapping-in-time
    // cohort has finished, so a late snapshot cannot blame sibling WORK edits.
    private readonly Dictionary<string, HashSet<string>> _recentCohortScopes =
        new(StringComparer.OrdinalIgnoreCase);

    internal static bool IsAllowedPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Trim() != path ||
            !MilestoneDefinitionContract.IsSafeRelativePath(path))
            return false;
        var normalized = path.Replace('\\', '/').TrimEnd('/');
        if (normalized is "" or "." || normalized.StartsWith("/", StringComparison.Ordinal))
            return false;
        return !normalized.Split('/').Any(segment =>
            segment is "." or ".." ||
            segment.Equals(".git", StringComparison.OrdinalIgnoreCase) ||
            segment.Equals(".projecthub", StringComparison.OrdinalIgnoreCase) ||
            segment.Equals("temp", StringComparison.OrdinalIgnoreCase) ||
            segment.Equals(".github", StringComparison.OrdinalIgnoreCase) ||
            segment.Equals("Master-Polish.md", StringComparison.OrdinalIgnoreCase));
    }

    internal static bool Overlaps(string a, string b)
    {
        var x = a.Replace('\\', '/').TrimEnd('/');
        var y = b.Replace('\\', '/').TrimEnd('/');
        return x.Equals(y, StringComparison.OrdinalIgnoreCase) ||
            x.StartsWith(y + "/", StringComparison.OrdinalIgnoreCase) ||
            y.StartsWith(x + "/", StringComparison.OrdinalIgnoreCase);
    }

    internal bool TryAcquire(string projectRoot, IReadOnlyList<string> paths,
        out IDisposable? lease)
    {
        lease = null;
        if (paths.Count is < 1 or > 5 || paths.Any(p => !IsAllowedPath(p)))
            return false;
        var root = Path.GetFullPath(projectRoot);
        // Deny existing symlink/junction components: a lexical relative path
        // must not escape the selected real project through a reparse point.
        foreach (var relative in paths)
        {
            var current = root;
            foreach (var part in relative.Replace('\\', '/').Split('/'))
            {
                current = Path.Combine(current, part);
                if ((File.Exists(current) || Directory.Exists(current)) &&
                    (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    return false;
            }
        }
        lock (_gate)
        {
            if (_leases.Values.Any(active =>
                string.Equals(active.Root, root, StringComparison.OrdinalIgnoreCase) &&
                active.Paths.Any(other => paths.Any(p => Overlaps(p, other)))))
                return false;
            if (!_leases.Values.Any(active =>
                active.Root.Equals(root, StringComparison.OrdinalIgnoreCase)))
                _recentCohortScopes.Remove(root);
            var id = Guid.NewGuid().ToString("N");
            _leases.Add(id, (root, paths.ToArray()));
            lease = new Reservation(this, id);
            return true;
        }
    }

    internal IReadOnlyList<string> OtherConcurrentScopes(
        string projectRoot, IReadOnlyList<string> ownPaths)
    {
        var root = Path.GetFullPath(projectRoot);
        lock (_gate)
        {
            var active = _leases.Values
                .Where(item => item.Root.Equals(root, StringComparison.OrdinalIgnoreCase))
                .SelectMany(item => item.Paths);
            var recentlyCompleted = _recentCohortScopes.TryGetValue(root, out var recent)
                ? recent : Enumerable.Empty<string>();
            return active.Concat(recentlyCompleted)
                .Where(path => !ownPaths.Any(own => Overlaps(own, path)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }

    private void Release(string id)
    {
        lock (_gate)
        {
            if (!_leases.Remove(id, out var released))
                return;
            if (!_recentCohortScopes.TryGetValue(released.Root, out var recent))
            {
                recent = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                _recentCohortScopes[released.Root] = recent;
            }
            recent.UnionWith(released.Paths);
            if (!_leases.Values.Any(active =>
                active.Root.Equals(released.Root, StringComparison.OrdinalIgnoreCase)))
                _recentCohortScopes.Remove(released.Root);
        }
    }

    private sealed class Reservation(TemporaryWorkScopeRegistry registry, string id)
        : IDisposable
    {
        private int _disposed;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                registry.Release(id);
        }
    }
}
