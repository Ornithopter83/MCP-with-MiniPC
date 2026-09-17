namespace ProjectHub.Agent;

public sealed class LargeDataScanner(long thresholdBytes = 1L << 30)
{
    public Task<IReadOnlyList<LargeFileInventoryItem>> ScanInventoryAsync(ProjectRegistration project, CancellationToken cancellationToken)
    {
        var result = new List<LargeFileInventoryItem>();
        if (!Directory.Exists(project.LocalPath)) return Task.FromResult<IReadOnlyList<LargeFileInventoryItem>>(result);
        foreach (var path in Directory.EnumerateFiles(project.LocalPath, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsIgnored(path)) continue;
            var first = new FileInfo(path);
            if (first.Length < thresholdBytes) continue;
            var relative = Path.GetRelativePath(project.LocalPath, path).Replace(Path.DirectorySeparatorChar, '/');
            result.Add(new LargeFileInventoryItem(project.ProjectId, relative, first.Length, first.LastWriteTimeUtc));
        }
        return Task.FromResult<IReadOnlyList<LargeFileInventoryItem>>(result);
    }

    private static bool IsIgnored(string path) => path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(part => part is ".git" or "bin" or "obj" or "node_modules" or "Library" or "Temp" or "Logs");
}

public sealed record LargeFileInventoryItem(string ProjectId, string RelativePath, long SizeBytes, DateTime LastWriteTimeUtc);
