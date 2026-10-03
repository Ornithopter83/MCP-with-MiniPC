using System.IO;

namespace ProjectHub.Worker;

public sealed record TargetWorkspaceFinalizationResult(
    bool Success,
    string? ErrorCode,
    string Message,
    string? LandedWorkItemId = null,
    string? LandedResultRef = null,
    bool FastForwarded = false);

public sealed class TargetWorkspaceFinalizer
{
    private readonly string _workspace;
    private readonly string? _expectedPrimaryBranch;
    private readonly GitWorktreeManager _worktrees;

    public TargetWorkspaceFinalizer(
        string workspace,
        string? expectedPrimaryBranch,
        GitWorktreeManager? worktrees = null)
    {
        if (string.IsNullOrWhiteSpace(workspace))
            throw new ArgumentException("사용자 작업 폴더가 비어 있습니다.", nameof(workspace));

        _workspace = Path.GetFullPath(workspace);
        _expectedPrimaryBranch = string.IsNullOrWhiteSpace(expectedPrimaryBranch)
            ? null
            : expectedPrimaryBranch.Trim();
        _worktrees = worktrees ?? new GitWorktreeManager();
    }

    public async Task<TargetWorkspaceFinalizationResult> FinalizeAsync(
        WorkGraphSnapshot graph,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var completedCodeChanges = graph.Items
            .Where(item =>
                item.State == WorkItemState.Completed &&
                item.ResultType == WorkItemResultType.CodeChange &&
                !string.IsNullOrWhiteSpace(item.ResultRef))
            .ToArray();

        if (completedCodeChanges.Length == 0)
            return new(true, null, "최종 반영할 CODE_CHANGE가 없습니다.");

        var consumedIds = completedCodeChanges
            .SelectMany(item => item.Dependencies)
            .ToHashSet(StringComparer.Ordinal);
        var tips = completedCodeChanges
            .Where(item => !consumedIds.Contains(item.Id))
            .OrderBy(item => item.CreatedOrder)
            .ToArray();

        if (tips.Length == 0)
            return new(true, null, "최종 반영할 CODE_CHANGE tip이 없습니다.");

        string finalResultRef;
        if (tips.Length == 1)
        {
            finalResultRef = tips[0].ResultRef!;
        }
        else
        {
            var reduced = await _worktrees.ResolveNormalBaseRefAsync(
                _workspace,
                tips[0].ResultRef!,
                tips.Skip(1).Select(item => item.ResultRef!).ToArray(),
                cancellationToken).ConfigureAwait(false);

            if (!reduced.Success || string.IsNullOrWhiteSpace(reduced.EffectiveBaseRef))
            {
                if (string.Equals(
                        reduced.ErrorCode,
                        "NORMAL_MULTIPLE_CODE_BASES_REQUIRE_INTEGRATION",
                        StringComparison.Ordinal))
                {
                    return new(
                        false,
                        "TARGET_INTEGRATION_REQUIRED",
                        "서로 독립된 원격 CODE_CHANGE tip이 둘 이상 남아 있습니다." +
                        Environment.NewLine +
                        "HQ가 해당 결과들을 dependency로 둔 INTEGRATION WorkItem을 먼저 완료해야 합니다." +
                        Environment.NewLine +
                        string.Join(
                            Environment.NewLine,
                            tips.Select(item => $"- workItemId={item.Id} resultRef={item.ResultRef}")));
                }

                return new(
                    false,
                    "TARGET_CODE_LINEAGE_CHECK_FAILED",
                    "최종 원격 CODE_CHANGE 계보를 하나의 commit으로 축약하지 못했습니다." +
                    Environment.NewLine +
                    $"gitError={reduced.ErrorCode ?? "NORMAL_CODE_LINEAGE_CHECK_FAILED"}" +
                    Environment.NewLine +
                    (reduced.ErrorDetail ?? "추가 정보 없음"));
            }

            finalResultRef = reduced.EffectiveBaseRef;
        }

        var target = tips.FirstOrDefault(item =>
                         string.Equals(
                             item.ResultRef,
                             finalResultRef,
                             StringComparison.OrdinalIgnoreCase))
                     ?? tips.OrderByDescending(item => item.CreatedOrder).First();

        var freshness = await CheckPublishFreshnessAsync(
            graph.JobId,
            finalResultRef,
            cancellationToken).ConfigureAwait(false);
        if (freshness is not null)
            return freshness;

        var containment = await _worktrees.InspectTargetContainmentAsync(
            _workspace,
            finalResultRef,
            _expectedPrimaryBranch,
            cancellationToken).ConfigureAwait(false);

        if (!containment.Success)
        {
            return new(
                false,
                containment.ErrorCode ?? "TARGET_CONTAINMENT_CHECK_FAILED",
                "사용자 작업 폴더와 최종 원격 CODE_CHANGE의 포함 관계를 확인하지 못했습니다." +
                Environment.NewLine +
                $"resultRef={finalResultRef}" +
                Environment.NewLine +
                $"targetWorkspace={_workspace}");
        }

        var fastForwarded = false;
        if (!containment.IsContained)
        {
            var landing = await _worktrees.LandIntegrationAsync(
                _workspace,
                finalResultRef,
                _expectedPrimaryBranch,
                cancellationToken).ConfigureAwait(false);

            if (!landing.Success)
            {
                if (string.Equals(
                        landing.ErrorCode,
                        "INTEGRATION_TARGET_DIRTY",
                        StringComparison.Ordinal))
                {
                    return new(
                        false,
                        "TARGET_WORKSPACE_DIRTY",
                        "원격 commit을 최종 반영하기 전에 사용자 작업 폴더가 변경되었습니다." +
                        Environment.NewLine +
                        "ProjectHub는 파일 단위 materialize나 강제 reset으로 우회하지 않습니다." +
                        Environment.NewLine +
                        "로컬 변경을 commit·push하거나 정리한 뒤 다시 진행해야 합니다." +
                        Environment.NewLine +
                        $"resultRef={finalResultRef}" +
                        Environment.NewLine +
                        $"targetWorkspace={_workspace}");
                }

                return new(
                    false,
                    MapLandingError(landing.ErrorCode),
                    "최종 원격 CODE_CHANGE를 사용자 branch에 ff-only로 반영하지 못했습니다." +
                    Environment.NewLine +
                    $"resultRef={finalResultRef}" +
                    Environment.NewLine +
                    $"targetWorkspace={_workspace}" +
                    Environment.NewLine +
                    $"gitError={landing.ErrorCode ?? "INTEGRATION_LANDING_FAILED"}");
            }

            fastForwarded = landing.FastForwarded;
        }

        return new(
            true,
            null,
            fastForwarded
                ? "최종 원격 CODE_CHANGE를 사용자 branch에 ff-only로 반영했습니다."
                : "최종 원격 CODE_CHANGE가 사용자 branch에 이미 포함되어 있습니다.",
            target.Id,
            finalResultRef,
            fastForwarded);
    }

