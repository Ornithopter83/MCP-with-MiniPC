using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace ProjectHub.Worker;

public partial class MainWindow
{
    private bool _loadingDirectWorkControls;
    private bool _directWorkRunning;

    private sealed record DirectWorkChoice<T>(T Value, string Label)
    {
        public override string ToString() => Label;
    }

    private bool IsDirectWorkMode =>
        DirectWorkModeCheckBox?.IsChecked == true;

    private void InitializeDirectWorkControls()
    {
        if (DirectWorkProviderCombo is null ||
            DirectWorkModelCombo is null ||
            DirectWorkReasoningCombo is null)
            return;

        _loadingDirectWorkControls = true;
        try
        {
            var providers = AiProviderCatalog.Current
                .Select(provider => new DirectWorkChoice<AiProviderDescriptor>(provider, provider.DisplayName))
                .ToArray();
            DirectWorkProviderCombo.ItemsSource = providers;
            DirectWorkProviderCombo.SelectedItem =
                providers.FirstOrDefault(option => option.Value.Provider == AiServiceProvider.OpenAI) ??
                providers.FirstOrDefault();
            RefreshDirectWorkModels(preferDefault: true);
        }
        finally
        {
            _loadingDirectWorkControls = false;
        }

        UpdateDirectWorkControlState(active: false);
    }

    private void DirectWorkModeCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (DirectWorkOptionsPanel is null)
            return;

        DirectWorkOptionsPanel.Visibility =
            IsDirectWorkMode ? Visibility.Visible : Visibility.Collapsed;

        if (!IsDirectWorkMode &&
            _continuationState is null &&
            DashboardFollowupComposer is not null)
            SetFollowupComposerVisible(false);

