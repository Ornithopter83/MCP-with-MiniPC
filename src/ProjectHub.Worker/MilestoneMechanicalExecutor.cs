using System.Diagnostics;
using System.Text;

namespace ProjectHub.Worker;

internal static class MilestoneMechanicalExecutor
{
    public static async Task<MilestoneGitResult> CheckGitReadyAsync(
        string workingDirectory,
        string configuredTargetBranch,
        CancellationToken cancellationToken)
    {
        var git = new ProcessGitCommandRunner();

        Task<GitCommandResult> Run(params string[] args) =>
            git.RunAsync(
                workingDirectory,
                args,
                TimeSpan.FromSeconds(30),
                cancellationToken);

        var inside = await Run(
            "rev-parse",
            "--is-inside-work-tree").ConfigureAwait(false);

        if (inside.ExitCode != 0 ||
            !string.Equals(
                inside.StandardOutput.Trim(),
                "true",
                StringComparison.OrdinalIgnoreCase))
        {
            return new(
                false,
                true,
                configuredTargetBranch,
                null,
                "Git 저장소가 필수입니다. 현재 프로젝트 루트가 Git 저장소가 아니므로 PAUSE합니다.");
        }

        var targetBranch = await ResolveTargetBranchAsync(
            configuredTargetBranch,
            Run).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(targetBranch))
        {
            return new(
                false,
                true,
                configuredTargetBranch,
                null,
                "main/master 대상 branch를 확인할 수 없어 PAUSE합니다.");
        }

        var current = await Run(
            "rev-parse",
            "--abbrev-ref",
            "HEAD").ConfigureAwait(false);
        var currentBranch = current.ExitCode == 0
            ? current.StandardOutput.Trim()
            : string.Empty;

        if (!string.Equals(
                currentBranch,
                targetBranch,
                StringComparison.Ordinal))
        {
            var status = await Run(
                "status",
                "--porcelain=v1").ConfigureAwait(false);
            if (status.ExitCode != 0 ||
                !string.IsNullOrWhiteSpace(status.StandardOutput))
            {
                return new(
                    false,
                    true,
                    targetBranch,
                    null,
                    "현재 checkout branch가 대상 branch와 다르고 로컬 변경이 있어 안전하게 전환할 수 없습니다." +
                    Environment.NewLine +
                    $"current={currentBranch}" +
                    Environment.NewLine +
                    $"target={targetBranch}" +
                    Environment.NewLine +
                    "사용자가 직접 정리한 뒤 재개하세요.");
            }

            var switchResult = await Run(
                "switch",
                targetBranch).ConfigureAwait(false);
            if (switchResult.ExitCode != 0)
            {
                var remoteTarget = await Run(
                    "show-ref",
                    "--verify",
                    "--quiet",
                    "refs/remotes/origin/" + targetBranch)
                    .ConfigureAwait(false);
                if (remoteTarget.ExitCode == 0)
                {
                    switchResult = await Run(
                        "switch",
                        "-c",
                        targetBranch,
                        "--track",
                        "origin/" + targetBranch)
                        .ConfigureAwait(false);
                }
            }

            if (switchResult.ExitCode != 0)
            {
                return new(
                    false,
                    true,
                    targetBranch,
                    null,
                    "대상 branch로 안전하게 전환하지 못했습니다." +
                    Environment.NewLine +
                    $"current={currentBranch}" +
                    Environment.NewLine +
                    $"target={targetBranch}" +
                    Environment.NewLine +
                    (string.IsNullOrWhiteSpace(switchResult.StandardError)
                        ? switchResult.StandardOutput
                        : switchResult.StandardError));
            }

            currentBranch = targetBranch;
        }

