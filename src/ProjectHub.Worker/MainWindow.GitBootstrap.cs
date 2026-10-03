namespace ProjectHub.Worker;

public partial class MainWindow
{
    private readonly GitWorkspaceBootstrapper _gitWorkspaceBootstrapper = new();
    private bool _gitPreparationInProgress;

    private async Task<GitTargetSnapshot?> PrepareParallelGitForLaunchAsync(
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        if (_gitPreparationInProgress)
            return null;

        _gitPreparationInProgress = true;
        UpdateDashboardRunButtonState();
        UpdateFollowupButtonState();

        try
        {
            DashboardPreflightText.Text = "원격 Git 기준점을 확인하는 중입니다.";
            DashboardPreflightText.Foreground =
                (System.Windows.Media.Brush)FindResource("Muted");

            var state = await _gitWorkspaceBootstrapper.PrepareAsync(
                workingDirectory,
                cancellationToken);

            if (!state.Success)
            {
                ShowGitPreparationError(state.ErrorCode, state.RepositoryRoot);
                return null;
            }

            var target = new GitTargetSnapshot(
                state.RepositoryRoot,
                state.OriginUrl,
                state.Branch,
                state.HeadCommit,
                "REMOTE_GIT_PREFLIGHT",
                true);

            var preflight = ParallelWorkGitPreflight.Validate(target);
            if (!preflight.Success)
            {
                ShowGitPreparationError(preflight.ErrorCode, target.ProjectPath);
                return null;
            }

            RefreshGitTargetPresentation(target);
            DashboardPreflightText.Text = "원격 Git 준비 완료 · HQ 시작 준비";
            DashboardPreflightText.Foreground =
                (System.Windows.Media.Brush)FindResource("Muted");
            return target;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            ShowGitPreparationError("GIT_REMOTE_PREPARATION_EXCEPTION", workingDirectory);
            AddTaskMessage(
                "GIT PREP EXCEPTION",
                exception.GetType().Name + ": " + exception.Message,
                status: "GIT_REMOTE_PREPARATION_EXCEPTION",
                includeHistory: false);
            return null;
        }
        finally
        {
            _gitPreparationInProgress = false;
            UpdateDashboardRunButtonState();
            UpdateFollowupButtonState();
        }
    }

    private void RefreshGitTargetPresentation(GitTargetSnapshot target)
    {
        _gitTarget = target;
        RepositoryUrlInput.Text = target.RepositoryUrl ?? string.Empty;
        TargetGitStateText.Text = target.IsRepository
            ? $"Branch: {target.Branch ?? "unknown"} · Remote HEAD: {target.HeadSha?[..Math.Min(12, target.HeadSha.Length)] ?? "unknown"}"
            : "Git: UNCONFIGURED";
        RepositoryNameText.Text = " · " + (target.RepositoryUrl ?? "REMOTE_REQUIRED");
    }

    private void ShowGitPreparationError(string? errorCode, string? path)
    {
        var detail = errorCode switch
        {
            "GIT_REMOTE_WORKSPACE_MISSING" => "작업 폴더를 찾거나 접근할 수 없습니다.",
            "GIT_REMOTE_REPOSITORY_REQUIRED" => "작업 폴더는 기존 Git 저장소여야 합니다. ProjectHub는 로컬 저장소를 자동 생성하지 않습니다.",
            "GIT_REMOTE_EXACT_ROOT_REQUIRED" => "선택한 작업 폴더 자체가 Git 저장소 루트여야 합니다.",
            "GIT_REMOTE_ATTACHED_BRANCH_REQUIRED" => "원격 기준점을 사용하려면 detached HEAD가 아닌 branch가 필요합니다.",
            "GIT_REMOTE_HEAD_REQUIRED" => "현재 Git HEAD commit을 확인할 수 없습니다.",
            "GIT_REMOTE_ORIGIN_REQUIRED" => "origin 원격 저장소가 반드시 설정되어 있어야 합니다.",
            "GIT_REMOTE_ORIGIN_NETWORK_REQUIRED" => "origin은 로컬 경로가 아닌 네트워크 Git 원격이어야 합니다.",
            "GIT_REMOTE_STATUS_UNAVAILABLE" => "작업 폴더의 Git 상태를 확인할 수 없습니다.",
            "GIT_REMOTE_WORKSPACE_DIRTY" => "작업 폴더에 commit되지 않은 변경이 있습니다. 먼저 commit·push하여 원격과 동기화한 뒤 다시 실행하세요.",
            "GIT_REMOTE_FETCH_TIMEOUT" => "origin fetch 시간이 초과되었습니다.",
            "GIT_REMOTE_FETCH_CANCELED" => "origin fetch가 취소되었습니다.",
            "GIT_REMOTE_FETCH_FAILED" => "origin 원격 저장소에 접근하거나 fetch하지 못했습니다.",
            "GIT_REMOTE_BRANCH_REQUIRED" => "현재 로컬 branch와 같은 origin 원격 branch를 찾을 수 없습니다.",
            "GIT_REMOTE_HEAD_MISMATCH" => "로컬 HEAD와 origin 원격 branch HEAD가 다릅니다. 원격과 완전히 동기화한 뒤 다시 실행하세요.",
            "PARALLEL_GIT_REMOTE_REQUIRED" => "ProjectHub 병렬 작업에는 origin 원격 저장소가 필요합니다.",
            "PARALLEL_GIT_NETWORK_REMOTE_REQUIRED" => "ProjectHub 병렬 작업에는 로컬 경로가 아닌 네트워크 Git origin이 필요합니다.",
            "PARALLEL_GIT_HEAD_REQUIRED" => "WORK 기준으로 사용할 원격 동기화 HEAD를 확인할 수 없습니다.",
            "PARALLEL_GIT_ATTACHED_BRANCH_REQUIRED" => "현재 작업공간이 branch에 연결되어 있어야 합니다.",
            _ => "원격 Git 준비에 실패했습니다."
        };

        var pathLine = string.IsNullOrWhiteSpace(path)
            ? string.Empty
            : Environment.NewLine + "경로: " + path;
        var codeLine = string.IsNullOrWhiteSpace(errorCode)
            ? string.Empty
            : Environment.NewLine + "코드: " + errorCode;

        DashboardPreflightText.Text = detail;
        DashboardPreflightText.Foreground = System.Windows.Media.Brushes.Firebrick;
        TaskDirection.Text = "PREFLIGHT";
        TaskTitle.Text = "원격 Git 준비를 확인하세요";
        ResultTitle.Text = "GIT BLOCKED";
        ResultBody.Text = detail + pathLine + codeLine;
        AddTaskMessage(
            "GIT PREP ERROR",
            detail + pathLine + codeLine,
            status: errorCode ?? "GIT_REMOTE_PREPARATION_FAILED",
            includeHistory: false);
        ActivateResultTab(web: false);
        SetFlowState(false, false, false);
    }
}
