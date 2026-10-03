using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class CodexWorkItemExecutorTests
{
    [Fact]
    public async Task CompletedReportCreatesCheckpointResultAndKeepsSession()
    {
        var fixture = CreateFixture("""
            [GOTO : HQ]
            WORK_ITEM_STATUS: COMPLETED
            구현과 검증을 완료했습니다.
            """);

        try
        {
            var result = await fixture.Executor.ExecuteAsync(fixture.Request, CancellationToken.None);

            Assert.Equal(WorkItemExecutionOutcome.Completed, result.Outcome);
            Assert.Equal("head123", result.ResultRef);
            Assert.Equal(WorkItemResultType.Analysis, result.ResultType);
            Assert.Equal("session-1", result.SessionId);
            Assert.Equal(fixture.Branch, result.Branch);
            Assert.Contains("workItemId: W1", fixture.Runner.LastRequest!.Prompt);
            Assert.Contains("현재 WorkItem을 수행하는 WORK", fixture.Runner.LastRequest.Prompt);
            Assert.Contains("WORK_ITEM_STATUS: COMPLETED", fixture.Runner.LastRequest.Prompt);
            Assert.NotNull(fixture.Runner.LastRequest.EnvironmentVariables);
            Assert.Equal(
                "never",
                fixture.Runner.LastRequest.EnvironmentVariables!["GIT_CONFIG_VALUE_1"]);

            var repositoryRoot = Path.Combine(fixture.Parent, "repo");
            var runtime = WorkerPaths.GetRepositoryRuntimePaths(repositoryRoot);
            var workTemp = WorkerPaths.BuildWorkTempPath(runtime, "job", "W1");
            Assert.Equal(runtime.NuGetPackages, fixture.Runner.LastRequest.EnvironmentVariables["NUGET_PACKAGES"]);
            Assert.Equal(runtime.NuGetPackages, fixture.Runner.LastRequest.EnvironmentVariables["RestorePackagesPath"]);
            Assert.Equal(runtime.NuGetHttpCache, fixture.Runner.LastRequest.EnvironmentVariables["NUGET_HTTP_CACHE_PATH"]);
            Assert.Equal(runtime.NuGetPluginsCache, fixture.Runner.LastRequest.EnvironmentVariables["NUGET_PLUGINS_CACHE_PATH"]);
            Assert.Equal(runtime.NuGetScratch, fixture.Runner.LastRequest.EnvironmentVariables["NUGET_SCRATCH"]);
            Assert.Equal(runtime.DotNetHome, fixture.Runner.LastRequest.EnvironmentVariables["DOTNET_CLI_HOME"]);
            Assert.Equal(workTemp, fixture.Runner.LastRequest.EnvironmentVariables["TEMP"]);
            Assert.Equal(workTemp, fixture.Runner.LastRequest.EnvironmentVariables["TMP"]);
            Assert.Equal(workTemp, fixture.Runner.LastRequest.EnvironmentVariables["PROJECTHUB_WORK_TEMP"]);
            Assert.Contains("WORK 임시 산출물 루트: " + workTemp, fixture.Runner.LastRequest.Prompt);
            Assert.False(fixture.Runner.LastRequest.IncludeAppBaseWritable);
            Assert.Contains(runtime.NuGetRoot, fixture.Runner.LastRequest.AdditionalWritableDirectories!);
            Assert.Contains(runtime.DotNetHome, fixture.Runner.LastRequest.AdditionalWritableDirectories!);
            Assert.Contains(workTemp, fixture.Runner.LastRequest.AdditionalWritableDirectories!);
            Assert.True(Directory.Exists(runtime.NuGetPackages));
            Assert.True(Directory.Exists(runtime.DotNetHome));
            Assert.True(Directory.Exists(workTemp));
            Assert.Contains(
                fixture.Git.Calls,
                call => call.SequenceEqual(
                    new[] { "worktree", "remove", Path.GetFullPath(fixture.Request.Item.WorktreePath!) }));
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public async Task BuildPublishSlotReceivesTargetWorkspaceWriteAccess()
    {
        var fixture = CreateFixture(
            """
            [GOTO : HQ]
            WORK_ITEM_STATUS: COMPLETED
            BUILD / PUBLISH 완료
            """,
            workItemId: FixedWorkItemSlots.BuildPublish,
            createdOrder: 4);

        try
        {
            var result = await fixture.Executor.ExecuteAsync(
                fixture.Request,
                CancellationToken.None);

            var rootPath = Path.Combine(fixture.Parent, "repo");
            var executionKey = FixedWorkItemSlots.BuildExecutionKey(
                FixedWorkItemSlots.BuildPublish,
                fixture.Request.Item.CreatedOrder);
            var runtime = WorkerPaths.GetRepositoryRuntimePaths(rootPath);
            var workTemp = WorkerPaths.BuildWorkTempPath(
                runtime,
                "job",
                executionKey);

            Assert.Contains(
                Path.GetFullPath(rootPath),
                fixture.Runner.LastRequest!.AdditionalWritableDirectories!);
            Assert.Contains(
                "대상 프로젝트 루트: " + Path.GetFullPath(rootPath),
                fixture.Runner.LastRequest.Prompt);
            Assert.Equal(
                workTemp,
                fixture.Runner.LastRequest.EnvironmentVariables!["PROJECTHUB_WORK_TEMP"]);
            Assert.Equal(WorkItemExecutionOutcome.Completed, result.Outcome);
            Assert.Equal(WorkItemResultType.Artifact, result.ResultType);
            Assert.StartsWith("artifact-run-", result.ResultRef);
            Assert.Null(result.CommitManifestPath);
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public async Task DuplicateWorkItemStatusReturnsDecisionToHqWithoutCorrectionRetry()
    {
        var fixture = CreateFixture("""
            [GOTO : HQ]
            WORK_ITEM_STATUS: COMPLETED
            구현 완료
            WORK_ITEM_STATUS: COMPLETED
            """);

        try
        {
            var result = await fixture.Executor.ExecuteAsync(
                fixture.Request,
                CancellationToken.None);

            Assert.Equal(WorkItemExecutionOutcome.Failed, result.Outcome);
            Assert.Equal("WORK_ITEM_STATUS_DUPLICATE", result.FailureCode);
            Assert.Equal("WORK_REPORT", result.FailureStage);
            Assert.Equal(1, fixture.Runner.RunCount);
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public void WorkspaceWritableRootsCanExcludeWorkerExecutableDirectory()
    {
        var parent = Path.Combine(Path.GetTempPath(), "projecthub-writable-roots-" + Guid.NewGuid().ToString("N"));
        var workspace = Path.Combine(parent, "workspace");
        var runtime = Path.Combine(parent, "repo.projecthub", "nuget");
        Directory.CreateDirectory(workspace);
        Directory.CreateDirectory(runtime);

        try
        {
            var resolved = CodexCliRunner.ResolveAdditionalWritableDirectories(
                CodexSandboxMode.WorkspaceWrite,
                workspace,
                null,
                new[] { runtime });

            Assert.Single(resolved);
            Assert.Equal(Path.GetFullPath(runtime), resolved[0]);
            Assert.DoesNotContain(
                resolved,
                path => string.Equals(
                    Path.GetFullPath(path),
                    Path.GetFullPath(AppContext.BaseDirectory),
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(parent, true);
        }
    }

    [Fact]
    public async Task ExistingWorkSessionDoesNotRepeatRoutingContract()
    {
        var fixture = CreateFixture("""
            [GOTO : HQ]
            WORK_ITEM_STATUS: COMPLETED
            재개 완료
            """);

        try
        {
            var resumed = fixture.Request with
            {
                Item = fixture.Request.Item with { SessionId = "existing-session" },
                InboundType = "RESOURCE_RESULT",
                InboundBody = "requestId=R1 status=SAVED"
            };

            await fixture.Executor.ExecuteAsync(resumed, CancellationToken.None);

            Assert.Contains("입력 유형: RESOURCE_RESULT", fixture.Runner.LastRequest!.Prompt);
            Assert.DoesNotContain("당신은 WORK다.", fixture.Runner.LastRequest.Prompt);
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public void SplitRequestStatusIsNotPartOfWorkReportContract()
    {
        const string body = """
            WORK_ITEM_STATUS: SPLIT_REQUEST
            별도 작업이 필요하다는 판단
            """;

        Assert.False(WorkItemReportContract.TryParse(body, out _, out var error));
        Assert.Equal("WORK_ITEM_STATUS_INVALID", error);
    }

    [Fact]
    public async Task ResourceRequestBlocksOnlyThisWorkItemForLaterRouting()
    {
        var fixture = CreateFixture("""
            [GOTO : RESOURCE]
            RESOURCE_TYPE: IMAGE
            작은 아이콘을 생성해줘.
            """);

        try
        {
            var result = await fixture.Executor.ExecuteAsync(fixture.Request, CancellationToken.None);

            Assert.Equal(WorkItemExecutionOutcome.Blocked, result.Outcome);
            Assert.Equal("RESOURCE_REQUEST", result.BlockCode);
            Assert.Contains("RESOURCE_TYPE: IMAGE", result.ResultSummary);
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public async Task CallCompletedReportsWorkItemUsageBoundary()
    {
        var fixture = CreateFixture("""
            [GOTO : HQ]
            WORK_ITEM_STATUS: COMPLETED
            완료
            """);
        CodexWorkItemCallCompleted? observed = null;
        fixture.Executor.CallCompleted += value => observed = value;

        try
        {
            await fixture.Executor.ExecuteAsync(
                fixture.Request,
                CancellationToken.None);

            Assert.NotNull(observed);
            Assert.Equal("W1", observed!.WorkItemId);
            Assert.Equal(fixture.Request.Item.CreatedOrder + 1, observed.WorkNumber);
            Assert.Equal("WORK_ITEM", observed.InboundType);
            Assert.True(observed.PromptBytes > 0);
            Assert.Equal(0, observed.Result.ExitCode);
            Assert.True(fixture.Runner.LastRequest!.DisableComputerUse);
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public async Task JudgeRouteIsRejectedByWorkProtocol()
    {
        var fixture = CreateFixture("""
            [GOTO : JUDGE]
            legacy judge request
            """);

        try
        {
            var result = await fixture.Executor.ExecuteAsync(
                fixture.Request,
                CancellationToken.None);

            Assert.Equal(WorkItemExecutionOutcome.Failed, result.Outcome);
            Assert.Equal("WORK_ROUTE_GOTO_NOT_ALLOWED", result.FailureCode);
            Assert.Equal("WORK_ROUTE", result.FailureStage);
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public async Task WorktreeCreateFailureDetailReachesWorkItemResult()
    {
        var parent = Path.Combine(Path.GetTempPath(), "projecthub-worktree-failure-" + Guid.NewGuid().ToString("N"));
        var root = Path.Combine(parent, "repo");
        Directory.CreateDirectory(root);
        var git = new FakeGitRunner();
        git.Enqueue(0, root);
        git.Enqueue(0, "base123");
        git.Enqueue(0, "");
        git.Enqueue(1, "");
        git.Enqueue(128, "", "fatal: simulated concurrent worktree failure");

        try
        {
            var executor = new CodexWorkItemExecutor(
                "job",
                root,
                new WorkerAiRoleSettings(Model: "gpt-6-luna", Reasoning: "medium"),
                new FakeAiRoleRunner("[GOTO : HQ]\nWORK_ITEM_STATUS: COMPLETED\nunused"),
                new GitWorktreeManager(git));

            var item = new WorkItemSnapshot(
                "W1",
                "기능을 구현하세요.",
                Array.Empty<string>(),
                WorkItemKind.Normal,
                WorkItemState.Running,
                0,
                "main",
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                null);

            var result = await executor.ExecuteAsync(
                new WorkItemExecutionRequest(
                    item,
                    1,
                    Array.Empty<WorkItemDependencyResult>(),
                    "WORK_ITEM",
                    item.Goal),
                CancellationToken.None);

            Assert.Equal(WorkItemExecutionOutcome.Blocked, result.Outcome);
            Assert.Equal("WORKTREE_CREATE_FAILED", result.BlockCode);
            Assert.Null(result.FailureCode);
            Assert.Contains("exitCode=128", result.ResultSummary ?? string.Empty);
            Assert.Contains("fatal: simulated concurrent worktree failure", result.ResultSummary ?? string.Empty);
        }
        finally
        {
            Directory.Delete(parent, true);
        }
    }

    [Fact]
    public void DependencyManifestIsSummarizedWithoutInliningFileContent()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "projecthub-manifest-summary-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var manifestPath = Path.Combine(directory, "manifest.json");

        try
        {
            File.WriteAllText(
                manifestPath,
                """
                {
                  "workItemId": "W0",
                  "commit": "abcdef123456",
                  "parentCommit": "parent",
                  "tree": "tree",
                  "changedFiles": [
                    {
                      "path": "src/large.cs",
                      "changeType": "MODIFY",
                      "previousPath": null,
                      "size": 999999,
                      "sha256": "deadbeef",
                      "isText": true,
                      "content": "THIS_LARGE_INLINE_CONTENT_MUST_NOT_APPEAR"
                    }
                  ]
                }
                """);

            var prompt = RoleContractLoader.BuildWorkPrompt(
                "WORK_ITEM",
                "후속 작업",
                new WorkItemPromptContext(
                    "W1",
                    WorkItemKind.Normal,
                    "후속 작업",
                    new[] { "W0" },
                    "main",
                    "branch",
                    "worktree",
                    DependencyResults: new[]
                    {
                        new WorkItemDependencyPromptContext(
                            "W0",
                            "abcdef123456",
                            "선행 완료",
                            WorkItemResultType.CodeChange,
                            manifestPath)
                    }),
                includeContract: false);

            Assert.Contains("manifest=" + manifestPath, prompt);
            Assert.Contains("resultRef=abcdef123456", prompt);
            Assert.DoesNotContain("- MODIFY src/large.cs", prompt);
            Assert.DoesNotContain("THIS_LARGE_INLINE_CONTENT_MUST_NOT_APPEAR", prompt);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task CodeDependencyBecomesEffectiveNormalBaseRef()
    {
        var parent = Path.Combine(Path.GetTempPath(), "projecthub-code-base-" + Guid.NewGuid().ToString("N"));
        var root = Path.Combine(parent, "repo");
        Directory.CreateDirectory(root);
        const string jobId = "job";
        const string workItemId = "W1";
        var branch = GitWorktreeManager.BuildBranchName(jobId, workItemId);
        var worktree = GitWorktreeManager.BuildWorktreePath(root, jobId, workItemId);
        Directory.CreateDirectory(worktree);

        var git = new FakeGitRunner();
        git.Enqueue(0, root);
        git.Enqueue(0, "base123");
        git.Enqueue(0, "dep456");
        git.Enqueue(0, "");
        git.Enqueue(0, root);
        git.Enqueue(0, "dep456");
        git.Enqueue(0, $"worktree {worktree}\nHEAD dep456\nbranch refs/heads/{branch}\n");
        git.Enqueue(0, "dep456");
        git.Enqueue(0, branch);
        git.Enqueue(0, "");

        var ai = new FakeAiRoleRunner("""
            [GOTO : HQ]
            WORK_ITEM_STATUS: COMPLETED
            선행 코드 위에서 검증했습니다.
            """);
        var executor = new CodexWorkItemExecutor(
            jobId,
            root,
            new WorkerAiRoleSettings(Model: "gpt-6-luna", Reasoning: "medium"),
            ai,
            new GitWorktreeManager(git));

        var item = new WorkItemSnapshot(
            workItemId,
            "선행 코드 검증",
            new[] { "W0" },
            WorkItemKind.Normal,
            WorkItemState.Running,
            0,
            "main",
            branch,
            worktree,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            null);

        try
        {
            var result = await executor.ExecuteAsync(
                new WorkItemExecutionRequest(
                    item,
                    1,
                    new[]
                    {
                        new WorkItemDependencyResult(
                            "W0",
                            "dep-ref",
                            "선행 구현",
                            WorkItemResultType.CodeChange,
                            "manifest-W0.json")
                    },
                    "WORK_ITEM",
                    item.Goal),
                CancellationToken.None);

            Assert.Equal(WorkItemExecutionOutcome.Completed, result.Outcome);
            Assert.Contains("기준 ref: dep456", ai.LastRequest!.Prompt);
            Assert.Equal(worktree, ai.LastRequest.WorkingDirectory);
        }
        finally
        {
            if (Directory.Exists(parent))
                Directory.Delete(parent, true);
        }
    }

    [Fact]
    public async Task CheckpointPendingResumeDoesNotRunAiAgain()
    {
        var fixture = CreateFixture("""
            [GOTO : HQ]
            WORK_ITEM_STATUS: COMPLETED
            의미 작업은 완료했습니다.
            """);

        try
        {
            fixture.Git.Clear();
            EnqueueExistingWorktreePreparation(fixture);
            EnqueueCheckpointAddFailure(fixture, attempts: 3);

            var first = await fixture.Executor.ExecuteAsync(
                fixture.Request,
                CancellationToken.None);

            Assert.Equal(WorkItemExecutionOutcome.Blocked, first.Outcome);
            Assert.Equal("WORKTREE_CHECKPOINT_PENDING", first.BlockCode);
            Assert.Equal("WORKTREE_CHECKPOINT_ADD_FAILED", first.BlockDetailCode);
            Assert.Contains("의미 작업은 완료했습니다.", first.ResultSummary ?? string.Empty);
            Assert.Equal(1, fixture.Runner.RunCount);

            fixture.Git.Clear();
            EnqueueExistingWorktreePreparation(fixture);
            EnqueueCheckpointAddFailure(fixture, attempts: 3);

            var retryItem = fixture.Request.Item with
            {
                ResultSummary = first.ResultSummary,
                ResultRef = first.ResultRef,
                SessionId = first.SessionId,
                Branch = first.Branch,
                WorktreePath = first.WorktreePath
            };
            var retry = fixture.Request with
            {
                Item = retryItem,
                InboundType = "WORKTREE_CHECKPOINT_RETRY",
                InboundBody = string.Empty
            };

            var second = await fixture.Executor.ExecuteAsync(
                retry,
                CancellationToken.None);

            Assert.Equal(WorkItemExecutionOutcome.Blocked, second.Outcome);
            Assert.Equal("WORKTREE_CHECKPOINT_PENDING", second.BlockCode);
            Assert.Equal(1, fixture.Runner.RunCount);
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public async Task MissingCodeDependencyResultRefBlocksBeforeAiExecution()
    {
        var fixture = CreateFixture(
            """
            [GOTO : HQ]
            WORK_ITEM_STATUS: COMPLETED
            실행되면 안 됩니다.
            """,
            dependencies: new[]
            {
                new WorkItemDependencyResult("W0", null, "선행 코드", WorkItemResultType.CodeChange, "manifest-W0.json")
            });

        try
        {
            var result = await fixture.Executor.ExecuteAsync(fixture.Request, CancellationToken.None);

            Assert.Equal(WorkItemExecutionOutcome.Blocked, result.Outcome);
            Assert.Equal("NORMAL_CODE_DEPENDENCY_RESULT_REF_MISSING", result.BlockCode);
            Assert.Null(fixture.Runner.LastRequest);
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public async Task DependencyResultsAreIncludedInWorkItemPrompt()
    {
        var fixture = CreateFixture("""
            [GOTO : HQ]
            WORK_ITEM_STATUS: COMPLETED
            완료
            """,
            dependencies: new[]
            {
                new WorkItemDependencyResult("W0", "dep-ref", "선행 완료", WorkItemResultType.Analysis, "manifest-W0.json")
            });

        try
        {
            await fixture.Executor.ExecuteAsync(fixture.Request, CancellationToken.None);

            Assert.Contains("선행 WorkItem 결과:", fixture.Runner.LastRequest!.Prompt);
            Assert.Contains("- workItemId=W0 resultType=ANALYSIS resultRef=dep-ref manifest=manifest-W0.json snapshot=없음", fixture.Runner.LastRequest.Prompt);
            Assert.Contains("report:" + Environment.NewLine + "선행 완료", fixture.Runner.LastRequest.Prompt);
        }
        finally
        {
            fixture.Dispose();
        }
    }


    [Fact]
    public async Task FirstIntegrationUsesCurrentPrimaryHeadInsteadOfStoredGraphBase()
    {
        var parent = Path.Combine(Path.GetTempPath(), "projecthub-integration-base-" + Guid.NewGuid().ToString("N"));
        var root = Path.Combine(parent, "repo");
        Directory.CreateDirectory(root);

        var integrationClone = GitWorktreeManager.BuildIntegrationClonePath(root, "job", "I1");
        var integrationBranch = GitWorktreeManager.BuildBranchName("job", "I1");
        var git = new FakeGitRunner();
        git.Enqueue(0, root);
        git.Enqueue(0, "main");
        git.Enqueue(0, "primary999");
        git.Enqueue(0, "Cloning");
        git.Enqueue(0, "Switched");
        git.Enqueue(0, "");
        git.Enqueue(0, "");
        git.Enqueue(0, Path.Combine(integrationClone, ".git"));
        git.Enqueue(0, "primary999");

        var ai = new FakeAiRoleRunner("""
            [GOTO : RESOURCE]
            RESOURCE_TYPE: FILE
            통합 검증용 파일을 생성해줘.
            """);
        var executor = new CodexWorkItemExecutor(
            "job",
            root,
            new WorkerAiRoleSettings(Model: "gpt-6-luna", Reasoning: "medium"),
            ai,
            new GitWorktreeManager(git),
            expectedPrimaryBranch: "main");

        var item = new WorkItemSnapshot(
            "I1",
            "선행 결과를 통합하세요.",
            Array.Empty<string>(),
            WorkItemKind.Integration,
            WorkItemState.Running,
            0,
            "stale-base",
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            null);

        try
        {
            var result = await executor.ExecuteAsync(
                new WorkItemExecutionRequest(
                    item,
                    1,
                    Array.Empty<WorkItemDependencyResult>(),
                    "WORK_ITEM",
                    item.Goal),
                CancellationToken.None);

            Assert.Equal(WorkItemExecutionOutcome.Blocked, result.Outcome);
            Assert.Equal("RESOURCE_REQUEST", result.BlockCode);
            Assert.Contains("기준 ref: primary999", ai.LastRequest!.Prompt);
            Assert.Equal(integrationClone, ai.LastRequest.WorkingDirectory);
            Assert.Equal(CodexSandboxMode.WorkspaceWrite, ai.LastRequest.Sandbox);
            Assert.Contains("branch: " + integrationBranch, ai.LastRequest.Prompt);
        }
        finally
        {
            Directory.Delete(parent, true);
        }
    }

    [Fact]
    public async Task CompletedIntegrationImportsResultWithoutTouchingPrimaryWorkspace()
    {
        var fixture = CreateFixture(
            """
            [GOTO : HQ]
            WORK_ITEM_STATUS: COMPLETED
            모든 선행 결과를 통합하고 검증했습니다.
            """,
            kind: WorkItemKind.Integration);

        var integrationClone = fixture.Request.Item.WorktreePath!;
        fixture.Git.Enqueue(0, Path.Combine(fixture.Parent, "repo"));
        fixture.Git.Enqueue(0, integrationClone);
        fixture.Git.Enqueue(0, Path.Combine(integrationClone, ".git"));
        fixture.Git.Enqueue(0, fixture.Branch);
        fixture.Git.Enqueue(0, "head123");
        fixture.Git.Enqueue(0, "Imported");
        fixture.Git.Enqueue(0, "head123");

        try
        {
            var result = await fixture.Executor.ExecuteAsync(
                fixture.Request,
                CancellationToken.None);

            Assert.Equal(WorkItemExecutionOutcome.Completed, result.Outcome);
            Assert.Equal("head123", result.ResultRef);
            Assert.Contains("INTEGRATION_IMPORT", result.ResultSummary);
            Assert.Contains("status: IMPORTED", result.ResultSummary);
            Assert.Contains(
                "refs/projecthub/integration-results/job/" + fixture.Request.Item.Id,
                result.ResultSummary);
            Assert.DoesNotContain(
                fixture.Git.Calls,
                call => call.Count > 0 &&
                        call[0] == "merge");
            Assert.False(Directory.Exists(integrationClone));
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public async Task IntegrationImportSourceBranchChangeBlocksWithCheckpoint()
    {
        var fixture = CreateFixture(
            """
            [GOTO : HQ]
            WORK_ITEM_STATUS: COMPLETED
            통합 worktree 검증은 완료했습니다.
            """,
            kind: WorkItemKind.Integration);

        var integrationClone = fixture.Request.Item.WorktreePath!;
        fixture.Git.Enqueue(0, Path.Combine(fixture.Parent, "repo"));
        fixture.Git.Enqueue(0, integrationClone);
        fixture.Git.Enqueue(0, Path.Combine(integrationClone, ".git"));
        fixture.Git.Enqueue(0, "feature");

        try
        {
            var result = await fixture.Executor.ExecuteAsync(
                fixture.Request,
                CancellationToken.None);

            Assert.Equal(WorkItemExecutionOutcome.Blocked, result.Outcome);
            Assert.Equal("INTEGRATION_IMPORT_FAILED", result.BlockCode);
            Assert.Equal("INTEGRATION_IMPORT_SOURCE_BRANCH_CHANGED", result.BlockDetailCode);
            Assert.Equal("head123", result.ResultRef);
            Assert.StartsWith("INTEGRATION_IMPORT", result.ResultSummary);
            Assert.Contains("errorCode: INTEGRATION_IMPORT_SOURCE_BRANCH_CHANGED", result.ResultSummary);
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public async Task ResumeRequestUsesExplicitInboundBodyInsteadOfRepeatingGoal()
    {
        var fixture = CreateFixture("""
            [GOTO : HQ]
            WORK_ITEM_STATUS: COMPLETED
            재개 완료
            """);

        try
        {
            var resumeRequest = fixture.Request with
            {
                InboundType = "RESOURCE_RESULT",
                InboundBody = "resourceId=R1 status=SAVED"
            };

            await fixture.Executor.ExecuteAsync(resumeRequest, CancellationToken.None);

            Assert.Contains("입력 유형: RESOURCE_RESULT", fixture.Runner.LastRequest!.Prompt);
            Assert.Contains("resourceId=R1 status=SAVED", fixture.Runner.LastRequest.Prompt);
        }
        finally
        {
            fixture.Dispose();
        }
    }

    private static void EnqueueExistingWorktreePreparation(Fixture fixture)
    {
        var root = Path.Combine(fixture.Parent, "repo");
        var worktree = fixture.Request.Item.WorktreePath!;
        fixture.Git.Enqueue(0, root);
        fixture.Git.Enqueue(0, "base123");
        fixture.Git.Enqueue(
            0,
            $"worktree {worktree}\nHEAD head123\nbranch refs/heads/{fixture.Branch}\n");
    }

    private static void EnqueueCheckpointAddFailure(Fixture fixture, int attempts)
    {
        for (var index = 0; index < attempts; index++)
        {
            fixture.Git.Enqueue(0, "head123");
            fixture.Git.Enqueue(0, fixture.Branch);
            fixture.Git.Enqueue(0, " M changed.cs");
            fixture.Git.Enqueue(0, " M changed.cs");
            fixture.Git.Enqueue(128, "", "fatal: simulated checkpoint add lock");
        }
    }

    private static Fixture CreateFixture(
        string finalMessage,
        IReadOnlyList<WorkItemDependencyResult>? dependencies = null,
        WorkItemKind kind = WorkItemKind.Normal,
        string workItemId = "W1",
        long createdOrder = 0,
        IReadOnlyList<WorkItemDependencyResult>? materializationCandidates = null)
    {
        var parent = Path.Combine(Path.GetTempPath(), "projecthub-codex-workitem-" + Guid.NewGuid().ToString("N"));
        var root = Path.Combine(parent, "repo");
        Directory.CreateDirectory(root);

        const string jobId = "job";
        var executionWorkItemId = FixedWorkItemSlots.BuildExecutionKey(
            workItemId,
            createdOrder);
        var branch = GitWorktreeManager.BuildBranchName(jobId, executionWorkItemId);
        var worktree = kind == WorkItemKind.Integration
            ? GitWorktreeManager.BuildIntegrationClonePath(root, jobId, workItemId)
            : GitWorktreeManager.BuildWorktreePath(root, jobId, executionWorkItemId);
        Directory.CreateDirectory(worktree);
        if (kind == WorkItemKind.Integration)
            Directory.CreateDirectory(Path.Combine(worktree, ".git"));

        var git = new FakeGitRunner();
        if (kind == WorkItemKind.Integration)
        {
            git.Enqueue(0, root);
            git.Enqueue(0, worktree);
            git.Enqueue(0, Path.Combine(worktree, ".git"));
            git.Enqueue(0, branch);
            git.Enqueue(0, "head123");
            git.Enqueue(0, "head123");
            git.Enqueue(0, branch);
            git.Enqueue(0, "");
        }
        else
        {
            git.Enqueue(0, root);
            git.Enqueue(0, "base123");
            git.Enqueue(0, $"worktree {worktree}\nHEAD head123\nbranch refs/heads/{branch}\n");
            git.Enqueue(0, "head123");
            git.Enqueue(0, branch);
            git.Enqueue(0, "");
            git.Enqueue(0, "head123");
            git.Enqueue(0, branch);
            git.Enqueue(0, "");
            git.Enqueue(0, "removed");
        }

        var ai = new FakeAiRoleRunner(finalMessage);
        var executor = new CodexWorkItemExecutor(
            jobId,
            root,
            new WorkerAiRoleSettings(Model: "gpt-6-luna", Reasoning: "medium"),
            ai,
            new GitWorktreeManager(git),
            expectedPrimaryBranch: "main");

        var item = new WorkItemSnapshot(
            workItemId,
            "기능을 구현하세요.",
            dependencies?.Select(value => value.WorkItemId).ToArray() ?? Array.Empty<string>(),
            kind,
            WorkItemState.Running,
            createdOrder,
            "main",
            branch,
            worktree,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            null);

        return new Fixture(
            parent,
            branch,
            executor,
            ai,
            git,
            new WorkItemExecutionRequest(
                item,
                1,
                dependencies ?? Array.Empty<WorkItemDependencyResult>(),
                "WORK_ITEM",
                "기능을 구현하세요.",
                materializationCandidates));
    }

    private sealed class Fixture : IDisposable
    {
        public Fixture(
            string parent,
            string branch,
            CodexWorkItemExecutor executor,
            FakeAiRoleRunner runner,
            FakeGitRunner git,
            WorkItemExecutionRequest request)
        {
            Parent = parent;
            Branch = branch;
            Executor = executor;
            Runner = runner;
            Git = git;
            Request = request;
        }

        public string Parent { get; }
        public string Branch { get; }
        public CodexWorkItemExecutor Executor { get; }
        public FakeAiRoleRunner Runner { get; }
        public FakeGitRunner Git { get; }
        public WorkItemExecutionRequest Request { get; }

        public void Dispose()
        {
            if (Directory.Exists(Parent))
                Directory.Delete(Parent, true);
        }
    }

    private sealed class FakeAiRoleRunner : IAiRoleRunner
    {
        private readonly Queue<string> _finalMessages = new();
        private string _lastFinalMessage;

        public FakeAiRoleRunner(string finalMessage)
        {
            _lastFinalMessage = finalMessage;
            _finalMessages.Enqueue(finalMessage);
        }

        public AiRoleRunRequest? LastRequest { get; private set; }
        public int RunCount { get; private set; }

        public void EnqueueFinalMessage(string finalMessage)
            => _finalMessages.Enqueue(finalMessage);

        public AiServiceProvider Provider => AiServiceProvider.OpenAI;
        public bool SupportsSessions => true;

        public string? GetPreflightError(WorkerAiRoleSettings role, string workingDirectory, bool openAiAuthenticated)
            => null;

        public Task<AiRoleRunResult> RunAsync(AiRoleRunRequest request)
        {
            RunCount++;
            LastRequest = request;
            request.SessionStarted?.Invoke("session-1");
            if (_finalMessages.Count > 0)
                _lastFinalMessage = _finalMessages.Dequeue();

            return Task.FromResult(new AiRoleRunResult(
                "openai",
                request.Role.Model,
                request.Role.Reasoning,
                "session-1",
                0,
                string.Empty,
                string.Empty,
                _lastFinalMessage,
                Array.Empty<CodexCliFile>(),
                CodexUsage.Empty,
                Array.Empty<CodexCommandExecution>()));
        }
    }

    private sealed class FakeGitRunner : IGitWorktreeCommandRunner
    {
        private readonly Queue<GitCommandResult> _results = new();

        public List<IReadOnlyList<string>> Calls { get; } = new();

        public void Enqueue(int exitCode, string stdout, string stderr = "")
            => _results.Enqueue(new GitCommandResult(exitCode, stdout, stderr));

        public void Clear() => _results.Clear();

        public Task<GitCommandResult> RunAsync(
            string workingDirectory,
            IReadOnlyList<string> arguments,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            Calls.Add(arguments.ToArray());
            if (arguments.Count >= 3 &&
                string.Equals(arguments[0], "clone", StringComparison.Ordinal) &&
                !string.IsNullOrWhiteSpace(arguments[^1]))
            {
                Directory.CreateDirectory(arguments[^1]);
                Directory.CreateDirectory(Path.Combine(arguments[^1], ".git"));
            }
            if (_results.Count == 0 &&
                arguments.Count > 0 &&
                string.Equals(arguments[0], "add", StringComparison.Ordinal))
            {
                return Task.FromResult(new GitCommandResult(0, string.Empty, string.Empty));
            }
            if (_results.Count == 0)
                throw new InvalidOperationException("예상하지 않은 Git 호출입니다: " + string.Join(" ", arguments));
            return Task.FromResult(_results.Dequeue());
        }
    }
}
