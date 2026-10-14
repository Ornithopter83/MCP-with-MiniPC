using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class WorkerAutonomyRiskTests
{
    [Theory]
    [InlineData(CodexSandboxMode.ReadOnly)]
    [InlineData(CodexSandboxMode.WorkspaceWrite)]
    [InlineData(CodexSandboxMode.DangerFullAccess)]
    public void FullAutonomySelectsUnrestrictedAccessWithoutApprovalPrompts(
        CodexSandboxMode requested)
    {
        Assert.True(WorkerAutonomyPolicy.Enabled);
        Assert.Equal(CodexSandboxMode.DangerFullAccess,
            WorkerAutonomyPolicy.EffectiveSandbox(requested));
        var arguments = WorkerAutonomyPolicy.CodexPermissionArguments(requested);
        Assert.Contains("--dangerously-bypass-approvals-and-sandbox", arguments);
        Assert.DoesNotContain("--sandbox", arguments);
    }

    [Fact]
    public void BackgroundComputerUseIsDisabledEvenWithFullAutonomy()
    {
        Assert.True(WorkerAutonomyPolicy.Enabled);
        Assert.Equal(new[] { "-c", "features.computer_use=false" },
            WorkerAutonomyPolicy.ComputerUseArguments(true));
        Assert.Empty(WorkerAutonomyPolicy.ComputerUseArguments(false));
        var script = BuildExecutionPolicy.CreateUnityBackgroundPreToolHookScript();
        Assert.Contains("pyautogui", script);
        Assert.Contains("SendInput", script);
        Assert.Contains("AppActivate", script);
    }

    [Theory]
    [InlineData("git status", WorkerCommandRiskLevel.Normal)]
    [InlineData("git add Assets/Scene.unity", WorkerCommandRiskLevel.Caution)]
    [InlineData("Set-Content -Path ./Assets/Scripts/Game.cs -Value x", WorkerCommandRiskLevel.Caution)]
    [InlineData("curl.exe -fL https://api.polyhaven.com/files/pine_tree_01", WorkerCommandRiskLevel.Warning)]
    [InlineData("pip install requests", WorkerCommandRiskLevel.Warning)]
    [InlineData("git push origin main", WorkerCommandRiskLevel.Warning)]
    [InlineData("git reset --hard origin/main", WorkerCommandRiskLevel.Danger)]
    [InlineData("git clean -fdx", WorkerCommandRiskLevel.Danger)]
    [InlineData("Remove-Item -LiteralPath ./Assets -Recurse -Force", WorkerCommandRiskLevel.Danger)]
    [InlineData("Format-Volume -DriveLetter C", WorkerCommandRiskLevel.Danger)]
    public void CommandsReceiveAdvisoryRiskTags(
        string command, WorkerCommandRiskLevel expected)
    {
        Assert.Equal(expected, WorkerCommandRisk.Classify(command).Level);
    }

    [Fact]
    public void ProgressTagsDoNotRequireCommandApproval()
    {
        Assert.True(WorkerCommandRisk.TryReadProgress(
            "명령 실행 시작 · git reset --hard origin/main",
            out var command, out var phase, out var risk));
        Assert.Equal("git reset --hard origin/main", command);
        Assert.Equal("STARTED", phase);
        Assert.Equal("[위험]", risk.Tag);

        Assert.True(WorkerCommandRisk.TryReadProgress(
            "명령 실행 종료 · exit 0 · curl.exe -I https://example.com",
            out command, out phase, out risk));
        Assert.Equal("curl.exe -I https://example.com", command);
        Assert.Equal("명령 실행 종료 · exit 0", phase);
        Assert.Equal("[경고]", risk.Tag);
        Assert.False(WorkerCommandRisk.TryReadProgress(
            "명령 실행 시작 · git status",
            out _, out _, out _));
    }

    [Fact]
    public void RiskAuditPersistsTaggedEventsAndRedactsObviousTokens()
    {
        var root = Path.Combine(Path.GetTempPath(),
            "unity-risk-audit-" + Guid.NewGuid().ToString("N"));
        try
        {
            var risk = WorkerCommandRisk.Classify("git reset --hard HEAD");
            var record = WorkerRiskAuditLog.Format(
                "job-123", "ASSET_MANAGER", risk,
                "git reset --hard HEAD", "FINISHED exit=0");
            Assert.True(WorkerRiskAuditLog.TryAppend(root, record));
            var saved = File.ReadAllText(Path.Combine(root, "risk-audit.log"));
            Assert.Contains("[위험]", saved);
            Assert.Contains("job=job-123", saved);
            Assert.Contains("phase=FINISHED exit=0", saved);

            Assert.DoesNotContain("my-secret-value",
                WorkerCommandRisk.SafePreview(
                    "curl.exe --token my-secret-value https://example.com"));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
