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
    [InlineData("HighLevel", "#FFECD8E4", "#FF82194B", "#FF74133F", "current-openai.png")]
    [InlineData("Resource", "#FFE4EEF9", "#FF326FA8", "#FF245B8D", "current-web.png")]
    [InlineData("Judge", "#FFFFF0B8", "#FFB87900", "#FF765000", "current-jev.png")]
    [InlineData("Message", "#FFEEF8F2", "#FF168A4A", "#FF116B39", "current-console.png")]
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
    [InlineData(WorkerRoleState.Hq, "[ACTION=CONTINUE]\n[GOTO : HIGH]\n고권한 복구", WorkerAction.Continue, WorkerRoleState.High)]
    [InlineData(WorkerRoleState.Hq, "[ACTION=PAUSE]\nreport", WorkerAction.Pause, null)]
    [InlineData(WorkerRoleState.Hq, "[ACTION=END]\nreport", WorkerAction.End, null)]
    [InlineData(WorkerRoleState.Work, "[GOTO : HQ]\nreport", null, WorkerRoleState.Hq)]
    [InlineData(WorkerRoleState.Work, "[GOTO : HQ] report on the same line", null, WorkerRoleState.Hq)]
    [InlineData(WorkerRoleState.Work, "[GOTO=HQ] report", null, WorkerRoleState.Hq)]
    [InlineData(WorkerRoleState.Work, "[GOTO : RESOURCE]\nRESOURCE_TYPE: IMAGE\n게임용 포탑 이미지를 만들어줘.", null, WorkerRoleState.Resource)]
    [InlineData(WorkerRoleState.High, "[GOTO : HQ]\n복구 완료", null, WorkerRoleState.Hq)]
    [InlineData(WorkerRoleState.Resource, "[GOTO : WORK]\nsaved", null, WorkerRoleState.Work)]
    public void WorkerGoto_ParsesAllowedRoutesAndLeavesBodyOpaque(WorkerRoleState source, string text, WorkerAction? action, WorkerRoleState? target)
    {
        var result = WorkerGotoContract.Parse(source, text);
        Assert.Null(result.Error);
        Assert.Equal(action, result.Action);
        Assert.Equal(target, result.Target);
        Assert.NotEmpty(result.Body);
    }

    [Fact]
    public void WorkerGoto_HqStartsAtFirstCanonicalActionLineAndIgnoresEarlierOutput()
    {
        const string text = """
            Python
            /mnt/data/HQ_response_time.txt

            파일 생성이 완료되었습니다.

            [ACTION=END]
            응답 시각: 2026-09-27 09:18 KST
            HQ_response_time.txt 다운로드
            """;

        var result = WorkerGotoContract.Parse(WorkerRoleState.Hq, text);

        Assert.Null(result.Error);
        Assert.Equal(WorkerAction.End, result.Action);
        Assert.Null(result.Target);
        Assert.StartsWith("응답 시각:", result.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("Python", result.Body);
        Assert.DoesNotContain("/mnt/data/HQ_response_time.txt", result.Body);
    }

    [Fact]
    public void WorkerGoto_HqUsesFirstActionLineWhenMultipleActionsAppear()
    {
        const string text = """
            설명문
            [ACTION=PAUSE]
            첫 번째 ACTION 뒤의 본문
            [ACTION=END]
            이후 문자열
            """;

        var result = WorkerGotoContract.Parse(WorkerRoleState.Hq, text);

        Assert.Null(result.Error);
        Assert.Equal(WorkerAction.Pause, result.Action);
        Assert.Contains("[ACTION=END]", result.Body);
    }

    [Fact]
    public void WorkerGoto_HqAllowsIndentedCanonicalActionStart()
    {
        const string text = "도구 출력\n   [ACTION=END]\n완료";

        var result = WorkerGotoContract.Parse(WorkerRoleState.Hq, text);

        Assert.Null(result.Error);
        Assert.Equal(WorkerAction.End, result.Action);
        Assert.Equal("완료", result.Body);
    }

    [Fact]
    public void WorkerGoto_HqFindsGotoAfterExplanatoryLines()
    {
        const string text = """
            설명
            [ACTION=CONTINUE]
            중간 설명도 허용한다.
            [GOTO : WORK]
            WORK_GRAPH_PATCH:
            {"expectedRevision":0,"operations":[]}
            """;

        var result = WorkerGotoContract.Parse(WorkerRoleState.Hq, text);

        Assert.Null(result.Error);
        Assert.Equal(WorkerAction.Continue, result.Action);
        Assert.Equal(WorkerRoleState.Work, result.Target);
        Assert.StartsWith("WORK_GRAPH_PATCH:", result.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("중간 설명도 허용한다.", result.Body);
    }

    [Theory]
    [InlineData(WorkerRoleState.Hq, "[ACTION=CONTINUE]\n[GOTO=WORK\nbody", "GOTO_INVALID")]
    [InlineData(WorkerRoleState.Hq, "[ACTION=CONTINUE]\nGOTO=WORK\nbody", "GOTO_INVALID")]
    [InlineData(WorkerRoleState.Hq, "[ACTION=HQ]\nbody", "ACTION_INVALID")]
    [InlineData(WorkerRoleState.Hq, "[ACTION=CONTINUE]\n[GOTO : RESOURCE]\nbody", "GOTO_NOT_ALLOWED")]
    [InlineData(WorkerRoleState.Hq, "[ACTION=CONTINUE]\n[GOTO : JUDGE]\nbody", "GOTO_NOT_ALLOWED")]
    [InlineData(WorkerRoleState.Hq, "[ACTION=PAUSE]\n[GOTO : WORK]\nbody", "GOTO_NOT_ALLOWED_WITH_ACTION")]
    [InlineData(WorkerRoleState.Work, "[ACTION=END]\nbody", "ACTION_NOT_ALLOWED")]
    [InlineData(WorkerRoleState.Work, "[GOTO : JUDGE]\nbody", "GOTO_NOT_ALLOWED")]
    [InlineData(WorkerRoleState.Work, "[GOTO : HIGH]\nbody", "GOTO_NOT_ALLOWED")]
    [InlineData(WorkerRoleState.High, "[GOTO : WORK]\nbody", "GOTO_NOT_ALLOWED")]
    [InlineData(WorkerRoleState.Resource, "[GOTO : HQ]\nbody", "GOTO_NOT_ALLOWED")]
    [InlineData(WorkerRoleState.Judge, "[GOTO : WORK]\nraw judgment", "GOTO_NOT_ALLOWED")]
    [InlineData(WorkerRoleState.Judge, "[GOTO : HQ]\nbody", "GOTO_NOT_ALLOWED")]
    public void WorkerGoto_RejectsInvalidControlsAndForbiddenTransitions(WorkerRoleState source, string text, string error)
        => Assert.Equal(error, WorkerGotoContract.Parse(source, text).Error);

    [Fact]
    public void ResourceTransport_RequiresExplicitTypeAndForwardsOnlyNaturalLanguagePrompt()
    {
        const string prompt = "게임용 아이콘 이미지를 선명하게 만들어줘.";
        const string body = "RESOURCE_TYPE: IMAGE\n" + prompt;

        Assert.True(ResourceTransportContract.TryParse(body, out var request, out var error));
        Assert.Null(error);
        Assert.Equal("IMAGE", request!.Type);
        Assert.Equal(prompt, request.Prompt);

        Assert.False(ResourceTransportContract.TryParse(prompt, out _, out var missingType));
        Assert.Equal("RESOURCE_TYPE_MISSING", missingType);

        Assert.False(ResourceTransportContract.TryParse("RESOURCE_TYPE: AUDIO\n효과음 요청", out _, out var unsupportedAudio));
        Assert.Equal("RESOURCE_TYPE_UNSUPPORTED", unsupportedAudio);

        Assert.False(ResourceTransportContract.TryParse("RESOURCE_TYPE: UNKNOWN\n요청", out _, out var unsupportedType));
        Assert.Equal("RESOURCE_TYPE_UNSUPPORTED", unsupportedType);

        Assert.False(ResourceTransportContract.TryParse("RESOURCE_TYPE: IMAGE", out _, out var emptyPrompt));
        Assert.Equal("RESOURCE_REQUEST_EMPTY", emptyPrompt);
    }

    [Fact]
    public void ResourceTransportFindsTypeAfterExplanatoryTextAndForwardsOnlyFollowingPrompt()
    {
        const string body = """
            RESOURCE 요청을 준비합니다.
            RESOURCE_TYPE: IMAGE
            디펜스게임용 기관총 포탑 이미지를 생성해줘.
            투명 배경 PNG로 만들어줘.
            """;

        Assert.True(ResourceTransportContract.TryParse(body, out var request, out var error), error);
        Assert.Null(error);
        Assert.Equal("IMAGE", request!.Type);
        Assert.DoesNotContain("RESOURCE 요청을 준비합니다.", request.Prompt);
        Assert.Contains("디펜스게임용 기관총 포탑", request.Prompt);
        Assert.Contains("투명 배경 PNG", request.Prompt);
    }

    [Fact]
    public void ResourceTransportRejectsDuplicateTypeMarkers()
    {
        const string body = """
            RESOURCE_TYPE: IMAGE
            첫 요청
            RESOURCE_TYPE: AUDIO
            둘째 요청
            """;

        Assert.False(ResourceTransportContract.TryParse(body, out _, out var error));
        Assert.Equal("RESOURCE_TYPE_DUPLICATE", error);
    }

    [Fact]
    public void ResourceTransport_AllowsOnlyImageGeneration()
    {
        Assert.True(ResourceTransportContract.IsSupportedType("IMAGE"));
        Assert.True(ResourceTransportContract.TryParse(
            "RESOURCE_TYPE: IMAGE\n테스트 이미지 생성 요청",
            out var request,
            out var error));
        Assert.Null(error);
        Assert.Equal("IMAGE", request!.Type);

        foreach (var unsupported in new[] { "AUDIO", "VIDEO", "DOCUMENT", "FILE" })
        {
            Assert.False(ResourceTransportContract.IsSupportedType(unsupported));
            Assert.False(ResourceTransportContract.TryParse(
                $"RESOURCE_TYPE: {unsupported}\n테스트 생성 요청",
                out _,
                out var unsupportedError));
            Assert.Equal("RESOURCE_TYPE_UNSUPPORTED", unsupportedError);
        }
    }

    [Fact]
    public void TaskContinuation_AllowsPausedSegmentOnly()
        => Assert.True(TaskContinuationContract.IsResumableStatus("PAUSED"));

    [Fact]
    public void TaskContinuation_TreatsCanceledAsTerminalFullCancel()
    {
        Assert.False(TaskContinuationContract.IsResumableStatus("CANCELED"));
        Assert.False(TaskContinuationContract.IsFreshStartStatus("CANCELED"));
        Assert.False(TaskContinuationContract.CanAcceptFollowupStatus("CANCELED"));
        Assert.Throws<InvalidOperationException>(() =>
            TaskContinuationContract.BuildHqFollowupInput(
                "CANCELED",
                "취소됨",
                "계속해줘."));
    }

    [Theory]
    [InlineData("DONE")]
    [InlineData("DONE_WITH_ERROR")]
    public void TaskContinuation_CompletedSegmentsRequireFreshStart(string status)
    {
        Assert.False(TaskContinuationContract.IsResumableStatus(status));
        Assert.True(TaskContinuationContract.IsFreshStartStatus(status));
        Assert.True(TaskContinuationContract.CanAcceptFollowupStatus(status));
    }

    [Theory]
    [InlineData("RUNNING")]
    [InlineData("CANCELED")]
    [InlineData("")]
    public void TaskContinuation_RejectsNonResumableStatuses(string status)
        => Assert.False(TaskContinuationContract.IsResumableStatus(status));

    [Fact]
    public void TaskContinuation_BuildsUserFollowupForSameHqContext()
    {
        var input = TaskContinuationContract.BuildHqFollowupInput(
            "PAUSED",
            "이전 작업을 완료했습니다.",
            "효과음을 추가하고 계속 다듬어줘.");

        Assert.DoesNotContain("이전 작업 상태:", input);
        Assert.DoesNotContain("이전 작업을 완료했습니다.", input);
        Assert.Contains("사용자 추가 요청:", input);
        Assert.Contains("효과음을 추가하고 계속 다듬어줘.", input);
    }

    [Theory]
    [InlineData(false, false, false, true)]
    [InlineData(true, false, false, false)]
    [InlineData(false, true, false, false)]
    [InlineData(false, false, true, false)]
    public void TaskConfiguration_EditabilityFollowsTaskLifecycle(
        bool executionActive,
        bool canceling,
        bool taskHistoryVisible,
        bool expected)
        => Assert.Equal(
            expected,
            TaskContinuationContract.CanEditTaskConfiguration(
                executionActive,
                canceling,
                taskHistoryVisible));

    [Fact]
    public void TaskContinuation_IncludesParallelWorkGraphStateForWebOrCliRecovery()
    {
        var input = TaskContinuationContract.BuildHqFollowupInput(
            "PAUSED",
            "이전 병렬 관제",
            "계속 진행해줘.",
            "C:/work/.projecthub/last-handoff.md",
            "C:/work/.projecthub/events/job.jsonl",
            "WorkGraph revision=7\n- id=W1 state=BLOCKED blockCode=RECOVERY_REQUIRED");

        Assert.Contains("WorkGraph 현재 상태:", input);
        Assert.Contains("revision=7", input);
        Assert.Contains("id=W1 state=BLOCKED", input);
        Assert.Contains("RECOVERY_REQUIRED", input);
        Assert.Contains("사용자 추가 요청:", input);
    }

    [Fact]
    public void TaskContinuation_RejectsEmptyFollowup()
        => Assert.Throws<InvalidOperationException>(() =>
            TaskContinuationContract.BuildHqFollowupInput("PAUSED", "사용자 입력 대기", "   "));

    [Fact]
    public void TaskContinuation_IncludesProjectMemoryPathsForRecoveredSession()
    {
        var input = TaskContinuationContract.BuildHqFollowupInput(
            "PAUSED",
            "이전 작업 완료",
            "계속 진행해줘.",
            "C:/work/.projecthub/last-handoff.md",
            "C:/work/.projecthub/events/job.jsonl");

        Assert.Contains("프로젝트 기억 파일:", input);
        Assert.Contains("last-handoff.md", input);
        Assert.Contains("이벤트 로그:", input);
    }

    [Fact]
    public void ProjectWorkspacePersistence_RejectsCanceledContinuation()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "projecthub-canceled-continuation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var canceled = new CoordinatorContinuationState(
                "job-canceled",
                directory,
                new WorkerAiRoleSettings(Model: "gpt-6-sol", Reasoning: "high"),
                new WorkerAiRoleSettings(Model: "gpt-6-luna", Reasoning: "medium"),
                null,
                null,
                "CANCELED",
                "완전 취소");

            Assert.False(ProjectWorkspacePersistence.SaveContinuation(canceled));
            Assert.False(File.Exists(ProjectWorkspacePersistence.StatePath(directory)));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void ProjectWorkspacePersistence_WritesStateHandoffAndRealtimeJsonl()
    {
        var directory = Path.Combine(Path.GetTempPath(), "projecthub-memory-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            const string jobId = "job-memory";
            var eventId = ProjectWorkspacePersistence.AppendEvent(
                directory,
                jobId,
                DateTimeOffset.UtcNow,
                "WORK PROGRESS",
                "한글 Full Message",
                "RUNNING");

            Assert.False(string.IsNullOrWhiteSpace(eventId));
            var eventPath = ProjectWorkspacePersistence.EventLogPath(directory, jobId);
            Assert.True(File.Exists(eventPath));
            var lines = File.ReadAllLines(eventPath);
            Assert.Single(lines);
            using (var document = JsonDocument.Parse(lines[0]))
                Assert.Equal("한글 Full Message", document.RootElement.GetProperty("fullMessage").GetString());

            var state = new CoordinatorContinuationState(
                jobId,
                directory,
                new WorkerAiRoleSettings(Model: "gpt-6-sol", Reasoning: "high"),
                new WorkerAiRoleSettings(Model: "gpt-6-luna", Reasoning: "medium"),
                null,
                null,
                "PAUSED",
                "마지막 HQ 메시지",
                new WorkerAiRoleSettings(Model: "gpt-6-astra", Reasoning: "high"));

            Assert.True(ProjectWorkspacePersistence.SaveContinuation(state));
            Assert.True(File.Exists(ProjectWorkspacePersistence.StatePath(directory)));
            Assert.True(File.Exists(ProjectWorkspacePersistence.HandoffPath(directory)));

            var restored = ProjectWorkspacePersistence.TryLoad(directory);
            Assert.NotNull(restored);
            Assert.Equal(jobId, restored!.JobId);
            Assert.Equal("PAUSED", restored.Status);
            Assert.Equal("마지막 HQ 메시지", restored.LastHqMessage);
            Assert.Equal("gpt-6-astra", restored.HighLevel?.Model);
            var continuation = restored.ToContinuation();
            Assert.Equal("gpt-6-astra", continuation.HighLevel?.Model);
            Assert.Equal("한글 Full Message", Assert.Single(ProjectWorkspacePersistence.ReadRecentEvents(directory, jobId)).FullMessage);

            ProjectWorkspacePersistence.ClearContinuation(directory);
            Assert.False(File.Exists(ProjectWorkspacePersistence.StatePath(directory)));
            Assert.True(File.Exists(ProjectWorkspacePersistence.HandoffPath(directory)));
            Assert.True(File.Exists(eventPath));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
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
    public void MultiResourceResultPayload_DeserializesMixedGeneratedFiles()
    {
        const string json = """
            {
              "success": true,
              "resultType": "RESOURCE_FILES",
              "resultFiles": [
                {"base64":"YQ==","mimeType":"image/png","fileName":"tile.png"},
                {"base64":"Yg==","mimeType":"audio/mpeg","fileName":"match.mp3"},
                {"base64":"Yw==","mimeType":"application/pdf","fileName":"guide.pdf"}
              ]
            }
            """;
        var request = JsonSerializer.Deserialize<ResultRequest>(
            json,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(request);
        Assert.Equal("RESOURCE_FILES", request!.ResultType);
        Assert.Equal(3, request.ResultFiles!.Count);
        Assert.Equal("audio/mpeg", request.ResultFiles[1].MimeType);
        Assert.Equal("guide.pdf", request.ResultFiles[2].FileName);
    }

    [Fact]
    public void BridgeTask_PreservesMultipleSavedPaths()
    {
        var task = new BridgeTask(
            "id", "conversation", "project", "prompt", "COMPLETED", "ok",
            null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
            "RESOURCE", SavedPath: "assets/resources/id/tile.png",
            SavedPaths: new List<string>
            {
                "assets/resources/id/tile.png",
                "assets/resources/id/match.mp3",
                "assets/resources/id/guide.pdf"
            });

        Assert.Equal(3, task.SavedPaths!.Count);
        Assert.EndsWith("tile.png", task.SavedPath!, StringComparison.Ordinal);
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
        Assert.Equal(8, CodexServedModels.Current.Count);
        // The settings combo offers this exact pair; it must remain a valid CLI request
        // even when the independently refreshed `codex debug models` cache disagrees.
        Assert.True(CodexModelRequest.TryCreate("gpt-6.1-sol", "max", out var sol61Request));
        Assert.Equal("gpt-6.1-sol", sol61Request.ToCliArguments()[1]);
        Assert.Equal("max", sol61Request.ReasoningId);
        Assert.False(CodexModelRequest.TryCreate("gpt-6.1-sol", "ultra", out _));

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
    public void ParallelWorkConcurrencyDefaultsToOneAndPreservesValidUserSetting()
    {
        var defaults = JsonSerializer.Deserialize<WorkerTargetSettings>("""
            {"manualRepositoryUrl":null,"manualServerBaseUrl":null,"repositoryUrlSource":null,"serverBaseUrlSource":null}
            """)!;
        Assert.Equal(1, defaults.EffectiveMaxConcurrentWork);

        var configured = JsonSerializer.Deserialize<WorkerTargetSettings>("""
            {"manualRepositoryUrl":null,"manualServerBaseUrl":null,"repositoryUrlSource":null,"serverBaseUrlSource":null,"maxConcurrentWork":4}
            """)!;
        Assert.Equal(4, configured.EffectiveMaxConcurrentWork);

        var invalid = JsonSerializer.Deserialize<WorkerTargetSettings>("""
            {"manualRepositoryUrl":null,"manualServerBaseUrl":null,"repositoryUrlSource":null,"serverBaseUrlSource":null,"maxConcurrentWork":99}
            """)!;
        Assert.Equal(1, invalid.EffectiveMaxConcurrentWork);
        Assert.Equal(99, invalid.MaxConcurrentWork);
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
        var sol61 = openAiDescriptor.FindModel("gpt-6.1-sol");
        Assert.NotNull(sol61);
        Assert.True(sol61!.SupportsReasoning("high"));
        Assert.True(sol61.SupportsReasoning("max"));
        Assert.False(sol61.SupportsReasoning("ultra"));

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
    public void CodexThreadStartedParser_ExtractsSessionIdImmediately()
    {
        const string started = """
            {"type":"thread.started","thread_id":"session-live-123"}
            """;
        const string message = """
            {"type":"item.completed","item":{"type":"agent_message","text":"진행 중"}}
            """;

        Assert.True(CodexCliRunner.TryExtractThreadStarted(started, out var sessionId));
        Assert.Equal("session-live-123", sessionId);
        Assert.False(CodexCliRunner.TryExtractThreadStarted(message, out _));
    }

    [Fact]
    public void CodexProgressParser_ExtractsCompletedAgentMessagesOnly()
    {
        const string agent = """
            {"type":"item.completed","item":{"type":"agent_message","text":"프로젝트 구조를 확인했습니다.\n다음 구현을 진행합니다."}}
            """;
        const string command = """
            {"type":"item.completed","item":{"type":"command_execution","command":"dotnet test Sample.sln","exit_code":0}}
            """;
        const string started = """
            {"type":"item.started","item":{"type":"agent_message","text":"중간 상태"}}
            """;

        Assert.True(CodexCliRunner.TryExtractAgentMessage(agent, out var text));
        Assert.Equal("프로젝트 구조를 확인했습니다.\n다음 구현을 진행합니다.", text);
        Assert.False(CodexCliRunner.TryExtractAgentMessage(command, out _));
        Assert.False(CodexCliRunner.TryExtractAgentMessage(started, out _));
    }

    [Fact]
    public void ProgressPreview_PreservesLinesWithoutSemanticSummarization()
    {
        var preview = WorkerHistoryCardFormatter.ProgressPreview(
            "첫 번째 줄\n\n두 번째    줄\n세 번째 줄");

        Assert.Equal(
            "첫 번째 줄" + Environment.NewLine +
            "두 번째 줄" + Environment.NewLine +
            "세 번째 줄",
            preview);
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
    public void RemovedJudgeConfigurationIsIgnoredAtRuntime()
    {
        var settings = new WorkerTargetSettings(
            null,
            null,
            null,
            null,
            Judge: new JudgeSettings(true, "jev", "https://example.test/v1", 30),
            JudgeEndpointValidation: new JudgeEndpointValidation("legacy", true, "PASS", DateTimeOffset.UtcNow));

        var normalized = WorkerTargetConfiguration.NormalizeForRuntime(settings);

        Assert.False(normalized.EffectiveJudge.Enabled);
        Assert.Null(normalized.Judge);
        Assert.Null(normalized.JudgeEndpointValidation);
    }

    [Fact]
    public async Task MechanicalWorkRegistry_SeparatesRequiredReviewFromFinalizeOnlyWork()
    {
        var registry = new MechanicalWorkRegistry();
        registry.Register("resource-1", "RESOURCE", MechanicalWorkCompletionMode.FinalizeOnly);
        registry.Register("observation-1", "OBSERVATION", MechanicalWorkCompletionMode.WorkResultRequired);

        Assert.Equal(2, registry.OutstandingCount);
        Assert.Equal(1, registry.WorkResultRequiredCount);

        var requiredWait = registry.WaitForWorkResultRequiredAsync(CancellationToken.None);
        Assert.False(requiredWait.IsCompleted);

        registry.Complete("observation-1", true, "ok", resultPaths: new[] { "result.json" }, exitCode: 0);
        await requiredWait;
        Assert.Equal(1, registry.OutstandingCount);
        Assert.Equal(0, registry.WorkResultRequiredCount);

        var observation = Assert.Single(registry.DrainCompletions(
            "OBSERVATION",
            MechanicalWorkCompletionMode.WorkResultRequired));
        Assert.True(observation.Success);
        Assert.Equal(0, observation.ExitCode);

        registry.Complete("resource-1", false, "failed", "RESOURCE_FAILED");
        await registry.WaitForAllAsync(CancellationToken.None);
        Assert.Equal(0, registry.OutstandingCount);

        var resource = Assert.Single(registry.DrainCompletions(
            "RESOURCE",
            MechanicalWorkCompletionMode.FinalizeOnly));
        Assert.False(resource.Success);
    }

    [Fact]
    public void ObservationRequestContract_RequiresExplicitCompletionModeAndMechanicalFields()
    {
        const string requiredJson = """
            {
              "kind": "OBSERVATION",
              "id": "obs-17",
              "command": "dotnet",
              "arguments": ["test", "Sample.sln"],
              "timeoutSeconds": 180,
              "completionMode": "WORK_RESULT_REQUIRED",
              "resultPaths": ["build/results"]
            }
            """;

        Assert.True(ObservationRequestContract.TryParse(
            requiredJson,
            out var request,
            out var mode,
            out var error));
        Assert.Null(error);
        Assert.NotNull(request);
        Assert.Equal("obs-17", request!.Id);
        Assert.Equal(MechanicalWorkCompletionMode.WorkResultRequired, mode);

        const string finalizeJson = """
            {"kind":"OBSERVATION","id":"obs-final","command":"collector","completionMode":"FINALIZE_ONLY"}
            """;
        Assert.True(ObservationRequestContract.TryParse(
            finalizeJson,
            out _,
            out var finalizeMode,
            out _));
        Assert.Equal(MechanicalWorkCompletionMode.FinalizeOnly, finalizeMode);

        Assert.False(ObservationRequestContract.TryParse(
            """{"kind":"OBSERVATION","id":"../bad","command":"collector","completionMode":"WORK_RESULT_REQUIRED"}""",
            out _,
            out _,
            out var idError));
        Assert.Equal("OBSERVATION_ID_INVALID", idError);
    }

    [Fact]
    public void WorkPrompt_ExposesObservationInboxWithoutAddingANewGotoDestination()
    {
        var prompt = RoleContractLoader.BuildWorkPrompt(
            "HQ_INSTRUCTION",
            "작업을 진행하라.",
            new WorkItemPromptContext(
                "W1",
                WorkItemKind.Normal,
                "작업을 진행하라.",
                Array.Empty<string>(),
                "abc123",
                "branch",
                "worktree"),
            @"C:\work\.projecthub\mechanical\job\requests");

        Assert.Contains("비동기 계측 요청 폴더:", prompt);
        Assert.Contains("WORK_ITEM_STATUS: COMPLETED", prompt);
        Assert.Contains("WORK_ITEM_STATUS: BLOCKED", prompt);
        Assert.DoesNotContain("[GOTO : OBSERVATION]", prompt);
    }

    [Fact]
    public void RoleContracts_KeepDecompositionInHqAndExposeWorkChecklist()
    {
        var hqPrompt = RoleContractLoader.BuildHqPrompt(
            "USER_REQUEST",
            "기능을 완성하라.",
            new WorkGraphPromptContext(0, 4, "abc123"),
            includeContract: true);
        var workPrompt = RoleContractLoader.BuildWorkPrompt(
            "WORK_ITEM",
            "기능을 완성하라.",
            new WorkItemPromptContext(
                "W10",
                WorkItemKind.Normal,
                "검색 기능 구현",
                Array.Empty<string>(),
                "abc123",
                "branch",
                "worktree",
                Checklist: new[] { "검색 모델 구현", "필터 결과 검증" }));

        Assert.Contains("`checklist` 문자열 배열", hqPrompt);
        Assert.Contains("작업 분해와 추가 WorkItem 판단은 HQ", hqPrompt);
        Assert.Contains("WorkItem 작업 목록:", workPrompt);
        Assert.Contains("[1] 검색 모델 구현", workPrompt);
        Assert.Contains("[2] 필터 결과 검증", workPrompt);
        Assert.Contains("새 WorkItem이 필요한지 판단하거나 작업 분할을 요청하지 않는다", workPrompt);
        Assert.DoesNotContain("WORK_ITEM_STATUS: SPLIT_REQUEST", workPrompt);
        Assert.Contains("computerUse: disabled", hqPrompt);
        Assert.Contains("computerUse: disabled", workPrompt);
    }

    [Fact]
    public void HqAndHighContracts_RequireHqRecoveryBeforeHighEscalation()
    {
        var hqContract = RoleContractLoader.LoadHqFooter();
        var highContract = RoleContractLoader.LoadHighFooter();

        Assert.Contains("HIGH 후보가 되더라도 바로 HIGH를 호출하지 않는다", hqContract);
        Assert.Contains("현재 WorkGraph 수단으로 가능한 일반 복구를 시도", hqContract);
        Assert.Contains("HIGH는 HQ의 일반 복구보다 뒤의 escalation", hqContract);
        Assert.Contains("같은 입력을 그대로 재실행하는 대신 새 근거나 구체적인 복구 변경", hqContract);
        Assert.Contains("일반 권한 범위의 복구를 먼저 검토·시도", highContract);
    }

    [Fact]
    public void HqContract_ReviewsRemoteCodeAndRequiresIntegrationBeforeBuildOrEnd()
    {
        var contract = RoleContractLoader.LoadHqFooter();

        Assert.Contains("실제 코드 상태와 변경 범위는 원격 저장소의 해당 commit을 기준으로 판단", contract);
        Assert.Contains("resultType=CODE_CHANGE", contract);
        Assert.Contains("변경 파일과 diff를 직접 확인", contract);
        Assert.Contains("BLOCKED WorkItem에 CODE_CHANGE resultRef", contract);
        Assert.Contains("INTEGRATION이 CODE_CHANGE를 반환하면", contract);
        Assert.Contains("primary branch에 아직 반영되지 않은 CODE_CHANGE", contract);
        Assert.Contains("END 전에 목표에 필요한 모든 CODE_CHANGE", contract);
    }

    [Fact]
    public async Task ResourceSidecar_RegistersResourceAsFinalizeOnlyMechanicalWork()
    {
        var directory = Path.Combine(Path.GetTempPath(), "projecthub-resource-mechanical-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var registry = new MechanicalWorkRegistry();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await using var queue = new ResourceSidecarQueue(null, directory, cts.Token, registry);
            queue.Enqueue("IMAGE", "테스트 이미지 생성");

            await registry.WaitForAllAsync(cts.Token);
            var completion = Assert.Single(registry.DrainCompletions(
                "RESOURCE",
                MechanicalWorkCompletionMode.FinalizeOnly));
            Assert.False(completion.Success);
            Assert.Equal("RESOURCE_WEB_UNAVAILABLE", completion.ErrorCode);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void CodexWorkspaceWrite_AddsProjectHubExecutableRootForSiblingFolders()
    {
        var root = Path.Combine(Path.GetTempPath(), "projecthub-add-dir-" + Guid.NewGuid().ToString("N"));
        var appRoot = Path.Combine(root, "Worker");
        var project = Path.Combine(appRoot, "Game");
        Directory.CreateDirectory(project);
        try
        {
            var additional = CodexCliRunner.ResolveAdditionalWritableDirectories(
                CodexSandboxMode.WorkspaceWrite,
                project,
                appRoot);

            Assert.Equal(Path.GetFullPath(appRoot), Assert.Single(additional));
            Assert.Empty(CodexCliRunner.ResolveAdditionalWritableDirectories(
                CodexSandboxMode.ReadOnly,
                project,
                appRoot));
            Assert.Empty(CodexCliRunner.ResolveAdditionalWritableDirectories(
                CodexSandboxMode.WorkspaceWrite,
                appRoot,
                appRoot));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

}
