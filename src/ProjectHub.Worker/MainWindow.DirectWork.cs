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
        var configured = _targetSettings.ManualWorkingDirectory;
        if (!string.IsNullOrWhiteSpace(configured) && Directory.Exists(configured))
            return Path.GetFullPath(configured);

        return ResolveWorkingDirectory(CodexThreadCombo.SelectedItem as CodexThreadOption);
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
            DashboardPreflightText.Text =
                "직접 작업에 사용할 제공사, 모델, 추론 깊이를 선택하세요.";
            DashboardPreflightText.Foreground =
                System.Windows.Media.Brushes.Firebrick;
            return;
        }

        var workingDirectory = ResolveDirectWorkDirectory();
        var preflightError = _aiRoleRunners.GetPreflightError(
            role,
            workingDirectory,
            _codexAuthenticated);
        if (preflightError is not null)
        {
            DashboardPreflightText.Text = preflightError;
            DashboardPreflightText.Foreground =
                System.Windows.Media.Brushes.Firebrick;
            return;
        }

        var runner = _aiRoleRunners.Resolve(role);
        if (runner is null)
        {
            DashboardPreflightText.Text =
                $"Provider runner가 등록되지 않았습니다: {role.Provider}";
            DashboardPreflightText.Foreground =
                System.Windows.Media.Brushes.Firebrick;
            return;
        }

        IReadOnlyList<AiInputAttachment> stagedAttachments;
        try
        {
            stagedAttachments = StageUserAttachments(
                attachments,
                workingDirectory,
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
        _activeWorkingDirectory = workingDirectory;
        _activePrompt = prompt;
        _activeCliModel = role.Model;
        _activeReasoning = role.Reasoning;
        _activeCoordinatorFirst = false;
        _directWorkRunning = true;
        _userCanceledTask = false;
        _jobTimedOut = false;
        _lastActivityAt = DateTimeOffset.UtcNow;

        TaskDirection.Text = "작업";
        TaskTitle.Text = "계약문서 무시 · 직접 실행";
        ResultTitle.Text = "직접 작업";
        ResultBody.Text = "선택한 모델의 응답을 기다리는 중입니다.";
        SetFlowState(
            codexActive: true,
            workerActive: true,
            webActive: false,
            explicitStage: TaskStage.Implementer);

        try
        {
            var result = await runner.RunAsync(new AiRoleRunRequest(
                Prompt: prompt,
                Role: role,
                WorkingDirectory: workingDirectory,
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
                InputAttachments: stagedAttachments));

            _lastActivityAt = DateTimeOffset.UtcNow;
            var response = string.IsNullOrWhiteSpace(result.FinalMessage)
                ? (string.IsNullOrWhiteSpace(result.StandardError)
                    ? "(응답 없음)"
                    : result.StandardError.Trim())
                : result.FinalMessage;

            ResultBody.Text = response;
            AddRoleResponseHistory(
                WorkerRoleState.Work,
                "직접 작업 결과",
                response,
                usage: result.Usage,
                files: result.Files,
                status: result.ExitCode == 0 ? "COMPLETED" : "ERROR",
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
