using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class WorkConcurrencySettingsTests
{
    [Fact]
    public void NewTargetSettings_DefaultToSixteen()
    {
        var settings = new WorkerTargetSettings(null, null, null, null);
        Assert.Equal(16, settings.EffectiveMaxConcurrentWork);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(8, 8)]
    [InlineData(16, 16)]
    [InlineData(30, 30)]
    [InlineData(100, 100)]
    [InlineData(0, 16)]
    [InlineData(-1, 16)]
    public void ConcurrencySettings_HonorPositiveValuesWithoutAnArtificialUpperBound(
        int requested, int expected)
    {
        var settings = new WorkerTargetSettings(
            null, null, null, null, MaxConcurrentWork: requested);

        var normalized = WorkerTargetConfiguration.NormalizeForRuntime(settings);

        Assert.Equal(expected, normalized.EffectiveMaxConcurrentWork);
    }
}
