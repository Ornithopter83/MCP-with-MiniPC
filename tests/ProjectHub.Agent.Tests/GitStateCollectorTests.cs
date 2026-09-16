using ProjectHub.Agent;

namespace ProjectHub.Agent.Tests;

public sealed class GitStateCollectorTests
{
    [Fact]
    public void ParseStatus_CountsTrackedUntrackedAndDeletedEntries()
    {
        var result = GitStateCollector.ParseStatus(" M changed.cs\nD  removed.cs\n?? new.txt\n");

        Assert.True(result.IsDirty);
        Assert.Equal(2, result.ChangedCount);
        Assert.Equal(1, result.UntrackedCount);
        Assert.Equal(1, result.DeletedCount);
    }

    [Fact]
    public void ParseStatus_EmptyOutputIsClean()
    {
        var result = GitStateCollector.ParseStatus(string.Empty);

        Assert.False(result.IsDirty);
        Assert.Equal(0, result.ChangedCount);
        Assert.Equal(0, result.UntrackedCount);
        Assert.Equal(0, result.DeletedCount);
    }

    [Fact]
    public async Task CollectAsync_NonRepositoryProvidesDiagnosticError()
    {
        var collector = new GitStateCollector();

        var exception = await Assert.ThrowsAsync<GitStateException>(() =>
            collector.CollectAsync("sample", "DEV-PC-01", Path.GetTempPath()));

        Assert.Contains("Git command failed", exception.Message);
    }
}
