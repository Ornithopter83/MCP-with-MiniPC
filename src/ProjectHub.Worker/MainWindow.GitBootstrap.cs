namespace ProjectHub.Worker;

public partial class MainWindow
{
    private readonly GitWorkspaceBootstrapper _gitWorkspaceBootstrapper = new();
    private bool _gitPreparationInProgress;

    private async Task<GitTargetSnapshot?> PrepareParallelGitForLaunchAsync(
        string workingDirectory,
        string jobId,
        CancellationToken cancellationToken = default)
    {
        if (_gitPreparationInProgress)
            return null;

        _gitLaunchErrorMessage = null;
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

            var headCommit = state.HeadCommit;
            if (string.IsNullOrWhiteSpace(headCommit))
            {
                DashboardPreflightText.Text = "최초 Git baseline을 준비하는 중입니다.";
                DashboardPreflightText.Foreground =
                    (System.Windows.Media.Brush)FindResource("Muted");

                var baseline = await new GitWorktreeManager()
                    .PrepareInitialBaselineAsync(
                        workingDirectory,
                        jobId,
                        cancellationToken);
                if (!baseline.Success || string.IsNullOrWhiteSpace(baseline.HeadCommit))
                {
                    ShowGitPreparationError(
                        baseline.ErrorCode ?? "INITIAL_BASE_PREPARATION_FAILED",
                        baseline.RepositoryRoot);
                    return null;
                }

                headCommit = baseline.HeadCommit;
            }

            var target = new GitTargetSnapshot(
                state.RepositoryRoot,
                state.OriginUrl,
                state.Branch,
                headCommit,
                "REMOTE_GIT_PREFLIGHT",
                true);

            var preflight = ParallelWorkGitPreflight.Validate(target);
            if (!preflight.Success)
            {
                ShowGitPreparationError(preflight.ErrorCode, target.ProjectPath);
                return null;
            }

            _gitLaunchErrorMessage = null;
            RefreshGitTargetPresentation(target);
            DashboardPreflightText.Text = state.IsDirty
                ? "원격 Git 준비 완료 · 로컬 미커밋 변경은 WORK 기준점에 포함하지 않음 · HQ 시작 준비"
                : "원격 Git 준비 완료 · HQ 시작 준비";
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
        RepositoryUrlInput.Text =
            target.IsRepository && !string.IsNullOrWhiteSpace(target.RepositoryUrl)
                ? target.RepositoryUrl
                : string.Empty;
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
            "INITIAL_BASE_WORKSPACE_MISSING" => "최초 Git baseline을 만들 작업 폴더를 찾을 수 없습니다.",
            "INITIAL_BASE_REPOSITORY_REQUIRED" => "최초 Git baseline을 만들 Git 저장소를 확인할 수 없습니다.",
            "INITIAL_BASE_REMOTE_REQUIRED" => "최초 Git baseline을 게시할 origin 원격 저장소가 필요합니다.",
            "INITIAL_BASE_NETWORK_REMOTE_REQUIRED" => "최초 Git baseline은 네트워크 origin 원격 저장소에 게시해야 합니다.",
            "INITIAL_BASE_CLONE_FAILED" => "최초 Git baseline용 격리 clone을 만들지 못했습니다.",
            "INITIAL_BASE_ORPHAN_BRANCH_FAILED" => "최초 Git baseline용 projecthub branch를 만들지 못했습니다.",
            "INITIAL_BASE_FILE_LIST_FAILED" => "최초 Git baseline에 포함할 파일을 확인하지 못했습니다.",
            "INITIAL_BASE_FILE_PATH_INVALID" => "최초 Git baseline에 포함할 파일 경로를 안전하게 확인하지 못했습니다.",
            "INITIAL_BASE_COPY_FAILED" => "최초 Git baseline에 작업 폴더 파일을 복사하지 못했습니다.",
            "INITIAL_BASE_ADD_FAILED" => "최초 Git baseline 파일을 checkpoint 대상으로 준비하지 못했습니다.",
            "INITIAL_BASE_COMMIT_FAILED" => "최초 Git baseline commit을 만들지 못했습니다.",
            "INITIAL_BASE_HEAD_UNAVAILABLE" => "생성한 최초 Git baseline commit을 확인하지 못했습니다.",
            "INITIAL_BASE_PUSH_FAILED" => "최초 Git baseline을 projecthub 원격 branch에 게시하지 못했습니다.",
            "INITIAL_BASE_REMOTE_VERIFY_FAILED" => "게시한 최초 Git baseline을 원격에서 확인하지 못했습니다.",
            "INITIAL_BASE_SOURCE_FETCH_FAILED" => "최초 Git baseline을 사용자 작업 폴더로 가져오지 못했습니다.",
            "INITIAL_BASE_LOCAL_ADOPT_FAILED" => "사용자 작업 폴더를 최초 Git baseline에 연결하지 못했습니다.",
            "INITIAL_BASE_LOCAL_ADOPT_DIRTY" => "최초 Git baseline 연결 뒤 작업 폴더 내용이 달라 안전하게 시작할 수 없습니다.",
            "INITIAL_BASE_PREPARATION_FAILED" => "최초 Git baseline 준비에 실패했습니다.",
            "GIT_REMOTE_ORIGIN_REQUIRED" => "origin 원격 저장소가 반드시 설정되어 있어야 합니다.",
            "GIT_REMOTE_ORIGIN_NETWORK_REQUIRED" => "origin은 로컬 경로가 아닌 네트워크 Git 원격이어야 합니다.",
            "GIT_REMOTE_STATUS_UNAVAILABLE" => "작업 폴더의 Git 상태를 확인할 수 없습니다.",
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

        _gitLaunchErrorMessage = detail;

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
