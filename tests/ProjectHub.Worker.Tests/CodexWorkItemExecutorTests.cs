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
            Assert.Contains("당신은 WORK다.", fixture.Runner.LastRequest.Prompt);
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
            Assert.False(fixture.Runner.LastRequest.IncludeAppBaseWritable);
            Assert.Contains(runtime.NuGetRoot, fixture.Runner.LastRequest.AdditionalWritableDirectories!);
            Assert.Contains(runtime.DotNetHome, fixture.Runner.LastRequest.AdditionalWritableDirectories!);
            Assert.Contains(workTemp, fixture.Runner.LastRequest.AdditionalWritableDirectories!);
            Assert.True(Directory.Exists(runtime.NuGetPackages));
            Assert.True(Directory.Exists(runtime.DotNetHome));
            Assert.True(Directory.Exists(workTemp));
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
    public async Task SplitRequestReturnsBlockedOutcomeWithCheckpoint()
    {
        var fixture = CreateFixture("""
            [GOTO : HQ]
            WORK_ITEM_STATUS: SPLIT_REQUEST
            별도 WorkItem이 필요합니다.
            """);

        try
        {
            var result = await fixture.Executor.ExecuteAsync(fixture.Request, CancellationToken.None);

            Assert.Equal(WorkItemExecutionOutcome.Blocked, result.Outcome);
            Assert.Equal("SPLIT_REQUEST", result.BlockCode);
            Assert.Equal("head123", result.ResultRef);
            Assert.Contains("별도 WorkItem", result.ResultSummary);
        }
        finally
        {
            fixture.Dispose();
        }
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
    public async Task JudgeRequestBlocksForHqWhenJudgeIsDisabled()
    {
        var fixture = CreateFixture("""
            [GOTO : JUDGE]
            NOUL | QID:q1 판단이 필요한가?
            """);

        try
        {
            var result = await fixture.Executor.ExecuteAsync(
                fixture.Request,
                CancellationToken.None);

            Assert.Equal(WorkItemExecutionOutcome.Blocked, result.Outcome);
            Assert.Equal("JUDGE_UNAVAILABLE", result.BlockCode);
            Assert.Contains("QID:q1", result.ResultSummary ?? string.Empty);
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

            Assert.Contains("commitManifestSummary: commit=abcdef123456 changedFiles=1", prompt);
            Assert.Contains("- MODIFY src/large.cs", prompt);
            Assert.DoesNotContain("THIS_LARGE_INLINE_CONTENT_MUST_NOT_APPEAR", prompt);
        }
        finally
        {
            Directory.Delete(directory, true);
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
                new WorkItemDependencyResult("W0", "dep-ref", "선행 완료", WorkItemResultType.CodeChange, "manifest-W0.json")
            });

        try
        {
            await fixture.Executor.ExecuteAsync(fixture.Request, CancellationToken.None);

            Assert.Contains("선행 WorkItem 결과:", fixture.Runner.LastRequest!.Prompt);
            Assert.Contains("W0 | resultType=CODE_CHANGE | ref=dep-ref | commitManifest=manifest-W0.json | snapshot=없음 | report=선행 완료", fixture.Runner.LastRequest.Prompt);
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
        git.Enqueue(0, "");
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
    public async Task CompletedIntegrationFastForwardsPrimaryWorkspaceBeforeCompletion()
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
        fixture.Git.Enqueue(0, "");
        fixture.Git.Enqueue(0, "main");
        fixture.Git.Enqueue(0, "base123");
        fixture.Git.Enqueue(0, integrationClone);
        fixture.Git.Enqueue(0, Path.Combine(integrationClone, ".git"));
        fixture.Git.Enqueue(0, fixture.Branch);
        fixture.Git.Enqueue(0, "head123");
        fixture.Git.Enqueue(0, "Imported");
        fixture.Git.Enqueue(0, "head123");
        fixture.Git.Enqueue(0, "");
        fixture.Git.Enqueue(0, "Fast-forward");
        fixture.Git.Enqueue(0, "head123");
        fixture.Git.Enqueue(0, "");

        try
        {
            var result = await fixture.Executor.ExecuteAsync(
                fixture.Request,
                CancellationToken.None);

            Assert.Equal(WorkItemExecutionOutcome.Completed, result.Outcome);
            Assert.Equal("head123", result.ResultRef);
            Assert.Contains("INTEGRATION_LANDING", result.ResultSummary);
            Assert.Contains("status: FAST_FORWARDED", result.ResultSummary);
            Assert.Contains("targetBranch: main", result.ResultSummary);
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public async Task IntegrationBranchChangeBlocksWithCheckpointForHqRecovery()
    {
        var fixture = CreateFixture(
            """
            [GOTO : HQ]
            WORK_ITEM_STATUS: COMPLETED
            통합 worktree 검증은 완료했습니다.
            """,
            kind: WorkItemKind.Integration);

        fixture.Git.Enqueue(0, Path.Combine(fixture.Parent, "repo"));
        fixture.Git.Enqueue(0, "");
        fixture.Git.Enqueue(0, "feature");

        try
        {
            var result = await fixture.Executor.ExecuteAsync(
                fixture.Request,
                CancellationToken.None);

            Assert.Equal(WorkItemExecutionOutcome.Blocked, result.Outcome);
            Assert.Equal("INTEGRATION_LANDING_FAILED", result.BlockCode);
            Assert.Equal("INTEGRATION_TARGET_BRANCH_CHANGED", result.BlockDetailCode);
            Assert.Equal("head123", result.ResultRef);
            Assert.StartsWith("INTEGRATION_LANDING", result.ResultSummary);
            Assert.Contains("errorCode: INTEGRATION_TARGET_BRANCH_CHANGED", result.ResultSummary);
            Assert.Contains("targetBranch: feature", result.ResultSummary);
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public async Task IntegrationLandingFailureBlocksWithCheckpointForHqRecovery()
    {
        var fixture = CreateFixture(
            """
            [GOTO : HQ]
            WORK_ITEM_STATUS: COMPLETED
            통합 worktree 검증은 완료했습니다.
            """,
            kind: WorkItemKind.Integration);

        fixture.Git.Enqueue(0, Path.Combine(fixture.Parent, "repo"));
        fixture.Git.Enqueue(0, " M local-change.cs");

        try
        {
            var result = await fixture.Executor.ExecuteAsync(
                fixture.Request,
                CancellationToken.None);

            Assert.Equal(WorkItemExecutionOutcome.Blocked, result.Outcome);
            Assert.Equal("INTEGRATION_LANDING_FAILED", result.BlockCode);
            Assert.Equal("INTEGRATION_TARGET_DIRTY", result.BlockDetailCode);
            Assert.Equal("head123", result.ResultRef);
            Assert.StartsWith("INTEGRATION_LANDING", result.ResultSummary);
            Assert.Contains("errorCode: INTEGRATION_TARGET_DIRTY", result.ResultSummary);
            Assert.Contains("integrationRef: head123", result.ResultSummary);
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

    private static Fixture CreateFixture(
        string finalMessage,
        IReadOnlyList<WorkItemDependencyResult>? dependencies = null,
        WorkItemKind kind = WorkItemKind.Normal)
    {
        var parent = Path.Combine(Path.GetTempPath(), "projecthub-codex-workitem-" + Guid.NewGuid().ToString("N"));
        var root = Path.Combine(parent, "repo");
        Directory.CreateDirectory(root);

        const string jobId = "job";
        const string workItemId = "W1";
        var branch = GitWorktreeManager.BuildBranchName(jobId, workItemId);
        var worktree = kind == WorkItemKind.Integration
            ? GitWorktreeManager.BuildIntegrationClonePath(root, jobId, workItemId)
            : GitWorktreeManager.BuildWorktreePath(root, jobId, workItemId);
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
                "기능을 구현하세요."));
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
        private readonly string _finalMessage;

        public FakeAiRoleRunner(string finalMessage)
        {
            _finalMessage = finalMessage;
        }

        public AiRoleRunRequest? LastRequest { get; private set; }

        public AiServiceProvider Provider => AiServiceProvider.OpenAI;
        public bool SupportsSessions => true;

        public string? GetPreflightError(WorkerAiRoleSettings role, string workingDirectory, bool openAiAuthenticated)
            => null;

        public Task<AiRoleRunResult> RunAsync(AiRoleRunRequest request)
        {
            LastRequest = request;
            request.SessionStarted?.Invoke("session-1");
            return Task.FromResult(new AiRoleRunResult(
                "openai",
                request.Role.Model,
                request.Role.Reasoning,
                "session-1",
                0,
                string.Empty,
                string.Empty,
                _finalMessage,
                Array.Empty<CodexCliFile>(),
                CodexUsage.Empty,
                Array.Empty<CodexCommandExecution>()));
        }
    }

    private sealed class FakeGitRunner : IGitWorktreeCommandRunner
    {
        private readonly Queue<GitCommandResult> _results = new();

        public void Enqueue(int exitCode, string stdout, string stderr = "")
            => _results.Enqueue(new GitCommandResult(exitCode, stdout, stderr));

        public Task<GitCommandResult> RunAsync(
            string workingDirectory,
            IReadOnlyList<string> arguments,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            if (_results.Count == 0)
                throw new InvalidOperationException("예상하지 않은 Git 호출입니다: " + string.Join(" ", arguments));
            return Task.FromResult(_results.Dequeue());
        }
    }
}
