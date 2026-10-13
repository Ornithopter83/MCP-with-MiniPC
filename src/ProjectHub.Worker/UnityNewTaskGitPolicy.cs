using System.IO;
using System.Text;

namespace ProjectHub.Worker;

/// <summary>
/// NEW-task checkout recovery: replace only local paths that collide with
/// origin/main. Never stash or create backup copies. The separate user backup
/// is intentionally outside ProjectHub's responsibility.
/// </summary>
internal static class UnityNewTaskGitPolicy
{
    private static readonly string[] UnityIgnoreRules =
    {
        "/[Ll]ibrary/", "/[Tt]emp/", "/[Oo]bj/",
        "/[Bb]uild/", "/[Bb]uilds/", "/[Ll]ogs/",
        "/[Uu]ser[Ss]ettings/", "/[Mm]emoryCaptures/",
        "/[Rr]ecordings/", "/.vs/", "/.idea/",
        "/*.csproj", "/*.sln", "/*.suo", "/*.user",
        "/.projecthub/", "/temp/", "/bin/"
    };

    // The local exclude takes effect before checkout, even when .gitignore
    // itself is one of the files that must be overwritten from origin/main.
    internal static void PrepareLocalExcludes(string root)
    {
        var gitDir = Path.Combine(root, ".git");
        if (!Directory.Exists(gitDir))
            return;
        WorkerPaths.EnsureProjectHubLocalExclude(root);
        var info = Path.Combine(gitDir, "info");
        Directory.CreateDirectory(info);
        AppendMissing(Path.Combine(info, "exclude"));
    }

    internal static bool EnsureUnityGitIgnore(string root) =>
        AppendMissing(Path.Combine(root, ".gitignore"));

