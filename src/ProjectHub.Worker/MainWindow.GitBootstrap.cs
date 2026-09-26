using System.Windows;

namespace ProjectHub.Worker;

public partial class MainWindow
{
    private readonly GitWorkspaceBootstrapper _gitWorkspaceBootstrapper = new();

    private async Task<bool> PrepareParallelGitForLaunchAsync(
        string workingDirectory)
    {
        var state = await _gitWorkspaceBootstrapper.PrepareAsync(
            workingDirectory,
            CancellationToken.None);

        if (!state.Success)
        {
            ShowGitPreparationError(state.ErrorCode, state.RepositoryRoot);
            return false;
        }

        if (state.NeedsBaseline)
        {
            var reason = state.HasHead
                ? "현재 작업 폴더에 commit되지 않은 변경사항이 있습니다.\n현재 변경사항을 새 Git 기준점에 포함합니다."
                : "병렬 WORK를 위한 최초 Git 기준점이 필요합니다.\n현재 폴더의 내용을 Git 기준점으로 생성합니다.";

            var prompt =
                reason + Environment.NewLine + Environment.NewLine +
                "기준점 생성 경로" + Environment.NewLine +
                state.RepositoryRoot + Environment.NewLine + Environment.NewLine +
                "현재 Branch" + Environment.NewLine +
                (state.Branch ?? "unknown") + Environment.NewLine + Environment.NewLine +
                "확인을 누르면 기준점을 생성하고, 취소를 누르면 작업을 시작하지 않습니다.";

            var answer = MessageBox.Show(
                this,
                prompt,
                "Git 기준점 생성",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Question);

            if (answer != MessageBoxResult.OK)
            {
                DashboardPreflightText.Text = "Git 기준점 생성을 취소했습니다.";
                DashboardPreflightText.Foreground =
                    (System.Windows.Media.Brush)FindResource("Muted");
                return false;
            }

            state = await _gitWorkspaceBootstrapper.CreateBaselineAsync(
                state,
                CancellationToken.None);

            if (!state.Success)
            {
                ShowGitPreparationError(state.ErrorCode, state.RepositoryRoot);
                return false;
            }
        }

        var target = WorkerTargetConfiguration.ResolveGit(
            workingDirectory,
            _targetSettings);
        var preflight = ParallelWorkGitPreflight.Validate(target);
        if (!preflight.Success)
        {
            ShowGitPreparationError(preflight.ErrorCode, target.ProjectPath);
            return false;
        }

        RefreshGitTargetPresentation(target);
        DashboardPreflightText.Text = string.Empty;
        return true;
    }

    private void RefreshGitTargetPresentation(GitTargetSnapshot target)
    {
        _gitTarget = target;
        RepositoryUrlInput.Text = target.RepositoryUrl ?? string.Empty;
        TargetGitStateText.Text = target.IsRepository
            ? $"Branch: {target.Branch ?? "unknown"} · Local HEAD: {target.HeadSha?[..Math.Min(12, target.HeadSha.Length)] ?? "unknown"}"
            : "Git: UNCONFIGURED";
        RepositoryNameText.Text = " · " + (target.RepositoryUrl ?? "LOCAL_GIT");
    }

    private void ShowGitPreparationError(string? errorCode, string? path)
    {
        var detail = errorCode switch
        {
            "GIT_BOOTSTRAP_WORKSPACE_MISSING" => "작업 폴더를 찾거나 접근할 수 없습니다.",
            "GIT_BOOTSTRAP_INIT_TIMEOUT" => "git init 실행 시간이 초과되었습니다.",
            "GIT_BOOTSTRAP_INIT_CANCELED" => "git init 실행이 취소되었습니다.",
            "GIT_BOOTSTRAP_INIT_FAILED" => "git init을 실행하지 못했습니다.",
            "GIT_BOOTSTRAP_ATTACHED_BRANCH_REQUIRED" => "현재 Git 상태가 detached HEAD입니다. branch에 연결한 뒤 다시 실행하세요.",
            "GIT_BASELINE_ADD_TIMEOUT" => "기준점 파일 등록 시간이 초과되었습니다.",
            "GIT_BASELINE_ADD_CANCELED" => "기준점 파일 등록이 취소되었습니다.",
            "GIT_BASELINE_ADD_FAILED" => "기준점 파일을 Git에 등록하지 못했습니다.",
            "GIT_BASELINE_COMMIT_TIMEOUT" => "기준점 commit 시간이 초과되었습니다.",
            "GIT_BASELINE_COMMIT_CANCELED" => "기준점 commit이 취소되었습니다.",
            "GIT_BASELINE_COMMIT_FAILED" => "Git 기준점 commit을 생성하지 못했습니다.",
            "GIT_BASELINE_NOT_CLEAN" => "기준점 commit 뒤에도 commit되지 않은 변경사항이 남아 있습니다.",
            "PARALLEL_GIT_HEAD_REQUIRED" => "병렬 WORK 기준으로 사용할 Git HEAD commit을 확인할 수 없습니다.",
            "PARALLEL_GIT_ATTACHED_BRANCH_REQUIRED" => "Integration 결과를 반영하려면 현재 작업공간이 branch에 연결되어 있어야 합니다.",
            _ => "병렬 WORK를 위한 Git 준비에 실패했습니다."
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
        TaskTitle.Text = "Git 준비를 확인하세요";
        ResultTitle.Text = "GIT BLOCKED";
        ResultBody.Text = detail + pathLine + codeLine;
        SetFlowState(false, false, false);
    }
}
