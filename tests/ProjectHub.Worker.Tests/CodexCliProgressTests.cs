using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class CodexCliProgressTests
{
    [Fact]
    public void CommandProgress_ReportsStartedCommand()
    {
        const string line = """
            {"type":"item.started","item":{"id":"1","type":"command_execution","command":"dotnet build ProjectHub.sln"}}
            """;

        Assert.True(CodexCliRunner.TryExtractCommandProgress(line, out var progress));
        Assert.Equal("명령 실행 시작 · dotnet build ProjectHub.sln", progress);
    }

    [Fact]
    public void CommandProgress_ReportsCompletedCommandAndExitCode()
    {
        const string line = """
            {"type":"item.completed","item":{"id":"1","type":"command_execution","command":"dotnet test ProjectHub.sln","exit_code":0}}
            """;

        Assert.True(CodexCliRunner.TryExtractCommandProgress(line, out var progress));
        Assert.Equal("명령 실행 종료 · exit 0 · dotnet test ProjectHub.sln", progress);
    }

    [Fact]
    public void CommandProgress_IgnoresAgentMessages()
    {
        const string line = """
            {"type":"item.completed","item":{"id":"1","type":"agent_message","text":"완료"}}
            """;

        Assert.False(CodexCliRunner.TryExtractCommandProgress(line, out _));
    }
}
