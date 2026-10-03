using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class GitWorktreeManagerTests
{
    [Fact]
    public void WorkClonePathsAreOutsideRepositoryAndBranchIsStable()
    {
        var root = CreateTempRepositoryDirectory();
        try
        {
            var work = GitWorktreeManager.BuildWorktreePath(root, "job-1", "W1");
            var integration = GitWorktreeManager.BuildIntegrationClonePath(root, "job-1", "I1");
            var runtime = WorkerPaths.GetRepositoryRuntimePaths(root);
            var branchA = GitWorktreeManager.BuildBranchName("job-1", "W1");
            var branchB = GitWorktreeManager.BuildBranchName("job-1", "W1");

            Assert.StartsWith(Path.GetFullPath(runtime.Worktrees), Path.GetFullPath(work));
            Assert.StartsWith(Path.GetFullPath(runtime.IntegrationClones), Path.GetFullPath(integration));
            Assert.False(IsWithin(work, root));
            Assert.False(IsWithin(integration, root));
            Assert.NotEqual(Path.GetFullPath(work), Path.GetFullPath(integration));
            Assert.Equal(branchA, branchB);
            Assert.StartsWith("projecthub/", branchA);
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public async Task ResolveNormalBaseRefFetchesOriginAndUsesNewestLinearDependency()
    {
        var root = CreateTempRepositoryDirectory();
        var runner = new FakeGitRunner();
        runner.Enqueue(0, root);
        runner.Enqueue(0, "");
        runner.Enqueue(0, "base123");
        runner.Enqueue(0, "dep456");
        runner.Enqueue(0, "");
        runner.Enqueue(0, "dep789");
        runner.Enqueue(0, "");

        try
        {
            var result = await new GitWorktreeManager(runner)
                .ResolveNormalBaseRefAsync(root, "main", new[] { "dep-one", "dep-two" });

            Assert.True(result.Success);
            Assert.Equal("dep789", result.EffectiveBaseRef);
            Assert.Contains(
                runner.Calls,
                call => call.Arguments.SequenceEqual(new[] { "fetch", "--prune", "origin" }));
            Assert.Contains(
                runner.Calls,
                call => call.Arguments.SequenceEqual(
                    new[] { "merge-base", "--is-ancestor", "dep456", "dep789" }));
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public async Task OriginFetchIsSerializedAcrossManagersForSameRepository()
    {
        var root = CreateTempRepositoryDirectory();
        var runner = new ConcurrentFetchGitRunner(root);

        try
        {
            var first = new GitWorktreeManager(runner);
            var second = new GitWorktreeManager(runner);

            var results = await Task.WhenAll(
                first.ResolveNormalBaseRefAsync(
                    root,
                    "base-one",
                    Array.Empty<string>()),
                second.ResolveNormalBaseRefAsync(
                    root,
                    "base-two",
                    Array.Empty<string>()));

            Assert.All(results, result => Assert.True(result.Success));
            Assert.Equal(1, runner.MaxConcurrentFetches);
            Assert.Equal(2, runner.FetchCount);
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public async Task ResolveNormalBaseRefRequiresIntegrationForDivergentDependencies()
    {
        var root = CreateTempRepositoryDirectory();
        var runner = new FakeGitRunner();
        runner.Enqueue(0, root);
        runner.Enqueue(0, "");
        runner.Enqueue(0, "base123");
        runner.Enqueue(0, "left456");
        runner.Enqueue(0, "");
        runner.Enqueue(0, "right789");
        runner.Enqueue(1, "");
        runner.Enqueue(1, "");

        try
        {
            var result = await new GitWorktreeManager(runner)
                .ResolveNormalBaseRefAsync(root, "main", new[] { "left", "right" });

            Assert.False(result.Success);
            Assert.Equal("NORMAL_MULTIPLE_CODE_BASES_REQUIRE_INTEGRATION", result.ErrorCode);
            Assert.Contains("left456", result.ErrorDetail ?? string.Empty);
            Assert.Contains("right789", result.ErrorDetail ?? string.Empty);
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public async Task PrepareCreatesDisposableCloneFromOrigin()
    {
        var root = CreateTempRepositoryDirectory();
        var clonePath = GitWorktreeManager.BuildWorktreePath(root, "job", "W1");
        var branch = GitWorktreeManager.BuildBranchName("job", "W1");
        var runner = new FakeGitRunner();
        runner.Enqueue(0, root);
        runner.Enqueue(0, "https://example.invalid/repo.git");
        runner.Enqueue(0, "");
        runner.Enqueue(0, "abc123");
        runner.Enqueue(0, "cloned");
        runner.Enqueue(0, "checked out");
        runner.Enqueue(0, "");
        runner.Enqueue(0, "");
        runner.Enqueue(0, "abc123");

        try
        {
            var result = await new GitWorktreeManager(runner)
                .PrepareAsync(root, "job", "W1", "main");

            Assert.True(result.Success);
            Assert.False(result.Reused);
            Assert.Equal("abc123", result.BaseCommit);
            Assert.Equal("abc123", result.HeadCommit);
            Assert.Equal(clonePath, result.WorktreePath);
            Assert.Equal(branch, result.Branch);
            Assert.Contains(
                runner.Calls,
                call => call.Arguments.SequenceEqual(new[]
                {
                    "clone",
                    "--no-checkout",
                    "https://example.invalid/repo.git",
                    clonePath
                }));
            Assert.DoesNotContain(
                runner.Calls.SelectMany(call => call.Arguments),
                argument => string.Equals(argument, "worktree", StringComparison.Ordinal));
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public async Task PrepareReusesExistingDisposableCloneOnlyWhenBranchAndBaseMatch()
    {
        var root = CreateTempRepositoryDirectory();
        var clonePath = GitWorktreeManager.BuildWorktreePath(root, "job", "W1");
        var branch = GitWorktreeManager.BuildBranchName("job", "W1");
        Directory.CreateDirectory(Path.Combine(clonePath, ".git"));
        var runner = new FakeGitRunner();
        runner.Enqueue(0, root);
        runner.Enqueue(0, "https://example.invalid/repo.git");
        runner.Enqueue(0, "");
        runner.Enqueue(0, "base123");
        runner.Enqueue(0, clonePath);
        runner.Enqueue(0, Path.Combine(clonePath, ".git"));
        runner.Enqueue(0, branch);
        runner.Enqueue(0, "head456");
        runner.Enqueue(0, "");

        try
        {
            var result = await new GitWorktreeManager(runner)
                .PrepareAsync(root, "job", "W1", "main");

            Assert.True(result.Success);
            Assert.True(result.Reused);
            Assert.Equal("head456", result.HeadCommit);
            Assert.DoesNotContain(
                runner.Calls,
                call => call.Arguments.Count > 0 && call.Arguments[0] == "clone");
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public async Task CheckpointCommitsDirtyCloneAndPublishesVerifiedRemoteBranch()
    {
        var root = CreateTempRepositoryDirectory();
        var clone = GitWorktreeManager.BuildWorktreePath(root, "job", "W1");
        Directory.CreateDirectory(clone);
        var branch = GitWorktreeManager.BuildBranchName("job", "W1");
        var runner = new FakeGitRunner();
        runner.Enqueue(0, "base123");
        runner.Enqueue(0, branch);
        runner.Enqueue(0, " M changed.cs");
        runner.Enqueue(0, "");
        runner.Enqueue(0, "committed");
        runner.Enqueue(0, "new456");
        runner.Enqueue(0, "");
        runner.Enqueue(0, $"new456\trefs/heads/{branch}");
        runner.Enqueue(0, "new456");
        runner.Enqueue(0, branch);
        runner.Enqueue(0, "");

        try
        {
            var result = await new GitWorktreeManager(runner)
                .CreateCheckpointAsync(clone, "W1");

            Assert.True(result.Success);
            Assert.True(result.CreatedCommit);
            Assert.Equal("new456", result.HeadCommit);
            Assert.Contains(
                runner.Calls,
                call => call.Arguments.SequenceEqual(new[] { "add", "--all", "--", "." }));
            Assert.Contains(
                runner.Calls,
                call => call.Arguments.SequenceEqual(
                    new[] { "push", "origin", "HEAD:refs/heads/" + branch }));
            Assert.Contains(
                runner.Calls,
                call => call.Arguments.SequenceEqual(
                    new[] { "ls-remote", "--exit-code", "origin", "refs/heads/" + branch }));
            Assert.DoesNotContain(
                runner.Calls.SelectMany(call => call.Arguments),
                argument => argument.StartsWith(":(exclude", StringComparison.Ordinal));
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public async Task CleanCheckpointDoesNotCreateRemoteBranchByDefault()
    {
        var root = CreateTempRepositoryDirectory();
        var clone = GitWorktreeManager.BuildWorktreePath(root, "job", "W1");
        Directory.CreateDirectory(clone);
        var branch = GitWorktreeManager.BuildBranchName("job", "W1");
        var runner = new FakeGitRunner();
        runner.Enqueue(0, "head123");
        runner.Enqueue(0, branch);
        runner.Enqueue(0, "");

        try
        {
            var result = await new GitWorktreeManager(runner)
                .CreateCheckpointAsync(clone, "W1");

            Assert.True(result.Success);
            Assert.False(result.CreatedCommit);
            Assert.Equal("head123", result.HeadCommit);
            Assert.DoesNotContain(
                runner.Calls,
                call => call.Arguments.Count > 0 &&
                        (call.Arguments[0] == "push" ||
                         call.Arguments[0] == "ls-remote"));
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public async Task CleanCheckpointCanRepublishRemoteHeadForRecovery()
    {
        var root = CreateTempRepositoryDirectory();
        var clone = GitWorktreeManager.BuildWorktreePath(root, "job", "W1");
        Directory.CreateDirectory(clone);
        var branch = GitWorktreeManager.BuildBranchName("job", "W1");
        var runner = new FakeGitRunner();
        runner.Enqueue(0, "head123");
        runner.Enqueue(0, branch);
        runner.Enqueue(0, "");
        runner.Enqueue(0, "");
        runner.Enqueue(0, $"head123\trefs/heads/{branch}");
        runner.Enqueue(0, "head123");
        runner.Enqueue(0, branch);
        runner.Enqueue(0, "");

        try
        {
            var result = await new GitWorktreeManager(runner)
                .CreateCheckpointAsync(
                    clone,
                    "W1",
                    publishCleanHead: true);

            Assert.True(result.Success);
            Assert.False(result.CreatedCommit);
            Assert.Equal("head123", result.HeadCommit);
            Assert.Contains(
                runner.Calls,
                call => call.Arguments.SequenceEqual(
                    new[] { "push", "origin", "HEAD:refs/heads/" + branch }));
            Assert.Contains(
                runner.Calls,
                call => call.Arguments.SequenceEqual(
                    new[] { "ls-remote", "--exit-code", "origin", "refs/heads/" + branch }));
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public async Task RemoveRefusesDirtyCloneAndNeverUsesForce()
    {
        var root = CreateTempRepositoryDirectory();
        var clone = GitWorktreeManager.BuildWorktreePath(root, "job", "W1");
        Directory.CreateDirectory(clone);
        var branch = GitWorktreeManager.BuildBranchName("job", "W1");
        var runner = new FakeGitRunner();
        runner.Enqueue(0, "head123");
        runner.Enqueue(0, branch);
        runner.Enqueue(0, " M changed.cs");

        try
        {
            var result = await new GitWorktreeManager(runner)
                .RemoveAsync(root, clone, branch);

            Assert.False(result.Success);
            Assert.Equal("WORK_CLONE_DIRTY", result.ErrorCode);
            Assert.True(Directory.Exists(clone));
            Assert.DoesNotContain(
                runner.Calls.SelectMany(call => call.Arguments),
                argument => argument is "--force" or "-f");
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public async Task RemoveDeletesCleanCloneWithoutGitWorktreeCommands()
    {
        var root = CreateTempRepositoryDirectory();
        var clone = GitWorktreeManager.BuildWorktreePath(root, "job", "W1");
        Directory.CreateDirectory(clone);
        var branch = GitWorktreeManager.BuildBranchName("job", "W1");
        var runner = new FakeGitRunner();
        runner.Enqueue(0, "head123");
        runner.Enqueue(0, branch);
        runner.Enqueue(0, "");

        try
        {
            var result = await new GitWorktreeManager(runner)
                .RemoveAsync(root, clone, branch);

            Assert.True(result.Success);
            Assert.False(Directory.Exists(clone));
            Assert.DoesNotContain(
                runner.Calls,
                call => call.Arguments.Count > 0 && call.Arguments[0] == "worktree");
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public async Task IntegrationCloneCleanupRemovesExternalDependencySnapshots()
    {
        var root = CreateTempRepositoryDirectory();
        var clone = GitWorktreeManager.BuildIntegrationClonePath(root, "job", "I1");
        Directory.CreateDirectory(Path.Combine(clone, ".git"));
        var parent = Directory.GetParent(clone)!.FullName;
        var inputRoot = Path.Combine(parent, ".inputs-" + Path.GetFileName(clone));
        Directory.CreateDirectory(inputRoot);
        File.WriteAllText(Path.Combine(inputRoot, "snapshot.txt"), "dependency");

        try
        {
            var result = await new GitWorktreeManager(new FakeGitRunner())
                .CleanupIntegrationCloneAsync(root, clone);

            Assert.True(result.Success, result.ErrorDetail);
            Assert.False(Directory.Exists(clone));
            Assert.False(Directory.Exists(inputRoot));
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public async Task RuntimeResetDeletesOnlyExternalProjectHubRuntime()
    {
        var root = CreateTempRepositoryDirectory();
        var runtime = WorkerPaths.GetRepositoryRuntimePaths(root);
        var clone = GitWorktreeManager.BuildWorktreePath(root, "job", "W1");
        Directory.CreateDirectory(Path.Combine(clone, ".git"));
        File.WriteAllText(Path.Combine(clone, "dirty.tmp"), "data");

        var runner = new FakeGitRunner();
        runner.Enqueue(0, root);

        try
        {
            var result = await new GitWorktreeManager(runner)
                .ResetRepositoryRuntimeAsync(root);

            Assert.True(result.Success, result.ErrorDetail);
            Assert.True(result.RuntimeDeleted);
            Assert.False(Directory.Exists(runtime.Root));
            Assert.Contains(Path.GetFullPath(clone), result.RemovedWorktrees);
            Assert.DoesNotContain(
                runner.Calls,
                call => call.Arguments.Count > 0 &&
                        call.Arguments[0] == "worktree");
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public async Task RuntimeCleanupPreservesDirtyCloneAndRemovesDisposableCaches()
    {
        var root = CreateTempRepositoryDirectory();
        var runtime = WorkerPaths.GetRepositoryRuntimePaths(root);
        var clone = GitWorktreeManager.BuildWorktreePath(root, "job", "W1");
        Directory.CreateDirectory(Path.Combine(clone, ".git"));
        Directory.CreateDirectory(runtime.NuGetPackages);
        Directory.CreateDirectory(runtime.DotNetHome);
        Directory.CreateDirectory(runtime.TempRoot);
        var runner = new FakeGitRunner();
        runner.Enqueue(0, root);
        runner.Enqueue(0, "head123");
        runner.Enqueue(0, GitWorktreeManager.BuildBranchName("job", "W1"));
        runner.Enqueue(0, " M changed.cs");

        try
        {
            var result = await new GitWorktreeManager(runner)
                .CleanupRepositoryRuntimeAsync(root);

            Assert.False(result.Success);
            Assert.Equal("RUNTIME_CLEANUP_CLONE_DIRTY", result.ErrorCode);
            Assert.True(Directory.Exists(clone));
            Assert.False(Directory.Exists(runtime.NuGetRoot));
            Assert.False(Directory.Exists(runtime.DotNetHome));
            Assert.False(Directory.Exists(runtime.TempRoot));
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public async Task IntegrationPreparationClonesOriginAtCurrentRemoteSyncedHead()
    {
        var root = CreateTempRepositoryDirectory();
        var clone = GitWorktreeManager.BuildIntegrationClonePath(root, "job", "I1");
        var branch = GitWorktreeManager.BuildBranchName("job", "I1");
        var runner = new FakeGitRunner();
        runner.Enqueue(0, root);
        runner.Enqueue(0, "main");
        runner.Enqueue(0, "primary999");
        runner.Enqueue(0, "https://example.invalid/repo.git");
        runner.Enqueue(0, "");
        runner.Enqueue(0, "primary999");
        runner.Enqueue(0, "cloned");
        runner.Enqueue(0, "checked out");
        runner.Enqueue(0, "");
        runner.Enqueue(0, "");
        runner.Enqueue(0, Path.Combine(clone, ".git"));
        runner.Enqueue(0, "primary999");

        try
        {
            var result = await new GitWorktreeManager(runner)
                .PrepareIntegrationAsync(root, "job", "I1", "main");

            Assert.True(result.Success);
            Assert.Equal("primary999", result.BaseCommit);
            Assert.Equal("primary999", result.HeadCommit);
            Assert.Equal(clone, result.WorktreePath);
            Assert.Equal(branch, result.Branch);
            Assert.Contains(
                runner.Calls,
                call => call.Arguments.SequenceEqual(new[]
                {
                    "clone",
                    "--no-checkout",
                    "https://example.invalid/repo.git",
                    clone
                }));
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public async Task ResumeIntegrationRejectsLinkedWorktreeGitDirOutsideClone()
    {
        var root = CreateTempRepositoryDirectory();
        var clone = GitWorktreeManager.BuildIntegrationClonePath(root, "job", "I1");
        Directory.CreateDirectory(clone);
        var branch = GitWorktreeManager.BuildBranchName("job", "I1");
        var runner = new FakeGitRunner();
        runner.Enqueue(0, root);
        runner.Enqueue(0, clone);
        runner.Enqueue(0, Path.Combine(root, ".git", "worktrees", "I1"));

        try
        {
            var result = await new GitWorktreeManager(runner)
                .ResumeIntegrationAsync(root, clone, branch, "base123");

            Assert.False(result.Success);
            Assert.Equal("INTEGRATION_CLONE_GITDIR_NOT_LOCAL", result.ErrorCode);
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public async Task TargetContainmentRequiresLocalBranchToMatchFetchedRemoteHead()
    {
        var root = CreateTempRepositoryDirectory();
        var runner = new FakeGitRunner();
        runner.Enqueue(0, root);
        runner.Enqueue(0, "main");
        runner.Enqueue(0, "");
        runner.Enqueue(0, "base123");
        runner.Enqueue(0, "base123");
        runner.Enqueue(0, "result456");
        runner.Enqueue(1, "");

        try
        {
            var result = await new GitWorktreeManager(runner)
                .InspectTargetContainmentAsync(root, "result-ref", "main");

            Assert.True(result.Success);
            Assert.False(result.IsContained);
            Assert.Equal("result456", result.ResultCommit);
            Assert.Contains(
                runner.Calls,
                call => call.Arguments.SequenceEqual(new[] { "fetch", "--prune", "origin" }));
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public async Task TargetContainmentRejectsRemoteBranchDrift()
    {
        var root = CreateTempRepositoryDirectory();
        var runner = new FakeGitRunner();
        runner.Enqueue(0, root);
        runner.Enqueue(0, "main");
        runner.Enqueue(0, "");
        runner.Enqueue(0, "local123");
        runner.Enqueue(0, "remote456");

        try
        {
            var result = await new GitWorktreeManager(runner)
                .InspectTargetContainmentAsync(root, "result-ref", "main");

            Assert.False(result.Success);
            Assert.Equal("TARGET_REMOTE_HEAD_MISMATCH", result.ErrorCode);
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public async Task TargetCheckoutSwitchesToVerifiedRemoteResultBranchWithoutMergeOrPush()
    {
        var root = CreateTempRepositoryDirectory();
        var resultBranch = GitWorktreeManager.BuildBranchName("job", "W1");
        var runner = new FakeGitRunner();
        runner.Enqueue(0, root);
        runner.Enqueue(0, "");
        runner.Enqueue(0, "main");
        runner.Enqueue(0, "base123");
        runner.Enqueue(0, "");
        runner.Enqueue(0, "base123");
        runner.Enqueue(0, "result456");
        runner.Enqueue(0, "result456");
        runner.Enqueue(1, "");
        runner.Enqueue(0, "");
        runner.Enqueue(0, resultBranch);
        runner.Enqueue(0, "result456");
        runner.Enqueue(0, "");

        try
        {
            var result = await new GitWorktreeManager(runner)
                .SwitchTargetToRemoteResultAsync(
                    root,
                    "result-ref",
                    resultBranch,
                    "main");

            Assert.True(result.Success);
            Assert.True(result.Switched);
            Assert.Equal("main", result.PreviousBranch);
            Assert.Equal("base123", result.PreviousHead);
            Assert.Equal(resultBranch, result.CurrentBranch);
            Assert.Equal("result456", result.CurrentHead);
            Assert.Contains(
                runner.Calls,
                call => call.Arguments.SequenceEqual(
                    new[]
                    {
                        "switch",
                        "-c",
                        resultBranch,
                        "--track",
                        "origin/" + resultBranch
                    }));
            Assert.DoesNotContain(
                runner.Calls.SelectMany(call => call.Arguments),
                argument => argument is "merge" or "push" or "reset" or "--force" or "-f");
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public async Task TargetCheckoutRetryAcceptsAlreadySwitchedRemoteResultBranch()
    {
        var root = CreateTempRepositoryDirectory();
        var resultBranch = GitWorktreeManager.BuildBranchName("job", "W1");
        var runner = new FakeGitRunner();
        runner.Enqueue(0, root);
        runner.Enqueue(0, "");
        runner.Enqueue(0, resultBranch);
        runner.Enqueue(0, "result456");
        runner.Enqueue(0, "");
        runner.Enqueue(0, "result456");
        runner.Enqueue(0, "result456");
        runner.Enqueue(0, "result456");

        try
        {
            var result = await new GitWorktreeManager(runner)
                .SwitchTargetToRemoteResultAsync(
                    root,
                    "result-ref",
                    resultBranch,
                    "main");

            Assert.True(result.Success);
            Assert.False(result.Switched);
            Assert.Equal(resultBranch, result.CurrentBranch);
            Assert.Equal("result456", result.CurrentHead);
            Assert.DoesNotContain(
                runner.Calls,
                call => call.Arguments.Count > 0 &&
                        call.Arguments[0] == "switch");
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public async Task TargetCheckoutRejectsCurrentBranchRemoteDrift()
    {
        var root = CreateTempRepositoryDirectory();
        var resultBranch = GitWorktreeManager.BuildBranchName("job", "W1");
        var runner = new FakeGitRunner();
        runner.Enqueue(0, root);
        runner.Enqueue(0, "");
        runner.Enqueue(0, "main");
        runner.Enqueue(0, "local123");
        runner.Enqueue(0, "");
        runner.Enqueue(0, "remote456");

        try
        {
            var result = await new GitWorktreeManager(runner)
                .SwitchTargetToRemoteResultAsync(
                    root,
                    "result-ref",
                    resultBranch,
                    "main");

            Assert.False(result.Success);
            Assert.Equal("TARGET_CHECKOUT_CURRENT_REMOTE_CHANGED", result.ErrorCode);
            Assert.DoesNotContain(
                runner.Calls,
                call => call.Arguments.Count > 0 &&
                        call.Arguments[0] == "switch");
        }
        finally
        {
            Cleanup(root);
        }
    }

    private static string CreateTempRepositoryDirectory()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "projecthub-remote-git-test-" + Guid.NewGuid().ToString("N"),
            "repo");
        Directory.CreateDirectory(root);
        return root;
    }

    private static void Cleanup(string repositoryRoot)
    {
        var runtime = WorkerPaths.GetRepositoryRuntimePaths(repositoryRoot).Root;
        var parent = Directory.GetParent(repositoryRoot)?.FullName;

        if (!string.IsNullOrWhiteSpace(parent) && Directory.Exists(parent))
            Directory.Delete(parent, true);
        if (Directory.Exists(runtime))
            Directory.Delete(runtime, true);
    }

    private static bool IsWithin(string candidate, string root)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var fullCandidate = Path.GetFullPath(candidate)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(fullCandidate, fullRoot, comparison) ||
               fullCandidate.StartsWith(
                   fullRoot + Path.DirectorySeparatorChar,
                   comparison);
    }

    private sealed class ConcurrentFetchGitRunner : IGitWorktreeCommandRunner
    {
        private readonly string _root;
        private readonly object _sync = new();
        private int _activeFetches;

        public ConcurrentFetchGitRunner(string root)
        {
            _root = root;
        }

        public int MaxConcurrentFetches { get; private set; }
        public int FetchCount { get; private set; }

        public async Task<GitCommandResult> RunAsync(
            string workingDirectory,
            IReadOnlyList<string> arguments,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            if (arguments.SequenceEqual(new[] { "rev-parse", "--show-toplevel" }))
                return new(0, _root, string.Empty);

            if (arguments.SequenceEqual(new[] { "fetch", "--prune", "origin" }))
            {
                lock (_sync)
                {
                    _activeFetches++;
                    FetchCount++;
                    MaxConcurrentFetches = Math.Max(
                        MaxConcurrentFetches,
                        _activeFetches);
                }

                try
                {
                    await Task.Delay(80, cancellationToken);
                    return new(0, string.Empty, string.Empty);
                }
                finally
                {
                    lock (_sync)
                        _activeFetches--;
                }
            }

            if (arguments.Count == 3 &&
                arguments[0] == "rev-parse" &&
                arguments[1] == "--verify" &&
                arguments[2].EndsWith("^{commit}", StringComparison.Ordinal))
            {
                return new(
                    0,
                    arguments[2][..^9] + "-commit",
                    string.Empty);
            }

            throw new InvalidOperationException(
                "예상하지 않은 Git 호출입니다: " +
                string.Join(" ", arguments));
        }
    }

    private sealed class FakeGitRunner : IGitWorktreeCommandRunner
    {
        private readonly Queue<GitCommandResult> _results = new();

        public List<GitCall> Calls { get; } = new();

        public void Enqueue(int exitCode, string stdout, string stderr = "")
            => _results.Enqueue(new GitCommandResult(exitCode, stdout, stderr));

        public Task<GitCommandResult> RunAsync(
            string workingDirectory,
            IReadOnlyList<string> arguments,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            Calls.Add(new GitCall(workingDirectory, arguments.ToArray()));

            if (arguments.Count > 0 &&
                arguments[0] == "clone" &&
                arguments.Count >= 2)
            {
                var clonePath = arguments[^1];
                Directory.CreateDirectory(clonePath);
                Directory.CreateDirectory(Path.Combine(clonePath, ".git"));
            }

            if (_results.Count == 0)
                throw new InvalidOperationException(
                    "예상하지 않은 Git 호출입니다: " + string.Join(" ", arguments));

            return Task.FromResult(_results.Dequeue());
        }
    }

    private sealed record GitCall(
        string WorkingDirectory,
        IReadOnlyList<string> Arguments);
}
