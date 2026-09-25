using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class ParallelWorkGitPreflightTests
{
    [Fact]
    public void RepositoryWithAttachedBranchAndHeadPasses()
    {
        var result = ParallelWorkGitPreflight.Validate(new GitTargetSnapshot(
            "C:/repo",
            null,
            "main",
            "abc123",
            "UNCONFIGURED",
            true));

        Assert.True(result.Success);
        Assert.Null(result.ErrorCode);
        Assert.Contains("branch=main", result.Message);
    }

    [Fact]
    public void NonRepositoryIsRejectedBeforeHqExecution()
    {
        var result = ParallelWorkGitPreflight.Validate(new GitTargetSnapshot(
            "C:/work",
            null,
            null,
            null,
            "UNCONFIGURED",
            false));

        Assert.False(result.Success);
        Assert.Equal("PARALLEL_GIT_REPOSITORY_REQUIRED", result.ErrorCode);
    }

    [Fact]
    public void MissingHeadIsRejected()
    {
        var result = ParallelWorkGitPreflight.Validate(new GitTargetSnapshot(
            "C:/repo",
            null,
            "main",
            null,
            "UNCONFIGURED",
            true));

        Assert.False(result.Success);
        Assert.Equal("PARALLEL_GIT_HEAD_REQUIRED", result.ErrorCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("HEAD")]
    public void DetachedOrUnknownBranchIsRejected(string? branch)
    {
        var result = ParallelWorkGitPreflight.Validate(new GitTargetSnapshot(
            "C:/repo",
            null,
            branch,
            "abc123",
            "UNCONFIGURED",
            true));

        Assert.False(result.Success);
        Assert.Equal("PARALLEL_GIT_ATTACHED_BRANCH_REQUIRED", result.ErrorCode);
    }
}
