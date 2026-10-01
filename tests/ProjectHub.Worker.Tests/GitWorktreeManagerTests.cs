using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class GitWorktreeManagerTests
{
    [Fact]
    public void WorktreePathIsOutsideRepositoryAndBranchIsStable()
    {
        var parent = Path.Combine(Path.GetTempPath(), "projecthub-worktree-path-" + Guid.NewGuid().ToString("N"));
        var repository = Path.Combine(parent, "repo");
        Directory.CreateDirectory(repository);

        try
        {
            var path = GitWorktreeManager.BuildWorktreePath(repository, "job-1", "W1");
            var branchA = GitWorktreeManager.BuildBranchName("job-1", "W1");
            var branchB = GitWorktreeManager.BuildBranchName("job-1", "W1");

            Assert.StartsWith(
                WorkerPaths.GetRepositoryRuntimePaths(repository).Worktrees + Path.DirectorySeparatorChar,
                path,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
            Assert.True(
                Path.GetRelativePath(parent, path).Length < 100,
                "Worktree 상대 경로는 Windows 도구 호환성을 위해 짧게 유지해야 합니다.");
            Assert.True(
                Path.GetFullPath(path).StartsWith(
                    Path.GetFullPath(repository) + Path.DirectorySeparatorChar,
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));
            Assert.Equal(branchA, branchB);
            Assert.StartsWith("projecthub/", branchA, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(parent, true);
        }
    }

    [Fact]
    public void IntegrationClonePathIsOutsideRepositoryAndSeparateFromLinkedWorktrees()
    {
        var parent = Path.Combine(Path.GetTempPath(), "projecthub-integration-clone-path-" + Guid.NewGuid().ToString("N"));
        var repository = Path.Combine(parent, "repo");
        Directory.CreateDirectory(repository);

        try
        {
            var clone = GitWorktreeManager.BuildIntegrationClonePath(repository, "job-1", "I1");
            var worktree = GitWorktreeManager.BuildWorktreePath(repository, "job-1", "I1");

            Assert.StartsWith(
                WorkerPaths.GetRepositoryRuntimePaths(repository).IntegrationClones + Path.DirectorySeparatorChar,
                clone,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
            Assert.NotEqual(
                Path.GetFullPath(worktree),
                Path.GetFullPath(clone));
            Assert.True(
                Path.GetFullPath(clone).StartsWith(
                    Path.GetFullPath(repository) + Path.DirectorySeparatorChar,
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(parent, true);
        }
    }

    [Fact]
    public async Task ResolveNormalBaseRefUsesNewestLinearCodeDependency()
    {
        var root = CreateTempRepositoryDirectory();
        var runner = new FakeGitRunner(root);
        runner.Enqueue(0, root);
        runner.Enqueue(0, "base123");
        runner.Enqueue(0, "dep456");
        runner.Enqueue(0, "");
        runner.Enqueue(0, "dep789");
        runner.Enqueue(0, "");

        try
        {
            var manager = new GitWorktreeManager(runner);
            var result = await manager.ResolveNormalBaseRefAsync(
                root,
                "main",
                new[] { "dep-one", "dep-two" });

            Assert.True(result.Success);
            Assert.Equal("dep789", result.EffectiveBaseRef);
            Assert.Contains(
                runner.Calls,
                call => call.Arguments.SequenceEqual(
                    new[] { "merge-base", "--is-ancestor", "base123", "dep456" }));
            Assert.Contains(
                runner.Calls,
                call => call.Arguments.SequenceEqual(
                    new[] { "merge-base", "--is-ancestor", "dep456", "dep789" }));
        }
        finally
        {
            DeleteTempTree(root);
        }
    }

    [Fact]
    public async Task ResolveNormalBaseRefRequiresIntegrationForDivergentCodeDependencies()
    {
        var root = CreateTempRepositoryDirectory();
        var runner = new FakeGitRunner(root);
        runner.Enqueue(0, root);
        runner.Enqueue(0, "base123");
        runner.Enqueue(0, "left456");
        runner.Enqueue(0, "");
        runner.Enqueue(0, "right789");
        runner.Enqueue(1, "");
        runner.Enqueue(1, "");

        try
        {
            var manager = new GitWorktreeManager(runner);
            var result = await manager.ResolveNormalBaseRefAsync(
                root,
                "main",
                new[] { "left-ref", "right-ref" });

            Assert.False(result.Success);
            Assert.Equal(
                "NORMAL_MULTIPLE_CODE_BASES_REQUIRE_INTEGRATION",
                result.ErrorCode);
            Assert.Contains("left456", result.ErrorDetail ?? string.Empty);
            Assert.Contains("right789", result.ErrorDetail ?? string.Empty);
        }
        finally
        {
            DeleteTempTree(root);
        }
    }

    [Fact]
    public async Task PrepareCreatesBranchAndWorktreeFromResolvedCommit()
    {
        var root = CreateTempRepositoryDirectory();
        var runner = new FakeGitRunner(root);
        runner.Enqueue(0, root);
        runner.Enqueue(0, "abc123");
        runner.Enqueue(0, "");
        runner.Enqueue(1, "");
        runner.Enqueue(0, "Preparing worktree");
        runner.Enqueue(0, "abc123");

        try
        {
            var manager = new GitWorktreeManager(runner);
            var result = await manager.PrepareAsync(root, "job", "W1", "main");

            Assert.True(result.Success);
            Assert.False(result.Reused);
            Assert.Equal("abc123", result.BaseCommit);
            Assert.Equal("abc123", result.HeadCommit);
            Assert.Equal(GitWorktreeManager.BuildBranchName("job", "W1"), result.Branch);

            var add = runner.Calls.Single(call => call.Arguments.Count > 1 && call.Arguments[0] == "worktree" && call.Arguments[1] == "add");
            Assert.Contains("-b", add.Arguments);
            Assert.Contains(result.Branch, add.Arguments);
            Assert.Contains(result.WorktreePath, add.Arguments);
            Assert.Equal("abc123", add.Arguments[^1]);
        }
        finally
        {
            DeleteTempTree(root);
        }
    }

    [Fact]
    public async Task PrepareReusesOnlyRegisteredWorktreeWithExpectedBranch()
    {
        var root = CreateTempRepositoryDirectory();
        var expectedPath = GitWorktreeManager.BuildWorktreePath(root, "job", "W1");
        var expectedBranch = GitWorktreeManager.BuildBranchName("job", "W1");
        Directory.CreateDirectory(expectedPath);
        var runner = new FakeGitRunner(root);
        runner.Enqueue(0, root);
        runner.Enqueue(0, "base123");
        runner.Enqueue(0, $"worktree {expectedPath}\nHEAD head999\nbranch refs/heads/{expectedBranch}\n");

        try
        {
            var manager = new GitWorktreeManager(runner);
            var result = await manager.PrepareAsync(root, "job", "W1", "main");

            Assert.True(result.Success);
            Assert.True(result.Reused);
            Assert.Equal("head999", result.HeadCommit);
            Assert.DoesNotContain(runner.Calls, call => call.Arguments.Count > 1 && call.Arguments[0] == "worktree" && call.Arguments[1] == "add");
        }
        finally
        {
            DeleteTempTree(root);
        }
    }

    [Fact]
    public async Task PrepareRejectsUnexpectedExistingBranchInsteadOfReusingIt()
    {
        var root = CreateTempRepositoryDirectory();
        var runner = new FakeGitRunner(root);
        runner.Enqueue(0, root);
        runner.Enqueue(0, "abc123");
        runner.Enqueue(0, "");
        runner.Enqueue(0, "");
        runner.Enqueue(0, "different456");

        try
        {
            var manager = new GitWorktreeManager(runner);
            var result = await manager.PrepareAsync(root, "job", "W1", "main");

            Assert.False(result.Success);
            Assert.Equal("WORKTREE_BRANCH_EXISTS", result.ErrorCode);
            Assert.DoesNotContain(runner.Calls, call => call.Arguments.Count > 1 && call.Arguments[0] == "worktree" && call.Arguments[1] == "add");
        }
        finally
        {
            DeleteTempTree(root);
        }
    }


    [Fact]
    public async Task PrepareReusesDetachedResidueBranchWhenItMatchesBase()
    {
        var root = CreateTempRepositoryDirectory();
        var runner = new FakeGitRunner(root);
        runner.Enqueue(0, root);
        runner.Enqueue(0, "abc123");
        runner.Enqueue(0, "");
        runner.Enqueue(0, "");
        runner.Enqueue(0, "abc123");
        runner.Enqueue(0, "Preparing worktree");
        runner.Enqueue(0, "abc123");

        try
        {
            var manager = new GitWorktreeManager(runner);
            var result = await manager.PrepareAsync(root, "job", "W1", "main");

            Assert.True(result.Success);
            var add = runner.Calls.Single(call =>
                call.Arguments.Count > 1 &&
                call.Arguments[0] == "worktree" &&
                call.Arguments[1] == "add");
            Assert.DoesNotContain("-b", add.Arguments);
            Assert.Equal(result.WorktreePath, add.Arguments[2]);
            Assert.Equal(result.Branch, add.Arguments[3]);
        }
        finally
        {
            DeleteTempTree(root);
        }
    }

    [Fact]
    public async Task PrepareSerializesRepositoryMutationAcrossManagers()
    {
        var root = CreateTempRepositoryDirectory();
        var runner = new ConcurrentPrepareGitRunner(root);

        try
        {
            var first = new GitWorktreeManager(runner);
            var second = new GitWorktreeManager(runner);

            var results = await Task.WhenAll(
                first.PrepareAsync(root, "job", "W1", "main"),
                second.PrepareAsync(root, "job", "W2", "main"));

            Assert.All(results, result => Assert.True(result.Success));
            Assert.Equal(1, runner.MaxConcurrentAdds);
        }
        finally
        {
            DeleteTempTree(root);
        }
    }

    [Fact]
    public async Task PreparePreservesGitWorktreeAddFailureDetail()
    {
        var root = CreateTempRepositoryDirectory();
        var runner = new FakeGitRunner(root);
        runner.Enqueue(0, root);
        runner.Enqueue(0, "abc123");
        runner.Enqueue(0, "");
        runner.Enqueue(1, "");
        runner.Enqueue(128, "", "fatal: simulated worktree lock failure");

        try
        {
            var result = await new GitWorktreeManager(runner)
                .PrepareAsync(root, "job", "W1", "main");

            Assert.False(result.Success);
            Assert.Equal("WORKTREE_CREATE_FAILED", result.ErrorCode);
            Assert.Contains("exitCode=128", result.ErrorDetail ?? string.Empty);
            Assert.Contains("fatal: simulated worktree lock failure", result.ErrorDetail ?? string.Empty);
        }
        finally
        {
            DeleteTempTree(root);
        }
    }

    [Fact]
    public async Task CheckpointCommitsDirtyWorktreeWithoutPushOrForce()
    {
        var root = CreateTempRepositoryDirectory();
        var worktree = Path.Combine(Directory.GetParent(root)!.FullName, "worktree");
        Directory.CreateDirectory(worktree);
        var runner = new FakeGitRunner(root);

        runner.Enqueue(0, "base123");
        runner.Enqueue(0, "projecthub/job/W1");
        runner.Enqueue(0, " M changed.cs");
        runner.Enqueue(0, " M changed.cs");
        runner.Enqueue(0, "");
        runner.Enqueue(0, "[projecthub/job/W1 new456] checkpoint");
        runner.Enqueue(0, "new456");
        runner.Enqueue(0, "projecthub/job/W1");
        runner.Enqueue(0, "");

        try
        {
            var manager = new GitWorktreeManager(runner);
            var result = await manager.CreateCheckpointAsync(worktree, "W1");

            Assert.True(result.Success);
            Assert.True(result.CreatedCommit);
            Assert.Equal("new456", result.HeadCommit);
            Assert.Contains(runner.Calls, call =>
                call.Arguments.Count > 2 &&
                call.Arguments[0] == "add" &&
                call.Arguments[1] == "--all" &&
                call.Arguments.Contains(":(exclude,glob)**/bin/**"));
            Assert.Contains(runner.Calls, call => call.Arguments.Contains("commit"));
            Assert.DoesNotContain(runner.Calls.SelectMany(call => call.Arguments), argument => argument == "push");
            Assert.DoesNotContain(runner.Calls.SelectMany(call => call.Arguments), argument => argument == "--force" || argument == "-f");
        }
        finally
        {
            DeleteTempTree(root);
        }
    }

    [Fact]
    public async Task CheckpointReusesCleanHeadWithoutCreatingCommit()
    {
        var root = CreateTempRepositoryDirectory();
        var worktree = Path.Combine(Directory.GetParent(root)!.FullName, "worktree");
        Directory.CreateDirectory(worktree);
        var runner = new FakeGitRunner(root);

        runner.Enqueue(0, "head123");
        runner.Enqueue(0, "projecthub/job/W1");
        runner.Enqueue(0, "");

        try
        {
            var manager = new GitWorktreeManager(runner);
            var result = await manager.CreateCheckpointAsync(worktree, "W1");

            Assert.True(result.Success);
            Assert.False(result.CreatedCommit);
            Assert.Equal("head123", result.HeadCommit);
            Assert.DoesNotContain(runner.Calls, call => call.Arguments.Contains("commit"));
            Assert.DoesNotContain(runner.Calls, call => call.Arguments.SequenceEqual(new[] { "add", "--all" }));
        }
        finally
        {
            DeleteTempTree(root);
        }
    }

    [Fact]
    public async Task RemoveRefusesDirtyWorktreeAndNeverUsesForce()
    {
        var root = CreateTempRepositoryDirectory();
        var worktree = Path.Combine(Path.GetTempPath(), "projecthub-worktree-dirty-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(worktree);
        var runner = new FakeGitRunner(root);
        runner.Enqueue(0, "head123");
        runner.Enqueue(0, "projecthub/job/W1");
        runner.Enqueue(0, " M changed.cs");

        try
        {
            var manager = new GitWorktreeManager(runner);
            var result = await manager.RemoveAsync(root, worktree, "projecthub/job/W1");

            Assert.False(result.Success);
            Assert.Equal("WORKTREE_DIRTY", result.ErrorCode);
            Assert.DoesNotContain(runner.Calls, call => call.Arguments.Contains("remove"));
            Assert.DoesNotContain(runner.Calls.SelectMany(call => call.Arguments), argument => argument == "--force" || argument == "-f");
        }
        finally
        {
            Directory.Delete(worktree, true);
            DeleteTempTree(root);
        }
    }

    [Fact]
    public async Task RuntimeResetForceRemovesOwnedWorktreeAndLegacySibling()
    {
        var root = CreateTempRepositoryDirectory();
        var runtime = WorkerPaths.GetRepositoryRuntimePaths(root);
        var legacyRuntime = WorkerPaths.GetLegacyRepositoryRuntimeRoot(root);
        var ownedWorktree = GitWorktreeManager.BuildWorktreePath(root, "job", "W1");

        Directory.CreateDirectory(runtime.NuGetPackages);
        File.WriteAllText(Path.Combine(runtime.NuGetPackages, "cache.txt"), "cache");
        Directory.CreateDirectory(legacyRuntime);
        File.WriteAllText(Path.Combine(legacyRuntime, "legacy.txt"), "legacy");

        var runner = new FakeGitRunner(root);
        runner.Enqueue(0, root);
        runner.Enqueue(
            0,
            $"worktree {root}\nHEAD main123\nbranch refs/heads/main\n\n" +
            $"worktree {ownedWorktree}\nHEAD work123\nbranch refs/heads/projecthub/job/W1\n");
        runner.Enqueue(0, "removed");
        runner.Enqueue(0, "pruned");

        try
        {
            var manager = new GitWorktreeManager(runner);
            var result = await manager.ResetRepositoryRuntimeAsync(root);

            Assert.True(result.Success, result.ErrorDetail);
            Assert.True(result.RuntimeDeleted);
            Assert.Contains(Path.GetFullPath(ownedWorktree), result.RemovedWorktrees);
            Assert.False(Directory.Exists(runtime.Root));
            Assert.False(Directory.Exists(legacyRuntime));
            Assert.Contains(
                runner.Calls,
                call => call.Arguments.SequenceEqual(
                    new[]
                    {
                        "worktree",
                        "remove",
                        "--force",
                        Path.GetFullPath(ownedWorktree)
                    }));
            Assert.Contains(
                runner.Calls,
                call => call.Arguments.SequenceEqual(
                    new[] { "worktree", "prune", "--expire", "now" }));
        }
        finally
        {
            DeleteTempTree(root);
            if (Directory.Exists(legacyRuntime))
                Directory.Delete(legacyRuntime, true);
        }
    }

    [Fact]
    public async Task RuntimeCompactKeepsDirtyWorktreeAndRemovesToolCaches()
    {
        var root = CreateTempRepositoryDirectory();
        var runtime = WorkerPaths.GetRepositoryRuntimePaths(root);
        var ownedWorktree = GitWorktreeManager.BuildWorktreePath(root, "job", "W1");
        Directory.CreateDirectory(ownedWorktree);
        Directory.CreateDirectory(runtime.NuGetPackages);
        Directory.CreateDirectory(runtime.DotNetHome);
        File.WriteAllText(Path.Combine(runtime.NuGetPackages, "cache.txt"), "cache");
        File.WriteAllText(Path.Combine(runtime.DotNetHome, "state.txt"), "dotnet");

        var listing =
            $"worktree {root}\nHEAD main123\nbranch refs/heads/main\n\n" +
            $"worktree {ownedWorktree}\nHEAD work123\nbranch refs/heads/projecthub/job/W1\n";
        var runner = new FakeGitRunner(root);
        runner.Enqueue(0, root);
        runner.Enqueue(0, listing);
        runner.Enqueue(0, "work123");
        runner.Enqueue(0, "projecthub/job/W1");
        runner.Enqueue(0, " M changed.cs");
        runner.Enqueue(0, "pruned");

        try
        {
            var manager = new GitWorktreeManager(runner);
            var result = await manager.CompactRepositoryRuntimeAsync(root);

            Assert.True(result.Success, result.ErrorDetail);
            Assert.True(Directory.Exists(ownedWorktree));
            Assert.False(Directory.Exists(runtime.NuGetRoot));
            Assert.False(Directory.Exists(runtime.DotNetHome));
            Assert.DoesNotContain(
                runner.Calls,
                call => call.Arguments.Count > 1 &&
                        call.Arguments[0] == "worktree" &&
                        call.Arguments[1] == "remove");
        }
        finally
        {
            DeleteTempTree(root);
        }
    }

    [Fact]
    public async Task RuntimeCleanupRemovesOwnedWorktreesPrunesAndDeletesRuntimeRoot()
    {
        var root = CreateTempRepositoryDirectory();
        var runtime = WorkerPaths.GetRepositoryRuntimePaths(root);
        var ownedWorktree = GitWorktreeManager.BuildWorktreePath(root, "job", "W1");
        var unrelatedWorktree = Path.Combine(
            Directory.GetParent(root)!.FullName,
            "manual-worktree");
        Directory.CreateDirectory(ownedWorktree);
        Directory.CreateDirectory(unrelatedWorktree);
        Directory.CreateDirectory(runtime.NuGetPackages);
        File.WriteAllText(Path.Combine(runtime.NuGetPackages, "cache.txt"), "cache");

        var runner = new FakeGitRunner(root);
        runner.Enqueue(0, root);
        runner.Enqueue(
            0,
            $"worktree {root}\nHEAD main123\nbranch refs/heads/main\n\n" +
            $"worktree {ownedWorktree}\nHEAD work123\nbranch refs/heads/projecthub/job/W1\n\n" +
            $"worktree {unrelatedWorktree}\nHEAD other123\nbranch refs/heads/feature/manual\n");
        runner.Enqueue(0, "work123");
        runner.Enqueue(0, "projecthub/job/W1");
        runner.Enqueue(0, "");
        runner.Enqueue(0, "removed");
        runner.Enqueue(0, "pruned");
        runner.Enqueue(
            0,
            $"worktree {root}\nHEAD main123\nbranch refs/heads/main\n\n" +
            $"worktree {unrelatedWorktree}\nHEAD other123\nbranch refs/heads/feature/manual\n");

        try
        {
            var manager = new GitWorktreeManager(runner);
            var result = await manager.CleanupRepositoryRuntimeAsync(root);

            Assert.True(result.Success);
            Assert.True(result.RuntimeDeleted);
            Assert.Equal(runtime.Root, result.RuntimeRoot);
            Assert.Contains(Path.GetFullPath(ownedWorktree), result.RemovedWorktrees);
            Assert.False(Directory.Exists(runtime.Root));

            Assert.Contains(
                runner.Calls,
                call => call.Arguments.SequenceEqual(
                    new[] { "worktree", "remove", Path.GetFullPath(ownedWorktree) }));
            Assert.Contains(
                runner.Calls,
                call => call.Arguments.SequenceEqual(
                    new[] { "worktree", "prune", "--expire", "now" }));
            Assert.DoesNotContain(
                runner.Calls,
                call => call.Arguments.Count > 2 &&
                        call.Arguments[0] == "worktree" &&
                        call.Arguments[1] == "remove" &&
                        string.Equals(
                            Path.GetFullPath(call.Arguments[2]),
                            Path.GetFullPath(unrelatedWorktree),
                            OperatingSystem.IsWindows()
                                ? StringComparison.OrdinalIgnoreCase
                                : StringComparison.Ordinal));
            Assert.DoesNotContain(
                runner.Calls.SelectMany(call => call.Arguments),
                argument => argument is "--force" or "-f");
        }
        finally
        {
            DeleteTempTree(root);
        }
    }

    [Fact]
    public async Task RuntimeCleanupDeletesRuntimeRootWhenNoOwnedWorktreesRemain()
    {
        var root = CreateTempRepositoryDirectory();
        var runtime = WorkerPaths.GetRepositoryRuntimePaths(root);
        Directory.CreateDirectory(runtime.Root);

        var runner = new FakeGitRunner(root);
        runner.Enqueue(0, root);
        runner.Enqueue(
            0,
            $"worktree {root}\nHEAD main123\nbranch refs/heads/main\n");
        runner.Enqueue(0, "pruned");
        runner.Enqueue(
            0,
            $"worktree {root}\nHEAD main123\nbranch refs/heads/main\n");

        try
        {
            var manager = new GitWorktreeManager(runner);
            var result = await manager.CleanupRepositoryRuntimeAsync(root);

            Assert.True(result.Success);
            Assert.True(result.RuntimeDeleted);
            Assert.Empty(result.RemovedWorktrees);
            Assert.False(Directory.Exists(runtime.Root));
            Assert.Contains(
                runner.Calls,
                call => call.Arguments.SequenceEqual(
                    new[] { "worktree", "prune", "--expire", "now" }));
        }
        finally
        {
            DeleteTempTree(root);
        }
    }

    [Fact]
    public async Task RuntimeCleanupPreservesDirtyWorktreeButRemovesDisposableRuntimeData()
    {
        var root = CreateTempRepositoryDirectory();
        var runtime = WorkerPaths.GetRepositoryRuntimePaths(root);
        var ownedWorktree = GitWorktreeManager.BuildWorktreePath(root, "job", "W1");
        Directory.CreateDirectory(ownedWorktree);
        Directory.CreateDirectory(runtime.IntegrationClones);
        Directory.CreateDirectory(runtime.NuGetPackages);
        Directory.CreateDirectory(runtime.TempRoot);
        File.WriteAllText(Path.Combine(runtime.IntegrationClones, "clone.tmp"), "clone");
        File.WriteAllText(Path.Combine(runtime.NuGetPackages, "cache.tmp"), "cache");
        File.WriteAllText(Path.Combine(runtime.TempRoot, "work.tmp"), "temp");

        var listing =
            $"worktree {root}\nHEAD main123\nbranch refs/heads/main\n\n" +
            $"worktree {ownedWorktree}\nHEAD work123\nbranch refs/heads/projecthub/job/W1\n";
        var runner = new FakeGitRunner(root);
        runner.Enqueue(0, root);
        runner.Enqueue(0, listing);
        runner.Enqueue(0, "work123");
        runner.Enqueue(0, "projecthub/job/W1");
        runner.Enqueue(0, " M changed.cs");
        runner.Enqueue(0, "pruned");
        runner.Enqueue(0, listing);

        try
        {
            var manager = new GitWorktreeManager(runner);
            var result = await manager.CleanupRepositoryRuntimeAsync(root);

            Assert.False(result.Success);
            Assert.Equal("RUNTIME_CLEANUP_WORKTREE_DIRTY", result.ErrorCode);
            Assert.True(Directory.Exists(runtime.Root));
            Assert.True(Directory.Exists(ownedWorktree));
            Assert.False(Directory.Exists(runtime.IntegrationClones));
            Assert.False(Directory.Exists(runtime.NuGetRoot));
            Assert.False(Directory.Exists(runtime.TempRoot));
            Assert.Contains(ownedWorktree, result.ErrorDetail ?? string.Empty);
            Assert.DoesNotContain(
                runner.Calls,
                call => call.Arguments.Count > 1 &&
                        call.Arguments[0] == "worktree" &&
                        call.Arguments[1] == "remove");
            Assert.Contains(
                runner.Calls,
                call => call.Arguments.SequenceEqual(
                    new[] { "worktree", "prune", "--expire", "now" }));
        }
        finally
        {
            DeleteTempTree(root);
        }
    }

    [Fact]
    public async Task RuntimeCleanupClearsReadOnlyFilesBeforeDeletingRuntimeRoot()
    {
        var root = CreateTempRepositoryDirectory();
        var runtime = WorkerPaths.GetRepositoryRuntimePaths(root);
        var cloneDirectory = Path.Combine(runtime.IntegrationClones, "clone");
        Directory.CreateDirectory(cloneDirectory);
        var readOnlyFile = Path.Combine(cloneDirectory, "readonly.pack");
        File.WriteAllText(readOnlyFile, "data");
        File.SetAttributes(
            readOnlyFile,
            File.GetAttributes(readOnlyFile) | FileAttributes.ReadOnly);

        var runner = new FakeGitRunner(root);
        runner.Enqueue(0, root);
        runner.Enqueue(
            0,
            $"worktree {root}\nHEAD main123\nbranch refs/heads/main\n");
        runner.Enqueue(0, "pruned");
        runner.Enqueue(
            0,
            $"worktree {root}\nHEAD main123\nbranch refs/heads/main\n");

        try
        {
            var manager = new GitWorktreeManager(runner);
            var result = await manager.CleanupRepositoryRuntimeAsync(root);

            Assert.True(result.Success);
            Assert.True(result.RuntimeDeleted);
            Assert.False(Directory.Exists(runtime.Root));
        }
        finally
        {
            if (File.Exists(readOnlyFile))
                File.SetAttributes(readOnlyFile, FileAttributes.Normal);
            DeleteTempTree(root);
        }
    }

    [Fact]
    public async Task IntegrationPreparationCreatesIndependentCloneFromCurrentPrimaryHead()
    {
        var root = CreateTempRepositoryDirectory();
        var clonePath = GitWorktreeManager.BuildIntegrationClonePath(root, "job", "I1");
        var branch = GitWorktreeManager.BuildBranchName("job", "I1");
        var runner = new FakeGitRunner(root);
        runner.Enqueue(0, root);
        runner.Enqueue(0, "main");
        runner.Enqueue(0, "primary999");
        runner.Enqueue(0, "Cloning");
        runner.Enqueue(0, "Switched");
        runner.Enqueue(0, "");
        runner.Enqueue(0, "");
        runner.Enqueue(0, Path.Combine(clonePath, ".git"));
        runner.Enqueue(0, "primary999");

        try
        {
            var manager = new GitWorktreeManager(runner);
            var result = await manager.PrepareIntegrationAsync(
                root,
                "job",
                "I1",
                "main");

            Assert.True(result.Success);
            Assert.Equal("primary999", result.BaseRef);
            Assert.Equal("primary999", result.BaseCommit);
            Assert.Equal(clonePath, result.WorktreePath);
            Assert.Equal(branch, result.Branch);

            var clone = runner.Calls.Single(call =>
                call.Arguments.Count > 0 &&
                call.Arguments[0] == "clone");
            Assert.Equal(
                new[] { "clone", "--no-hardlinks", "--no-checkout", root, clonePath },
                clone.Arguments);

            var checkout = runner.Calls.Single(call =>
                call.Arguments.Count > 0 &&
                call.Arguments[0] == "checkout");
            Assert.Equal(
                new[] { "checkout", "-b", branch, "primary999" },
                checkout.Arguments);

            Assert.Contains(
                runner.Calls,
                call => call.Arguments.SequenceEqual(
                    new[] { "config", "user.name", "ProjectHub" }));
            Assert.Contains(
                runner.Calls,
                call => call.Arguments.SequenceEqual(
                    new[] { "config", "user.email", "projecthub@local" }));
            Assert.DoesNotContain(
                runner.Calls,
                call => call.Arguments.Count > 1 &&
                        call.Arguments[0] == "worktree" &&
                        call.Arguments[1] == "add");
            Assert.DoesNotContain(
                runner.Calls,
                call => call.Arguments.Count > 0 &&
                        call.Arguments[0] == "status");
        }
        finally
        {
            DeleteTempTree(root);
        }
    }

    [Fact]
    public async Task ResumeIntegrationRejectsLinkedWorktreeGitDirOutsideClone()
    {
        var root = CreateTempRepositoryDirectory();
        var clonePath = GitWorktreeManager.BuildIntegrationClonePath(root, "job", "I1");
        var branch = GitWorktreeManager.BuildBranchName("job", "I1");
        Directory.CreateDirectory(clonePath);
        var externalGitDir = Path.Combine(root, ".git", "worktrees", "I1");
        var runner = new FakeGitRunner(root);
        runner.Enqueue(0, root);
        runner.Enqueue(0, clonePath);
        runner.Enqueue(0, externalGitDir);

        try
        {
            var manager = new GitWorktreeManager(runner);
            var result = await manager.ResumeIntegrationAsync(
                root,
                clonePath,
                branch,
                "base123");

            Assert.False(result.Success);
            Assert.Equal("INTEGRATION_CLONE_GITDIR_NOT_LOCAL", result.ErrorCode);
        }
        finally
        {
            DeleteTempTree(root);
        }
    }

    [Fact]
    public async Task IntegrationCloneImportDoesNotInspectOrMergeDirtyTargetWorkspace()
    {
        var root = CreateTempRepositoryDirectory();
        var clonePath = GitWorktreeManager.BuildIntegrationClonePath(root, "job", "I1");
        var branch = GitWorktreeManager.BuildBranchName("job", "I1");
        Directory.CreateDirectory(Path.Combine(clonePath, ".git"));
        var runner = new FakeGitRunner(root);
        runner.Enqueue(0, root);
        runner.Enqueue(0, clonePath);
        runner.Enqueue(0, Path.Combine(clonePath, ".git"));
        runner.Enqueue(0, branch);
        runner.Enqueue(0, "integrated456");
        runner.Enqueue(0, "Imported");
        runner.Enqueue(0, "integrated456");

        try
        {
            var manager = new GitWorktreeManager(runner);
            var result = await manager.ImportIntegrationCloneAsync(
                root,
                clonePath,
                branch,
                "integrated456",
                "job",
                "I1");

            Assert.True(result.Success);
            Assert.Equal("integrated456", result.ImportedCommit);
            Assert.Equal(
                "refs/projecthub/integration-results/job/I1",
                result.ImportedRef);
            Assert.Contains(
                runner.Calls,
                call => call.Arguments.SequenceEqual(
                    new[]
                    {
                        "fetch",
                        "--no-tags",
                        "--no-write-fetch-head",
                        clonePath,
                        "+refs/heads/" + branch + ":refs/projecthub/integration-results/job/I1"
                    }));
            Assert.DoesNotContain(
                runner.Calls,
                call => call.Arguments.Count > 0 &&
                        (call.Arguments[0] == "status" ||
                         call.Arguments[0] == "merge"));
        }
        finally
        {
            DeleteTempTree(root);
        }
    }

    [Fact]
    public async Task IntegrationCloneLandingImportsCommitBeforeFastForward()
    {
        var root = CreateTempRepositoryDirectory();
        var clonePath = GitWorktreeManager.BuildIntegrationClonePath(root, "job", "I1");
        var branch = GitWorktreeManager.BuildBranchName("job", "I1");
        Directory.CreateDirectory(Path.Combine(clonePath, ".git"));
        var runner = new FakeGitRunner(root);
        runner.Enqueue(0, root);
        runner.Enqueue(0, "");
        runner.Enqueue(0, "main");
        runner.Enqueue(0, "base123");
        runner.Enqueue(0, clonePath);
        runner.Enqueue(0, Path.Combine(clonePath, ".git"));
        runner.Enqueue(0, branch);
        runner.Enqueue(0, "integrated456");
        runner.Enqueue(0, "Imported");
        runner.Enqueue(0, "integrated456");
        runner.Enqueue(0, "");
        runner.Enqueue(0, "Fast-forward");
        runner.Enqueue(0, "integrated456");
        runner.Enqueue(0, "");

        try
        {
            var manager = new GitWorktreeManager(runner);
            var result = await manager.LandIntegrationCloneAsync(
                root,
                clonePath,
                branch,
                "integrated456",
                "main");

            Assert.True(result.Success);
            Assert.True(result.FastForwarded);
            Assert.Equal("integrated456", result.AfterHead);

            var fetch = runner.Calls.Single(call =>
                call.Arguments.Count > 0 &&
                call.Arguments[0] == "fetch");
            Assert.Equal(
                new[]
                {
                    "fetch",
                    "--no-tags",
                    "--no-write-fetch-head",
                    clonePath,
                    "refs/heads/" + branch
                },
                fetch.Arguments);

            var fetchIndex = runner.Calls.IndexOf(fetch);
            var merge = runner.Calls.Single(call =>
                call.Arguments.Count > 0 &&
                call.Arguments[0] == "merge");
            Assert.True(fetchIndex < runner.Calls.IndexOf(merge));
            Assert.Equal(
                new[] { "merge", "--ff-only", "integrated456" },
                merge.Arguments);
        }
        finally
        {
            DeleteTempTree(root);
        }
    }

    [Fact]
    public async Task IntegrationLandingSerializesPrimaryMutationAcrossManagers()
    {
        var root = CreateTempRepositoryDirectory();
        var runner = new ConcurrentLandingGitRunner(root);

        try
        {
            var first = new GitWorktreeManager(runner);
            var second = new GitWorktreeManager(runner);

            var results = await Task.WhenAll(
                first.LandIntegrationAsync(root, "integrated-a", "main"),
                second.LandIntegrationAsync(root, "integrated-b", "main"));

            Assert.All(results, result => Assert.True(result.Success));
            Assert.Equal(1, runner.MaxConcurrentMerges);
        }
        finally
        {
            DeleteTempTree(root);
        }
    }

    [Fact]
    public async Task TargetContainmentReportsWhetherResultIsAlreadyInHead()
    {
        var root = CreateTempRepositoryDirectory();
        var runner = new FakeGitRunner(root);
        runner.Enqueue(0, root);
        runner.Enqueue(0, "main");
        runner.Enqueue(0, "result456");
        runner.Enqueue(0, "head789");
        runner.Enqueue(0, "");

        try
        {
            var manager = new GitWorktreeManager(runner);
            var result = await manager.InspectTargetContainmentAsync(
                root,
                "result-ref",
                "main");

            Assert.True(result.Success);
            Assert.True(result.IsContained);
            Assert.Equal("result456", result.ResultCommit);
            Assert.Equal("head789", result.TargetHead);
        }
        finally
        {
            DeleteTempTree(root);
        }
    }

    [Fact]
    public async Task TargetWorkspaceFinalizerLeavesIntegratedDependenciesAlone()
    {
        var root = CreateTempRepositoryDirectory();
        try
        {
            var graph = new WorkGraph("job", 1);
            Assert.True(graph.ApplyPatch(new WorkGraphPatch(0, new[]
            {
                WorkGraphPatchOperation.Add(new WorkItemSpec("W10", "기능 구현", Kind: WorkItemKind.Normal, BaseRef: "base123")),
                WorkGraphPatchOperation.Add(new WorkItemSpec("I10", "통합", new[] { "W10" }, WorkItemKind.Integration, "base123"))
            })).Success);
            Assert.True(graph.TryMarkRunning("W10"));
            Assert.True(graph.TryMarkCompleted("W10", "normal-ref", "normal", WorkItemResultType.CodeChange));
            Assert.True(graph.TryMarkRunning("I10"));
            Assert.True(graph.TryMarkCompleted("I10", "integration-ref", "integration", WorkItemResultType.CodeChange));
            WriteVerifiedMaterializationLedger(root, "job", "integration-ref");

            var runner = new FakeGitRunner(root);
            var finalizer = new TargetWorkspaceFinalizer(
                root,
                "main",
                new GitWorktreeManager(runner));

            var result = await finalizer.FinalizeAsync(graph.Snapshot());

            Assert.True(result.Success);
            Assert.Empty(runner.Calls);
        }
        finally
        {
            DeleteTempTree(root);
        }
    }

    [Fact]
    public async Task TargetWorkspaceFinalizerFastForwardsSingleUnintegratedCodeChange()
    {
        var root = CreateTempRepositoryDirectory();
        var runner = new FakeGitRunner(root);
        runner.Enqueue(0, root);
        runner.Enqueue(0, "main");
        runner.Enqueue(0, "result456");
        runner.Enqueue(0, "base123");
        runner.Enqueue(1, "");
        runner.Enqueue(0, root);
        runner.Enqueue(0, "");
        runner.Enqueue(0, "main");
        runner.Enqueue(0, "base123");
        runner.Enqueue(0, "result456");
        runner.Enqueue(0, "");
        runner.Enqueue(0, "Updating base123..result456");
        runner.Enqueue(0, "result456");
        runner.Enqueue(0, "");

        try
        {
            var graph = new WorkGraph("job", 1);
            Assert.True(graph.ApplyPatch(new WorkGraphPatch(0, new[]
            {
                WorkGraphPatchOperation.Add(new WorkItemSpec("W10", "기능 구현", Kind: WorkItemKind.Normal, BaseRef: "base123"))
            })).Success);
            Assert.True(graph.TryMarkRunning("W10"));
            Assert.True(graph.TryMarkCompleted("W10", "result-ref", "완료", WorkItemResultType.CodeChange));

            var finalizer = new TargetWorkspaceFinalizer(
                root,
                "main",
                new GitWorktreeManager(runner));

            var result = await finalizer.FinalizeAsync(graph.Snapshot());

            Assert.True(result.Success);
            Assert.True(result.FastForwarded);
            Assert.Equal("W10", result.LandedWorkItemId);
            Assert.Equal("result-ref", result.LandedResultRef);
            Assert.Contains(
                runner.Calls,
                call => call.Arguments.SequenceEqual(new[] { "merge", "--ff-only", "result456" }));
        }
        finally
        {
            DeleteTempTree(root);
        }
    }

    [Fact]
    public async Task TargetWorkspaceFinalizerCollapsesLinearCodeChangeChainToLatestTip()
    {
        var root = CreateTempRepositoryDirectory();
        var runner = new FakeGitRunner(root);

        runner.Enqueue(0, root);
        runner.Enqueue(0, "main");
        runner.Enqueue(0, "first456");
        runner.Enqueue(0, "base123");
        runner.Enqueue(1, "");

        runner.Enqueue(0, root);
        runner.Enqueue(0, "main");
        runner.Enqueue(0, "second789");
        runner.Enqueue(0, "base123");
        runner.Enqueue(1, "");

        runner.Enqueue(0, root);
        runner.Enqueue(0, "base123");
        runner.Enqueue(0, "first456");
        runner.Enqueue(0, "");
        runner.Enqueue(0, "second789");
        runner.Enqueue(0, "");

        runner.Enqueue(0, root);
        runner.Enqueue(0, "");
        runner.Enqueue(0, "main");
        runner.Enqueue(0, "base123");
        runner.Enqueue(0, "second789");
        runner.Enqueue(0, "");
        runner.Enqueue(0, "Updating base123..second789");
        runner.Enqueue(0, "second789");
        runner.Enqueue(0, "");

        try
        {
            var graph = new WorkGraph("job", 2);
            Assert.True(graph.ApplyPatch(new WorkGraphPatch(0, new[]
            {
                WorkGraphPatchOperation.Add(new WorkItemSpec("W10", "기능 구현", Kind: WorkItemKind.Normal, BaseRef: "base123")),
                WorkGraphPatchOperation.Add(new WorkItemSpec("W11", "후속 수정", new[] { "W10" }, WorkItemKind.Normal, "base123"))
            })).Success);
            Assert.True(graph.TryMarkRunning("W10"));
            Assert.True(graph.TryMarkCompleted("W10", "first-ref", "1차 완료", WorkItemResultType.CodeChange));
            Assert.True(graph.TryMarkRunning("W11"));
            Assert.True(graph.TryMarkCompleted("W11", "second-ref", "2차 완료", WorkItemResultType.CodeChange));

            var finalizer = new TargetWorkspaceFinalizer(
                root,
                "main",
                new GitWorktreeManager(runner));

            var result = await finalizer.FinalizeAsync(graph.Snapshot());

            Assert.True(result.Success);
            Assert.True(result.FastForwarded);
            Assert.Equal("W11", result.LandedWorkItemId);
            Assert.Equal("second789", result.LandedResultRef);
            Assert.Contains(
                runner.Calls,
                call => call.Arguments.SequenceEqual(new[] { "merge", "--ff-only", "second789" }));
        }
        finally
        {
            DeleteTempTree(root);
        }
    }

    [Fact]
    public async Task TargetWorkspaceFinalizerRequiresIntegrationForTwoIndependentChanges()
    {
        var root = CreateTempRepositoryDirectory();
        var runner = new FakeGitRunner(root);
        for (var index = 0; index < 2; index++)
        {
            runner.Enqueue(0, root);
            runner.Enqueue(0, "main");
            runner.Enqueue(0, "result-" + index);
            runner.Enqueue(0, "base123");
            runner.Enqueue(1, "");
        }

        runner.Enqueue(0, root);
        runner.Enqueue(0, "base123");
        runner.Enqueue(0, "result-0");
        runner.Enqueue(0, "");
        runner.Enqueue(0, "result-1");
        runner.Enqueue(1, "");
        runner.Enqueue(1, "");

        try
        {
            var graph = new WorkGraph("job", 2);
            Assert.True(graph.ApplyPatch(new WorkGraphPatch(0, new[]
            {
                WorkGraphPatchOperation.Add(new WorkItemSpec("W10", "기능 A", Kind: WorkItemKind.Normal, BaseRef: "base123")),
                WorkGraphPatchOperation.Add(new WorkItemSpec("W11", "기능 B", Kind: WorkItemKind.Normal, BaseRef: "base123"))
            })).Success);
            Assert.True(graph.TryMarkRunning("W10"));
            Assert.True(graph.TryMarkCompleted("W10", "result-ref-0", "A 완료", WorkItemResultType.CodeChange));
            Assert.True(graph.TryMarkRunning("W11"));
            Assert.True(graph.TryMarkCompleted("W11", "result-ref-1", "B 완료", WorkItemResultType.CodeChange));

            var finalizer = new TargetWorkspaceFinalizer(
                root,
                "main",
                new GitWorktreeManager(runner));

            var result = await finalizer.FinalizeAsync(graph.Snapshot());

            Assert.False(result.Success);
            Assert.Equal("TARGET_INTEGRATION_REQUIRED", result.ErrorCode);
            Assert.DoesNotContain(
                runner.Calls,
                call => call.Arguments.Count > 0 && call.Arguments[0] == "merge");
        }
        finally
        {
            DeleteTempTree(root);
        }
    }

    [Fact]
    public async Task IntegrationLandingFastForwardsOnlyCleanTargetBranch()
    {
        var root = CreateTempRepositoryDirectory();
        var runner = new FakeGitRunner(root);
        runner.Enqueue(0, root);
        runner.Enqueue(0, "");
        runner.Enqueue(0, "main");
        runner.Enqueue(0, "base123");
        runner.Enqueue(0, "integrated456");
        runner.Enqueue(0, "");
        runner.Enqueue(0, "Updating base123..integrated456");
        runner.Enqueue(0, "integrated456");
        runner.Enqueue(0, "");

        try
        {
            var manager = new GitWorktreeManager(runner);
            var result = await manager.LandIntegrationAsync(root, "integration-ref");

            Assert.True(result.Success);
            Assert.True(result.FastForwarded);
            Assert.Equal("main", result.TargetBranch);
            Assert.Equal("base123", result.BeforeHead);
            Assert.Equal("integrated456", result.AfterHead);

            var statusCalls = runner.Calls
                .Where(call => call.Arguments.Count > 0 && call.Arguments[0] == "status")
                .ToArray();
            Assert.Equal(2, statusCalls.Length);
            Assert.All(statusCalls, call =>
            {
                Assert.Contains(":(exclude).projecthub", call.Arguments);
                Assert.Contains(":(exclude).projecthub/**", call.Arguments);
            });

            var merge = runner.Calls.Single(call => call.Arguments.Count > 0 && call.Arguments[0] == "merge");
            Assert.Equal(new[] { "merge", "--ff-only", "integrated456" }, merge.Arguments);
            Assert.DoesNotContain(
                runner.Calls.SelectMany(call => call.Arguments),
                argument => argument is "push" or "reset" or "--force" or "-f");
        }
        finally
        {
            DeleteTempTree(root);
        }
    }

    [Fact]
    public async Task IntegrationLandingExcludesNestedProjectHubRuntimeState()
    {
        var root = CreateTempRepositoryDirectory();
        var workspace = Path.Combine(root, "src", "Game");
        Directory.CreateDirectory(workspace);
        var runner = new FakeGitRunner(root);
        runner.Enqueue(0, root);
        runner.Enqueue(0, "");
        runner.Enqueue(0, "main");
        runner.Enqueue(0, "same123");
        runner.Enqueue(0, "same123");

        try
        {
            var manager = new GitWorktreeManager(runner);
            var result = await manager.LandIntegrationAsync(workspace, "integration-ref");

            Assert.True(result.Success);
            Assert.False(result.FastForwarded);

            var status = runner.Calls.Single(call =>
                call.Arguments.Count > 0 &&
                call.Arguments[0] == "status");
            Assert.Contains(":(exclude)src/Game/.projecthub", status.Arguments);
            Assert.Contains(":(exclude)src/Game/.projecthub/**", status.Arguments);
        }
        finally
        {
            DeleteTempTree(root);
        }
    }

    [Fact]
    public async Task IntegrationLandingRejectsChangedPrimaryBranch()
    {
        var root = CreateTempRepositoryDirectory();
        var runner = new FakeGitRunner(root);
        runner.Enqueue(0, root);
        runner.Enqueue(0, "");
        runner.Enqueue(0, "feature");

        try
        {
            var manager = new GitWorktreeManager(runner);
            var result = await manager.LandIntegrationAsync(
                root,
                "integration-ref",
                "main");

            Assert.False(result.Success);
            Assert.Equal("INTEGRATION_TARGET_BRANCH_CHANGED", result.ErrorCode);
            Assert.Equal("feature", result.TargetBranch);
            Assert.DoesNotContain(
                runner.Calls,
                call => call.Arguments.Count > 0 && call.Arguments[0] == "merge");
        }
        finally
        {
            DeleteTempTree(root);
        }
    }

    [Fact]
    public async Task IntegrationLandingRejectsDirtyTargetBeforeChangingHead()
    {
        var root = CreateTempRepositoryDirectory();
        var runner = new FakeGitRunner(root);
        runner.Enqueue(0, root);
        runner.Enqueue(0, " M local-change.cs");

        try
        {
            var manager = new GitWorktreeManager(runner);
            var result = await manager.LandIntegrationAsync(root, "integration-ref");

            Assert.False(result.Success);
            Assert.Equal("INTEGRATION_TARGET_DIRTY", result.ErrorCode);
            Assert.DoesNotContain(runner.Calls, call => call.Arguments.Count > 0 && call.Arguments[0] == "merge");
        }
        finally
        {
            DeleteTempTree(root);
        }
    }

    [Fact]
    public async Task IntegrationLandingRejectsNonFastForwardResult()
    {
        var root = CreateTempRepositoryDirectory();
        var runner = new FakeGitRunner(root);
        runner.Enqueue(0, root);
        runner.Enqueue(0, "");
        runner.Enqueue(0, "main");
        runner.Enqueue(0, "base123");
        runner.Enqueue(0, "other456");
        runner.Enqueue(1, "");

        try
        {
            var manager = new GitWorktreeManager(runner);
            var result = await manager.LandIntegrationAsync(root, "integration-ref");

            Assert.False(result.Success);
            Assert.Equal("INTEGRATION_NOT_FAST_FORWARD", result.ErrorCode);
            Assert.Equal("other456", result.IntegrationCommit);
            Assert.DoesNotContain(runner.Calls, call => call.Arguments.Count > 0 && call.Arguments[0] == "merge");
        }
        finally
        {
            DeleteTempTree(root);
        }
    }

    private static void WriteVerifiedMaterializationLedger(
        string root,
        string jobId,
        string resultRef)
    {
        var directory = Path.Combine(
            root,
            ".projecthub",
            "materialization-ledger",
            jobId);
        Directory.CreateDirectory(directory);
        var entry = new MaterializationLedgerEntry(
            jobId,
            FixedWorkItemSlots.Materialize,
            1,
            DateTimeOffset.UtcNow,
            true,
            null,
            new[] { resultRef },
            Array.Empty<MaterializationFileRecord>(),
            Array.Empty<string>(),
            "verified");
        File.WriteAllText(
            Path.Combine(directory, "000000000001-8.json"),
            System.Text.Json.JsonSerializer.Serialize(
                entry,
                new System.Text.Json.JsonSerializerOptions(
                    System.Text.Json.JsonSerializerDefaults.Web)
                {
                    WriteIndented = true
                }));
    }

    private static string CreateTempRepositoryDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "projecthub-worktree-test-" + Guid.NewGuid().ToString("N"), "repo");
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteTempTree(string repository)
    {
        var parent = Directory.GetParent(repository)?.FullName;
        if (!string.IsNullOrWhiteSpace(parent) && Directory.Exists(parent))
            Directory.Delete(parent, true);
    }

    private sealed class ConcurrentLandingGitRunner : IGitWorktreeCommandRunner
    {
        private readonly string _root;
        private readonly object _sync = new();
        private string _head = "base123";
        private int _activeMerges;

        public ConcurrentLandingGitRunner(string root)
        {
            _root = root;
        }

        public int MaxConcurrentMerges { get; private set; }

        public async Task<GitCommandResult> RunAsync(
            string workingDirectory,
            IReadOnlyList<string> arguments,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            if (arguments.SequenceEqual(new[] { "rev-parse", "--show-toplevel" }))
                return new(0, _root, string.Empty);
            if (arguments.Count > 0 && arguments[0] == "status")
                return new(0, string.Empty, string.Empty);
            if (arguments.SequenceEqual(new[] { "symbolic-ref", "--quiet", "--short", "HEAD" }))
                return new(0, "main", string.Empty);
            if (arguments.SequenceEqual(new[] { "rev-parse", "--verify", "HEAD" }))
            {
                lock (_sync)
                    return new(0, _head, string.Empty);
            }
            if (arguments.Count == 3 &&
                arguments[0] == "rev-parse" &&
                arguments[1] == "--verify" &&
                arguments[2].EndsWith("^{commit}", StringComparison.Ordinal))
            {
                return new(0, arguments[2][..^9], string.Empty);
            }
            if (arguments.Count > 0 && arguments[0] == "merge-base")
                return new(0, string.Empty, string.Empty);
            if (arguments.Count == 3 &&
                arguments[0] == "merge" &&
                arguments[1] == "--ff-only")
            {
                lock (_sync)
                {
                    _activeMerges++;
                    MaxConcurrentMerges = Math.Max(MaxConcurrentMerges, _activeMerges);
                }

                try
                {
                    await Task.Delay(80, cancellationToken);
                    lock (_sync)
                        _head = arguments[2];
                    return new(0, "Fast-forward", string.Empty);
                }
                finally
                {
                    lock (_sync)
                        _activeMerges--;
                }
            }

            throw new InvalidOperationException("예상하지 않은 Git 호출입니다: " + string.Join(" ", arguments));
        }
    }

    private sealed class ConcurrentPrepareGitRunner : IGitWorktreeCommandRunner
    {
        private readonly string _root;
        private readonly object _sync = new();
        private int _activeAdds;

        public ConcurrentPrepareGitRunner(string root)
        {
            _root = root;
        }

        public int MaxConcurrentAdds { get; private set; }

        public async Task<GitCommandResult> RunAsync(
            string workingDirectory,
            IReadOnlyList<string> arguments,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            if (arguments.SequenceEqual(new[] { "rev-parse", "--show-toplevel" }))
                return new(0, _root, string.Empty);
            if (arguments.Count >= 2 &&
                arguments[0] == "rev-parse" &&
                arguments[1] == "--verify")
                return new(0, "abc123", string.Empty);
            if (arguments.Count >= 2 &&
                arguments[0] == "worktree" &&
                arguments[1] == "list")
                return new(0, string.Empty, string.Empty);
            if (arguments.Count > 0 && arguments[0] == "show-ref")
                return new(1, string.Empty, string.Empty);
            if (arguments.Count >= 2 &&
                arguments[0] == "worktree" &&
                arguments[1] == "add")
            {
                lock (_sync)
                {
                    _activeAdds++;
                    MaxConcurrentAdds = Math.Max(MaxConcurrentAdds, _activeAdds);
                }

                try
                {
                    await Task.Delay(80, cancellationToken);
                    return new(0, "Preparing worktree", string.Empty);
                }
                finally
                {
                    lock (_sync)
                        _activeAdds--;
                }
            }

            throw new InvalidOperationException("예상하지 않은 Git 호출입니다: " + string.Join(" ", arguments));
        }
    }

    private sealed class FakeGitRunner : IGitWorktreeCommandRunner
    {
        private readonly Queue<GitCommandResult> _results = new();

        public FakeGitRunner(string root)
        {
            Root = root;
        }

        public string Root { get; }

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
            if (_results.Count == 0)
                throw new InvalidOperationException("예상하지 않은 Git 호출입니다: " + string.Join(" ", arguments));
            return Task.FromResult(_results.Dequeue());
        }
    }

    private sealed record GitCall(string WorkingDirectory, IReadOnlyList<string> Arguments);
}
