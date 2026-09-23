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
    public void ExistingSettings_MigrateToCoordinatorFirstWithoutChangingLegacyTargetFields()
    {
        var settings = JsonSerializer.Deserialize<WorkerTargetSettings>("""
            {"manualRepositoryUrl":null,"manualServerBaseUrl":null,"repositoryUrlSource":null,"serverBaseUrlSource":null,"manualWorkingDirectory":"C:/work"}
            """)!;

        Assert.True(settings.IsCoordinatorFirst);
        Assert.Equal("gpt-6-sol", settings.EffectiveCoordinator.Model);
        Assert.Equal("gpt-6-luna", settings.EffectiveImplementer.Model);
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
        Assert.Equal("gpt-5.6-luna", settings.EffectiveImplementer.Model);
        Assert.Equal("low", settings.EffectiveImplementer.Reasoning);
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
}
