using System.IO;
using System.Windows;

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
            var gitResetAttempted = false;

            while (true)
            {
                DashboardPreflightText.Text = "Git 기준점을 확인하는 중입니다.";
                DashboardPreflightText.Foreground =
                    (System.Windows.Media.Brush)FindResource("Muted");

                var state = await _gitWorkspaceBootstrapper.PrepareAsync(
                    workingDirectory,
                    cancellationToken);

                if (!state.Success)
                {
                    var prepareResetHandled = false;
                    if (!gitResetAttempted &&
                        TryResetGitMetadataForRetry(
                            workingDirectory,
                            state.ErrorCode,
                            out prepareResetHandled))
                    {
                        gitResetAttempted = true;
                        continue;
                    }

                    if (prepareResetHandled)
                        return null;

                    ShowGitPreparationError(state.ErrorCode, state.RepositoryRoot);
                    return null;
                }

                if (state.NeedsBaseline)
                {
                    var freshWorkspace = state.InitializedNow || !state.HasHead;
                    if (!freshWorkspace)
                    {
                        var reason = state.NeedsManagedIgnoreUpdate || state.NeedsManagedIndexCleanup
                            ? "ProjectHub의 Git 관리 규칙을 적용해야 합니다.\n.projecthub, .vs와 검증 캐시는 소스 추적에서 제외하고 현재 변경사항과 함께 새 기준점에 반영합니다."
                            : "현재 작업 폴더에 commit되지 않은 변경사항이 있습니다.\n현재 파일 상태를 새 Git 기준점으로 사용합니다.";

                        var prompt =
                            reason + Environment.NewLine + Environment.NewLine +
                            "기준점 생성 경로" + Environment.NewLine +
                            state.RepositoryRoot + Environment.NewLine + Environment.NewLine +
                            "현재 Branch" + Environment.NewLine +
                            (state.Branch ?? "unknown") + Environment.NewLine + Environment.NewLine +
                            "확인을 누르면 현재 파일을 기준점으로 만들고, 취소를 누르면 작업을 시작하지 않습니다.";

                        var answer = System.Windows.MessageBox.Show(
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
                            return null;
                        }
                    }

                    DashboardPreflightText.Text = freshWorkspace
                        ? "현재 파일에서 새 Git 기준점을 자동 생성하는 중입니다."
                        : "현재 파일을 Git 기준점으로 만드는 중입니다.";
                    DashboardPreflightText.Foreground =
                        (System.Windows.Media.Brush)FindResource("Muted");

                    state = await _gitWorkspaceBootstrapper.CreateBaselineAsync(
                        state,
                        cancellationToken);

                    if (!state.Success)
                    {
                        var baselineResetHandled = false;
                        if (!gitResetAttempted &&
                            TryResetGitMetadataForRetry(
                                workingDirectory,
                                state.ErrorCode,
                                out baselineResetHandled))
                        {
                            gitResetAttempted = true;
                            continue;
                        }

                        if (baselineResetHandled)
                            return null;

                        ShowGitPreparationError(state.ErrorCode, state.RepositoryRoot);
                        return null;
                    }
                }

                var resolvedTarget = WorkerTargetConfiguration.ResolveGit(
                    workingDirectory,
                    _targetSettings,
                    requireExactRoot: true);

                // Bootstrapper가 방금 검증한 branch/HEAD를 우선한다.
                // fresh repository의 최초 commit 직후 재조회가 일시적으로 비어도
                // 이미 검증된 baseline HEAD를 다시 실패로 취급하지 않는다.
                var target = resolvedTarget with
                {
                    ProjectPath = state.RepositoryRoot,
                    Branch = resolvedTarget.Branch ?? state.Branch,
                    HeadSha = resolvedTarget.HeadSha ?? state.HeadCommit,
                    IsRepository = true
                };

                var preflight = ParallelWorkGitPreflight.Validate(target);
                if (!preflight.Success)
                {
                    var preflightResetHandled = false;
                    if (!gitResetAttempted &&
                        TryResetGitMetadataForRetry(
                            workingDirectory,
                            preflight.ErrorCode,
                            out preflightResetHandled))
                    {
                        gitResetAttempted = true;
                        continue;
                    }

                    if (preflightResetHandled)
                        return null;

                    ShowGitPreparationError(preflight.ErrorCode, target.ProjectPath);
                    return null;
                }

                RefreshGitTargetPresentation(target);
                DashboardPreflightText.Text = "Git 준비 완료 · HQ 시작 준비";
                DashboardPreflightText.Foreground =
                    (System.Windows.Media.Brush)FindResource("Muted");
                return target;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            ShowGitPreparationError("GIT_PREPARATION_EXCEPTION", workingDirectory);
            AddTaskMessage(
                "GIT PREP EXCEPTION",
                exception.GetType().Name + ": " + exception.Message,
                status: "GIT_PREPARATION_EXCEPTION",
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

    private bool TryResetGitMetadataForRetry(
        string workingDirectory,
        string? errorCode,
        out bool handled)
    {
        handled = false;

        if (string.IsNullOrWhiteSpace(workingDirectory) ||
            !Directory.Exists(workingDirectory))
            return false;

        var workspace = Path.GetFullPath(workingDirectory);
        var gitMetadata = Path.Combine(workspace, ".git");
        if (!Directory.Exists(gitMetadata) && !File.Exists(gitMetadata))
            return false;

        var prompt =
            "Git 준비에 실패했습니다." + Environment.NewLine +
            $"코드: {errorCode ?? "GIT_PREPARATION_FAILED"}" +
            Environment.NewLine + Environment.NewLine +
            "현재 작업 파일과 .gitignore는 그대로 보존하고 .git 메타데이터만 삭제한 뒤 " +
            "현재 파일 상태에서 새 로컬 Git 기준점을 만들 수 있습니다." +
            Environment.NewLine + Environment.NewLine +
            "기존 local commit, branch, tag, remote 설정은 삭제됩니다." +
            Environment.NewLine +
            "계속하시겠습니까?";

        var answer = MessageBox.Show(
            this,
            prompt,
            "Git 재초기화",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning);

        if (answer != MessageBoxResult.OK)
            return false;

        handled = true;

        try
        {
            if (Directory.Exists(gitMetadata))
            {
                ClearGitMetadataAttributes(new DirectoryInfo(gitMetadata));
                Directory.Delete(gitMetadata, recursive: true);
            }
            else
            {
                File.SetAttributes(gitMetadata, FileAttributes.Normal);
                File.Delete(gitMetadata);
            }

            AddTaskMessage(
                "GIT RESET",
                "사용자 승인으로 .git 메타데이터를 삭제했습니다. 작업 파일과 .gitignore는 보존합니다.",
                status: "COMPLETED",
                includeHistory: false);
            return true;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            ShowGitPreparationError("GIT_METADATA_RESET_FAILED", workspace);
            AddTaskMessage(
                "GIT RESET ERROR",
                exception.GetType().Name + ": " + exception.Message,
                status: "GIT_METADATA_RESET_FAILED",
                includeHistory: false);
            return false;
        }
    }

    private static void ClearGitMetadataAttributes(DirectoryInfo directory)
    {
        if (!directory.Exists)
            return;

        foreach (var entry in directory.EnumerateFileSystemInfos())
        {
            if (entry is DirectoryInfo child &&
                !entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                ClearGitMetadataAttributes(child);
            }

            entry.Attributes &= ~(FileAttributes.ReadOnly | FileAttributes.System);
        }

        directory.Attributes &= ~(FileAttributes.ReadOnly | FileAttributes.System);
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
            "GIT_BOOTSTRAP_EXACT_ROOT_REQUIRED" => "화면의 작업 폴더 자체를 독립 Git 저장소 루트로 준비하지 못했습니다.",
            "GIT_BOOTSTRAP_LONGPATHS_CONFIG_TIMEOUT" => "Git 긴 경로 설정 시간이 초과되었습니다.",
            "GIT_BOOTSTRAP_LONGPATHS_CONFIG_CANCELED" => "Git 긴 경로 설정이 취소되었습니다.",
            "GIT_BOOTSTRAP_LONGPATHS_CONFIG_FAILED" => "저장소의 core.longpaths 설정을 적용하지 못했습니다.",
            "GIT_IGNORE_INSPECTION_FAILED" => ".gitignore 상태를 확인하지 못했습니다.",
            "GIT_BOOTSTRAP_MANAGED_PATH_SCAN_FAILED" => "ProjectHub 관리 경로의 Git 추적 상태를 확인하지 못했습니다.",
            "GIT_BOOTSTRAP_ATTACHED_BRANCH_REQUIRED" => "현재 Git 상태가 detached HEAD입니다. branch에 연결한 뒤 다시 실행하세요.",
            "GIT_IGNORE_UPDATE_FAILED" => "ProjectHub 관리 .gitignore 규칙을 갱신하지 못했습니다.",
            "GIT_MANAGED_INDEX_CLEANUP_TIMEOUT" => "ProjectHub 관리 경로의 Git 추적 해제 시간이 초과되었습니다.",
            "GIT_MANAGED_INDEX_CLEANUP_CANCELED" => "ProjectHub 관리 경로의 Git 추적 해제가 취소되었습니다.",
            "GIT_MANAGED_INDEX_CLEANUP_FAILED" => "ProjectHub 관리 경로를 Git index에서 정리하지 못했습니다.",
            "GIT_BASELINE_ADD_TIMEOUT" => "기준점 파일 등록 시간이 초과되었습니다.",
            "GIT_BASELINE_ADD_CANCELED" => "기준점 파일 등록이 취소되었습니다.",
            "GIT_BASELINE_ADD_FAILED" => "기준점 파일을 Git에 등록하지 못했습니다.",
            "GIT_BASELINE_COMMIT_TIMEOUT" => "기준점 commit 시간이 초과되었습니다.",
            "GIT_BASELINE_COMMIT_CANCELED" => "기준점 commit이 취소되었습니다.",
            "GIT_BASELINE_COMMIT_FAILED" => "Git 기준점 commit을 생성하지 못했습니다.",
            "GIT_BASELINE_NOT_CLEAN" => "기준점 commit 뒤에도 commit되지 않은 변경사항이 남아 있습니다.",
            "GIT_METADATA_RESET_FAILED" => ".git 메타데이터를 삭제하지 못했습니다. 작업 파일은 변경하지 않았습니다.",
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
        AddTaskMessage(
            "GIT PREP ERROR",
            detail + pathLine + codeLine,
            status: errorCode ?? "GIT_PREPARATION_FAILED",
            includeHistory: false);
        ActivateResultTab(web: false);
        SetFlowState(false, false, false);
    }
}
