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

    internal static bool IsAllowedPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Trim() != path ||
            !MilestoneDefinitionContract.IsSafeRelativePath(path))
            return false;
        var normalized = path.Replace('\\', '/').TrimEnd('/');
        if (normalized is "" or "." || normalized.StartsWith("/", StringComparison.Ordinal))
            return false;
        return !normalized.Split('/').Any(segment =>
            segment is "." or ".." or ".git" or ".projecthub" or "temp" or ".github" ||
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
            var id = Guid.NewGuid().ToString("N");
            _leases.Add(id, (root, paths.ToArray()));
            lease = new Reservation(this, id);
            return true;
        }
    }

    private void Release(string id)
    {
        lock (_gate) _leases.Remove(id);
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
