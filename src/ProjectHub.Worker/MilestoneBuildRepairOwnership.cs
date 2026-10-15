namespace ProjectHub.Worker;

/// <summary>Never create a fake WORK #8 when recompilation fails.
/// Use the responsible existing WORKITEM or defer ambiguous attribution to HQ.</summary>
internal static class MilestoneBuildRepairOwnership
{
    internal static string? Resolve(
        IReadOnlyDictionary<string, MilestoneWorkDefinition> items,
        string diagnostic)
    {
        var writable = items.Values.Where(w => !w.ReadOnly).ToArray();
        if (writable.Length == 0) return null;
        var message = (diagnostic ?? string.Empty).Replace('\\', '/');
        var matches = writable.Where(w => w.WritePaths.Any(path =>
            message.Contains(path.Replace('\\', '/'),
                StringComparison.OrdinalIgnoreCase))).ToArray();
        if (matches.Length == 1) return matches[0].Id;
        if (matches.Length > 1) return null;
        return writable.Length == 1 ? writable[0].Id : null;
    }
}