    private async Task<TargetWorkspaceFinalizationResult?> CheckPublishFreshnessAsync(
        string jobId,
        string finalResultRef,
        CancellationToken cancellationToken)
    {
        try
        {
            var state = await new WorkspacePublishState(_workspace, jobId)
                .ReadAsync(cancellationToken)
                .ConfigureAwait(false);
            if (!state.IsStaleFor(finalResultRef))
                return null;

            return new(
                false,
                "TARGET_PUBLISH_STALE",
                "마지막 성공 #9 BUILD/PUBLISH가 최종 원격 CODE_CHANGE와 다른 commit을 사용했습니다." +
                Environment.NewLine +
                "최종 resultRef를 baseRef로 #9를 다시 완료해야 합니다." +
                Environment.NewLine +
                $"finalResultRef={finalResultRef}" +
                Environment.NewLine +
                $"publishedSourceRef={state.PublishedSourceRef ?? "없음"}");
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return new(
                false,
                "TARGET_PUBLISH_STATE_UNAVAILABLE",
                "publish freshness 상태를 확인하지 못했습니다." +
                Environment.NewLine +
                exception.Message);
        }
    }

    private static string MapLandingError(string? errorCode)
        => errorCode switch
        {
            "INTEGRATION_TARGET_WORKSPACE_MISSING" => "TARGET_WORKSPACE_MISSING",
            "INTEGRATION_RESULT_REF_MISSING" => "TARGET_RESULT_REF_MISSING",
            "INTEGRATION_TARGET_REPOSITORY_REQUIRED" => "TARGET_REPOSITORY_REQUIRED",
            "INTEGRATION_TARGET_STATUS_UNAVAILABLE" => "TARGET_STATUS_UNAVAILABLE",
            "INTEGRATION_TARGET_DIRTY" => "TARGET_DIRTY",
            "INTEGRATION_TARGET_BRANCH_REQUIRED" => "TARGET_BRANCH_REQUIRED",
            "INTEGRATION_TARGET_BRANCH_CHANGED" => "TARGET_BRANCH_CHANGED",
            "INTEGRATION_TARGET_HEAD_UNAVAILABLE" => "TARGET_HEAD_UNAVAILABLE",
            "INTEGRATION_RESULT_REF_INVALID" => "TARGET_RESULT_REF_INVALID",
            "INTEGRATION_NOT_FAST_FORWARD" => "TARGET_NOT_FAST_FORWARD",
            "INTEGRATION_ANCESTRY_CHECK_FAILED" => "TARGET_ANCESTRY_CHECK_FAILED",
            "INTEGRATION_FAST_FORWARD_TIMEOUT" => "TARGET_FAST_FORWARD_TIMEOUT",
            "INTEGRATION_FAST_FORWARD_CANCELED" => "TARGET_FAST_FORWARD_CANCELED",
            "INTEGRATION_FAST_FORWARD_FAILED" => "TARGET_FAST_FORWARD_FAILED",
            "INTEGRATION_TARGET_HEAD_MISMATCH" => "TARGET_HEAD_MISMATCH",
            "INTEGRATION_TARGET_NOT_CLEAN" => "TARGET_NOT_CLEAN",
            null or "" => "TARGET_LANDING_FAILED",
            _ => "TARGET_LANDING_FAILED"
        };
}
