using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class ParallelWorkActivationPolicyTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    public void NewJobsAlwaysUseWorkGraphEvenWithOneSlot(int maxConcurrentWork)
        => Assert.True(ParallelWorkActivationPolicy.ShouldUseParallel(
            isContinuation: false,
            maxConcurrentWork,
            hasPersistedWorkGraph: false));

    [Fact]
    public void PersistedWorkGraphContinuationAlwaysReturnsToParallelRuntime()
        => Assert.True(ParallelWorkActivationPolicy.ShouldUseParallel(
            isContinuation: true,
            maxConcurrentWork: 1,
            hasPersistedWorkGraph: true));

    [Fact]
    public void LegacyOneSlotContinuationWithoutGraphKeepsLegacyRuntime()
        => Assert.False(ParallelWorkActivationPolicy.ShouldUseParallel(
            isContinuation: true,
            maxConcurrentWork: 1,
            hasPersistedWorkGraph: false));

    [Fact]
    public void LegacyContinuationSwitchesToParallelWhenConcurrencyIsRaised()
        => Assert.True(ParallelWorkActivationPolicy.ShouldUseParallel(
            isContinuation: true,
            maxConcurrentWork: 4,
            hasPersistedWorkGraph: false));

    [Theory]
    [InlineData(0)]
    [InlineData(9)]
    public void InvalidConcurrencyIsRejected(int value)
        => Assert.Throws<ArgumentOutOfRangeException>(() =>
            ParallelWorkActivationPolicy.ShouldUseParallel(
                isContinuation: false,
                value,
                hasPersistedWorkGraph: false));
}