        var head = await Run("rev-parse", "HEAD").ConfigureAwait(false);
        return new(
            true,
            false,
            targetBranch,
            head.ExitCode == 0 ? head.StandardOutput.Trim() : null,
            "Git preflight 완료" +
            Environment.NewLine +
            $"branch={targetBranch}" +
            Environment.NewLine +
            $"head={(head.ExitCode == 0 ? head.StandardOutput.Trim() : "확인 실패")}");
    }

    public static async Task<MilestoneMechanicalResult> ExecuteAsync(
        string jobId,
        string workingDirectory,
        string operation,
        string body,
        CancellationToken cancellationToken)
    {
        var command = MilestoneDefinitionContract.ReadBodyDirective(
            body,
            "COMMAND");
        var normalizedOperation = (operation ?? string.Empty)
            .Trim()
            .ToUpperInvariant();

        if (string.IsNullOrWhiteSpace(command))
        {
            return new(
                normalizedOperation,
                false,
                -1,
                string.Empty,
                string.Empty,
                "MECHANICAL_STATUS: BLOCKED" +
                Environment.NewLine +
                "COMMAND 지시가 없습니다.");
        }

        var outputRoot = Path.Combine(
            workingDirectory,
            "bin");
        if (normalizedOperation is "BUILD" or "PUBLISH")
        {
            try
            {
                if (Directory.Exists(outputRoot))
                    Directory.Delete(outputRoot, recursive: true);
                Directory.CreateDirectory(outputRoot);
            }
            catch (Exception exception)
                when (exception is IOException or UnauthorizedAccessException)
            {
                return new(
                    normalizedOperation,
                    false,
                    -1,
                    command,
                    string.Empty,
                    "MECHANICAL_STATUS: BLOCKED" +
                    Environment.NewLine +
                    "bin 정리에 실패했습니다." +
                    Environment.NewLine +
                    exception.Message);
            }
        }

        var logRoot = Path.Combine(
            workingDirectory,
            "temp",
            "ProjectHub",
            jobId,
            "mechanical");
        Directory.CreateDirectory(logRoot);
        var logPath = Path.Combine(
            logRoot,
            normalizedOperation.ToLowerInvariant() +
            "-" +
            DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfff") +
            ".log");

        var startInfo = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("/d");
        startInfo.ArgumentList.Add("/s");
        startInfo.ArgumentList.Add("/c");
        startInfo.ArgumentList.Add(command);

        using var processJob = new WorkerChildProcessJob(
            "Manager " + normalizedOperation);
        using var launched = processJob.Start(
            startInfo,
            cancellationToken);
        var process = launched.Process;
        var stdoutTask = launched.StandardOutput!
            .ReadToEndAsync(cancellationToken);
        var stderrTask = launched.StandardError!
            .ReadToEndAsync(cancellationToken);

        try
        {
            await process.WaitForExitAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            processJob.Dispose();
            throw;
        }

        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);
        var combined = stdout +
            (string.IsNullOrWhiteSpace(stderr)
                ? string.Empty
                : Environment.NewLine + stderr);

        await File.WriteAllTextAsync(
            logPath,
            combined,
            new UTF8Encoding(false),
            cancellationToken).ConfigureAwait(false);

        var outputRequired =
            normalizedOperation is "BUILD" or "PUBLISH";
        var outputPresent =
            !outputRequired ||
            Directory.Exists(outputRoot) &&
            Directory.EnumerateFiles(
                outputRoot,
                "*",
                SearchOption.AllDirectories).Any();
        var success =
            process.ExitCode == 0 &&
            outputPresent;
        var summary =
            MilestoneDefinitionContract.Limit(combined, 16000);
        if (process.ExitCode == 0 && !outputPresent)
        {
            summary =
                "MECHANICAL_OUTPUT_MISSING: BUILD/PUBLISH 성공 exit code를 반환했지만 프로젝트 루트 bin에 결과 파일이 없습니다." +
                Environment.NewLine +
                summary;
        }

        return new(
            normalizedOperation,
            success,
            process.ExitCode,
            command,
            logPath,
            summary);
    }

    public static async Task<IReadOnlySet<string>> SnapshotChangedPathsAsync(
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        var state = await SnapshotChangeStateAsync(
            workingDirectory,
            cancellationToken).ConfigureAwait(false);
        return state.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public static async Task<IReadOnlyDictionary<string, string>> SnapshotChangeStateAsync(
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        var git = new ProcessGitCommandRunner();
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var args in new[]
                 {
                     new[] { "diff", "--name-only", "-z" },
                     new[] { "diff", "--cached", "--name-only", "-z" },
                     new[] { "ls-files", "--others", "--exclude-standard", "-z" }
                 })
        {
            var result = await git.RunAsync(
                workingDirectory,
                args,
                TimeSpan.FromSeconds(30),
                cancellationToken).ConfigureAwait(false);

            if (result.ExitCode != 0)
                continue;

            foreach (var rawPath in result.StandardOutput.Split(
                         '\0',
                         StringSplitOptions.RemoveEmptyEntries))
            {
                var normalized = NormalizeGitPath(rawPath);
                if (!string.IsNullOrWhiteSpace(normalized) &&
                    !IsRuntimeOutput(normalized))
                {
                    paths.Add(normalized);
                }
            }
        }

        var state = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);
        var root = Path.GetFullPath(workingDirectory);

        foreach (var path in paths)
        {
            var fullPath = Path.GetFullPath(Path.Combine(root, path));
            if (!MilestoneDefinitionContract.IsPathInsideRoot(root, fullPath))
                continue;

            if (File.Exists(fullPath))
            {
                var info = new FileInfo(fullPath);
                state[path] =
                    "FILE:" +
                    info.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                    ":" +
                    info.LastWriteTimeUtc.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            else if (Directory.Exists(fullPath))
            {
                var info = new DirectoryInfo(fullPath);
                state[path] =
                    "DIR:" +
                    info.LastWriteTimeUtc.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            else
            {
                state[path] = "MISSING";
            }
        }

        return state;
    }

    public static IReadOnlyCollection<string> DiffChangeStates(
        IReadOnlyDictionary<string, string> before,
        IReadOnlyDictionary<string, string> after)
    {
        var allPaths = new HashSet<string>(
            before.Keys,
            StringComparer.OrdinalIgnoreCase);
        allPaths.UnionWith(after.Keys);

        return allPaths
            .Where(path =>
                !before.TryGetValue(path, out var beforeValue) ||
                !after.TryGetValue(path, out var afterValue) ||
                !string.Equals(
                    beforeValue,
                    afterValue,
                    StringComparison.Ordinal))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static bool IsPathWithinScopes(
        string path,
        IEnumerable<string> scopes)
    {
        var normalizedPath = NormalizeGitPath(path)
            .Trim('/');
        foreach (var scopeValue in scopes)
        {
            var scope = NormalizeGitPath(scopeValue)
                .Trim('/');
            if (scope is "" or ".")
                return true;

            if (string.Equals(
                    normalizedPath,
                    scope,
                    StringComparison.OrdinalIgnoreCase) ||
                normalizedPath.StartsWith(
                    scope + "/",
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static bool HasOverlappingScopes(
        IEnumerable<IReadOnlyList<string>> workScopes)
    {
        var groups = workScopes
            .Select(group => group
                .Select(path => NormalizeGitPath(path).Trim('/'))
                .Where(path => path.Length > 0)
                .ToArray())
            .ToArray();

        for (var leftIndex = 0; leftIndex < groups.Length; leftIndex++)
        {
            for (var rightIndex = leftIndex + 1;
                 rightIndex < groups.Length;
                 rightIndex++)
            {
                foreach (var left in groups[leftIndex])
                {
                    foreach (var right in groups[rightIndex])
                    {
                        if (ScopesOverlap(left, right))
                            return true;
                    }
                }
            }
        }

        return false;
    }

    public static async Task<MilestoneGitResult> FinalizeGitAsync(
        string workingDirectory,
        MilestoneDefinition milestone,
        IReadOnlyCollection<string> paths,
        CancellationToken cancellationToken)
    {
        var git = new ProcessGitCommandRunner();

        Task<GitCommandResult> Run(params string[] args) =>
            git.RunAsync(
                workingDirectory,
                args,
                TimeSpan.FromMinutes(3),
                cancellationToken);

        var inside = await Run(
            "rev-parse",
            "--is-inside-work-tree").ConfigureAwait(false);
        if (inside.ExitCode != 0 ||
            !string.Equals(
                inside.StandardOutput.Trim(),
                "true",
                StringComparison.OrdinalIgnoreCase))
        {
            return new(
                false,
                true,
                milestone.TargetBranch,
                null,
                "Git 저장소가 필수입니다. 현재 프로젝트 루트가 Git 저장소가 아니므로 PAUSE합니다.");
        }

        var targetBranch = await ResolveTargetBranchAsync(
            milestone.TargetBranch,
            Run).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(targetBranch))
        {
            return new(
                false,
                true,
                milestone.TargetBranch,
                null,
                "main/master 대상 branch를 확인할 수 없어 PAUSE합니다.");
        }

        var current = await Run(
            "rev-parse",
            "--abbrev-ref",
            "HEAD").ConfigureAwait(false);
        var currentBranch = current.ExitCode == 0
            ? current.StandardOutput.Trim()
            : string.Empty;

        if (!string.Equals(
                currentBranch,
                targetBranch,
                StringComparison.Ordinal))
        {
            return new(
                false,
                true,
                targetBranch,
                null,
                "현재 checkout branch가 대상 branch와 다릅니다." +
                Environment.NewLine +
                $"current={currentBranch}" +
                Environment.NewLine +
                $"target={targetBranch}" +
                Environment.NewLine +
                "자동 branch 전환 없이 PAUSE합니다.");
        }

        var scopedPaths = paths
            .Select(NormalizeGitPath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var scopedPathspecs = BuildScopedPathspecs(scopedPaths);
        if (scopedPathspecs.Count > 0)
        {
            var addArgs = new List<string>
            {
                "add",
                "-A",
                "--"
            };
            addArgs.AddRange(scopedPathspecs);

            var add = await Run(addArgs.ToArray()).ConfigureAwait(false);
            if (add.ExitCode != 0)
            {
                return new(
                    false,
                    false,
                    targetBranch,
                    null,
                    "git add 실패:" +
                    Environment.NewLine +
                    add.StandardError);
            }
        }

        string? commitSha = null;
        var createdCommit = false;

        if (scopedPathspecs.Count > 0)
        {
            var diffArgs = new List<string>
            {
                "diff",
                "--cached",
                "--quiet",
                "--"
            };
            diffArgs.AddRange(scopedPathspecs);
            var staged = await Run(diffArgs.ToArray())
                .ConfigureAwait(false);

            if (staged.ExitCode != 0)
            {
                var commitArgs = new List<string>
                {
                    "commit",
                    "-m",
                    $"ProjectHub milestone {milestone.Id}",
                    "--"
                };
                commitArgs.AddRange(scopedPathspecs);
                var commit = await Run(commitArgs.ToArray())
                    .ConfigureAwait(false);

                if (commit.ExitCode != 0)
                {
                    return new(
                        false,
                        false,
                        targetBranch,
                        null,
                        "git commit 실패:" +
                        Environment.NewLine +
                        commit.StandardError);
                }

                createdCommit = true;
            }
        }

        var head = await Run(
            "rev-parse",
            "HEAD").ConfigureAwait(false);
        if (head.ExitCode == 0)
            commitSha = head.StandardOutput.Trim();

        var push = await Run(
            "push",
            "origin",
            targetBranch).ConfigureAwait(false);

        if (push.ExitCode != 0)
        {
            var status = await Run(
                "status",
                "--porcelain=v1").ConfigureAwait(false);
            var hasUncommittedChanges =
                status.ExitCode == 0 &&
                !string.IsNullOrWhiteSpace(status.StandardOutput);

            if (!hasUncommittedChanges)
            {
                var fetch = await Run(
                    "fetch",
                    "origin",
                    targetBranch).ConfigureAwait(false);

                if (fetch.ExitCode == 0)
                {
                    var rebase = await Run(
                        "rebase",
                        "origin/" + targetBranch).ConfigureAwait(false);

                    if (rebase.ExitCode == 0)
                    {
                        push = await Run(
                            "push",
                            "origin",
                            targetBranch).ConfigureAwait(false);

                        if (push.ExitCode == 0)
                        {
                            head = await Run(
                                "rev-parse",
                                "HEAD").ConfigureAwait(false);
                            if (head.ExitCode == 0)
                                commitSha = head.StandardOutput.Trim();
                        }
                    }
                    else
                    {
                        await Run("rebase", "--abort")
                            .ConfigureAwait(false);
                    }
                }
            }
        }

        if (push.ExitCode != 0)
        {
            return new(
                false,
                false,
                targetBranch,
                commitSha,
                "Git commit/push finalize를 완료하지 못했습니다." +
                Environment.NewLine +
                $"commit={commitSha ?? "없음"}" +
                Environment.NewLine +
                $"createdCommit={(createdCommit ? "YES" : "NO")}" +
                Environment.NewLine +
                "pushError=" +
                (string.IsNullOrWhiteSpace(push.StandardError)
                    ? push.StandardOutput
                    : push.StandardError));
        }

        return new(
            true,
            false,
            targetBranch,
            commitSha,
            "Git finalize 완료" +
            Environment.NewLine +
            $"branch={targetBranch}" +
            Environment.NewLine +
            $"commit={commitSha ?? "없음"}" +
            Environment.NewLine +
            $"createdCommit={(createdCommit ? "YES" : "NO")}" +
            Environment.NewLine +
            "push=COMPLETED");
    }

    private static async Task<string?> ResolveTargetBranchAsync(
        string configured,
        Func<string[], Task<GitCommandResult>> run)
    {
        if (!string.Equals(
                configured,
                "AUTO",
                StringComparison.OrdinalIgnoreCase))
            return configured.Trim();

        foreach (var candidate in new[] { "main", "master" })
        {
            var local = await run(new[]
            {
                "show-ref",
                "--verify",
                "--quiet",
                "refs/heads/" + candidate
            }).ConfigureAwait(false);

            if (local.ExitCode == 0)
                return candidate;

            var remote = await run(new[]
            {
                "show-ref",
                "--verify",
                "--quiet",
                "refs/remotes/origin/" + candidate
            }).ConfigureAwait(false);

            if (remote.ExitCode == 0)
                return candidate;
        }

        return null;
    }

    private static IReadOnlyList<string> BuildScopedPathspecs(
        IReadOnlyList<string> scopedPaths)
    {
        if (scopedPaths.Count == 0)
            return Array.Empty<string>();

        var result = new List<string>(scopedPaths.Count + 6);
        result.AddRange(scopedPaths);
        result.Add(":(exclude)bin");
        result.Add(":(exclude)bin/**");
        result.Add(":(exclude)temp");
        result.Add(":(exclude)temp/**");
        return result;
    }

    private static bool ScopesOverlap(string left, string right)
    {
        if (left is "" or "." || right is "" or ".")
            return true;

        return string.Equals(
                   left,
                   right,
                   StringComparison.OrdinalIgnoreCase) ||
               left.StartsWith(
                   right + "/",
                   StringComparison.OrdinalIgnoreCase) ||
               right.StartsWith(
                   left + "/",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeGitPath(string path) =>
        (path ?? string.Empty)
        .Trim()
        .Replace('\\', '/');

    private static bool IsRuntimeOutput(string path) =>
        path.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("bin/", StringComparison.OrdinalIgnoreCase) ||
        path.Equals("temp", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("temp/", StringComparison.OrdinalIgnoreCase);
}
