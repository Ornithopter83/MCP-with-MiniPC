using System.Text.Json;
using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class CoordinatorFirstContractTests
{
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
        Assert.Equal("web", settings.EffectiveCoordinator.Transport);
        Assert.Equal("gpt-6-luna", settings.EffectiveImplementer.Model);
        Assert.Equal("codex_cli", settings.EffectiveImplementer.Transport);
        Assert.False(settings.HighLevelEnabled);
        Assert.Equal("gpt-6-astra", settings.EffectiveHighLevel.Model);
        Assert.Equal("high", settings.EffectiveHighLevel.Reasoning);
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
        Assert.Equal("web", settings.EffectiveCoordinator.Transport);
        Assert.Equal("gpt-5.6-luna", settings.EffectiveImplementer.Model);
        Assert.Equal("low", settings.EffectiveImplementer.Reasoning);
    }

    [Fact]
    public void RoleSettings_PersistTransportAndIndependentThreadSelections()
    {
        var settings = JsonSerializer.Deserialize<WorkerTargetSettings>("""
            {"manualRepositoryUrl":null,"manualServerBaseUrl":null,"repositoryUrlSource":null,"serverBaseUrlSource":null,"coordinator":{"provider":"openai","model":"gpt-6-sol","reasoning":"high","transport":"codex_cli","threadSessionId":"coord-session","threadProjectPath":"C:/work"},"implementer":{"provider":"openai","model":"gpt-6-luna","reasoning":"medium","transport":"codex_cli","threadSessionId":"impl-session","threadProjectPath":"C:/work"},"highLevelEnabled":false,"highLevel":{"provider":"openai","model":"gpt-6-astra","reasoning":"high","transport":"codex_cli"}}
            """)!;

        Assert.Equal("codex_cli", settings.EffectiveCoordinator.Transport);
        Assert.Equal("coord-session", settings.EffectiveCoordinator.ThreadSessionId);
        Assert.Equal("impl-session", settings.EffectiveImplementer.ThreadSessionId);
        Assert.Equal("C:/work", settings.EffectiveCoordinator.ThreadProjectPath);
        Assert.False(settings.HighLevelEnabled);
    }

    [Fact]
    public void WorkCard_RequiresOneAtomicCardAndUniqueAcceptanceIds()
    {
        const string good = """
            {"work_id":"W01","title":"One task","goal":"Fix one behavior","scope":["src"],"acceptance_criteria":[{"ac_id":"AC-1","claim":"The behavior is fixed","evidence_required":["test output"]}],"validation_commands":["dotnet test sample.sln"],"prohibited":[]}
            """;
        Assert.True(CoordinatorFirstContracts.TryParseWorkCard(good, out var card, out var error), error);
        Assert.Single(card!.AcceptanceCriteria);
        Assert.False(CoordinatorFirstContracts.TryParseWorkCard(good.Replace("\"ac_id\":\"AC-1\",\"claim\":\"The behavior is fixed\",\"evidence_required\":[\"test output\"]}", "\"ac_id\":\"AC-1\",\"claim\":\"The behavior is fixed\",\"evidence_required\":[\"test output\"]},{\"ac_id\":\"AC-1\",\"claim\":\"Duplicate\",\"evidence_required\":[\"evidence\"]}"), out _, out var duplicateError));
        Assert.Equal("WORK_CARD_DUPLICATE_AC_ID", duplicateError);
        Assert.False(CoordinatorFirstContracts.TryParseWorkCard("{}", out _, out _));
    }

    [Fact]
    public void Review_MustCoverExactlyEachRequiredAcOnce()
    {
        var acs = new[] { new WorkAcceptanceCriterion("AC-1", "claim one", new[] { "test" }), new WorkAcceptanceCriterion("AC-2", "claim two", new[] { "diff" }) };
        const string review = """
            {"decision":"ACCEPT","summary":"All checks pass","acceptance_criteria":[{"ac_id":"AC-1","status":"PASS","reason":"test passed"},{"ac_id":"AC-2","status":"PASS","reason":"diff matches"}]}
            """;
        Assert.True(CoordinatorFirstContracts.TryParseReview(review, acs, out var parsed, out var error), error);
        Assert.Equal("ACCEPT", parsed!.Decision);
        const string missingAc = """
            {"decision":"ACCEPT","summary":"All checks pass","acceptance_criteria":[{"ac_id":"AC-1","status":"PASS","reason":"test passed"}]}
            """;
        Assert.False(CoordinatorFirstContracts.TryParseReview(missingAc, acs, out _, out var mismatch));
        Assert.Equal("REVIEW_AC_SET_MISMATCH", mismatch);
    }

    [Fact]
    public void ValidationGate_RequiresObservedMatchingCommandsWithZeroExit()
    {
        Assert.True(CoordinatorFirstContracts.HasRequiredValidationEvidence(
            new("W01", "Task", "Goal", new[] { "src" }, new[] { new WorkAcceptanceCriterion("AC-1", "claim", new[] { "test" }) }, new[] { "dotnet test Sample.sln" }, Array.Empty<string>()),
            new[] { new CodexCommandExecution("powershell -Command dotnet test Sample.sln", 0) }, out var passDetail));
        Assert.Empty(passDetail);
        Assert.False(CoordinatorFirstContracts.HasRequiredValidationEvidence(
            new("W01", "Task", "Goal", new[] { "src" }, new[] { new WorkAcceptanceCriterion("AC-1", "claim", new[] { "test" }) }, new[] { "dotnet test Sample.sln" }, Array.Empty<string>()),
            new[] { new CodexCommandExecution("dotnet test Sample.sln", 1) }, out var failDetail));
        Assert.Contains("EXIT_1", failDetail);
    }

    [Fact]
    public void CommandExecutionParser_ExtractsOnlyCompletedShellCommandsAndExitCodes()
    {
        const string jsonl = """
            {"type":"thread.started","thread_id":"s1"}
            {"type":"item.completed","item":{"type":"command_execution","command":"dotnet test Sample.sln","exit_code":0,"aggregated_output":"passed"}}
            {"type":"item.completed","item":{"type":"agent_message","text":"test passed"}}
            """;

        var executions = CodexCliRunner.ExtractCommandExecutions(jsonl);

        Assert.Equal(new CodexCommandExecution("dotnet test Sample.sln", 0), Assert.Single(executions));
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