    private static bool AppendMissing(string file)
    {
        var lines = File.Exists(file) ? File.ReadAllLines(file).ToList() : new List<string>();
        var known = lines.Select(line => line.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = UnityIgnoreRules.Where(rule => !known.Contains(rule)).ToArray();
        if (missing.Length == 0) return false;
        var original = File.Exists(file) ? File.ReadAllText(file) : "";
        var separator = original.Length == 0 ||
            original.EndsWith("\n", StringComparison.Ordinal) ? "" : Environment.NewLine;
        File.WriteAllText(file, original + separator +
            "# ProjectHub Unity generated files\n" +
            string.Join("\n", missing) + "\n", new UTF8Encoding(false));
        return true;
    }

    internal sealed record Result(bool Success, string Detail,
        IReadOnlyList<string> ReplacedPaths);

    private static bool IsProtectedPath(string p)
    {
        var s = p.Replace('\\', '/');
        return s == ".git" || s.StartsWith(".git/", StringComparison.OrdinalIgnoreCase) ||
               s == ".projecthub" || s.StartsWith(".projecthub/", StringComparison.OrdinalIgnoreCase) ||
               s == "temp" || s.StartsWith("temp/", StringComparison.OrdinalIgnoreCase) ||
               s == "bin" || s.StartsWith("bin/", StringComparison.OrdinalIgnoreCase);
    }

    internal static IReadOnlyList<string> FindUntrackedCollisions(
        IReadOnlyCollection<string> targetFiles, IReadOnlyCollection<string> untracked)
    {
        var target = targetFiles.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var output = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in untracked)
        {
            if (IsProtectedPath(path)) continue;
            if (target.Contains(path) ||
                target.Any(file => file.StartsWith(path + "/", StringComparison.OrdinalIgnoreCase) ||
                    path.StartsWith(file + "/", StringComparison.OrdinalIgnoreCase)))
                output.Add(path);
        }
        return output.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    internal static async Task<Result> ResolveCheckoutConflictsAsync(
        string root, ProcessGitCommandRunner git, CancellationToken token)
    {
        async Task<GitCommandResult> Run(params string[] args) =>
            await git.RunAsync(root, args, TimeSpan.FromMinutes(2), token)
                .ConfigureAwait(false);
        static string[] ReadPaths(GitCommandResult result) => result.StandardOutput
            .Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Where(p => p.Length > 0).ToArray();

        var tree = await Run("ls-tree", "-r", "--name-only", "-z", "origin/main");
        var others = await Run("ls-files", "--others", "--exclude-standard", "-z");
        var tracked = await Run("diff", "--name-only", "-z");
        var staged = await Run("diff", "--cached", "--name-only", "-z");
        if (tree.ExitCode != 0 || others.ExitCode != 0 ||
            tracked.ExitCode != 0 || staged.ExitCode != 0)
            return new(false, "NEW_TASK_GIT_SCAN_FAILED", Array.Empty<string>());

        var targetFiles = ReadPaths(tree);
        var untrackedCollisions = FindUntrackedCollisions(targetFiles, ReadPaths(others));
        var head = await Run("rev-parse", "--verify", "HEAD");
        var trackedConflicts = Array.Empty<string>();
        if (head.ExitCode == 0)
        {
            var differing = await Run("diff", "--name-only", "-z", "HEAD", "origin/main");
            if (differing.ExitCode != 0)
                return new(false, "NEW_TASK_GIT_TARGET_DIFF_FAILED", Array.Empty<string>());
            var changedInTarget = ReadPaths(differing).ToHashSet(StringComparer.OrdinalIgnoreCase);
            trackedConflicts = ReadPaths(tracked).Concat(ReadPaths(staged))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(changedInTarget.Contains)
                .Where(p => !IsProtectedPath(p))
                .ToArray();
        }

        if (untrackedCollisions.Count == 0 && trackedConflicts.Length == 0)
            return new(false, "NEW_TASK_NO_DETERMINISTIC_COLLISIONS", Array.Empty<string>());

        // Avoid partially deleting a tree when a parent is a symlink/reparse point.
        foreach (var path in untrackedCollisions.Concat(trackedConflicts))
            if (!TryGetSafePath(root, path, out _, out var error))
                return new(false, "NEW_TASK_UNSAFE_PATH: " + path + " " + error,
                    Array.Empty<string>());

        // Only paths that differ in the target branch lose staged/unstaged edits.
        // All unrelated staged and unstaged entries remain unchanged.
        foreach (var path in trackedConflicts)
        {
            var restored = await Run("--literal-pathspecs", "restore",
                "--source=HEAD", "--staged", "--worktree", "--", path);
            if (restored.ExitCode != 0)
                return new(false, "NEW_TASK_CONFLICT_RESTORE_FAILED: " + path +
                    " " + restored.StandardError, Array.Empty<string>());
        }

        foreach (var path in untrackedCollisions)
        {
            if (!TryGetSafePath(root, path, out var full, out var error))
                return new(false, "NEW_TASK_UNSAFE_PATH: " + error, Array.Empty<string>());
            try
            {
                if (File.Exists(full))
                    File.Delete(full);
                else if (Directory.Exists(full))
                {
                    // Directory/file collision: only untracked conflicting descendants,
                    // never an entire directory that might contain unrelated files.
                    return new(false, "NEW_TASK_DIRECTORY_COLLISION_REQUIRES_EXPLICIT_HANDLING: " +
                        path, Array.Empty<string>());
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return new(false, "NEW_TASK_CONFLICT_DELETE_FAILED: " + path + " " + ex.Message,
                    Array.Empty<string>());
            }
        }
        var replaced = trackedConflicts.Concat(untrackedCollisions).Distinct(
            StringComparer.OrdinalIgnoreCase).ToArray();
        return new(true, "NEW_TASK_CONFLICTS_OVERWRITTEN=" + replaced.Length,
            replaced);
    }

    private static bool TryGetSafePath(string root, string relative,
        out string full, out string error)
    {
        full = ""; error = "";
        if (Path.IsPathRooted(relative) || relative.Length == 0 ||
            relative.Split('/', '\\').Any(part => part is "." or "..") ||
            IsProtectedPath(relative))
        { error = "invalid relative path"; return false; }
        var basePath = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        full = Path.GetFullPath(Path.Combine(basePath,
            relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(basePath + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase))
        { error = "outside project"; return false; }
        var parent = Path.GetDirectoryName(full);
        while (parent is not null &&
               !parent.Equals(basePath, StringComparison.OrdinalIgnoreCase))
        {
            if (Directory.Exists(parent) &&
                (File.GetAttributes(parent) & FileAttributes.ReparsePoint) != 0)
            { error = "symlink parent"; return false; }
            parent = Path.GetDirectoryName(parent);
        }
        return true;
    }
}