        if (RunButton is not null)
            UpdateDashboardRunButtonState();
        if (AddWorkButton is not null)
            UpdateFollowupButtonState();
    }

    private void DirectWorkProviderCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingDirectWorkControls)
            return;

        _loadingDirectWorkControls = true;
        try
        {
            RefreshDirectWorkModels(preferDefault: true);
        }
        finally
        {
            _loadingDirectWorkControls = false;
        }

        UpdateDashboardRunButtonState();
        UpdateFollowupButtonState();
    }

    private void DirectWorkModelCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingDirectWorkControls)
            return;

        _loadingDirectWorkControls = true;
        try
        {
            RefreshDirectWorkReasoning(preferDefault: true);
        }
        finally
        {
            _loadingDirectWorkControls = false;
        }

        UpdateDashboardRunButtonState();
        UpdateFollowupButtonState();
    }

    private void DirectWorkReasoningCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingDirectWorkControls)
            return;

        UpdateDashboardRunButtonState();
        UpdateFollowupButtonState();
    }

    private void RefreshDirectWorkModels(bool preferDefault)
    {
        var provider = (DirectWorkProviderCombo.SelectedItem as DirectWorkChoice<AiProviderDescriptor>)?.Value;
        var models = (provider?.Models ?? Array.Empty<AiModelDescriptor>())
            .Select(model => new DirectWorkChoice<AiModelDescriptor>(model, model.DisplayName))
            .ToArray();
        DirectWorkModelCombo.ItemsSource = models;

        if (models.Length == 0)
        {
            DirectWorkModelCombo.SelectedItem = null;
            DirectWorkReasoningCombo.ItemsSource = null;
            DirectWorkReasoningCombo.SelectedItem = null;
            return;
        }

        var selected = preferDefault
            ? models.FirstOrDefault(option =>
                string.Equals(option.Value.Id, "gpt-6-luna", StringComparison.OrdinalIgnoreCase))
            : null;
        DirectWorkModelCombo.SelectedItem = selected ?? models[0];
        RefreshDirectWorkReasoning(preferDefault);
    }

    private void RefreshDirectWorkReasoning(bool preferDefault)
    {
        var model = (DirectWorkModelCombo.SelectedItem as DirectWorkChoice<AiModelDescriptor>)?.Value;
        var options = (model?.ReasoningOptions ?? Array.Empty<string>())
            .Select(value => new DirectWorkChoice<string>(
                value,
                string.IsNullOrWhiteSpace(value)
                    ? value
                    : char.ToUpperInvariant(value[0]) + value[1..].ToLowerInvariant()))
            .ToArray();
        DirectWorkReasoningCombo.ItemsSource = options;

        if (options.Length == 0)
        {
            DirectWorkReasoningCombo.SelectedItem = null;
            return;
        }

        var preferred = preferDefault
            ? options.FirstOrDefault(option =>
                string.Equals(option.Value, "medium", StringComparison.OrdinalIgnoreCase))
            : null;
        DirectWorkReasoningCombo.SelectedItem =
            preferred ??
            options.FirstOrDefault(option =>
                string.Equals(option.Value, model?.DefaultReasoning, StringComparison.OrdinalIgnoreCase)) ??
            options[0];
    }

    private void UpdateDirectWorkControlState(bool active)
    {
        if (DirectWorkModeCheckBox is null)
            return;

        DirectWorkModeCheckBox.IsEnabled = !active;
        if (DirectWorkProviderCombo is not null)
            DirectWorkProviderCombo.IsEnabled = !active;
        if (DirectWorkModelCombo is not null)
            DirectWorkModelCombo.IsEnabled = !active;
        if (DirectWorkReasoningCombo is not null)
            DirectWorkReasoningCombo.IsEnabled = !active;
    }

    private WorkerAiRoleSettings? GetDirectWorkRole()
    {
        if (DirectWorkProviderCombo.SelectedItem is not DirectWorkChoice<AiProviderDescriptor> providerOption ||
            DirectWorkModelCombo.SelectedItem is not DirectWorkChoice<AiModelDescriptor> modelOption ||
            DirectWorkReasoningCombo.SelectedItem is not DirectWorkChoice<string> reasoningOption)
            return null;

        var provider = providerOption.Value;
        var model = modelOption.Value;
        return new WorkerAiRoleSettings(
            provider.WireId,
            model.Id,
            reasoningOption.Value,
            provider.DefaultTransport);
    }

    private string ResolveDirectWorkDirectory()
    {
        // 하네스 없음은 현재 화면에서 사용자가 지정한 작업 폴더를 우선한다.
        // 설정 팝업의 "적용" 여부 때문에 이전 ManualWorkingDirectory로 되돌아가지 않는다.
        var displayed = WorkingDirectoryInput?.Text?.Trim();
        if (!string.IsNullOrWhiteSpace(displayed))
            return Directory.Exists(displayed) ? Path.GetFullPath(displayed) : displayed;

        var configured = _targetSettings.ManualWorkingDirectory;
        if (!string.IsNullOrWhiteSpace(configured) && Directory.Exists(configured))
            return Path.GetFullPath(configured);

        return ResolveWorkingDirectory(CodexThreadCombo.SelectedItem as CodexThreadOption);
    }

    private void WorkingDirectoryInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        // 작업 폴더가 바뀌면 이전 저장소 정보는 더 이상 신뢰할 수 없다.
        // "자동 확인"이 현재 입력값을 다시 검사하기 전까지 stale Git 표시를 지운다.
        _gitTarget = null;
        if (RepositoryUrlInput is not null)
            RepositoryUrlInput.Text = string.Empty;
        if (TargetGitStateText is not null)
            TargetGitStateText.Text = "Git: 자동 확인 필요";
        if (RepositoryNameText is not null)
            RepositoryNameText.Text = " · GIT_CHECK_REQUIRED";

        if (RunButton is not null)
            UpdateDashboardRunButtonState();
    }

    private void ShowDirectWorkPreflightError(string message)
    {
        DashboardPreflightText.Text = message;
        DashboardPreflightText.Foreground = System.Windows.Media.Brushes.Firebrick;
        TaskDirection.Text = "PREFLIGHT";
        TaskTitle.Text = "하네스 없음 실행 준비를 확인하세요";
        ResultTitle.Text = "DIRECT WORK BLOCKED";
        ResultBody.Text = message;
        SetFlowState(false, false, false);
    }

    private string? GetDirectWorkPreflightError()
    {
        if (!IsDirectWorkMode)
            return null;

        var role = GetDirectWorkRole();
        if (role is null)
            return "직접 작업에 사용할 제공사, 모델, 추론 깊이를 선택하세요.";

        var workingDirectory = ResolveDirectWorkDirectory();
        return _aiRoleRunners.GetPreflightError(
            role,
            workingDirectory,
            _codexAuthenticated);
    }

    private Task RunDirectWorkFromDashboardAsync()
    {
        var prompt = DashboardTaskInput.Text ?? string.Empty;
        if (string.IsNullOrWhiteSpace(prompt) || prompt == DashboardPromptPlaceholder)
            return Task.CompletedTask;

        return RunDirectWorkAsync(
            prompt,
            appendToHistory: false,
            attachments: SnapshotInitialAttachments());
    }

    private async Task RunDirectWorkAsync(
        string prompt,
        bool appendToHistory,
        IReadOnlyList<UserAttachmentInput>? attachments = null)
    {
        var role = GetDirectWorkRole();
        if (role is null)
        {
            ShowDirectWorkPreflightError(
                "직접 작업에 사용할 제공사, 모델, 추론 깊이를 선택하세요.");
            return;
        }

        var workingDirectory = ResolveDirectWorkDirectory();
        var preflightError = _aiRoleRunners.GetPreflightError(
            role,
            workingDirectory,
            _codexAuthenticated);
        if (preflightError is not null)
        {
            ShowDirectWorkPreflightError(preflightError);
            return;
        }

        var runner = _aiRoleRunners.Resolve(role);
        if (runner is null)
        {
            ShowDirectWorkPreflightError(
                $"Provider runner가 등록되지 않았습니다: {role.Provider}");
            return;
        }

        var gitState = await _gitWorkspaceBootstrapper.PrepareAsync(
            workingDirectory,
            CancellationToken.None);
        if (!gitState.Success ||
            string.IsNullOrWhiteSpace(gitState.HeadCommit))
        {
            ShowGitPreparationError(gitState.ErrorCode, gitState.RepositoryRoot);
            return;
        }

        var directJobId =
            "direct-" +
            DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss") +
            "-" +
            Guid.NewGuid().ToString("N")[..8];
        const string directWorkItemId = "direct";
        var gitManager = new GitWorktreeManager();
        var preparation = await gitManager.PrepareAsync(
            gitState.RepositoryRoot,
            directJobId,
            directWorkItemId,
            gitState.HeadCommit,
            CancellationToken.None);
        if (!preparation.Success)
        {
            DashboardPreflightText.Text =
                "직접 작업용 원격 clone 준비 실패: " +
                (preparation.ErrorCode ?? "WORK_CLONE_PREPARE_FAILED");
            DashboardPreflightText.Foreground =
                System.Windows.Media.Brushes.Firebrick;
            return;
        }

        IReadOnlyList<AiInputAttachment> stagedAttachments;
        try
        {
            stagedAttachments = StageUserAttachments(
                attachments,
                "direct-" + Guid.NewGuid().ToString("N"));
        }
        catch (Exception exception)
        {
            DashboardPreflightText.Text = "첨부 준비 실패: " + exception.Message;
            DashboardPreflightText.Foreground =
                System.Windows.Media.Brushes.Firebrick;
            return;
        }

        ConsumePendingAttachments(attachments);

        if (!appendToHistory)
            _historyEvents.Clear();

        SetDashboardBodyMode(DashboardBodyMode.TaskHistory);
        SetFollowupComposerVisible(false);
        if (appendToHistory)
        {
            DashboardFollowupInput.Text = FollowupPromptPlaceholder;
            DashboardFollowupInput.Foreground =
                FindResource("Muted") as System.Windows.Media.Brush;
        }

        var providerVisual = ProviderVisualCatalog.Resolve(role.Provider);
        var requestCard = new WorkerHistoryEvent(
            DateTimeOffset.Now,
            "Implementer",
            "ROLE_REQUEST",
            "직접 작업 요청",
            WorkerHistoryCardFormatter.Preview(prompt),
            Encoding.UTF8.GetByteCount(prompt),
            1,
            attachments?.Count,
            "REQUESTED",
            null)
        {
            FullMessage = prompt,
            TokenDetails = "토큰 · 사용자 입력",
            FileDetails = FormatAttachmentHistory(attachments),
            IconAssetOverride = providerVisual.ColorAsset
        };
        _historyEvents.Add(requestCard);
        RefreshMessageLog();

        using var cts = new CancellationTokenSource();
        _activeTaskCts = cts;
        _activeWorkingDirectory = preparation.WorktreePath;
        _activePrompt = prompt;
        _activeCliModel = role.Model;
        _activeReasoning = role.Reasoning;
        _activeCoordinatorFirst = false;
        _directWorkRunning = true;
        _userCanceledTask = false;
        _jobTimedOut = false;
        _lastActivityAt = DateTimeOffset.UtcNow;

        TaskDirection.Text = "작업";
        TaskTitle.Text = "원격 Git 격리 · 직접 실행";
        ResultTitle.Text = "직접 작업";
        ResultBody.Text = "선택한 모델의 응답을 기다리는 중입니다.";
        SetFlowState(
            codexActive: true,
            workerActive: true,
            webActive: false,
            explicitStage: TaskStage.Implementer);

        try
        {
            AiRoleRunResult result;
            GitMetadataRestoreResult gitRestore;
            var gitIsolation = GitMetadataIsolationLease.Detach(
                preparation.WorktreePath,
                directJobId,
                directWorkItemId);
            try
            {
                result = await runner.RunAsync(new AiRoleRunRequest(
                    Prompt: prompt,
                    Role: role,
                    WorkingDirectory: preparation.WorktreePath,
                    SessionId: null,
                    Sandbox: CodexSandboxMode.WorkspaceWrite,
                    CancellationToken: cts.Token,
                    Progress: progress =>
                    {
                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            if (_directWorkRunning)
                                AddRoleProgressHistory(
                                    WorkerRoleState.Work,
                                    progress,
                                    role.Provider);
                        }));
                    },
                    IgnoreProjectInstructions: true,
                    InputAttachments: stagedAttachments,
                    EnvironmentVariables: GitMetadataIsolationLease.BuildGitNetworkDenyEnvironment()));
            }
            finally
            {
                gitRestore = gitIsolation.Restore();
            }

            _lastActivityAt = DateTimeOffset.UtcNow;
            var response = string.IsNullOrWhiteSpace(result.FinalMessage)
                ? (string.IsNullOrWhiteSpace(result.StandardError)
                    ? "(응답 없음)"
                    : result.StandardError.Trim())
                : result.FinalMessage;

            var directStatus = result.ExitCode == 0 ? "COMPLETED" : "ERROR";
            if (!gitRestore.Success)
            {
                directStatus = "ERROR";
                response += Environment.NewLine + Environment.NewLine +
                            "Git metadata restore 실패: " +
                            (gitRestore.ErrorCode ?? "GIT_METADATA_RESTORE_FAILED");
            }
            else if (result.ExitCode == 0)
            {
                var checkpoint = await gitManager.CreateCheckpointAsync(
                    preparation.WorktreePath,
                    directWorkItemId,
                    cts.Token);
                if (!checkpoint.Success)
                {
                    directStatus = "ERROR";
                    response += Environment.NewLine + Environment.NewLine +
                                "Remote checkpoint 실패: " +
                                (checkpoint.ErrorCode ?? "WORKTREE_CHECKPOINT_FAILED") +
                                (string.IsNullOrWhiteSpace(checkpoint.ErrorDetail)
                                    ? string.Empty
                                    : Environment.NewLine + checkpoint.ErrorDetail);
                }
                else if (checkpoint.CreatedCommit &&
                         !string.IsNullOrWhiteSpace(checkpoint.HeadCommit) &&
                         !string.IsNullOrWhiteSpace(checkpoint.Branch))
                {
                    var checkout = await gitManager.SwitchTargetToRemoteResultAsync(
                        workingDirectory,
                        checkpoint.HeadCommit,
                        checkpoint.Branch,
                        gitState.Branch,
                        cts.Token);
                    if (!checkout.Success)
                    {
                        directStatus = "ERROR";
                        response += Environment.NewLine + Environment.NewLine +
                                    "최종 remote result checkout 실패: " +
                                    (checkout.ErrorCode ?? "TARGET_CHECKOUT_FAILED");
                    }
                    else
                    {
                        response += Environment.NewLine + Environment.NewLine +
                                    "REMOTE_CODE_RESULT" + Environment.NewLine +
                                    "resultRef: " + checkpoint.HeadCommit + Environment.NewLine +
                                    "branch: " + checkpoint.Branch;
                    }
                }

                if (directStatus == "COMPLETED" &&
                    !string.IsNullOrWhiteSpace(preparation.Branch))
                {
                    await gitManager.RemoveAsync(
                        preparation.RepositoryRoot,
                        preparation.WorktreePath,
                        preparation.Branch,
                        CancellationToken.None);
                }
            }

            ResultBody.Text = response;
            AddRoleResponseHistory(
                WorkerRoleState.Work,
                "직접 작업 결과",
                response,
                usage: result.Usage,
                files: result.Files,
                status: directStatus,
                providerWireId: result.Provider,
                fullMessage: response);
        }
        catch (OperationCanceledException)
        {
            if (!_userCanceledTask)
            {
                AddRoleResponseHistory(
                    WorkerRoleState.Work,
                    "직접 작업 중단",
                    "직접 작업 실행이 취소되었습니다.",
                    status: "CANCELED",
                    providerWireId: role.Provider);
            }
        }
        catch (Exception exception)
        {
            ResultBody.Text = exception.Message;
            AddRoleResponseHistory(
                WorkerRoleState.Work,
                "직접 작업 오류",
                exception.Message,
                status: "ERROR",
                providerWireId: role.Provider,
                fullMessage: exception.ToString());
        }
        finally
        {
            _directWorkRunning = false;
            if (ReferenceEquals(_activeTaskCts, cts))
                _activeTaskCts = null;

            SetFlowState(false, false, false);
            SetFollowupComposerVisible(true);
            UpdateDirectWorkControlState(active: false);
            UpdateDashboardRunButtonState();
        }
    }
}
