using System.Diagnostics;
using System.Text;
using System.IO;
using System.Text.RegularExpressions;

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
            var addOrigin = await Run(
                "remote",
                "add",
                "origin",
                configuredRepositoryUrl.Trim()).ConfigureAwait(false);
            if (addOrigin.ExitCode != 0)
            {
                return new(
                    false,
                    true,
                    RequiredBranch,
                    null,
                    "CONFIGURED_ORIGIN_CREATE_FAILED" +
                    Environment.NewLine +
                    (string.IsNullOrWhiteSpace(addOrigin.StandardError)
                        ? addOrigin.StandardOutput
                        : addOrigin.StandardError));
            }
        }
        else
        {
            var actualRepositoryUrl = origin.StandardOutput.Trim();
            if (!RepositoryAddressesEqual(
                    configuredRepositoryUrl,
                    actualRepositoryUrl))
            {
                var setOrigin = await Run(
                    "remote",
                    "set-url",
                    "origin",
                    configuredRepositoryUrl.Trim()).ConfigureAwait(false);
                if (setOrigin.ExitCode != 0)
                {
                    return new(
                        false,
                        true,
                        RequiredBranch,
                        null,
                        "CONFIGURED_ORIGIN_SET_FAILED" +
                        Environment.NewLine +
                        (string.IsNullOrWhiteSpace(setOrigin.StandardError)
                            ? setOrigin.StandardOutput
                            : setOrigin.StandardError));
                }
            }
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
        var rawCommand = MilestoneDefinitionContract.ReadBodyDirective(
            body,
            "COMMAND");
        var normalizedOperation = (operation ?? string.Empty)
            .Trim()
            .ToUpperInvariant();
        var command = NormalizeMechanicalCommand(
            normalizedOperation,
            rawCommand ?? string.Empty);

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
            "Worker " + normalizedOperation);
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
            ProjectHubJson.Utf8NoBom,
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
        CancellationToken cancellationToken,
        bool requireSuccess = false)
    {
        var state = await SnapshotChangeStateAsync(
            workingDirectory,
            cancellationToken,
            requireSuccess).ConfigureAwait(false);
        return state.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public static async Task<IReadOnlyDictionary<string, string>> SnapshotChangeStateAsync(
        string workingDirectory,
        CancellationToken cancellationToken,
        bool requireSuccess = false)
    {
        var git = new ProcessGitCommandRunner();
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var args in new[]
                 {
                     new[] { "diff", "--name-only", "-z" },
                     new[] { "diff", "--cached", "--name-only", "-z" },
                     new[]
                      {
                          "ls-files", "--others", "--exclude-standard",
                          "--exclude=node_modules/", "--exclude=.npm-cache/",
                          "--exclude=dist-electron/", "--exclude=coverage/",
                          "--exclude=.vite/", "--exclude=.electron-cache/",
                          "--exclude=.playwright/", "--exclude=.next/",
                          "--exclude=.turbo/", "--exclude=*.tsbuildinfo", "-z"
                      }
                 })
        {
            var result = await git.RunAsync(
                workingDirectory,
                args,
                TimeSpan.FromSeconds(30),
                cancellationToken).ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();
            if (result.ExitCode != 0)
            {
                if (requireSuccess)
                    throw new InvalidOperationException(
                        "GIT_SNAPSHOT_FAILED: " + args[0] +
                        " exit=" + result.ExitCode + " " +
                        MilestoneDefinitionContract.Limit(
                            result.StandardError, 500));
                continue;
            }

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

    public static IReadOnlySet<string> FindOverlappingWorkItemIds(
        IEnumerable<(string Id, IReadOnlyList<string> Scopes)> workItems)
    {
        var groups = workItems
            .Select(item => new
            {
                item.Id,
                Scopes = item.Scopes
                    .Select(path => NormalizeGitPath(path).Trim('/'))
                    .Where(path => path.Length > 0)
                    .ToArray()
            })
            .ToArray();
        var blocked = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        for (var leftIndex = 0; leftIndex < groups.Length; leftIndex++)
        {
            for (var rightIndex = leftIndex + 1;
                 rightIndex < groups.Length;
                 rightIndex++)
            {
                var overlaps = groups[leftIndex].Scopes.Any(left =>
                    groups[rightIndex].Scopes.Any(right =>
                        ScopesOverlap(left, right)));
                if (!overlaps)
                    continue;

                blocked.Add(groups[leftIndex].Id);
                blocked.Add(groups[rightIndex].Id);
            }
        }

        return blocked;
    }

    public static async Task<MilestoneGitResult> ForceCommitPushAsync(
        string workingDirectory,
        string commitMessage,
        IReadOnlyCollection<string> paths,
        string? configuredRepositoryUrl,
        CancellationToken cancellationToken,
        IProgress<string>? progress = null)
    {
        var git = new ProcessGitCommandRunner();
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(6));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, deadline.Token);
        var token = linked.Token;
        string? pathspecFile = null;
        string? localHead = null;
        var commitCreated = false;
        var pushState = "SKIPPED";
        var eligibleCount = 0;
        var preservedOtherStaged = 0;
        var repairedRuntimeCount = 0;
        var trustedCleanupRemoved = false;
        string? replacedHead = null;

        async Task<GitCommandResult> Run(params string[] args)
        {
            token.ThrowIfCancellationRequested();
            var result = await git.RunAsync(
                workingDirectory, args, TimeSpan.FromMinutes(3), token)
                .ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            return result;
        }

        MilestoneGitResult Fail(string stage, GitCommandResult? result = null)
        {
            progress?.Report("GIT_FINALIZE 실패 · " + stage);
            var detail = result is null ? string.Empty :
                Environment.NewLine + $"exit={result.ExitCode}" +
                Environment.NewLine + $"timedOut={result.TimedOut}" +
                Environment.NewLine + "detail=" +
                MilestoneDefinitionContract.Limit(
                    string.IsNullOrWhiteSpace(result.StandardError)
                        ? result.StandardOutput : result.StandardError, 1000);
            return new(
                false, false, RequiredBranch, localHead,
                "FORCE_COMMIT_PUSH" + Environment.NewLine +
                $"branch={RequiredBranch}" + Environment.NewLine +
                $"step={stage}" + Environment.NewLine +
                $"stagingPaths={eligibleCount}" + Environment.NewLine +
                $"commitCreated={(commitCreated ? "YES" : "NO")}" +
                Environment.NewLine + "trustedCleanup=" +
                    (trustedCleanupRemoved ? "REMOVED" : "NONE") +
                Environment.NewLine + "push=" + pushState +
                Environment.NewLine + "mode=FORCE_LOCAL_MAIN_WINS" +
                (replacedHead is null ? string.Empty :
                    Environment.NewLine + $"repairedPreviousHead={replacedHead}" +
                    Environment.NewLine + $"removedRuntimeFiles={repairedRuntimeCount}") +
                detail);
        }

        try
        {
            progress?.Report("GIT_FINALIZE · 저장소 상태 확인");
            var inside = await Run("rev-parse", "--is-inside-work-tree")
                .ConfigureAwait(false);
            if (inside.ExitCode != 0 ||
                !string.Equals(inside.StandardOutput.Trim(), "true",
                    StringComparison.OrdinalIgnoreCase))
                return Fail("NOT_REPOSITORY", inside);

            var branch = await ReadCurrentBranchAsync(Run).ConfigureAwait(false);
            if (branch != RequiredBranch)
            {
                var switched = await Run("switch", RequiredBranch).ConfigureAwait(false);
                if (switched.ExitCode != 0)
                    return Fail("MAIN_SWITCH_FAILED", switched);
            }

            if (!string.IsNullOrWhiteSpace(configuredRepositoryUrl))
            {
                var origin = await Run("remote", "get-url", "origin")
                    .ConfigureAwait(false);
                GitCommandResult? updated = null;
                if (origin.ExitCode != 0 || string.IsNullOrWhiteSpace(origin.StandardOutput))
                    updated = await Run("remote", "add", "origin", configuredRepositoryUrl.Trim())
                        .ConfigureAwait(false);
                else if (!RepositoryAddressesEqual(
                             configuredRepositoryUrl, origin.StandardOutput.Trim()))
                    updated = await Run("remote", "set-url", "origin", configuredRepositoryUrl.Trim())
                        .ConfigureAwait(false);
                if (updated is not null && updated.ExitCode != 0)
                    return Fail("ORIGIN_UPDATE_FAILED", updated);
            }

            // 충돌 작업이 없는 경우 abort는 1/128을 반환할 수 있다.
            foreach (var action in new[] { "rebase", "merge", "cherry-pick" })
            {
                var aborted = await Run(action, "--abort").ConfigureAwait(false);
                if (aborted.ExitCode < 0 || aborted.TimedOut)
                    return Fail("ABORT_FAILED:" + action, aborted);
            }

            // A previous push may have failed after a ProjectHub commit accidentally
            // included generated files. Repair only when remote main is exactly its
            // parent; never reset source changes or the user's existing index.
            var repair = await RepairUnpublishedRuntimeHeadAsync(
                workingDirectory, git, Run, token, progress).ConfigureAwait(false);
            if (!repair.Success)
            {
                localHead = repair.PreviousHead;
                return Fail(repair.ErrorStep ?? "RUNTIME_COMMIT_REPAIR_FAILED",
                    repair.Error);
            }
            replacedHead = repair.PreviousHead;
            repairedRuntimeCount = repair.ExcludedFiles;

            var requestedScopes = BuildScopedPathspecs(
                paths.Select(NormalizeGitPath)
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
            progress?.Report("GIT_FINALIZE · 변경 파일 수집");
            var dirtyPaths = await SnapshotChangedPathsAsync(
                workingDirectory, token, requireSuccess: true).ConfigureAwait(false);
            var scopedPaths = dirtyPaths
                .Where(path => requestedScopes.Count > 0 &&
                               IsPathWithinScopes(path, requestedScopes))
                .Where(path => !IsRuntimeOutput(path))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
            eligibleCount = scopedPaths.Length;

            // GitHub rejects blobs >= 100 MiB; flag large source files explicitly
            // instead of silently omitting user content or creating an unpushable commit.
            foreach (var scopedPath in scopedPaths)
            {
                var fullPath = Path.Combine(
                    workingDirectory, scopedPath.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(fullPath) &&
                    new FileInfo(fullPath).Length >= 100L * 1024 * 1024)
                {
                    return Fail("SOURCE_FILE_TOO_LARGE",
                        new GitCommandResult(-1, string.Empty,
                            "path=" + scopedPath +
                            " sizeBytes=" + new FileInfo(fullPath).Length +
                            " (GitHub 100 MiB limit; use supported LFS or reduce file size)"));
                }
            }

            progress?.Report($"GIT_FINALIZE · 소스 {eligibleCount}개 일괄 staging");

            if (eligibleCount > 0)
            {
                // NUL 구분 UTF-8 pathspec: Windows 인수 길이 제한을 회피한다.
                pathspecFile = Path.GetTempFileName();
                await File.WriteAllBytesAsync(
                    pathspecFile,
                    new UTF8Encoding(false).GetBytes(string.Join('\0', scopedPaths) + "\0"),
                    token).ConfigureAwait(false);
                var added = await Run(
                    "--literal-pathspecs", "add", "-A",
                    "--pathspec-from-file=" + pathspecFile,
                    "--pathspec-file-nul").ConfigureAwait(false);
                if (added.ExitCode != 0)
                    return Fail("BATCH_STAGE_FAILED", added);

                // 별도로 staged된 타 작업 파일은 commit에 포함하지 않는다.
                var diff = await Run("diff", "--cached", "--name-only", "-z")
                    .ConfigureAwait(false);
                if (diff.ExitCode != 0)
                    return Fail("STAGED_DIFF_FAILED", diff);
                var selected = scopedPaths.ToHashSet(StringComparer.OrdinalIgnoreCase);
                var stagedPaths = diff.StandardOutput
                    .Split('\0', StringSplitOptions.RemoveEmptyEntries)
                    .Select(NormalizeGitPath)
                    .ToArray();
                preservedOtherStaged = stagedPaths.Count(path => !selected.Contains(path));
                if (!stagedPaths.Any(selected.Contains))
                    return Fail("STAGED_SCOPE_EMPTY_AFTER_ADD");

                progress?.Report($"GIT_FINALIZE · {eligibleCount}개 단일 커밋");
                Task<GitCommandResult> CommitAsync(bool useFallbackIdentity)
                {
                    var args = new List<string>();
                    if (useFallbackIdentity)
                        args.AddRange(new[]
                        {
                            "-c", "user.name=ProjectHub",
                            "-c", "user.email=projecthub@localhost"
                        });
                    args.AddRange(new[]
                    {
                        "--literal-pathspecs", "commit", "--only", "-m", commitMessage,
                        "--pathspec-from-file=" + pathspecFile, "--pathspec-file-nul"
                    });
                    return Run(args.ToArray());
                }

                var commit = await CommitAsync(false).ConfigureAwait(false);
                if (commit.ExitCode != 0 &&
                    (commit.StandardError.Contains("Author identity unknown",
                        StringComparison.OrdinalIgnoreCase) ||
                     commit.StandardError.Contains("unable to auto-detect email address",
                        StringComparison.OrdinalIgnoreCase)))
                    commit = await CommitAsync(true).ConfigureAwait(false);
                if (commit.ExitCode != 0)
                    return Fail("COMMIT_FAILED", commit);
                commitCreated = true;
            }

            // Scoped generated-artifact cleanup is owned by the trusted Worker
            // Git finalizer, never by the role sandbox. Use an isolated tree so
            // unrelated files staged by the user cannot enter the cleanup commit.
            var cleanup = await TrustedTrackedArtifactCleanup.ApplyAsync(
                workingDirectory, git,
                args => Run(args),
                token).ConfigureAwait(false);
            if (!cleanup.Success)
                return Fail(cleanup.ErrorStage ?? "TRACKED_CLEANUP_FAILED",
                    cleanup.Error);
            trustedCleanupRemoved = cleanup.Removed;
            if (trustedCleanupRemoved)
            {
                commitCreated = true;
                progress?.Report("GIT_FINALIZE · 추적된 생성물 1개 안전 제외");
            }

            var head = await Run("rev-parse", "HEAD").ConfigureAwait(false);
            if (head.ExitCode != 0 || string.IsNullOrWhiteSpace(head.StandardOutput))
                return Fail("HEAD_READ_FAILED", head);
            localHead = head.StandardOutput.Trim();

            // stage/commit 실패일 때는 절대로 이전 HEAD만 push하지 않는다.
            GitCommandResult? pushed = null;
            for (var attempt = 1; attempt <= 3; attempt++)
            {
                progress?.Report($"GIT_FINALIZE · push {attempt}/3");
                pushed = await Run("push", "--force", "origin",
                    "HEAD:refs/heads/" + RequiredBranch).ConfigureAwait(false);
                if (pushed.ExitCode == 0)
                    break;
                if (attempt < 3)
                    await Task.Delay(TimeSpan.FromMilliseconds(500 * attempt), token)
                        .ConfigureAwait(false);
            }
            pushState = pushed?.ExitCode == 0 ? "COMPLETED" : "FAILED";
            if (pushState != "COMPLETED")
                return Fail("PUSH_FAILED", pushed);

            if (trustedCleanupRemoved)
            {
                // A successful push alone does not prove that the remote now
                // references the cleanup commit. Verify SHA and target tree.
                var remoteHead = await Run(
                    "ls-remote", "--heads", "origin", RequiredBranch)
                    .ConfigureAwait(false);
                var publishedHead = remoteHead.StandardOutput.Split(
                    new[] { '\t', ' ', '\r', '\n' },
                    StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                if (remoteHead.ExitCode != 0 ||
                    !string.Equals(publishedHead, localHead,
                        StringComparison.OrdinalIgnoreCase))
                    return Fail("CLEANUP_REMOTE_SHA_NOT_CONFIRMED", remoteHead);

                var localTree = await Run(
                    "ls-tree", "-r", "--name-only", "HEAD", "--",
                    TrustedTrackedArtifactCleanup.TargetPath)
                    .ConfigureAwait(false);
                if (localTree.ExitCode != 0 ||
                    !string.IsNullOrWhiteSpace(localTree.StandardOutput))
                    return Fail("CLEANUP_TARGET_STILL_TRACKED", localTree);
            }

            progress?.Report("GIT_FINALIZE · 커밋 후 변경 확인");
            var remaining = (await SnapshotChangedPathsAsync(
                    workingDirectory, token, requireSuccess: true).ConfigureAwait(false))
                .Where(path => requestedScopes.Count > 0 &&
                               IsPathWithinScopes(path, requestedScopes))
                .Where(path => !IsRuntimeOutput(path))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
            var summary = "FORCE_COMMIT_PUSH" + Environment.NewLine +
                $"branch={RequiredBranch}" + Environment.NewLine +
                $"commit={localHead}" + Environment.NewLine +
                $"commitCreated={(commitCreated ? "YES" : "NO")}" +
                Environment.NewLine + "trustedCleanup=" +
                    (trustedCleanupRemoved ? "REMOVED_AND_REMOTE_VERIFIED" : "NONE") +
                Environment.NewLine + "noChanges=" + (!commitCreated ? "YES" : "NO") +
                Environment.NewLine + "push=" + pushState +
                Environment.NewLine + "mode=FORCE_LOCAL_MAIN_WINS" +
                Environment.NewLine + $"stagingPaths={eligibleCount}" +
                Environment.NewLine + $"preservedOtherStaged={preservedOtherStaged}" +
                Environment.NewLine + $"repairedRuntimeFiles={repairedRuntimeCount}" +
                (replacedHead is null ? string.Empty :
                    Environment.NewLine + $"repairedPreviousHead={replacedHead}") +
                Environment.NewLine + $"relevantDirtyAfterFinalize={remaining.Length}";
            if (remaining.Length > 0)
                summary += Environment.NewLine +
                    "relevantDirtySample=" + string.Join(",", remaining.Take(10));
            progress?.Report(remaining.Length == 0
                ? (commitCreated
                    ? "GIT_FINALIZE · 완료"
                    : "GIT_FINALIZE · 변경 없음, 기존 HEAD 재확인")
                : $"GIT_FINALIZE · 잔여 변경 {remaining.Length}개");
            return new(remaining.Length == 0, false, RequiredBranch, localHead, summary);
        }
        catch (OperationCanceledException)
            when (deadline.IsCancellationRequested &&
                  !cancellationToken.IsCancellationRequested)
        {
            return Fail("OVERALL_TIMEOUT_6_MIN");
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Fail("EXCEPTION:" + ex.GetType().Name,
                new GitCommandResult(-1, string.Empty, ex.Message));
        }
        finally
        {
            if (pathspecFile is not null)
            {
                try { File.Delete(pathspecFile); }
                catch { /* Cleanup failure must not hide the Git result. */ }
            }
        }
    }

    public static Task<MilestoneGitResult> FinalizeGitAsync(
        string workingDirectory,
        MilestoneDefinition milestone,
        IReadOnlyCollection<string> paths,
        string? configuredRepositoryUrl,
        CancellationToken cancellationToken,
        IProgress<string>? progress = null) =>
        ForceCommitPushAsync(
            workingDirectory,
            $"ProjectHub milestone {milestone.Id} final",
            paths,
            configuredRepositoryUrl,
            cancellationToken,
            progress);

    private sealed record RuntimeCommitRepairResult(
        bool Success,
        string? PreviousHead = null,
        string? RewrittenHead = null,
        int ExcludedFiles = 0,
        string? ErrorStep = null,
        GitCommandResult? Error = null);

    private static async Task<RuntimeCommitRepairResult> RepairUnpublishedRuntimeHeadAsync(
        string workingDirectory,
        ProcessGitCommandRunner git,
        Func<string[], Task<GitCommandResult>> run,
        CancellationToken cancellationToken,
        IProgress<string>? progress)
    {
        var head = await run(new[] { "rev-parse", "--verify", "HEAD" })
            .ConfigureAwait(false);
        // An unborn repository does not have any previous commit to repair.
        if (head.ExitCode != 0)
            return new(true);

        var headSha = head.StandardOutput.Trim();
        var additions = await run(new[]
        {
            "diff-tree", "--no-commit-id", "--diff-filter=A",
            "--name-only", "-r", "-z", "HEAD"
        }).ConfigureAwait(false);
        if (additions.ExitCode != 0)
            return new(false, headSha, ErrorStep: "RUNTIME_HEAD_SCAN_FAILED",
                Error: additions);

        var unwanted = additions.StandardOutput
            .Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Select(NormalizeGitPath)
            .Where(IsRuntimeOutput)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (unwanted.Length == 0)
            return new(true);

        var remote = await run(new[] {
            "ls-remote", "--heads", "origin", RequiredBranch
        }).ConfigureAwait(false);
        if (remote.ExitCode != 0)
            return new(false, headSha, ErrorStep: "RUNTIME_REMOTE_CHECK_FAILED",
                Error: remote);
        var remoteSha = remote.StandardOutput.Split(
            new[] { '\t', ' ', '\r', '\n' },
            StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (string.Equals(remoteSha, headSha, StringComparison.OrdinalIgnoreCase))
        {
            // This commit is already published. Never rewrite it on the user's behalf.
            progress?.Report(
                "GIT_FINALIZE · 원격에 게시된 이전 생성물은 보존하고 신규 커밋에서 제외");
            return new(true);
        }

        var subject = await run(new[] { "log", "-1", "--format=%s", "HEAD" })
            .ConfigureAwait(false);
        if (subject.ExitCode != 0)
            return new(false, headSha, ErrorStep: "RUNTIME_HEAD_SUBJECT_FAILED",
                Error: subject);
        if (!subject.StandardOutput.StartsWith(
                "ProjectHub milestone ", StringComparison.Ordinal))
        {
            return new(false, headSha, ErrorStep: "RUNTIME_HEAD_NOT_PROJECTHUB",
                Error: new GitCommandResult(-1, string.Empty,
                    "Generated files exist in a commit not owned by ProjectHub; " +
                    "manual review required. Example: " + unwanted[0]));
        }

        var parents = await run(new[] { "rev-list", "--parents", "-n", "1", "HEAD" })
            .ConfigureAwait(false);
        if (parents.ExitCode != 0)
            return new(false, headSha, ErrorStep: "RUNTIME_HEAD_PARENTS_FAILED",
                Error: parents);
        var tokens = parents.StandardOutput
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length != 2 ||
            !string.Equals(tokens[0], headSha, StringComparison.OrdinalIgnoreCase))
        {
            return new(false, headSha, ErrorStep: "RUNTIME_HEAD_NOT_SINGLE_PARENT",
                Error: new GitCommandResult(-1, string.Empty,
                    "Unpublished HEAD cannot be safely rewritten without review."));
        }

        // A precise remote lease prevents dropping unrelated local history.
        if (!string.Equals(
                remoteSha, tokens[1], StringComparison.OrdinalIgnoreCase))
        {
            return new(false, headSha, ErrorStep: "RUNTIME_REMOTE_PARENT_MISMATCH",
                Error: new GitCommandResult(-1, string.Empty,
                    "origin/main is not the sole parent of local HEAD; " +
                    "automatic history repair skipped. Local=" + headSha +
                    " parent=" + tokens[1] + " remote=" + (remoteSha ?? "missing")));
        }

        progress?.Report(
            $"GIT_FINALIZE · 미게시 커밋에서 생성물 {unwanted.Length}개 안전 제외");
        string? temporaryIndex = null;
        string? exclusionPathspec = null;
        try
        {
            // Use a disposable index so all pre-existing staged files remain intact.
            temporaryIndex = Path.Combine(
                Path.GetTempPath(),
                "projecthub-repair-index-" + Guid.NewGuid().ToString("N"));
            exclusionPathspec = Path.GetTempFileName();
            await File.WriteAllBytesAsync(
                exclusionPathspec,
                new UTF8Encoding(false).GetBytes(string.Join('\0', unwanted) + "\0"),
                cancellationToken).ConfigureAwait(false);
            var environment = new Dictionary<string, string>
            {
                ["GIT_INDEX_FILE"] = temporaryIndex
            };
            async Task<GitCommandResult> WithIndex(params string[] args)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = await git.RunAsync(
                    workingDirectory, args, TimeSpan.FromMinutes(3),
                    cancellationToken, environment).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                return result;
            }

            var read = await WithIndex("read-tree", "HEAD").ConfigureAwait(false);
            if (read.ExitCode != 0)
                return new(false, headSha, ErrorStep: "RUNTIME_REPAIR_READ_TREE_FAILED",
                    Error: read);
            var remove = await WithIndex(
                "--literal-pathspecs", "rm", "--cached", "-r", "--ignore-unmatch",
                "--pathspec-from-file=" + exclusionPathspec, "--pathspec-file-nul")
                .ConfigureAwait(false);
            if (remove.ExitCode != 0)
                return new(false, headSha, ErrorStep: "RUNTIME_REPAIR_REMOVE_FAILED",
                    Error: remove);
            var tree = await WithIndex("write-tree").ConfigureAwait(false);
            if (tree.ExitCode != 0 ||
                string.IsNullOrWhiteSpace(tree.StandardOutput))
                return new(false, headSha, ErrorStep: "RUNTIME_REPAIR_TREE_FAILED",
                    Error: tree);

            var originalMessage = await run(new[] {
                "log", "-1", "--format=%B", "HEAD"
            }).ConfigureAwait(false);
            if (originalMessage.ExitCode != 0)
                return new(false, headSha, ErrorStep: "RUNTIME_REPAIR_MESSAGE_FAILED",
                    Error: originalMessage);
            var replacement = await run(new[]
            {
                "-c", "user.name=ProjectHub",
                "-c", "user.email=projecthub@localhost",
                "commit-tree", tree.StandardOutput.Trim(),
                "-p", tokens[1], "-m", originalMessage.StandardOutput
            }).ConfigureAwait(false);
            if (replacement.ExitCode != 0 ||
                string.IsNullOrWhiteSpace(replacement.StandardOutput))
                return new(false, headSha, ErrorStep: "RUNTIME_REPAIR_COMMIT_FAILED",
                    Error: replacement);

            var replacementSha = replacement.StandardOutput.Trim();
            var moved = await run(new[] {
                "update-ref", "refs/heads/" + RequiredBranch, replacementSha, headSha
            }).ConfigureAwait(false);
            if (moved.ExitCode != 0)
                return new(false, headSha, ErrorStep: "RUNTIME_REPAIR_REF_FAILED",
                    Error: moved);

            progress?.Report(
                $"GIT_FINALIZE · 생성물 제외 완료 · {headSha[..8]} → {replacementSha[..8]}");
            return new(true, headSha, replacementSha, unwanted.Length);
        }
        finally
        {
            if (exclusionPathspec is not null)
            {
                try { File.Delete(exclusionPathspec); } catch { }
            }
            if (temporaryIndex is not null)
            {
                try { File.Delete(temporaryIndex); } catch { }
                try { File.Delete(temporaryIndex + ".lock"); } catch { }
            }
        }
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

    public static string NormalizeMechanicalCommand(
        string operation,
        string command)
    {
        var normalizedOperation = (operation ?? string.Empty)
            .Trim()
            .ToUpperInvariant();
        var normalizedCommand = (command ?? string.Empty).Trim();

        if (normalizedCommand.Length == 0 ||
            normalizedOperation is not ("BUILD" or "PUBLISH"))
        {
            return normalizedCommand;
        }

        // BUILD/PUBLISH 결과 위치는 Worker 소유 규칙으로 프로젝트 루트 bin에 고정한다.
        // 모델이 절대경로와 따옴표를 섞어 출력해도 그대로 실행하지 않는다.
        const string outputPattern =
            @"(?i)(?:--output|-o)(?:\s*=\s*|\s+)(?:""[^""]*""|'[^']*'|[^\s&|]+)";
        var rewritten = Regex.Replace(
            normalizedCommand,
            outputPattern,
            "--output bin");

        if (!Regex.IsMatch(
                rewritten,
                @"(?i)(?:^|\s)(?:--output|-o)(?:\s*=\s*|\s+)"))
        {
            var dotnetOperation = normalizedOperation == "BUILD"
                ? "build"
                : "publish";
            if (Regex.IsMatch(
                    rewritten,
                    @"(?i)^\s*dotnet\s+" + dotnetOperation + @"\b"))
            {
                rewritten += " --output bin";
            }
        }

        return rewritten;
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

    private static bool IsRuntimeOutput(string path)
    {
        var normalized = NormalizeGitPath(path).Trim('/');
        if (normalized.Length == 0)
            return false;

        // The trusted finalizer handles this exact ignored tracked binary.
        // Do not feed its staged deletion back into ordinary git add -A,
        // which can restage or reject an ignored local executable.
        if (normalized.Equals(
                TrustedTrackedArtifactCleanup.TargetPath,
                StringComparison.OrdinalIgnoreCase))
            return true;

        var parts = normalized.Split(
            '/',
            StringSplitOptions.RemoveEmptyEntries);

        if (parts.Any(part =>
                part.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
                part.Equals("obj", StringComparison.OrdinalIgnoreCase) ||
                part.Equals("dist", StringComparison.OrdinalIgnoreCase) ||
                part.Equals("dist-electron", StringComparison.OrdinalIgnoreCase) ||
                part.Equals("node_modules", StringComparison.OrdinalIgnoreCase) ||
                part.Equals(".npm-cache", StringComparison.OrdinalIgnoreCase) ||
                part.Equals(".pnpm-store", StringComparison.OrdinalIgnoreCase) ||
                part.Equals(".vite", StringComparison.OrdinalIgnoreCase) ||
                part.Equals(".cache", StringComparison.OrdinalIgnoreCase) ||
                part.Equals(".electron-cache", StringComparison.OrdinalIgnoreCase) ||
                part.Equals(".playwright", StringComparison.OrdinalIgnoreCase) ||
                part.Equals(".next", StringComparison.OrdinalIgnoreCase) ||
                part.Equals(".turbo", StringComparison.OrdinalIgnoreCase) ||
                part.Equals("coverage", StringComparison.OrdinalIgnoreCase) ||
                part.Equals(".projecthub", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (normalized.EndsWith(".tsbuildinfo", StringComparison.OrdinalIgnoreCase))
            return true;

        return normalized.Equals(
                   "temp",
                   StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith(
                   "temp/",
                   StringComparison.OrdinalIgnoreCase);
    }
}
