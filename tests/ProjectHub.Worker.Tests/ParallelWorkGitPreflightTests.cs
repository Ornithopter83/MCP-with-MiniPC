using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class ParallelWorkGitPreflightTests
{
    [Fact]
    public void RemoteRepositoryWithAttachedBranchAndHeadPasses()
    {
        var result = ParallelWorkGitPreflight.Validate(new GitTargetSnapshot(
            "C:/repo",
            "https://example.invalid/repo.git",
            "main",
            "abc123",
            "AUTO_GIT_REMOTE",
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
    public void MissingOriginRemoteIsRejected()
    {
        var result = ParallelWorkGitPreflight.Validate(new GitTargetSnapshot(
            "C:/repo",
            null,
            "main",
            "abc123",
            "UNCONFIGURED",
            true));

        Assert.False(result.Success);
        Assert.Equal("PARALLEL_GIT_REMOTE_REQUIRED", result.ErrorCode);
    }

    [Fact]
    public void MissingHeadIsRejected()
    {
        var result = ParallelWorkGitPreflight.Validate(new GitTargetSnapshot(
            "C:/repo",
            "https://example.invalid/repo.git",
            "main",
            null,
            "AUTO_GIT_REMOTE",
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
            "https://example.invalid/repo.git",
            branch,
            "abc123",
            "AUTO_GIT_REMOTE",
            true));

        Assert.False(result.Success);
        Assert.Equal("PARALLEL_GIT_ATTACHED_BRANCH_REQUIRED", result.ErrorCode);
    }
}
