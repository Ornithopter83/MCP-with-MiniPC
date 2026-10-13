using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class MilestoneHqDecisionPolicyTests
{
    [Theory]
    [InlineData("UNITY_IMPORT_PENDING: UNITY_EDITOR_UNAVAILABLE")]
    [InlineData("ASSET_ACQUIRE_RECONCILIATION_REQUIRED: ASSET_FILES_CHANGED_SINCE_CHECKPOINT")]
    [InlineData("GIT_PREFLIGHT_FAILED: fetch error")]
    [InlineData("INITIAL_PLAN_GIT_FAILED: push timeout")]
    [InlineData("ASSET_MANAGER_BLOCKED: DOWNLOAD_FAILED")]
    [InlineData("ASSET_MANAGER_RECONCILIATION_REQUIRED: RUNNING_INTEGRATE")]
    public void RecoverableFailuresBelongToHqDecision(string code) =>
        Assert.False(MilestoneHqDecisionPolicy.IsHardStop(code));

    [Theory]
    [InlineData("ASSET_MANAGER_SCOPE_VIOLATION: ProjectSettings/URPProjectSettings.asset")]
    [InlineData("ASSET_MANAGER_USER_FILE_CONFLICT: Assets/Scenes/Demo.unity")]
    [InlineData("CHECKPOINT_ERROR: ASSET_MANAGER_CHECKPOINT_SCOPE_OR_MODEL_CHANGED")]
    [InlineData("ASSET_TARGET_OUTSIDE_ASSETS")]
    [InlineData("ASSET_REPARSE_POINT")]
    [InlineData("ADDITIONAL_PROJECT_CHANGES: Packages/manifest.json")]
    public void UnplannedProjectChangesNeverBecomeHardStops(string detail) =>
        Assert.False(MilestoneHqDecisionPolicy.IsHardStop(detail));

    [Theory]
    [InlineData("WORK_WRITE_PATH_OVERLAP")]
    [InlineData("ASSET_MANAGER_WORK_PATH_OVERLAP")]
    [InlineData("ASSET_MANAGER_RESOURCE_PATH_OVERLAP")]
    public void DuplicateFileWriterOverlapRemainsRecognized(string detail) =>
        Assert.True(MilestoneHqDecisionPolicy.IsHardStop(detail));
}
