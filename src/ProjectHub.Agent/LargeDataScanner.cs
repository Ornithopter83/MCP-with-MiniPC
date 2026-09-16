using System.Security.Cryptography;
using ProjectHub.Core;

namespace ProjectHub.Agent;

public sealed class LargeDataScanner(long thresholdBytes = 1L << 30)
{
    public async Task<IReadOnlyList<ProjectLargeFile>> ScanAsync(ProjectRegistration project, CancellationToken cancellationToken)
    {
        var result = new List<ProjectLargeFile>();
        if (!Directory.Exists(project.LocalPath)) return result;
        foreach (var path in Directory.EnumerateFiles(project.LocalPath, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsIgnored(path)) continue;
            var first = new FileInfo(path);
            if (first.Length < thresholdBytes) continue;
            await Task.Delay(50, cancellationToken);
            var second = new FileInfo(path);
            if (first.Length != second.Length || first.LastWriteTimeUtc != second.LastWriteTimeUtc) continue;
            await using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
            var relative = Path.GetRelativePath(project.LocalPath, path).Replace(Path.DirectorySeparatorChar, '/');
            result.Add(new ProjectLargeFile(project.ProjectId, relative, new LargeObjectIdentity(hash, second.Length), LargeDataLifecycle.LocalOnly));
        }
        return result;
    }

    private static bool IsIgnored(string path) => path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(part => part is ".git" or "bin" or "obj" or "node_modules" or "Library" or "Temp" or "Logs");
}
