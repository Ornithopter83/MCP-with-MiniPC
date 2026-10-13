using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class CodexCompletionRecoveryTests
{
    [Fact]
    public void CompleteWorkContractIsEligibleForExitRecovery()
    {
        var valid = RoleTextProtocol.BuildResult("completed",
            "WORK #73 구현 완료", new[] { "Assets/Scripts/ForestExitGate.cs" });
        Assert.True(CodexCliRunner.IsTerminalWorkReport(valid));
        Assert.True(CodexCliRunner.IsTerminalWorkReport(
            RoleTextProtocol.BuildResult("blocked", "더 이상 복구 불가")));
    }

    [Fact]
    public void ReportWithoutFinalMarkerOrWithInvalidContractCannotRecover()
    {
        var valid = RoleTextProtocol.BuildResult("completed", "result");
        Assert.False(CodexCliRunner.IsTerminalWorkReport(
            valid.Replace("[RESPONSE=OK]", "", StringComparison.Ordinal)));
        Assert.False(CodexCliRunner.IsTerminalWorkReport(
            "[ACTION=RESULT]\n@@REPORT\n<STATUS>completed</>\n[RESPONSE=OK]"));
        Assert.False(CodexCliRunner.IsTerminalWorkReport(
            "작업 완료했습니다. [RESPONSE=OK]"));
    }

    [Fact]
    public async Task ValidTerminalReportUnblocksStalledLauncher()
    {
        var exited = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var terminal = Task.FromResult(
            RoleTextProtocol.BuildResult("completed", "result"));
        var recovered = await CodexCliRunner.WaitForTerminalWorkOrProcessExitAsync(
            exited.Task, terminal, () => true,
            TimeSpan.FromMilliseconds(5), CancellationToken.None);
        Assert.True(recovered);
    }

    [Fact]
    public async Task NormalCliExitMustAlwaysTakePrecedence()
    {
        var exited = Task.CompletedTask;
        var terminal = Task.FromResult(
            RoleTextProtocol.BuildResult("completed", "result"));
        Assert.False(await CodexCliRunner.WaitForTerminalWorkOrProcessExitAsync(
            exited, terminal, () => true,
            TimeSpan.FromMilliseconds(5), CancellationToken.None));
    }

    [Fact]
    public async Task StillRunningCommandMustNotBeDeclaredComplete()
    {
        var exited = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var terminal = Task.FromResult(
            RoleTextProtocol.BuildResult("completed", "result"));
        var waiting = CodexCliRunner.WaitForTerminalWorkOrProcessExitAsync(
            exited.Task, terminal, () => false,
            TimeSpan.FromMilliseconds(5), CancellationToken.None);
        await Task.Delay(30);
        Assert.False(waiting.IsCompleted);
        exited.TrySetResult(true);
        Assert.False(await waiting);
    }

    [Fact]
    public async Task ExplicitCancellationIsNotRecoveredAsSuccess()
    {
        using var cts = new CancellationTokenSource();
        var exited = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var terminal = Task.FromResult(
            RoleTextProtocol.BuildResult("completed", "result"));
        var waiting = CodexCliRunner.WaitForTerminalWorkOrProcessExitAsync(
            exited.Task, terminal, () => false,
            TimeSpan.FromMilliseconds(5), cts.Token);
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
    }
}
