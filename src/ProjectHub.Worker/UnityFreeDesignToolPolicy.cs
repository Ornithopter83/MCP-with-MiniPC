using System.IO;

namespace ProjectHub.Worker;

/// <summary>
/// Optional, no-cost graphics tools for a single Windows Unity workstation.
/// Discovery never installs software, starts a renderer, or contacts a server.
/// </summary>
internal static class UnityFreeDesignToolPolicy
{
    // The AI sees environment variable names, never workstation-specific paths.
    internal const string InkscapeVariable = "PROJECTHUB_INKSCAPE";
    internal const string BlenderVariable = "PROJECTHUB_BLENDER";

    internal static IReadOnlyDictionary<string, string> BuildDesignToolEnvironment() =>
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [InkscapeVariable] = ResolveDesignTool(
                InkscapeVariable, "inkscape.exe", "inkscape", InkscapeCandidates()),
            [BlenderVariable] = ResolveDesignTool(
                BlenderVariable, "blender.exe", "blender", BlenderCandidates())
        };

    private static string ResolveDesignTool(
        string variable, string executable, string command, IEnumerable<string> candidates)
    {
        var configured = Environment.GetEnvironmentVariable(variable);
        if (!string.IsNullOrWhiteSpace(configured))
            return configured.Trim();
        return FindExecutable(executable, candidates) ?? command;
    }

    internal static string BuildCliToolGuidance()
    {
        var rg = FindExecutable("rg.exe");
        return "CLI 명령 환경(Worker 탐지): rg=" + (rg ?? "NOT_INSTALLED") +
            ". rg가 없다면 다시 호출하지 말고 git ls-files, Get-ChildItem, Select-String 등 Windows 기본/기존 Git 명령으로 대체하라. " +
            "실패한 도구·명령의 상태를 역할 결과에 자연어로 적어 다음 역할이 동일 실패를 반복하지 않도록 하라.";
    }

    internal static string? FindExecutable(
        string name, IEnumerable<string>? candidates = null)
    {
        if (candidates is not null)
            foreach (var candidate in candidates)
                if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
                    return Path.GetFullPath(candidate);

        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var directory in path.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(directory))
                continue;
            try
            {
                var found = Path.Combine(directory.Trim().Trim('"'), name);
                if (File.Exists(found))
                    return Path.GetFullPath(found);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
            {
                // Skip invalid PATH entries. Design tool absence is not a stop.
            }
        }
        return null;
    }

    private static IEnumerable<string> InkscapeCandidates()
    {
        foreach (var root in ProgramFilesRoots())
        {
            yield return Path.Combine(root, "Inkscape", "bin", "inkscape.exe");
            yield return Path.Combine(root, "Inkscape", "inkscape.exe");
        }
    }

    private static IEnumerable<string> BlenderCandidates()
    {
        foreach (var root in ProgramFilesRoots())
        {
            var vendor = Path.Combine(root, "Blender Foundation");
            if (!Directory.Exists(vendor))
                continue;
            string[] folders;
            try { folders = Directory.GetDirectories(vendor, "Blender*"); }
            catch (IOException) { continue; }
            catch (UnauthorizedAccessException) { continue; }
            foreach (var folder in folders.OrderByDescending(path => path,
                         StringComparer.OrdinalIgnoreCase))
                yield return Path.Combine(folder, "blender.exe");
        }
    }

    private static IEnumerable<string> ProgramFilesRoots()
    {
        foreach (var kind in new[]
                 { Environment.SpecialFolder.ProgramFiles,
                   Environment.SpecialFolder.ProgramFilesX86 })
        {
            var path = Environment.GetFolderPath(kind);
            if (!string.IsNullOrWhiteSpace(path))
                yield return path;
        }
    }
}
