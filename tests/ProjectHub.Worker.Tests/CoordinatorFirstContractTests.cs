using System.Text.Json;
using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class CoordinatorFirstContractTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public void PipelineRoles_AreAllColoredAtInitialInputIdleEvenWhenOptionalRoleIsDisabled(bool current, bool disabled)
    {
        var visual = PipelineCardVisualPolicy.Resolve(initialInputIdle: true, isCurrent: current, disabled: disabled);
        Assert.True(visual.IsColored);
        Assert.Equal(1.0, visual.Opacity);
    }

    [Fact]
    public void PipelineRoles_AfterLaunchUseCurrentStageColorAndDisabledOpacity()
    {
        Assert.True(PipelineCardVisualPolicy.Resolve(initialInputIdle: false, isCurrent: true, disabled: false).IsColored);
        var inactiveOptional = PipelineCardVisualPolicy.Resolve(initialInputIdle: false, isCurrent: false, disabled: true);
        Assert.False(inactiveOptional.IsColored);
        Assert.Equal(0.85, inactiveOptional.Opacity);
    }

    [Fact]
    public void IdlePipelineCard_UsesTealWhenWaitingAndKeepsSharedGrayWhenInactive()
    {
        Assert.Equal(new PipelineIdleCardVisual("#E0F2F4", "#0D7884", "#0F6B73", "#0D7884"), PipelineIdleCardVisualPolicy.Resolve(active: true));
        Assert.Equal(new PipelineIdleCardVisual("#B8C8DA", "#526477", "#FFFFFF", "Transparent"), PipelineIdleCardVisualPolicy.Resolve(active: false));
    }

    [Theory]
    [InlineData("Coordinator", "#FFDDEEFF", "#FF1477E8", "#FF1267D5", "current-openai.png")]
    [InlineData("Implementer", "#FFDCF5E3", "#FF168A4A", "#FF116B39", "current-openai.png")]
    [InlineData("Resource", "#FFECD8E4", "#FF82194B", "#FF74133F", "current-web.png")]
    [InlineData("Judge", "#FFFFF0B8", "#FFB87900", "#FF765000", "current-jev.png")]
    public void HistoryRoleCard_UsesTheSameRolePaletteAsCurrentTask(string stage, string background, string iconBackground, string foreground, string icon)
    {
        var item = new MainWindow.WorkerHistoryEvent(DateTimeOffset.Now, stage, "TEST", "test", null, null, null, null, null, null);

        Assert.Equal(background, item.RowBackground.ToString());
        Assert.Equal(iconBackground, item.IconBackground.ToString());
        Assert.Equal(foreground, item.RoleForeground.ToString());
        Assert.Equal(icon, item.IconAssetName);
    }

    [Theory]
    [InlineData(WorkerRoleState.Hq, "[ACTION=CONTINUE]\n[GOTO : WORK]\nopaque body", WorkerAction.Continue, WorkerRoleState.Work)]
    [InlineData(WorkerRoleState.Hq, "[ACTION=PAUSE]\nreport", WorkerAction.Pause, null)]
    [InlineData(WorkerRoleState.Hq, "[ACTION=END]\nreport", WorkerAction.End, null)]
    [InlineData(WorkerRoleState.Work, "[GOTO : HQ]\nreport", null, WorkerRoleState.Hq)]
    [InlineData(WorkerRoleState.Work, "[GOTO : HQ] report on the same line", null, WorkerRoleState.Hq)]
    [InlineData(WorkerRoleState.Work, "[GOTO=HQ] report", null, WorkerRoleState.Hq)]
    [InlineData(WorkerRoleState.Work, "[GOTO : JUDGE]\nvalidation", null, WorkerRoleState.Judge)]
    [InlineData(WorkerRoleState.Work, "[GOTO : RESOURCE]\n{\"type\":\"IMAGE\",\"prompt\":\"tile\",\"targetDirectory\":\"assets\",\"targetFileName\":\"tile.png\"}", null, WorkerRoleState.Resource)]
    [InlineData(WorkerRoleState.Judge, "[GOTO : WORK]\nraw judgment", null, WorkerRoleState.Work)]
    [InlineData(WorkerRoleState.Resource, "[GOTO : WORK]\nsaved", null, WorkerRoleState.Work)]
    public void WorkerGoto_ParsesAllowedRoutesAndLeavesBodyOpaque(WorkerRoleState source, string text, WorkerAction? action, WorkerRoleState? target)
    {
        var result = WorkerGotoContract.Parse(source, text);
        Assert.Null(result.Error);
        Assert.Equal(action, result.Action);
        Assert.Equal(target, result.Target);
        Assert.NotEmpty(result.Body);
    }

    [Theory]
    [InlineData(WorkerRoleState.Hq, "[ACTION=CONTINUE]\n[GOTO=WORK\nbody", "GOTO_INVALID")]
    [InlineData(WorkerRoleState.Hq, "[ACTION=CONTINUE]\nGOTO=WORK\nbody", "GOTO_INVALID")]
    [InlineData(WorkerRoleState.Hq, "[ACTION=HQ]\nbody", "ACTION_INVALID")]
    [InlineData(WorkerRoleState.Hq, "[ACTION=CONTINUE]\n[GOTO : RESOURCE]\nbody", "GOTO_NOT_ALLOWED")]
    [InlineData(WorkerRoleState.Hq, "[ACTION=CONTINUE]\n[GOTO : JUDGE]\nbody", "GOTO_NOT_ALLOWED")]
    [InlineData(WorkerRoleState.Hq, "[ACTION=PAUSE]\n[GOTO : WORK]\nbody", "GOTO_NOT_ALLOWED_WITH_ACTION")]
    [InlineData(WorkerRoleState.Work, "[ACTION=END]\nbody", "ACTION_NOT_ALLOWED")]
    [InlineData(WorkerRoleState.Resource, "[GOTO : HQ]\nbody", "GOTO_NOT_ALLOWED")]
    [InlineData(WorkerRoleState.Judge, "[GOTO : HQ]\nbody", "GOTO_NOT_ALLOWED")]
    public void WorkerGoto_RejectsInvalidControlsAndForbiddenTransitions(WorkerRoleState source, string text, string error)
        => Assert.Equal(error, WorkerGotoContract.Parse(source, text).Error);

    [Fact]
    public void ResourceTransport_ValidatesMechanicalSchemaAndWorkspaceRelativePath()
    {
        Assert.True(ResourceTransportContract.TryParse(
            "{\"type\":\"IMAGE\",\"prompt\":\"fruit tiles\",\"targetDirectory\":\"assets/tiles\",\"targetFileName\":\"fruit_tiles.png\"}",
            out var request, out var error));
        Assert.Null(error);
        Assert.Equal("IMAGE", request!.Type);
        Assert.Equal("assets/tiles", request.TargetDirectory);
        Assert.False(ResourceTransportContract.TryParse(
            "{\"type\":\"IMAGE\",\"prompt\":\"x\",\"targetDirectory\":\"../outside\",\"targetFileName\":\"x.png\"}",
            out _, out var unsafeError));
        Assert.Equal("RESOURCE_TARGET_DIRECTORY_INVALID", unsafeError);
    }

    [Fact]
    public void TranscriptJson_KeepsKoreanReadableAndValidUtf8Json()
    {
        var json = WorkerTranscriptJson.Serialize(new { Summary = "모델 소개와 날짜 판정" });
        Assert.Contains("모델 소개와 날짜 판정", json);
        Assert.DoesNotContain("\\uBAA8", json);
        Assert.Equal("모델 소개와 날짜 판정", JsonDocument.Parse(json).RootElement.GetProperty("Summary").GetString());
    }

    [Fact]
    public void UnknownErrors_AreFormattedAsKoreanLogEntriesWithoutAiEnvelope()
    {
        var message = WorkerUnknownErrorLog.Format(WorkerRoleState.Work, "GOTO_INVALID_FIRST_LINE", "작업 결과 원문");

        Assert.Contains("올바른 전달 경로", message);
        Assert.Contains("발생 단계: 작업 AI", message);
        Assert.Contains("오류 원문과 상세 출력은 로그에만 기록", message);
        Assert.Contains("작업 결과 원문", message);
        Assert.DoesNotContain("[ROLE : UNKNOWN]", message);
        Assert.DoesNotContain("[ERROR ENVELOPE : JSON]", message);
    }

    [Fact]
    public void UnknownErrors_CreateKoreanHqHandoffSummaryWithoutOriginalDetail()
    {
        var summary = WorkerUnknownErrorLog.CreateHandoffSummary(WorkerRoleState.Work, "GOTO_INVALID_FIRST_LINE");

        Assert.Contains("작업 AI", summary);
        Assert.Contains("올바른 전달 경로", summary);
        Assert.Contains("오류 코드: GOTO_INVALID_FIRST_LINE", summary);
        Assert.Contains("원문과 상세 출력은 로그에만", summary);
        Assert.DoesNotContain("[ROLE : UNKNOWN]", summary);
        Assert.DoesNotContain("세부 내용:", summary);
    }

    [Fact]
    public void ModelCatalog_ExposesOnlyListedAndSupportedModelsAndTheirEfforts()
    {
        const string json = """
            {"models":[
              {"slug":"gpt-6-sol","display_name":"GPT-6-Sol","visibility":"list","supported_in_api":true,"default_reasoning_level":"high","supported_reasoning_levels":[{"effort":"medium"},{"effort":"high"}],"model_messages":{"secret":"do-not-copy"}},
              {"slug":"hidden","display_name":"Hidden","visibility":"hide","supported_in_api":true,"default_reasoning_level":"low","supported_reasoning_levels":[{"effort":"low"}]},
              {"slug":"unsupported","display_name":"Unsupported","visibility":"list","supported_in_api":false,"default_reasoning_level":"low","supported_reasoning_levels":[{"effort":"low"}]}
            ]}
            """;

        var catalog = CodexModelCatalog.Parse(json);

        Assert.Equal("READY", catalog.Status);
        Assert.Equal("gpt-6-sol", Assert.Single(catalog.Models).Id);
        Assert.True(catalog.Supports("gpt-6-sol", "high"));
        Assert.False(catalog.Supports("gpt-6-sol", "ultra"));
        Assert.DoesNotContain("do-not-copy", JsonSerializer.Serialize(catalog));
    }

    [Fact]
    public void CurrentServedModels_AreEnumsAndComposeTheActualCliRequest()
    {
        Assert.Equal(7, CodexServedModels.Current.Count);
        // The settings combo offers this exact pair; it must remain a valid CLI request
        // even when the independently refreshed `codex debug models` cache disagrees.
        Assert.True(CodexModelRequest.TryCreate("gpt-6-sol", "high", out var coordinatorRequest));
        Assert.Equal("--model", coordinatorRequest.ToCliArguments()[0]);
        Assert.Equal("gpt-6-sol", coordinatorRequest.ToCliArguments()[1]);
        Assert.Equal("high", coordinatorRequest.ReasoningId);
        Assert.True(CodexModelRequest.TryCreate("gpt-6-luna", "high", out var request));
        Assert.Equal("model=gpt-6-luna&reasoning=high", request.ToQueryString());
        Assert.Equal(new[] { "--model", "gpt-6-luna", "-c", "model_reasoning_effort=\"high\"" }, request.ToCliArguments());
        Assert.False(CodexModelRequest.TryCreate("gpt-6-luna", "ultra", out _));
        Assert.False(CodexModelRequest.TryCreate("unknown-model", "high", out _));
    }

    [Fact]
    public void ExistingSettings_MigrateToCoordinatorFirstWithoutChangingLegacyTargetFields()
    {
        var settings = JsonSerializer.Deserialize<WorkerTargetSettings>("""
            {"manualRepositoryUrl":null,"manualServerBaseUrl":null,"repositoryUrlSource":null,"serverBaseUrlSource":null,"manualWorkingDirectory":"C:/work"}
            """)!;

        Assert.True(settings.IsCoordinatorFirst);
        Assert.Equal("gpt-6-sol", settings.EffectiveCoordinator.Model);
        Assert.Equal("codex_cli", settings.EffectiveCoordinator.Transport);
        Assert.Equal("gpt-6-luna", settings.EffectiveImplementer.Model);
        Assert.Equal("medium", settings.EffectiveImplementer.Reasoning);
        Assert.Equal("codex_cli", settings.EffectiveImplementer.Transport);
        Assert.Equal("C:/work", settings.ManualWorkingDirectory);
    }

    [Fact]
    public void RoleSettings_PreserveIndependentProviderModelAndReasoningValues()
    {
        var settings = JsonSerializer.Deserialize<WorkerTargetSettings>("""
            {"manualRepositoryUrl":null,"manualServerBaseUrl":null,"repositoryUrlSource":null,"serverBaseUrlSource":null,"executionMode":"CLI_TO_CLI","coordinator":{"provider":"openai","model":"gpt-5.6-sol","reasoning":"high"},"implementer":{"provider":"openai","model":"gpt-5.6-luna","reasoning":"low"}}
            """)!;

        Assert.Equal("gpt-5.6-sol", settings.EffectiveCoordinator.Model);
        Assert.Equal("high", settings.EffectiveCoordinator.Reasoning);
        Assert.Equal("codex_cli", settings.EffectiveCoordinator.Transport);
        Assert.Equal("gpt-5.6-luna", settings.EffectiveImplementer.Model);
        Assert.Equal("low", settings.EffectiveImplementer.Reasoning);
    }

    [Fact]
    public void ProviderCatalog_ExposesStableEnumAndOpenAiCompatibilityWithoutFallback()
    {
        Assert.Equal(
            new[] { AiServiceProvider.OpenAI, AiServiceProvider.Claude, AiServiceProvider.Muse },
            AiProviderCatalog.Current.Select(provider => provider.Provider).ToArray());

        Assert.True(AiProviderCatalog.TryParse("openai", out var openAi));
        Assert.Equal(AiServiceProvider.OpenAI, openAi);
        Assert.True(AiProviderCatalog.TryParse("CLAUDE", out var claude));
        Assert.Equal(AiServiceProvider.Claude, claude);
        Assert.True(AiProviderCatalog.TryParse("muse", out var muse));
        Assert.Equal(AiServiceProvider.Muse, muse);
        Assert.False(AiProviderCatalog.TryParse("unknown-provider", out _));

        var openAiDescriptor = AiProviderCatalog.Get(AiServiceProvider.OpenAI);
        Assert.True(openAiDescriptor.ExecutionConfigured);
        var sol = openAiDescriptor.FindModel("gpt-6-sol");
        Assert.NotNull(sol);
        Assert.True(sol!.SupportsReasoning("high"));

        Assert.False(AiProviderCatalog.Get(AiServiceProvider.Claude).ExecutionConfigured);
        Assert.Empty(AiProviderCatalog.Get(AiServiceProvider.Claude).Models);
        Assert.False(AiProviderCatalog.Get(AiServiceProvider.Muse).ExecutionConfigured);
        Assert.Empty(AiProviderCatalog.Get(AiServiceProvider.Muse).Models);
    }

    [Fact]
    public void RoleSettings_KeepLowercaseProviderWireValueAndExposeTypedProviderKind()
    {
        var settings = JsonSerializer.Deserialize<WorkerTargetSettings>("""
            {"manualRepositoryUrl":null,"manualServerBaseUrl":null,"repositoryUrlSource":null,"serverBaseUrlSource":null,"implementer":{"provider":"claude","model":"future-model","reasoning":"medium","transport":"cli"}}
            """)!;

        Assert.Equal("claude", settings.EffectiveImplementer.Provider);
        Assert.Equal(AiServiceProvider.Claude, settings.EffectiveImplementer.ProviderKind);
        Assert.Contains("\"provider\":\"claude\"", JsonSerializer.Serialize(settings));

        var unknown = new WorkerAiRoleSettings(Provider: "vendor-x");
        Assert.Null(unknown.ProviderKind);
    }

    [Fact]
    public void ProviderCatalog_DeclaresTransportSessionAndVisualCapabilities()
    {
        var openAi = AiProviderCatalog.Get(AiServiceProvider.OpenAI);
        Assert.Equal("codex_cli", openAi.DefaultTransport);
        Assert.True(openAi.ExecutionConfigured);
        Assert.True(openAi.SupportsSessions);
        Assert.NotEmpty(openAi.Models);

        var claude = AiProviderCatalog.Get(AiServiceProvider.Claude);
        Assert.Equal("claude_cli", claude.DefaultTransport);
        Assert.False(claude.ExecutionConfigured);
        Assert.False(claude.SupportsSessions);
        Assert.Empty(claude.Models);

        var muse = AiProviderCatalog.Get(AiServiceProvider.Muse);
        Assert.Equal("muse_cli", muse.DefaultTransport);
        Assert.False(muse.ExecutionConfigured);
        Assert.False(muse.SupportsSessions);
        Assert.Empty(muse.Models);

        Assert.Equal("current-openai.png", ProviderVisualCatalog.Resolve("openai").ColorAsset);
        Assert.Equal("current-console.png", ProviderVisualCatalog.Resolve("claude").ColorAsset);
        Assert.Equal("current-console-gray.png", ProviderVisualCatalog.Resolve("muse").GrayAsset);
        Assert.Equal("?", ProviderVisualCatalog.Resolve("unknown").FallbackSymbol);
    }

    [Fact]
    public void RoleRunnerRegistry_UsesOpenAiAdapterAndBlocksUnconfiguredProvidersWithoutFallback()
    {
        var registry = AiRoleRunnerRegistry.CreateDefault(new CodexCliRunner());
        var openAi = new WorkerAiRoleSettings("openai", "gpt-6-luna", "medium", "codex_cli");
        Assert.Equal(AiServiceProvider.OpenAI, registry.Resolve(openAi)!.Provider);
        Assert.True(registry.Resolve(openAi)!.SupportsSessions);

        var claude = new WorkerAiRoleSettings("claude", "", "", "claude_cli");
        var claudeRunner = registry.Resolve(claude);
        Assert.NotNull(claudeRunner);
        Assert.Equal(AiServiceProvider.Claude, claudeRunner!.Provider);
        Assert.False(claudeRunner.SupportsSessions);
        Assert.Contains("CLAUDE_NOT_CONFIGURED", registry.GetPreflightError(claude, Path.GetTempPath(), true));

        var muse = new WorkerAiRoleSettings("muse", "", "", "muse_cli");
        Assert.Contains("MUSE_NOT_CONFIGURED", registry.GetPreflightError(muse, Path.GetTempPath(), true));

        var unknown = new WorkerAiRoleSettings("vendor-x", "", "", "vendor_cli");
        Assert.Null(registry.Resolve(unknown));
        Assert.Contains("지원되지 않는 AI Provider", registry.GetPreflightError(unknown, Path.GetTempPath(), true));
    }

    [Fact]
    public void RuntimeNormalization_PreservesCoordinatorWebTransport()
    {
        var settings = new WorkerTargetSettings(
            null, null, null, null,
            ExecutionMode: "CLI_TO_CLI",
            Coordinator: new WorkerAiRoleSettings("openai", "gpt-6-sol", "high", "web"));
        var normalized = WorkerTargetConfiguration.NormalizeForRuntime(settings);
        Assert.Equal("web", normalized.EffectiveCoordinator.Transport);
    }

    [Fact]
    public void RoleSettings_PersistIndependentProvidersForHqAndWork()
    {
        var settings = JsonSerializer.Deserialize<WorkerTargetSettings>("""
            {"manualRepositoryUrl":null,"manualServerBaseUrl":null,"repositoryUrlSource":null,"serverBaseUrlSource":null,
             "coordinator":{"provider":"openai","model":"gpt-6-sol","reasoning":"high","transport":"web"},
             "implementer":{"provider":"claude","model":"","reasoning":"","transport":"claude_cli"}}
            """)!;

        Assert.Equal(AiServiceProvider.OpenAI, settings.EffectiveCoordinator.ProviderKind);
        Assert.Equal("web", settings.EffectiveCoordinator.Transport);
        Assert.Equal(AiServiceProvider.Claude, settings.EffectiveImplementer.ProviderKind);
        Assert.Equal("claude_cli", settings.EffectiveImplementer.Transport);
    }

    [Fact]
    public void RoleSettings_PersistTransportAndIndependentThreadSelections()
    {
        var settings = JsonSerializer.Deserialize<WorkerTargetSettings>("""
            {"manualRepositoryUrl":null,"manualServerBaseUrl":null,"repositoryUrlSource":null,"serverBaseUrlSource":null,"coordinator":{"provider":"openai","model":"gpt-6-sol","reasoning":"high","transport":"codex_cli","threadSessionId":"coord-session","threadProjectPath":"C:/work"},"implementer":{"provider":"openai","model":"gpt-6-luna","reasoning":"medium","transport":"codex_cli","threadSessionId":"impl-session","threadProjectPath":"C:/work"}}
            """)!;

        Assert.Equal("codex_cli", settings.EffectiveCoordinator.Transport);
        Assert.Equal("coord-session", settings.EffectiveCoordinator.ThreadSessionId);
        Assert.Equal("impl-session", settings.EffectiveImplementer.ThreadSessionId);
        Assert.Equal("C:/work", settings.EffectiveCoordinator.ThreadProjectPath);
    }

    [Fact]
    public void CommandExecutionParser_ExtractsOnlyCompletedShellCommandsAndExitCodes()
    {
        const string jsonl = """
            {"type":"thread.started","thread_id":"s1"}
            {"type":"item.started","item":{"type":"command_execution","command":"dotnet test Sample.sln","exit_code":null}}
            {"type":"item.completed","item":{"type":"command_execution","command":"dotnet test Sample.sln","exit_code":0,"aggregated_output":"passed"}}
            {"type":"item.completed","item":{"type":"agent_message","text":"test passed"}}
            """;

        var executions = CodexCliRunner.ExtractCommandExecutions(jsonl);

        Assert.Equal(new CodexCommandExecution("dotnet test Sample.sln", 0, "passed"), Assert.Single(executions));
    }

    [Fact]
    public void SessionIdParser_HandlesUtf8BomAndJsonPropertyCasing()
    {
        const string jsonl = "\uFEFF{\"Type\":\"thread.started\",\"Thread_Id\":\"session-123\"}";

        Assert.Equal("session-123", CodexCliRunner.ExtractSessionId(jsonl));
    }

    [Fact]
    public void EmptyStoredSessionId_StartsNewSessionAndCanAcceptPlanSession()
    {
        Assert.Null(CodexCliRunner.NormalizeSessionId(null));
        Assert.Null(CodexCliRunner.NormalizeSessionId(string.Empty));
        Assert.Null(CodexCliRunner.NormalizeSessionId("  "));
        Assert.Equal("session-123", CodexCliRunner.NormalizeSessionId(" session-123 "));
    }

    [Fact]
    public void SessionLocator_PrefersCodexHomeThenUserProfileEnvironment()
    {
        Assert.Equal(Path.Combine("C:\\Users\\ornit", ".codex", "sessions"),
            CodexSessionLocator.ResolveSessionsRoot(null, "C:\\Users\\ornit", "C:\\Users\\CodexSandboxOffline"));
        Assert.Equal(Path.Combine("D:\\CodexData", "sessions"),
            CodexSessionLocator.ResolveSessionsRoot("D:\\CodexData", "C:\\Users\\ornit", "C:\\Users\\CodexSandboxOffline"));
        Assert.Equal(new[]
        {
            Path.Combine("D:\\CodexData", "sessions"),
            Path.Combine("C:\\Users\\ornit", ".codex", "sessions"),
            Path.Combine("C:\\Users\\CodexSandboxOffline", ".codex", "sessions")
        }, CodexSessionLocator.ResolveSessionsRoots("D:\\CodexData", "C:\\Users\\ornit", "C:\\Users\\CodexSandboxOffline"));
    }

    [Fact]
    public void SessionLocator_FindsSessionWhenCodexHomeAndCliProfileDiffer()
    {
        var root = Path.Combine(Path.GetTempPath(), "projecthub-session-roots-test-" + Guid.NewGuid().ToString("N"));
        var workingDirectory = Path.Combine(root, "workspace");
        var codexHomeSessions = Path.Combine(root, "custom-codex-home", "sessions");
        var userSessions = Path.Combine(root, "user-profile", ".codex", "sessions");
        Directory.CreateDirectory(workingDirectory);
        var startedAt = DateTimeOffset.Now.AddSeconds(-4);
        var finishedAt = DateTimeOffset.Now.AddSeconds(2);
        var day = startedAt.ToLocalTime();
        var dayDirectory = Path.Combine(userSessions, day.ToString("yyyy"), day.ToString("MM"), day.ToString("dd"));
        Directory.CreateDirectory(dayDirectory);
        try
        {
            var roots = CodexSessionLocator.ResolveSessionsRoots(
                Path.Combine(root, "custom-codex-home"),
                Path.Combine(root, "sandbox-user"),
                Path.Combine(root, "sandbox-special"),
                Path.Combine(root, "user-profile", "AppData", "Local"));
            Assert.Contains(userSessions, roots);
            var snapshot = new CodexSessionSnapshot(
                roots, Path.GetFullPath(workingDirectory), startedAt,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            File.WriteAllText(Path.Combine(dayDirectory, "rollout-cli-session.jsonl"), JsonSerializer.Serialize(new
            {
                type = "session_meta",
                payload = new { id = "session-from-user-profile", timestamp = startedAt.ToUniversalTime().ToString("O"), originator = "codex_exec", source = "exec", cwd = workingDirectory }
            }));

            Assert.Equal("session-from-user-profile", CodexSessionLocator.FindNewSessionId(snapshot, finishedAt));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void SessionLocator_UsesOnlyOneNewCodexExecSessionForTheSameWorkingFolder()
    {
        var root = Path.Combine(Path.GetTempPath(), "projecthub-session-test-" + Guid.NewGuid().ToString("N"));
        var workingDirectory = Path.Combine(root, "workspace");
        var sessionsRoot = Path.Combine(root, "sessions");
        Directory.CreateDirectory(workingDirectory);
        var startedAt = DateTimeOffset.Now.AddSeconds(-4);
        var finishedAt = DateTimeOffset.Now.AddSeconds(2);
        var day = startedAt.ToLocalTime();
        var dayDirectory = Path.Combine(sessionsRoot, day.ToString("yyyy"), day.ToString("MM"), day.ToString("dd"));
        Directory.CreateDirectory(dayDirectory);
        try
        {
            var snapshot = new CodexSessionSnapshot(new[] { sessionsRoot }, Path.GetFullPath(workingDirectory), startedAt, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            var sessionFile = Path.Combine(dayDirectory, "rollout-test-session.jsonl");
            File.WriteAllText(sessionFile, JsonSerializer.Serialize(new
            {
                type = "session_meta",
                payload = new { id = "session-123", timestamp = startedAt.ToUniversalTime().ToString("O"), originator = "codex_exec", source = "exec", cwd = workingDirectory }
            }));

            Assert.Equal("session-123", CodexSessionLocator.FindNewSessionId(snapshot, finishedAt));

            File.WriteAllText(Path.Combine(dayDirectory, "rollout-second-session.jsonl"), JsonSerializer.Serialize(new
            {
                type = "session_meta",
                payload = new { id = "session-456", timestamp = startedAt.ToUniversalTime().ToString("O"), originator = "codex_exec", source = "exec", cwd = workingDirectory }
            }));
            Assert.Null(CodexSessionLocator.FindNewSessionId(snapshot, finishedAt, out var diagnostic));
            Assert.Contains("ids=2", diagnostic);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void JudgeEndpointTest_IsPersistableAndBoundToTheTestedConfiguration()
    {
        var settings = new JudgeSettings(true, "jev", "https://example.test/v1", 30);
        var fingerprint = WorkerTargetConfiguration.GetJudgeEndpointFingerprint(settings);
        var validation = new JudgeEndpointValidation(fingerprint, true, "PASS", DateTimeOffset.Parse("2026-09-24T00:00:00Z"));
        var storedJson = JsonSerializer.Serialize(new WorkerTargetSettings(null, null, null, null, Judge: settings, JudgeEndpointValidation: validation));
        var loaded = JsonSerializer.Deserialize<WorkerTargetSettings>(storedJson)!;

        Assert.True(WorkerTargetConfiguration.IsJudgeEndpointValidationCurrent(loaded.JudgeEndpointValidation, settings));
        Assert.Equal("설정 테스트가 수행되지 않았습니다. 현재 설정으로 JSON 설정 테스트를 다시 실행해 주세요. 계속 적용합니다.",
            WorkerTargetConfiguration.GetJudgeApplyWarning(settings with { ManualExecutableOrEndpoint = "https://example.test/changed" }, loaded.JudgeEndpointValidation));
        Assert.Null(WorkerTargetConfiguration.GetJudgeApplyWarning(settings, loaded.JudgeEndpointValidation));
        Assert.DoesNotContain("example.test", JsonSerializer.Serialize(loaded.JudgeEndpointValidation), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void JudgeEndpointTest_FailureWarnsButDoesNotCreateRunConfigurationBlock()
    {
        var settings = new JudgeSettings(true, "jev", "https://example.test/v1", 30);
        var validation = new JudgeEndpointValidation(
            WorkerTargetConfiguration.GetJudgeEndpointFingerprint(settings), false, "ERROR_TIMEOUT", DateTimeOffset.UtcNow);

        Assert.Equal("설정 테스트가 실패했습니다. 환경을 확인한 뒤 직접 재검증해 주세요. 설정은 계속 적용합니다.",
            WorkerTargetConfiguration.GetJudgeApplyWarning(settings, validation));
        Assert.Null(WorkerTargetConfiguration.GetJudgeApplyWarning(settings with { Enabled = false }, validation));
    }
}
