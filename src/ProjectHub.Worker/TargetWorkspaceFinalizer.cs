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

        if (containment.IsContained)
        {
            return new(
                true,
                null,
                "현재 원격 동기화 branch가 최종 CODE_CHANGE를 이미 포함하고 있습니다.",
                target.Id,
                finalResultRef,
                false);
        }

        if (string.IsNullOrWhiteSpace(target.Branch))
        {
            return new(
                false,
                "TARGET_RESULT_BRANCH_MISSING",
                "최종 CODE_CHANGE의 원격 projecthub branch를 확인할 수 없습니다." +
                Environment.NewLine +
                $"workItemId={target.Id}" +
                Environment.NewLine +
                $"resultRef={finalResultRef}");
        }

        var checkout = await _worktrees.SwitchTargetToRemoteResultAsync(
            _workspace,
            finalResultRef,
            target.Branch,
            _expectedPrimaryBranch,
            cancellationToken).ConfigureAwait(false);

        if (!checkout.Success)
        {
            return new(
                false,
                checkout.ErrorCode ?? "TARGET_RESULT_CHECKOUT_FAILED",
                "최종 원격 CODE_CHANGE branch로 사용자 checkout을 전환하지 못했습니다." +
                Environment.NewLine +
                $"workItemId={target.Id}" +
                Environment.NewLine +
                $"resultRef={finalResultRef}" +
                Environment.NewLine +
                $"resultBranch={target.Branch}" +
                (string.IsNullOrWhiteSpace(checkout.ErrorDetail)
                    ? string.Empty
                    : Environment.NewLine + checkout.ErrorDetail));
        }

        return new(
            true,
            null,
            checkout.Switched
                ? "최종 원격 CODE_CHANGE branch로 사용자 checkout을 전환했습니다."
                : "사용자 checkout이 이미 최종 원격 CODE_CHANGE branch와 일치합니다.",
            target.Id,
            finalResultRef,
            false);
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

}
