using System.Diagnostics;
using System.Text;
using System.IO;

namespace ProjectHub.Worker;

internal static class MilestoneMechanicalExecutor
{
    private const string RequiredBranch = "main";

    public static async Task<MilestoneGitResult> RefreshConfiguredOriginMainAsync(
        string workingDirectory,
        string? configuredRepositoryUrl,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(configuredRepositoryUrl))
        {
            return new(
                false,
                true,
                RequiredBranch,
                null,
                "CONFIGURED_REPOSITORY_REQUIRED: 강제 설정된 원격 저장소 URL이 없습니다.");
        }

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
                RequiredBranch,
                null,
                "CONFIGURED_REPOSITORY_UNAVAILABLE: 현재 프로젝트 루트가 Git 저장소가 아닙니다.");
        }

        var origin = await Run(
            "remote",
            "get-url",
            "origin").ConfigureAwait(false);
        if (origin.ExitCode != 0 ||
            string.IsNullOrWhiteSpace(origin.StandardOutput))
        {
            return new(
                false,
                true,
                RequiredBranch,
                null,
                "CONFIGURED_ORIGIN_REQUIRED: origin 원격 저장소를 확인할 수 없습니다.");
        }

        var actualRepositoryUrl = origin.StandardOutput.Trim();
        if (!RepositoryAddressesEqual(
                configuredRepositoryUrl,
                actualRepositoryUrl))
        {
            return new(
                false,
                true,
                RequiredBranch,
                null,
                "CONFIGURED_ORIGIN_MISMATCH" +
                Environment.NewLine +
                $"configured={configuredRepositoryUrl.Trim()}" +
                Environment.NewLine +
                $"origin={actualRepositoryUrl}");
        }

        var fetch = await Run(
            "fetch",
            "--prune",
            "origin",
            "+refs/heads/" + RequiredBranch +
            ":refs/remotes/origin/" + RequiredBranch).ConfigureAwait(false);
        if (fetch.ExitCode != 0)
        {
            return new(
                false,
                true,
                RequiredBranch,
                null,
                "ORIGIN_MAIN_FETCH_FAILED" +
                Environment.NewLine +
                (string.IsNullOrWhiteSpace(fetch.StandardError)
                    ? fetch.StandardOutput
                    : fetch.StandardError));
        }

        var remoteHead = await Run(
            "rev-parse",
            "refs/remotes/origin/" + RequiredBranch).ConfigureAwait(false);
        if (remoteHead.ExitCode != 0 ||
            string.IsNullOrWhiteSpace(remoteHead.StandardOutput))
        {
            return new(
                false,
                true,
                RequiredBranch,
                null,
                "ORIGIN_MAIN_REFERENCE_MISSING");
        }

        await Run(
            "remote",
            "set-head",
            "origin",
            RequiredBranch).ConfigureAwait(false);

        var remoteSha = remoteHead.StandardOutput.Trim();
        return new(
            true,
            false,
            RequiredBranch,
            remoteSha,
            "원격 저장소 참조 갱신 완료" +
            Environment.NewLine +
            $"repository={configuredRepositoryUrl.Trim()}" +
            Environment.NewLine +
            "remoteBranch=origin/main" +
            Environment.NewLine +
            $"remoteHead={remoteSha}");
    }

    public static async Task<MilestoneGitResult> CheckGitReadyAsync(
        string workingDirectory,
        string configuredTargetBranch,
        bool initializeIfMissing,
        string? configuredRepositoryUrl,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(
                configuredTargetBranch,
                RequiredBranch,
                StringComparison.Ordinal))
        {
            return new(
                false,
                true,
                RequiredBranch,
                null,
                "MAIN_BRANCH_REQUIRED: ProjectHub 작업 branch는 main만 허용합니다.");
        }

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
            if (!initializeIfMissing)
            {
                return new(
                    false,
                    true,
                    RequiredBranch,
                    null,
                    "Git 저장소가 필수입니다. 현재 프로젝트 루트가 Git 저장소가 아니므로 PAUSE합니다.");
            }

            var init = await Run(
                "init",
                "-b",
                RequiredBranch).ConfigureAwait(false);
            if (init.ExitCode != 0)
            {
                init = await Run("init").ConfigureAwait(false);
                if (init.ExitCode != 0)
                {
                    return new(
                        false,
                        true,
                        RequiredBranch,
                        null,
                        "git init에 실패했습니다." +
                        Environment.NewLine +
                        (string.IsNullOrWhiteSpace(init.StandardError)
                            ? init.StandardOutput
                            : init.StandardError));
                }

                var setMainHead = await Run(
                    "symbolic-ref",
                    "HEAD",
                    "refs/heads/" + RequiredBranch).ConfigureAwait(false);
                if (setMainHead.ExitCode != 0)
                {
                    return new(
                        false,
                        true,
                        RequiredBranch,
                        null,
                        "Git 초기화 후 HEAD를 main으로 강제하지 못했습니다." +
                        Environment.NewLine +
                        (string.IsNullOrWhiteSpace(setMainHead.StandardError)
                            ? setMainHead.StandardOutput
                            : setMainHead.StandardError));
                }
            }

            WorkerPaths.EnsureProjectHubLocalExclude(workingDirectory);
        }

        var remoteReference =
            await RefreshConfiguredOriginMainAsync(
                workingDirectory,
                configuredRepositoryUrl,
                cancellationToken).ConfigureAwait(false);
        if (!remoteReference.Success)
            return remoteReference;

        var currentBranch = await ReadCurrentBranchAsync(Run)
            .ConfigureAwait(false);

        if (!string.Equals(
                currentBranch,
                RequiredBranch,
                StringComparison.Ordinal))
        {
            var localMain = await Run(
                "show-ref",
                "--verify",
                "--quiet",
                "refs/heads/" + RequiredBranch).ConfigureAwait(false);

            GitCommandResult mainPreparation;
            if (localMain.ExitCode == 0)
            {
                mainPreparation = await Run(
                    "switch",
                    RequiredBranch).ConfigureAwait(false);
            }
            else
            {
                var remoteMain = await Run(
                    "show-ref",
                    "--verify",
                    "--quiet",
                    "refs/remotes/origin/" + RequiredBranch)
                    .ConfigureAwait(false);

                if (remoteMain.ExitCode == 0)
                {
                    mainPreparation = await Run(
                        "switch",
                        "-c",
                        RequiredBranch,
                        "--track",
                        "origin/" + RequiredBranch)
                        .ConfigureAwait(false);
                }
                else if (!string.IsNullOrWhiteSpace(currentBranch) &&
                         !string.Equals(
                             currentBranch,
                             "HEAD",
                             StringComparison.OrdinalIgnoreCase))
                {
                    mainPreparation = await Run(
                        "branch",
                        "-M",
                        RequiredBranch).ConfigureAwait(false);
                }
                else
                {
                    return new(
                        false,
                        true,
                        RequiredBranch,
                        null,
                        "MAIN_BRANCH_REQUIRED: 현재 Git 상태를 main으로 수렴시킬 수 없습니다.");
                }
            }

            if (mainPreparation.ExitCode != 0)
            {
                return new(
                    false,
                    true,
                    RequiredBranch,
                    null,
                    "MAIN_BRANCH_REQUIRED: main 준비에 실패했습니다." +
                    Environment.NewLine +
                    $"current={currentBranch}" +
                    Environment.NewLine +
                    (string.IsNullOrWhiteSpace(mainPreparation.StandardError)
                        ? mainPreparation.StandardOutput
                        : mainPreparation.StandardError));
            }

            currentBranch = await ReadCurrentBranchAsync(Run)
                .ConfigureAwait(false);
        }

        if (!string.Equals(
                currentBranch,
                RequiredBranch,
                StringComparison.Ordinal))
        {
            return new(
                false,
                true,
                RequiredBranch,
                null,
                "MAIN_BRANCH_REQUIRED: 최종 checkout이 main이 아니므로 작업을 시작하지 않습니다." +
                Environment.NewLine +
                $"current={currentBranch}");
        }

        var originHeadSummary =
            await AlignLocalOriginHeadToMainAsync(Run)
                .ConfigureAwait(false);
        var head = await Run("rev-parse", "HEAD").ConfigureAwait(false);
        return new(
            true,
            false,
            RequiredBranch,
            head.ExitCode == 0 ? head.StandardOutput.Trim() : null,
            "Git preflight 완료" +
            Environment.NewLine +
            "branch=main" +
            Environment.NewLine +
            "remoteBranch=origin/main" +
            Environment.NewLine +
            $"remoteHead={remoteReference.CommitSha ?? "확인 실패"}" +
            Environment.NewLine +
            originHeadSummary +
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

    public static async Task<MilestoneGitResult> CheckpointWorkItemAsync(
        string workingDirectory,
        MilestoneDefinition milestone,
        MilestoneWorkDefinition work,
        IReadOnlyCollection<string> changedPaths,
        string? configuredRepositoryUrl,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(
                milestone.TargetBranch,
                RequiredBranch,
                StringComparison.Ordinal))
        {
            return new(
                false,
                true,
                RequiredBranch,
                null,
                "WORK_ITEM_GIT_MAIN_REQUIRED");
        }

        var git = new ProcessGitCommandRunner();

        Task<GitCommandResult> Run(params string[] args) =>
            git.RunAsync(
                workingDirectory,
                args,
                TimeSpan.FromMinutes(3),
                cancellationToken);

        var currentBranch = await ReadCurrentBranchAsync(Run)
            .ConfigureAwait(false);
        if (!string.Equals(
                currentBranch,
                RequiredBranch,
                StringComparison.Ordinal))
        {
            return new(
                false,
                true,
                RequiredBranch,
                null,
                "WORK_ITEM_GIT_MAIN_REQUIRED" +
                Environment.NewLine +
                $"current={currentBranch}");
        }

        var scopedPaths = changedPaths
            .Select(NormalizeGitPath)
            .Where(path =>
                !string.IsNullOrWhiteSpace(path) &&
                IsPathWithinScopes(path, work.WritePaths) &&
                !IsRuntimeOutput(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (scopedPaths.Length > 0)
        {
            var addArgs = new List<string>
            {
                "add",
                "-A",
                "--"
            };
            addArgs.AddRange(scopedPaths);

            var add = await Run(addArgs.ToArray()).ConfigureAwait(false);
            if (add.ExitCode != 0)
            {
                return new(
                    false,
                    false,
                    RequiredBranch,
                    null,
                    "WORK_ITEM_GIT_ADD_FAILED" +
                    Environment.NewLine +
                    (string.IsNullOrWhiteSpace(add.StandardError)
                        ? add.StandardOutput
                        : add.StandardError));
            }

            var diffArgs = new List<string>
            {
                "diff",
                "--cached",
                "--quiet",
                "--"
            };
            diffArgs.AddRange(scopedPaths);
            var staged = await Run(diffArgs.ToArray())
                .ConfigureAwait(false);

            if (staged.ExitCode > 1)
            {
                return new(
                    false,
                    false,
                    RequiredBranch,
                    null,
                    "WORK_ITEM_GIT_DIFF_FAILED" +
                    Environment.NewLine +
                    (string.IsNullOrWhiteSpace(staged.StandardError)
                        ? staged.StandardOutput
                        : staged.StandardError));
            }

            if (staged.ExitCode == 1)
            {
                var commitArgs = new List<string>
                {
                    "commit",
                    "-m",
                    $"ProjectHub {milestone.Id} WORK #{work.Id}",
                    "--only",
                    "--"
                };
                commitArgs.AddRange(scopedPaths);

                var commit = await Run(commitArgs.ToArray())
                    .ConfigureAwait(false);
                if (commit.ExitCode != 0)
                {
                    return new(
                        false,
                        false,
                        RequiredBranch,
                        null,
                        "WORK_ITEM_GIT_COMMIT_FAILED" +
                        Environment.NewLine +
                        (string.IsNullOrWhiteSpace(commit.StandardError)
                            ? commit.StandardOutput
                            : commit.StandardError));
                }
            }
        }

        var head = await Run(
            "rev-parse",
            "HEAD").ConfigureAwait(false);
        if (head.ExitCode != 0 ||
            string.IsNullOrWhiteSpace(head.StandardOutput))
        {
            return new(
                false,
                false,
                RequiredBranch,
                null,
                "WORK_ITEM_GIT_HEAD_FAILED");
        }

        var localHead = head.StandardOutput.Trim();
        string? lastError = null;

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            var remoteReference =
                await RefreshConfiguredOriginMainAsync(
                    workingDirectory,
                    configuredRepositoryUrl,
                    cancellationToken).ConfigureAwait(false);
            if (!remoteReference.Success ||
                string.IsNullOrWhiteSpace(remoteReference.CommitSha))
            {
                lastError = remoteReference.Summary;
            }
            else
            {
                var lease =
                    "--force-with-lease=refs/heads/" +
                    RequiredBranch +
                    ":" +
                    remoteReference.CommitSha;
                var push = await Run(
                    "push",
                    lease,
                    "origin",
                    RequiredBranch + ":" + RequiredBranch)
                    .ConfigureAwait(false);

                if (push.ExitCode == 0)
                {
                    var verify = await Run(
                        "ls-remote",
                        "--heads",
                        "origin",
                        "refs/heads/" + RequiredBranch)
                        .ConfigureAwait(false);

                    var remoteHead = verify.ExitCode == 0
                        ? verify.StandardOutput
                            .Split(
                                new[] { '\r', '\n', '\t', ' ' },
                                StringSplitOptions.RemoveEmptyEntries)
                            .FirstOrDefault()
                        : null;

                    if (string.Equals(
                            localHead,
                            remoteHead,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return new(
                            true,
                            false,
                            RequiredBranch,
                            localHead,
                            "WORK_ITEM_GIT_CHECKPOINT_COMPLETED" +
                            Environment.NewLine +
                            $"workItem={work.Id}" +
                            Environment.NewLine +
                            $"commit={localHead}" +
                            Environment.NewLine +
                            "push=FORCE_WITH_LEASE" +
                            Environment.NewLine +
                            $"attempt={attempt}");
                    }

                    lastError =
                        "WORK_ITEM_GIT_REMOTE_VERIFY_MISMATCH" +
                        Environment.NewLine +
                        $"local={localHead}" +
                        Environment.NewLine +
                        $"remote={remoteHead ?? "없음"}";
                }
                else
                {
                    lastError =
                        string.IsNullOrWhiteSpace(push.StandardError)
                            ? push.StandardOutput
                            : push.StandardError;
                }
            }

            if (attempt < 3)
            {
                await Task.Delay(
                    TimeSpan.FromMilliseconds(400 * attempt),
                    cancellationToken).ConfigureAwait(false);
            }
        }

        return new(
            false,
            false,
            RequiredBranch,
            localHead,
            "WORK_ITEM_GIT_CHECKPOINT_FAILED" +
            Environment.NewLine +
            $"workItem={work.Id}" +
            Environment.NewLine +
            (lastError ?? "알 수 없는 push 실패"));
    }

    public static async Task<MilestoneGitResult> FinalizeGitAsync(
        string workingDirectory,
        MilestoneDefinition milestone,
        IReadOnlyCollection<string> paths,
        string? configuredRepositoryUrl,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(
                milestone.TargetBranch,
                RequiredBranch,
                StringComparison.Ordinal))
        {
            return new(
                false,
                true,
                RequiredBranch,
                null,
                "MAIN_BRANCH_REQUIRED: main 이외의 마일스톤 branch는 finalize하지 않습니다.");
        }

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
                RequiredBranch,
                null,
                "Git 저장소가 필수입니다. 현재 프로젝트 루트가 Git 저장소가 아니므로 PAUSE합니다.");
        }

        var currentBranch = await ReadCurrentBranchAsync(Run)
            .ConfigureAwait(false);
        if (!string.Equals(
                currentBranch,
                RequiredBranch,
                StringComparison.Ordinal))
        {
            return new(
                false,
                true,
                RequiredBranch,
                null,
                "MAIN_BRANCH_REQUIRED: 현재 checkout이 main이 아니므로 commit/push하지 않습니다." +
                Environment.NewLine +
                $"current={currentBranch}");
        }

        var remoteReference =
            await RefreshConfiguredOriginMainAsync(
                workingDirectory,
                configuredRepositoryUrl,
                cancellationToken).ConfigureAwait(false);
        if (!remoteReference.Success)
            return remoteReference;

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
                    RequiredBranch,
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

            if (staged.ExitCode > 1)
            {
                return new(
                    false,
                    false,
                    RequiredBranch,
                    null,
                    "git diff --cached 확인 실패:" +
                    Environment.NewLine +
                    (string.IsNullOrWhiteSpace(staged.StandardError)
                        ? staged.StandardOutput
                        : staged.StandardError));
            }

            if (staged.ExitCode == 1)
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
                        RequiredBranch,
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
            RequiredBranch).ConfigureAwait(false);

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
                    "--prune",
                    "origin",
                    "+refs/heads/" + RequiredBranch +
                    ":refs/remotes/origin/" + RequiredBranch).ConfigureAwait(false);

                if (fetch.ExitCode == 0)
                {
                    var rebase = await Run(
                        "rebase",
                        "origin/" + RequiredBranch).ConfigureAwait(false);

                    if (rebase.ExitCode == 0)
                    {
                        push = await Run(
                            "push",
                            "origin",
                            RequiredBranch).ConfigureAwait(false);

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
                RequiredBranch,
                commitSha,
                "Git main commit/push finalize를 완료하지 못했습니다." +
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

        var originHeadSummary =
            await AlignLocalOriginHeadToMainAsync(Run)
                .ConfigureAwait(false);

        return new(
            true,
            false,
            RequiredBranch,
            commitSha,
            "Git finalize 완료" +
            Environment.NewLine +
            "branch=main" +
            Environment.NewLine +
            "remoteBranch=origin/main" +
            Environment.NewLine +
            $"configuredRepository={configuredRepositoryUrl?.Trim() ?? "없음"}" +
            Environment.NewLine +
            originHeadSummary +
            Environment.NewLine +
            $"commit={commitSha ?? "없음"}" +
            Environment.NewLine +
            $"createdCommit={(createdCommit ? "YES" : "NO")}" +
            Environment.NewLine +
            "push=COMPLETED");
    }

    private static async Task<string> AlignLocalOriginHeadToMainAsync(
        Func<string[], Task<GitCommandResult>> run)
    {
        var origin = await run(new[]
        {
            "remote",
            "get-url",
            "origin"
        }).ConfigureAwait(false);

        if (origin.ExitCode != 0)
            return "originHead=NO_ORIGIN";

        var remoteMain = await run(new[]
        {
            "show-ref",
            "--verify",
            "--quiet",
            "refs/remotes/origin/" + RequiredBranch
        }).ConfigureAwait(false);

        if (remoteMain.ExitCode != 0)
            return "originHead=origin/main not fetched yet";

        var setHead = await run(new[]
        {
            "remote",
            "set-head",
            "origin",
            RequiredBranch
        }).ConfigureAwait(false);

        return setHead.ExitCode == 0
            ? "originHead=origin/main"
            : "originHead=origin/main alignment failed";
    }

    private static async Task<string> ReadCurrentBranchAsync(
        Func<string[], Task<GitCommandResult>> run)
    {
        var current = await run(new[]
        {
            "rev-parse",
            "--abbrev-ref",
            "HEAD"
        }).ConfigureAwait(false);

        var branch = current.ExitCode == 0
            ? current.StandardOutput.Trim()
            : string.Empty;

        if (string.Equals(
                branch,
                "HEAD",
                StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(branch))
        {
            var symbolicHead = await run(new[]
            {
                "symbolic-ref",
                "--short",
                "HEAD"
            }).ConfigureAwait(false);
            if (symbolicHead.ExitCode == 0)
                branch = symbolicHead.StandardOutput.Trim();
        }

        return branch;
    }

    private static IReadOnlyList<string> BuildScopedPathspecs(
        IReadOnlyList<string> scopedPaths)
    {
        if (scopedPaths.Count == 0)
            return Array.Empty<string>();

        // milestoneChangedPaths에는 Worker가 실제로 관찰·기록한 대상만 들어온다.
        // Git에는 그 경로만 명시하여 지정되지 않은 dirty 변경을 stage하지 않는다.
        return scopedPaths
            .Where(path => !IsRuntimeOutput(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool RepositoryAddressesEqual(
        string left,
        string right)
    {
        static string Normalize(string value)
        {
            var normalized = (value ?? string.Empty)
                .Trim()
                .TrimEnd('/');

            if (normalized.EndsWith(
                    ".git",
                    StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized[..^4];
            }

            return normalized;
        }

        return string.Equals(
            Normalize(left),
            Normalize(right),
            StringComparison.OrdinalIgnoreCase);
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
        path.StartsWith("temp/", StringComparison.OrdinalIgnoreCase) ||
        path.Equals(".projecthub", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(".projecthub/", StringComparison.OrdinalIgnoreCase);
}
