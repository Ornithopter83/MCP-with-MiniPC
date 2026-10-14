using System.Text.Json;
using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class UnityDesignModelSettingsTests
{
    [Fact]
    public void OldProfile_DesignUsesConfiguredWorkModelUntilOverridden()
    {
        var original = new WorkerTargetSettings(null, null,
            Implementer: new WorkerAiRoleSettings("openai", "gpt-6-luna", "high"));
        Assert.Null(original.Design);
        Assert.Equal(original.EffectiveImplementer.Model, original.EffectiveDesign.Model);
        Assert.Equal("high", original.EffectiveDesign.Reasoning);
    }

    [Fact]
    public void DedicatedDesignModel_IsIndependentAndSurvivesJsonRoundTrip()
    {
        var settings = new WorkerTargetSettings(null, null,
            Implementer: new WorkerAiRoleSettings("openai", "gpt-6-luna", "medium"),
            Design: new WorkerAiRoleSettings("openai", "gpt-6-sol", "high"));
        var json = JsonSerializer.Serialize(settings);
        Assert.Contains("\"design\"", json);
        var restored = JsonSerializer.Deserialize<WorkerTargetSettings>(json)!;
        Assert.Equal("gpt-6-sol", restored.EffectiveDesign.Model);
        Assert.Equal("high", restored.EffectiveDesign.Reasoning);
        Assert.Equal("gpt-6-luna", restored.EffectiveImplementer.Model);
    }

    [Fact]
    public void DesignSettings_AlwaysUseStatelessWorkerRuns()
    {
        var settings = new WorkerTargetSettings(null, null, Design:
            new WorkerAiRoleSettings("openai", "gpt-6-sol", "medium",
                ThreadSessionId: "design-session",
                ThreadProjectPath: @"C:\UnityProject"));
        var normalized = WorkerTargetConfiguration.NormalizeForRuntime(settings);
        Assert.Equal("gpt-6-sol", normalized.EffectiveDesign.Model);
        Assert.Null(normalized.EffectiveDesign.ThreadSessionId);
        Assert.Null(normalized.EffectiveDesign.ThreadProjectPath);
    }
}
