using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class ModelUsageHandoffTests
{
    [Fact]
    public void AllRecordedProvidersAndModelsPassToNextPart()
    {
        var jobId = "usage-handoff-test-" + Guid.NewGuid().ToString("N");
        UsageTelemetryStore.RecordCall(jobId, "WORK", "model-a", "high",
            "TEMP_WORK", new CodexUsage(150, 20, 45, 10, 205, 195, true));
        UsageTelemetryStore.RecordCall(jobId, "HIGH", "model-b", "medium",
            "HIGH", new CodexUsage(80, 0, 22, 0, 102, 102, true));
        UsageTelemetryStore.RecordCall(jobId, "WORK", "model-a", "high",
            "TEMP_WORK", CodexUsage.Empty);
        var result = UsageTelemetryStore.FormatForNextRole(jobId);
        Assert.Contains("MODEL=model-a", result);
        Assert.Contains("MODEL=model-b", result);
        Assert.Contains("ROLE=HIGH", result);
        Assert.Contains("CALLS=2", result);
        Assert.Contains("USAGE_KNOWN_CALLS=1", result);
        Assert.Contains("UNKNOWN_CALLS=1", result);
        Assert.Contains("CACHED_INPUT=20", result);
        Assert.DoesNotContain("TEMP_WORK", result);
    }

    [Fact]
    public void NoPriorUsageIsNotMisrepresentedAsZero()
    {
        var actual = UsageTelemetryStore.FormatForNextRole(
            "never-seen-" + Guid.NewGuid().ToString("N"));
        Assert.Equal("MODEL_USAGE_HANDOFF: NO_PREVIOUS_CALLS", actual);
    }
}
