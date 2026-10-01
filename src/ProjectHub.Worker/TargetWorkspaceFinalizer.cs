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
            return new(true, null, "사용자 작업 폴더에 반영할 CODE_CHANGE가 없습니다.");

        var consumedByCompletedCodeChange = completedCodeChanges
            .SelectMany(item => item.Dependencies)
            .ToHashSet(StringComparer.Ordinal);

        var candidates = completedCodeChanges
            .Where(item => !consumedByCompletedCodeChange.Contains(item.Id))
            .OrderBy(item => item.CreatedOrder)
            .ToArray();

        if (candidates.Length == 0)
            return new(true, null, "사용자 작업 폴더에 별도 반영할 CODE_CHANGE가 없습니다.");

        var ledger = new TargetWorkspaceMaterializationLedger(
            _workspace,
            graph.JobId);
        var pending = new List<WorkItemSnapshot>();
        foreach (var item in candidates)
        {
            if (ledger.IsResultVerified(item.ResultRef))
                continue;

            var containment = await _worktrees.InspectTargetContainmentAsync(
                _workspace,
                item.ResultRef!,
                _expectedPrimaryBranch,
                cancellationToken).ConfigureAwait(false);

            if (!containment.Success)
            {
                return new(
                    false,
                    containment.ErrorCode ?? "TARGET_CONTAINMENT_CHECK_FAILED",
                    "사용자 작업 폴더의 현재 HEAD와 WorkItem 결과 포함 여부를 확인하지 못했습니다." +
                    Environment.NewLine +
                    $"workItemId={item.Id}" + Environment.NewLine +
                    $"resultRef={item.ResultRef}" + Environment.NewLine +
                    $"targetWorkspace={_workspace}");
            }

            if (!containment.IsContained)
                pending.Add(item);
        }

        if (pending.Count == 0)
            return new(true, null, "최종 CODE_CHANGE가 사용자 작업 폴더에 검증 반영되었거나 현재 HEAD에 포함되어 있습니다.");

        WorkItemSnapshot target;
        string landingRef;
        if (pending.Count > 1)
        {
            var reduced = await _worktrees.ResolveNormalBaseRefAsync(
                _workspace,
                "HEAD",
                pending.Select(item => item.ResultRef!).ToArray(),
                cancellationToken).ConfigureAwait(false);

            if (!reduced.Success || string.IsNullOrWhiteSpace(reduced.EffectiveBaseRef))
            {
                if (string.Equals(
                        reduced.ErrorCode,
                        "NORMAL_MULTIPLE_CODE_BASES_REQUIRE_INTEGRATION",
                        StringComparison.Ordinal))
                {
                    var detail = string.Join(
                        Environment.NewLine,
                        pending.Select(item => $"- workItemId={item.Id} resultRef={item.ResultRef}"));
                    return new(
                        false,
                        "TARGET_INTEGRATION_REQUIRED",
                        "서로 독립적으로 완료된 미반영 NORMAL CODE_CHANGE가 둘 이상 남아 있습니다." +
                        Environment.NewLine +
                        "Worker는 결과를 임의 병합하지 않습니다. HQ가 필요한 결과를 dependency로 둔 INTEGRATION WorkItem을 추가해야 합니다." +
                        Environment.NewLine +
                        detail);
                }

                return new(
                    false,
                    "TARGET_CODE_LINEAGE_CHECK_FAILED",
                    "미반영 NORMAL CODE_CHANGE의 Git 계보를 하나의 최종 tip으로 축약하지 못했습니다." +
                    Environment.NewLine +
                    $"gitError={reduced.ErrorCode ?? "NORMAL_CODE_LINEAGE_CHECK_FAILED"}" +
                    Environment.NewLine +
                    (reduced.ErrorDetail ?? "추가 정보 없음"));
            }

            landingRef = reduced.EffectiveBaseRef;
            target = pending.FirstOrDefault(item =>
                         string.Equals(
                             item.ResultRef,
                             landingRef,
                             StringComparison.OrdinalIgnoreCase))
                     ?? pending.OrderByDescending(item => item.CreatedOrder).First();
        }
        else
        {
            target = pending[0];
            landingRef = target.ResultRef!;
        }

        var landing = await _worktrees.LandIntegrationAsync(
            _workspace,
            landingRef,
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
                    "TARGET_MATERIALIZATION_REQUIRED",
                    "사용자 작업 폴더에는 확정된 중간 결과가 누적될 수 있으므로 dirty 상태를 오류로 정리하지 않습니다." +
                    Environment.NewLine +
                    "남은 CODE_CHANGE를 고정 materialize 작업으로 검증 반영해야 합니다." +
                    Environment.NewLine +
                    $"workItemId={target.Id}" + Environment.NewLine +
                    $"resultRef={landingRef}" + Environment.NewLine +
                    $"targetWorkspace={_workspace}");
            }

            return new(
                false,
                MapLandingError(landing.ErrorCode),
                "단일 CODE_CHANGE를 사용자 작업 폴더에 ff-only로 반영하지 못했습니다." +
                Environment.NewLine +
                $"workItemId={target.Id}" + Environment.NewLine +
                $"resultRef={landingRef}" + Environment.NewLine +
                $"targetWorkspace={_workspace}" + Environment.NewLine +
                $"gitError={landing.ErrorCode ?? "INTEGRATION_LANDING_FAILED"}");
        }

        return new(
            true,
            null,
            landing.FastForwarded
                ? "단일 CODE_CHANGE를 사용자 작업 폴더에 ff-only로 반영했습니다."
                : "단일 CODE_CHANGE가 사용자 작업 폴더에 이미 반영되어 있습니다.",
            target.Id,
            landingRef,
            landing.FastForwarded);
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
