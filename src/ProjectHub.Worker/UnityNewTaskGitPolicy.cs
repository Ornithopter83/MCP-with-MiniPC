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

    internal static bool IsCheckoutCollision(string error) =>
        error.Contains("would be overwritten by checkout",
            StringComparison.OrdinalIgnoreCase) ||
        error.Contains("Please commit your changes or stash them",
            StringComparison.OrdinalIgnoreCase) ||
        error.Contains("would be overwritten by merge",
            StringComparison.OrdinalIgnoreCase) ||
        error.Contains("untracked working tree files",
            StringComparison.OrdinalIgnoreCase);

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
        var stagedAdditions = Array.Empty<string>();
        var stagedSet = ReadPaths(staged).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (head.ExitCode == 0)
        {
            var differing = await Run("diff", "--name-only", "-z", "HEAD", "origin/main");
            var currentTree = await Run("ls-tree", "-r", "--name-only", "-z", "HEAD");
            if (differing.ExitCode != 0 || currentTree.ExitCode != 0)
                return new(false, "NEW_TASK_GIT_TARGET_DIFF_FAILED", Array.Empty<string>());
            var changedInTarget = ReadPaths(differing).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var currentTreePaths = ReadPaths(currentTree).ToHashSet(StringComparer.OrdinalIgnoreCase);
            trackedConflicts = ReadPaths(tracked).Concat(ReadPaths(staged))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(changedInTarget.Contains)
                .Where(p => currentTreePaths.Contains(p))
                .Where(p => !IsProtectedPath(p))
                .ToArray();
            stagedAdditions = stagedSet
                .Where(p => !currentTreePaths.Contains(p) &&
                    targetFiles.Any(target => target.Equals(p, StringComparison.OrdinalIgnoreCase) ||
                        target.StartsWith(p + "/", StringComparison.OrdinalIgnoreCase) ||
                        p.StartsWith(target + "/", StringComparison.OrdinalIgnoreCase)))
                .Where(p => !IsProtectedPath(p)).ToArray();
        }
        else
        {
            stagedAdditions = stagedSet
                .Where(p => targetFiles.Any(target =>
                    target.Equals(p, StringComparison.OrdinalIgnoreCase) ||
                    target.StartsWith(p + "/", StringComparison.OrdinalIgnoreCase) ||
                    p.StartsWith(target + "/", StringComparison.OrdinalIgnoreCase)))
                .Where(p => !IsProtectedPath(p)).ToArray();
        }

        if (untrackedCollisions.Count == 0 && trackedConflicts.Length == 0 &&
            stagedAdditions.Length == 0)
            return new(false, "NEW_TASK_NO_DETERMINISTIC_COLLISIONS", Array.Empty<string>());

        // Avoid partially deleting a tree when a parent is a symlink/reparse point.
        foreach (var path in untrackedCollisions.Concat(trackedConflicts).Concat(stagedAdditions))
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

        // A staged file absent from the old HEAD is not restorable from HEAD.
        // Remove only its colliding index entry; unrelated staged entries remain.
        foreach (var path in stagedAdditions)
        {
            var removed = await Run("--literal-pathspecs", "rm", "--cached",
                "--force", "--", path);
            if (removed.ExitCode != 0)
                return new(false, "NEW_TASK_STAGED_COLLISION_FAILED: " + path +
                    " " + removed.StandardError, Array.Empty<string>());
        }

        try
        {
            // A target file may replace a local directory. That entire directory
            // is the conflict, including ignored residue, and must disappear.
            foreach (var target in targetFiles)
            {
                if (!untrackedCollisions.Any(path =>
                    path.StartsWith(target + "/", StringComparison.OrdinalIgnoreCase)))
                    continue;
                if (!TryGetSafePath(root, target, out var full, out var error))
                    return new(false, "NEW_TASK_UNSAFE_PATH: " + error, Array.Empty<string>());
                if (Directory.Exists(full))
                    Directory.Delete(full, recursive: true);
            }
            foreach (var path in untrackedCollisions.Concat(stagedAdditions)
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!TryGetSafePath(root, path, out var full, out var error))
                    return new(false, "NEW_TASK_UNSAFE_PATH: " + error, Array.Empty<string>());
                if (File.Exists(full))
                    File.Delete(full);
                else if (Directory.Exists(full))
                    Directory.Delete(full, recursive: true);
                // Drop empty folders that remain from the overwritten old tree.
                RemoveEmptyParents(root, Path.GetDirectoryName(full));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new(false, "NEW_TASK_CONFLICT_DELETE_FAILED: " + ex.Message,
                Array.Empty<string>());
        }
        var replaced = trackedConflicts.Concat(untrackedCollisions)
            .Concat(stagedAdditions).Distinct(
            StringComparer.OrdinalIgnoreCase).ToArray();
        return new(true, "NEW_TASK_CONFLICTS_OVERWRITTEN=" + replaced.Length,
            replaced);
    }

    private static void RemoveEmptyParents(string root, string? directory)
    {
        var rootPath = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        while (directory is not null &&
               directory.StartsWith(rootPath + Path.DirectorySeparatorChar,
                   StringComparison.OrdinalIgnoreCase) &&
               Directory.Exists(directory))
        {
            if (Directory.EnumerateFileSystemEntries(directory).Any())
                break;
            Directory.Delete(directory);
            directory = Path.GetDirectoryName(directory);
        }
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
