using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace ProjectHub.Worker;

public partial class MainWindow : Window
{
    private readonly Forms.NotifyIcon _trayIcon;
    private readonly CodexCliRunner _codexRunner = new();
    private readonly JevJudgeRunner _jevJudgeRunner = new();
    private CancellationTokenSource? _activeTaskCts;
    private CodexCliResult? _lastCodexResult;
    private BridgeTask? _lastWebTask;
    private bool _awaitingWebResult;
    private string? _activePrompt;
    private string? _activeWebInstruction;
    private string? _activeWorkingDirectory;
    private string? _activeSessionId;
    private string? _activeCliModel;
    private string? _activeReasoning;
    private bool _webFollowupStarted;
    private bool _actionProtocolEnabled;
    private bool _activeReadOnly;
    private string? _initialGitReferenceHeader;
    private DateTimeOffset _lastActivityAt;
    private bool _jobTimedOut;
    private bool _userCanceledTask;
    private readonly HashSet<string> _userCanceledBridgeTaskIds = new(StringComparer.Ordinal);
    private static readonly TimeSpan JobInactivityTimeout = TimeSpan.FromMinutes(30);
    private string? _lastWebTaskId;
    private string? _lastExtensionProgressKey;
    private CodexUsage _commandUsage = CodexUsage.Empty;
    private sealed record TaskMessage(DateTimeOffset Timestamp, string Source, string Content);
    private readonly List<TaskMessage> _taskMessages = new();
    private readonly ObservableCollection<string> _messageLogItems = new();
    public ObservableCollection<string> MessageLogItems => _messageLogItems;
    private DateTimeOffset _taskStartedAt;
    private string _taskProjectName = "UnknownProject";
    private string _taskThreadName = "NewThread";
    private bool _taskExported;
    private readonly DispatcherTimer _flowTimer = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private readonly DispatcherTimer _connectionTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly DispatcherTimer _jobWatchdogTimer = new() { Interval = TimeSpan.FromSeconds(10) };
    private int _flowFrame;
    private bool _pairArrowActive;
    private bool _judgeReviewing;
    private string _judgeStatus = "OFF";
    private int _judgeRound;
    private bool _judgeReportOnly;
    private string? _pendingJevFailure;
    private string _activeJevJobId = Guid.NewGuid().ToString("N");
    private bool _allowClose;
    private const string Placeholder = "CLI에 즉시 전달할 작업 지시...";
    private const string WebInstructionPlaceholder = "CLI 답변 뒤에 붙여 GPT Web에 전달할 지침...";
    private readonly HttpClient _connectionClient = new() { Timeout = TimeSpan.FromSeconds(2) };
    private BridgeServer? _bridgeServer;
    private bool _codexAuthenticated;
    private bool _serverOnline;
    private bool _messageExpanded;
    private bool _startupConfigurationInitialized;
    private string _serverBaseUrl = WorkerTargetConfiguration.DefaultServerBaseUrl;
    private string _serverBaseUrlSource = "DEFAULT";
    private WorkerTargetSettings _targetSettings = new(null, null, null, null);
    private GitTargetSnapshot? _gitTarget;
    private List<CodexProjectOption> _codexProjects = new();
    private bool _loadingCodexSelections;
    private static string WindowPlacementPath => Path.Combine(WorkerPaths.Config, "window-placement.json");

    private enum FlowNode { Codex, Worker, Web, Judge }
    private enum WebActionKind { None, Begin, Continue, Pause, End, ProtocolError }
    private sealed record WebAction(WebActionKind Kind, string Body, string? Error = null);
    private sealed record CodexProjectOption(string Name, string Path);
    private sealed record CodexThreadOption(string Label, string SessionId, string ProjectPath)
    {
        public override string ToString() => Label;
    }

    public MainWindow(BridgeServer? bridgeServer = null)
    {
        InitializeComponent();
        _bridgeServer = bridgeServer;
        if (bridgeServer is not null)
        {
            bridgeServer.TaskChanged += OnBridgeTaskChanged;
            bridgeServer.ExtensionProgressChanged += OnExtensionProgress;
        }
        _flowTimer.Tick += (_, _) => UpdateArrowAnimation();
        _flowTimer.Start();
        _connectionTimer.Tick += async (_, _) => await RefreshConnectionChecksAsync();
        _connectionTimer.Start();
        _jobWatchdogTimer.Tick += (_, _) => CheckJobInactivity();
        _jobWatchdogTimer.Start();
        RepositoryNameText.Text = " · MCP-with-MiniPC";
        PcNameText.Text = Environment.MachineName;
        SetFlowState(codexActive: false, workerActive: false, webActive: false);
        ActivateResultTab(web: false);
        UpdateUsage(CodexUsage.Empty);
        Loaded += async (_, _) => { RestoreWindowPosition(); await InitializeStartupConfigurationAsync(); };
        ProjectStatusText.Text = "CHECKING";
        WebStatusText.Text = "WAITING";
        ServerStatusText.Text = "CHECKING";
        _trayIcon = new Forms.NotifyIcon
        {
            Text = "ProjectHub Worker · MCP-with-MiniPC",
            Icon = CreateTrayIcon(),
            Visible = true,
            ContextMenuStrip = new Forms.ContextMenuStrip()
        };
        _trayIcon.ContextMenuStrip.Items.Add("Open", null, (_, _) => ShowFromTray());
        _trayIcon.ContextMenuStrip.Items.Add("Pause", null, (_, _) => { });
        _trayIcon.ContextMenuStrip.Items.Add("Exit", null, (_, _) => ExitWorker());
        _trayIcon.DoubleClick += (_, _) => ShowFromTray();
    }

    private void RestoreWindowPosition()
    {
        try
        {
            if (!File.Exists(WindowPlacementPath)) return;
            using var document = JsonDocument.Parse(File.ReadAllText(WindowPlacementPath));
            var root = document.RootElement;
            if (!root.TryGetProperty("left", out var leftElement) || !root.TryGetProperty("top", out var topElement)) return;
            var left = leftElement.GetDouble();
            var top = topElement.GetDouble();
            if (double.IsNaN(left) || double.IsNaN(top) || double.IsInfinity(left) || double.IsInfinity(top)) return;
            if (Forms.Screen.AllScreens.Any(screen => screen.WorkingArea.Contains((int)left, (int)top)))
            {
                Left = left;
                Top = top;
            }
        }
        catch
        {
        }
    }
    private void SaveWindowPosition()
    {
        try
        {
            if (WindowState != WindowState.Normal || double.IsNaN(Left) || double.IsNaN(Top)) return;
            Directory.CreateDirectory(WorkerPaths.Config);
            File.WriteAllText(WindowPlacementPath, JsonSerializer.Serialize(new { left = Left, top = Top }));
        }
        catch
        {
        }
    }
    private static Drawing.Icon CreateTrayIcon()
    {
        try
        {
            var executable = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(executable))
                return Drawing.Icon.ExtractAssociatedIcon(executable) ?? Drawing.SystemIcons.Application;
        }
        catch
        {
        }

        return Drawing.SystemIcons.Application;
    }
    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!_allowClose && (((App)System.Windows.Application.Current).ShutdownRequested || Dispatcher.HasShutdownStarted))
            _allowClose = true;

        if (_allowClose)
        {
            SaveWindowPosition();
            _activeTaskCts?.Cancel();
            _flowTimer.Stop();
            _connectionTimer.Stop();
            _trayIcon.Visible = false;
            _trayIcon.ContextMenuStrip?.Dispose();
            _trayIcon.Dispose();
            _connectionClient.Dispose();
            return;
        }

        e.Cancel = true;
        Hide();
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void ExitWorker()
    {
        SaveWindowPosition();
        _allowClose = true;
        ((App)System.Windows.Application.Current).RequestShutdown();
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        SetSettingsPopupOpen(!StatusPopup.IsOpen);
    }

    private void CloseSettings_Click(object sender, RoutedEventArgs e)
    {
        SetSettingsPopupOpen(false);
    }

    private void SetSettingsPopupOpen(bool open)
    {
        StatusPopup.IsOpen = open;
        SettingsDimOverlay.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
    }
    private void CommandInput_GotFocus(object sender, RoutedEventArgs e)
    {
        SetInputFocusState(CommandInput, Placeholder, focused: true);
    }

    private void CommandInput_LostFocus(object sender, RoutedEventArgs e)
    {
        SetInputFocusState(CommandInput, Placeholder, focused: false);
    }

    private void WebInstructionInput_GotFocus(object sender, RoutedEventArgs e)
    {
        SetInputFocusState(WebInstructionInput, WebInstructionPlaceholder, focused: true);
    }

    private void WebInstructionInput_LostFocus(object sender, RoutedEventArgs e)
    {
        SetInputFocusState(WebInstructionInput, WebInstructionPlaceholder, focused: false);
    }

    private void SetInputFocusState(System.Windows.Controls.TextBox input, string placeholder, bool focused)
    {
        if (focused)
        {
            if (input.Text == placeholder)
            {
                input.Text = string.Empty;
                input.Foreground = FindResource("Ink") as System.Windows.Media.Brush;
            }
        }
        else if (string.IsNullOrWhiteSpace(input.Text))
        {
            input.Text = placeholder;
            input.Foreground = FindResource("Muted") as System.Windows.Media.Brush;
        }
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        if (_activeTaskCts is not null) return;
        CommandInput.Text = Placeholder;
        CommandInput.Foreground = FindResource("Muted") as System.Windows.Media.Brush;
        WebInstructionInput.Text = WebInstructionPlaceholder;
        WebInstructionInput.Foreground = FindResource("Muted") as System.Windows.Media.Brush;
    }
    private void SetFlowState(bool codexActive, bool workerActive, bool webActive)
    {
        var (left, right) = ResolveFlowPair();
        SetFlowNode(left, isActive: IsNodeActive(left, codexActive, workerActive, webActive), isLeft: true);
        SetFlowNode(right, isActive: IsNodeActive(right, codexActive, workerActive, webActive), isLeft: false);
        _pairArrowActive = codexActive || workerActive || webActive;
        _flowFrame = 0;
        UpdateArrowAnimation();
        var running = codexActive || workerActive || webActive || (!_userCanceledTask && (_activeTaskCts is not null || _awaitingWebResult));
        if (!running) _messageExpanded = false;
        UpdatePanelLayout(running);
    }

    private (FlowNode Left, FlowNode Right) ResolveFlowPair() => TaskDirection.Text switch
    {
        "WORKER → GPT WEB" => (FlowNode.Worker, FlowNode.Web),
        "GPT WEB → WORKER" => (FlowNode.Web, FlowNode.Worker),
        "GPT WEB → CODEX" => (FlowNode.Web, FlowNode.Codex),
        "WORKER → JUDGE" => (FlowNode.Worker, FlowNode.Judge),
        "JUDGE → CODEX" => (FlowNode.Judge, FlowNode.Codex),
        _ => (FlowNode.Codex, FlowNode.Worker)
    };

    private bool IsNodeActive(FlowNode node, bool codexActive, bool workerActive, bool webActive) => node switch
    {
        FlowNode.Codex => codexActive,
        FlowNode.Worker => workerActive,
        FlowNode.Web => webActive,
        FlowNode.Judge => _judgeReviewing,
        _ => false
    };

    private void SetFlowNode(FlowNode node, bool isActive, bool isLeft)
    {
        var background = isLeft ? LeftNodeBackground : RightNodeBackground;
        var codexGray = isLeft ? LeftCodexGrayIcon : RightCodexGrayIcon;
        var codexColor = isLeft ? LeftCodexColorIcon : RightCodexColorIcon;
        var workerGray = isLeft ? LeftWorkerGrayIcon : RightWorkerGrayIcon;
        var workerColor = isLeft ? LeftWorkerColorIcon : RightWorkerColorIcon;
        var webGray = isLeft ? LeftWebGrayIcon : RightWebGrayIcon;
        var webColor = isLeft ? LeftWebColorIcon : RightWebColorIcon;
        var judgeGray = isLeft ? LeftJudgeGrayIcon : RightJudgeGrayIcon;
        var judgeColor = isLeft ? LeftJudgeColorIcon : RightJudgeColorIcon;
        var label = isLeft ? FlowLeftLabel : FlowRightLabel;
        var stage = isLeft ? FlowLeftStage : FlowRightStage;
        SetNodeIcon(codexGray, codexColor, node == FlowNode.Codex, isActive);
        SetNodeIcon(workerGray, workerColor, node == FlowNode.Worker, isActive);
        SetNodeIcon(webGray, webColor, node == FlowNode.Web, isActive);
        judgeGray.Visibility = node == FlowNode.Judge && !isActive ? Visibility.Visible : Visibility.Collapsed;
        judgeColor.Visibility = node == FlowNode.Judge && isActive ? Visibility.Visible : Visibility.Collapsed;
        var (name, brush) = node switch
        {
            FlowNode.Codex => ("CODEX", System.Windows.Media.Brushes.MidnightBlue),
            FlowNode.Worker => ("WORKER", System.Windows.Media.Brushes.SeaGreen),
            FlowNode.Web => ("GPT WEB", System.Windows.Media.Brushes.RoyalBlue),
            _ => ("JUDGE", System.Windows.Media.Brushes.DarkViolet)
        };
        background.Visibility = node == FlowNode.Judge ? Visibility.Collapsed : Visibility.Visible;
        background.Background = isActive ? brush : System.Windows.Media.Brushes.SlateGray;
        label.Text = name;
        label.Foreground = isActive ? brush : System.Windows.Media.Brushes.SlateGray;
        stage.Text = isActive ? "진행 중" : "대기 중";
        stage.Foreground = isActive ? brush : System.Windows.Media.Brushes.SlateGray;
        judgeGray.Opacity = 0.72;
        judgeColor.Opacity = 1;
    }

    private static void SetNodeIcon(System.Windows.Controls.Image gray, System.Windows.Controls.Image color, bool selected, bool active)
    {
        gray.Visibility = selected && !active ? Visibility.Visible : Visibility.Collapsed;
        color.Visibility = selected && active ? Visibility.Visible : Visibility.Collapsed;
    }
    private void UpdatePanelLayout(bool running)
    {
        if (running) _messageExpanded = true;

        MessageSection.Visibility = Visibility.Visible;
        MessageContentGrid.Visibility = _messageExpanded ? Visibility.Visible : Visibility.Collapsed;
        MessageToggleButton.Content = _messageExpanded ? "▥  MESSAGE  ▲" : "▥  MESSAGE  ▼";
        MessageRow.Height = _messageExpanded ? new GridLength(1, GridUnitType.Star) : new GridLength(52);
        CommandRow.Height = running ? new GridLength(90) : _messageExpanded ? new GridLength(170) : new GridLength(1, GridUnitType.Star);
        CommandTextGrid.Visibility = running ? Visibility.Collapsed : Visibility.Visible;
        CommandControlsRow.Height = new GridLength(38);
        CommandControlsGrid.Visibility = Visibility.Visible;
    }

    private void MessageToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_activeTaskCts is not null || _awaitingWebResult) return;
        _messageExpanded = !_messageExpanded;
        UpdatePanelLayout(false);
    }

    private void UpdateArrowAnimation()
    {
        var activeIndex = _flowFrame++ % 5;
        var opacities = new[] { 1.0, 0.32, 0.32 };
        SetArrowFrame(new[] { FlowArrow1, FlowArrow2, FlowArrow3 }, _pairArrowActive, activeIndex, opacities);
        UpdateJudgeVisual();
    }
    private void UpdateJudgeVisual()
    {
        var enabled = _targetSettings.EffectiveJudge.Enabled;
        JudgeFlowText.Text = _judgeStatus switch { "REVIEWING" => "JUDGE · REVIEWING", "FALLBACK" => "JUDGE · WEB FALLBACK", "READY" => "JUDGE · READY", _ => "JUDGE · OFF" };
        JudgeFlowText.Foreground = _judgeReviewing ? System.Windows.Media.Brushes.DarkViolet : enabled ? System.Windows.Media.Brushes.SlateBlue : System.Windows.Media.Brushes.SlateGray;
        JudgePulseDot.Fill = _judgeReviewing ? System.Windows.Media.Brushes.MediumPurple : enabled ? System.Windows.Media.Brushes.SlateBlue : System.Windows.Media.Brushes.SlateGray;
        JudgePulseDot.Opacity = _judgeReviewing ? 0.45 + ((_flowFrame % 5) * 0.11) : 1;
    }
    private static void SetArrowFrame(TextBlock[] arrows, bool active, int frame, double[] opacities)
    {
        var phase = active ? frame : -1;
        for (var index = 0; index < arrows.Length; index++)
        {
            arrows[index].Opacity = phase switch
            {
                0 => index <= 0 ? opacities[0] : opacities[1],
                1 => index <= 1 ? opacities[0] : opacities[1],
                2 => opacities[0],
                3 => index == 0 ? opacities[1] : opacities[0],
                4 => index == 2 ? opacities[0] : opacities[1],
                _ => 0.3
            };
        }
    }
    private async void RunTask_Click(object sender, RoutedEventArgs e)
    {
        if (_activeTaskCts is not null || _awaitingWebResult)
        {
            _userCanceledTask = true;
            _activeTaskCts?.Cancel();
            if (_bridgeServer is not null && _bridgeServer.CancelActiveTask(out var canceledTaskId) && canceledTaskId is not null)
                _userCanceledBridgeTaskIds.Add(canceledTaskId);
            ResetTaskState();
            ApplyConnectionStatus();
            return;
        }
        await InitializeStartupConfigurationAsync();
        if (!_codexAuthenticated || _bridgeServer is null || !_bridgeServer.WebConnected || !_bridgeServer.WebExtensionSynchronized || !_bridgeServer.WebConversationBound)
        {
            TaskDirection.Text = "PREFLIGHT";
            TaskTitle.Text = _bridgeServer?.WebConversationBound == false ? "GPT Web 대화 연결 필요" : "연결 상태 확인 필요";
            SetFlowState(false, false, false);
            return;
        }

        if (string.IsNullOrWhiteSpace(CommandInput.Text) || CommandInput.Text == Placeholder)
            return;

        var prompt = CommandInput.Text.Trim();
        var webInstruction = WebInstructionInput.Text == WebInstructionPlaceholder ? string.Empty : WebInstructionInput.Text.Trim();
        var seedAction = ParseWebAction(prompt);
        if (seedAction.Kind is WebActionKind.ProtocolError or WebActionKind.Continue or WebActionKind.Pause or WebActionKind.End || (seedAction.Kind == WebActionKind.Begin && string.IsNullOrWhiteSpace(seedAction.Body)))
        {
            TaskTitle.Text = "잘못된 ACTION 시작 형식";
            ResultBody.Text = seedAction.Error ?? "[ACTION=BEGIN] 뒤에 작업 지시를 입력해야 합니다.";
            return;
        }
        var cliPrompt = seedAction.Kind == WebActionKind.Begin ? seedAction.Body : prompt;
        var model = GetSelectedContent(ModelCombo, "GPT-6 Luna");
        var reasoning = GetSelectedContent(ReasoningCombo, "Medium").ToLowerInvariant();
        var cliModel = ToCliModel(model);
        var selectedThread = CodexThreadCombo.SelectedItem as CodexThreadOption;
        StartTaskTranscript(selectedThread, cliPrompt, webInstruction);
        var workingDirectory = ResolveWorkingDirectory(selectedThread);
        var sessionId = string.IsNullOrWhiteSpace(selectedThread?.SessionId) ? null : selectedThread.SessionId;
        var initialGitReference = await GitReviewGate.CheckAsync(workingDirectory, _targetSettings);
        _initialGitReferenceHeader = BuildGitReferenceHeader(initialGitReference);
        var initialCliPrompt = _initialGitReferenceHeader + Environment.NewLine + Environment.NewLine + cliPrompt;
        _activePrompt = cliPrompt;
        _activeWebInstruction = webInstruction;
        _actionProtocolEnabled = true;
        _activeReadOnly = IsExplicitReadOnlyRequest(cliPrompt);
        _jobTimedOut = false;
        _userCanceledTask = false;
        _lastActivityAt = DateTimeOffset.UtcNow;
        _lastWebTaskId = null;
        _activeWorkingDirectory = workingDirectory;
        _activeSessionId = sessionId;
        _activeCliModel = cliModel;
        _activeReasoning = reasoning;
        AddTaskMessage("TASK START", BuildTaskStartInfo(cliModel, reasoning, workingDirectory, sessionId));
        _judgeRound = 0;
        _judgeReportOnly = false;
        _pendingJevFailure = null;
        _activeJevJobId = Guid.NewGuid().ToString("N");
        _judgeStatus = _targetSettings.EffectiveJudge.Enabled ? "READY" : "OFF";
        _webFollowupStarted = false;
        _commandUsage = CodexUsage.Empty;
        UpdateUsage(_commandUsage);
        var cts = new CancellationTokenSource();
        _activeTaskCts = cts;
        RunButton.IsEnabled = true;
        RunButton.Content = "■  Cancel";
        TaskDirection.Text = "CODEX → WORKER";
        TaskTitle.Text = "Codex 작업 실행 중";
        SetFlowState(codexActive: true, workerActive: false, webActive: false);

        try
        {
            var result = await RunCodexWithJevFooterAsync(initialCliPrompt, cliModel, reasoning, workingDirectory, sessionId, _activeReadOnly, cts.Token, "INITIAL_IMPLEMENTATION");
            if (_userCanceledTask) return;
            _lastActivityAt = DateTimeOffset.UtcNow;
            _lastCodexResult = result;
            AddCliRoundStatus(result);
            _activeSessionId = result.SessionId ?? _activeSessionId;
            CodexThreadArchive.Save(result, cliPrompt, workingDirectory);
            await RefreshCodexSelectionsAfterCliAsync(result.SessionId, workingDirectory);
            UpdateCodexSelectionDisplay();
            SetFlowState(codexActive: false, workerActive: true, webActive: false);
            TaskDirection.Text = "CODEX → WORKER";
            TaskTitle.Text = result.ExitCode == 0 ? "Codex 결과 수신 완료" : "Codex 실행 실패";
            ResultTitle.Text = result.ExitCode == 0 ? $"Codex PASS · {cliModel}" : $"Codex FAIL · exit {result.ExitCode}";
            ResultBody.Text = BuildResultBody(result);
            _commandUsage = result.Usage;
            UpdateUsage(_commandUsage);
            ActivateResultTab(web: false);

            if (result.ExitCode == 0 && _bridgeServer is not null)
            {
                var task = await RouteCodexResultAsync(result, webInstruction, includeWebInstruction: true, gitReferenceHeader: _initialGitReferenceHeader, cancellationToken: cts.Token);
                if (_userCanceledTask) return;
                if (task is not null)
                {
                    _awaitingWebResult = true;
                    RunButton.Content = "■  Cancel";
                    TaskDirection.Text = "WORKER → GPT WEB";
                    TaskTitle.Text = "GPT Web 전달 대기 중";
                    SetFlowState(false, true, true);
                }
            }
        }
        catch (OperationCanceledException)
        {
            if (_jobTimedOut || _userCanceledTask) return;
            _awaitingWebResult = false;
            TaskTitle.Text = "Codex 실행 취소";
            ResultTitle.Text = "Codex CANCELED";
            ResultBody.Text = "Codex CLI 실행이 취소되었습니다.";
            SetFlowState(codexActive: false, workerActive: false, webActive: false);
        }
        catch (Exception ex)
        {
            _awaitingWebResult = false;
            TaskTitle.Text = "Codex 실행을 시작하지 못했습니다";
            ResultTitle.Text = "Codex ERROR";
            ResultBody.Text = ex.ToString();
            SetFlowState(codexActive: false, workerActive: false, webActive: false);
        }
        finally
        {
            _activeTaskCts.Dispose();
            _activeTaskCts = null;
            var wasUserCanceled = _userCanceledTask;
            _userCanceledTask = false;
            UpdatePanelLayout(_awaitingWebResult);
            ApplyConnectionStatus();
            if (!_awaitingWebResult || wasUserCanceled) RunButton.Content = "▶  Run Task";
        }
    }

    private Task<BridgeTask?> CreateWebTaskAsync(string webPrompt, List<BridgeAttachment> attachments, string? gitReferenceHeader = null)
    {
        var prompt = string.IsNullOrWhiteSpace(gitReferenceHeader)
            ? webPrompt
            : gitReferenceHeader + Environment.NewLine + Environment.NewLine + webPrompt;
        AddTaskMessage("WORKER -> GPT WEB", prompt);
        return Task.FromResult(_bridgeServer?.CreateTaskForLatestBinding(prompt, attachments));
    }

    private async Task<BridgeTask?> RouteCodexResultAsync(CodexCliResult result, string? webInstruction, bool includeWebInstruction, string? gitReferenceHeader, CancellationToken cancellationToken)
    {
        if (result.ExitCode != 0 || _bridgeServer is null) return null;
        var output = string.IsNullOrWhiteSpace(result.FinalMessage) ? result.StandardOutput : result.FinalMessage;
        var judgeEnabled = _targetSettings.EffectiveJudge.Enabled;
        var reportOnly = _judgeReportOnly;
        var directive = judgeEnabled ? JevContract.ParseNext(output) : new NextDirective(NextRoute.Web, output);
        var report = output;
        string? protocolError = judgeEnabled ? JevContract.ValidateStructure(directive, reportOnly) : null;

        if (judgeEnabled && protocolError is not null)
        {
            AddTaskMessage("JEV", $"[CONTRACT_PROTOCOL_ERROR] {protocolError}");
            report += Environment.NewLine + Environment.NewLine + $"[JEV FALLBACK]{Environment.NewLine}CODE: {protocolError}{Environment.NewLine}ROUND: {_judgeRound}/3{Environment.NewLine}DETAIL: 계약 구조를 기계적으로 확인할 수 없습니다.";
            _judgeStatus = "FALLBACK";
        }
        else if (judgeEnabled && directive.Route == NextRoute.Jev)
        {
            if (reportOnly)
            {
                AddTaskMessage("JEV", "[CONTRACT_PROTOCOL_ERROR] REPORT_PHASE_REENTERED_JEV");
                report += Environment.NewLine + Environment.NewLine + "[JEV FALLBACK]" + Environment.NewLine + "CODE: REPORT_PHASE_REENTERED_JEV" + Environment.NewLine + "DETAIL: 보고서 전용 단계에서 JEV 재진입을 요청했습니다.";
            }
            else
            {
                var validation = JevContract.ExtractValidationRequest(directive.Body);
                _judgeRound++;
                _judgeReviewing = true;
                _judgeStatus = "REVIEWING";
                TaskDirection.Text = "WORKER → JEV";
                TaskTitle.Text = $"JEV 검증 중 · round {_judgeRound}";
                SetFlowState(false, true, false);
                AddTaskMessage("JEV REQUEST", validation);
                var request = new JudgeRequest(_activePrompt ?? "Current task", _judgeRound, _activeWorkingDirectory ?? AppContext.BaseDirectory, output, validation, result.Files, "GIT", _gitTarget?.HeadSha, _activeJevJobId);
                JudgeResult judgment;
                try { judgment = await _jevJudgeRunner.ReviewAsync(request, _targetSettings.EffectiveJudge, cancellationToken); }
                finally { _judgeReviewing = false; }
                AddTaskMessage("JEV RESULT", $"{judgment.Decision}: {judgment.Message}");
                if (judgment.Decision == JudgeDecision.Partial)
                {
                    _pendingJevFailure = judgment.Message;
                    if (_judgeRound < 3)
                    {
                        var retryPrompt = AppendJevFooter(JevRetryPromptBuilder.Build(judgment.Message, _judgeRound));
                        AddTaskMessage("WORKER -> CODEX", retryPrompt);
                        TaskDirection.Text = "JEV → CODEX";
                        TaskTitle.Text = "JEV FAIL 후 Codex 보완 실행 중";
                        SetFlowState(true, true, false);
                        var retry = await RunCodexWithJevFooterAsync(retryPrompt, _activeCliModel!, _activeReasoning!, _activeWorkingDirectory!, _activeSessionId, _activeReadOnly, cancellationToken, "JEV_PARTIAL_RETRY", "JEV_PARTIAL");
                        _activeSessionId = retry.SessionId ?? _activeSessionId;
                        _lastCodexResult = retry;
                        AddCliRoundStatus(retry);
                        CodexThreadArchive.Save(retry, retryPrompt, _activeWorkingDirectory!);
                        _commandUsage = _commandUsage.Add(retry.Usage);
                        UpdateUsage(_commandUsage);
                        return await RouteCodexResultAsync(retry, webInstruction, includeWebInstruction, gitReferenceHeader, cancellationToken);
                    }
                    report += Environment.NewLine + Environment.NewLine + "[JEV REVIEW]" + Environment.NewLine + "CODE: JEV_PARTIAL_LIMIT" + Environment.NewLine + "ROUND: 3/3" + Environment.NewLine + "DETAIL: PARTIAL 상태가 재검증 상한까지 해소되지 않아 검토가 필요합니다." + Environment.NewLine + Environment.NewLine + judgment.Message;
                }
                else if (judgment.Decision == JudgeDecision.Error)
                {
                    report += Environment.NewLine + Environment.NewLine + "[JEV FALLBACK]" + Environment.NewLine + $"CODE: {judgment.Message}" + Environment.NewLine + $"ROUND: {_judgeRound}/3" + Environment.NewLine + "DETAIL: JEV 검증 오류로 원래 Codex 결과를 전달합니다.";
                    _judgeStatus = "FALLBACK";
                }
                else
                {
                    _pendingJevFailure = null;
                    _judgeReportOnly = true;
                    var reportPrompt = AppendJevFooter("[JEV VALIDATION PASSED]" + Environment.NewLine + Environment.NewLine + "요청한 JEV 검증이 모두 통과했다. 추가 구현이나 변경은 하지 말고 현재 작업 상태를 기준으로 [NEXT : WEB]으로 시작하는 [REPORT]를 작성하라.");
                    AddTaskMessage("JEV -> CODEX", reportPrompt);
                    TaskDirection.Text = "JEV → CODEX";
                    TaskTitle.Text = "JEV 통과 · Codex 보고서 생성 중";
                    SetFlowState(true, true, false);
                    var reportResult = await RunCodexWithJevFooterAsync(reportPrompt, _activeCliModel!, _activeReasoning!, _activeWorkingDirectory!, _activeSessionId, _activeReadOnly, cancellationToken, "JEV_REPORT_ONLY");
                    _activeSessionId = reportResult.SessionId ?? _activeSessionId;
                    _lastCodexResult = reportResult;
                    AddCliRoundStatus(reportResult);
                    CodexThreadArchive.Save(reportResult, reportPrompt, _activeWorkingDirectory!);
                    _commandUsage = _commandUsage.Add(reportResult.Usage);
                    UpdateUsage(_commandUsage);
                    return await RouteCodexResultAsync(reportResult, webInstruction, includeWebInstruction, gitReferenceHeader, cancellationToken);
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(_pendingJevFailure))
            report += Environment.NewLine + Environment.NewLine + "[JEV UNRESOLVED]" + Environment.NewLine + _pendingJevFailure;
        var prompt = BuildWebPrompt(result with { FinalMessage = report }, webInstruction, includeControlInstructions: true, includeWebInstruction);
        var attachments = BuildWebAttachments(_bridgeServer, result.Files);
        _judgeReportOnly = false;
        _judgeRound = 0;
        _pendingJevFailure = null;
        return await CreateWebTaskAsync(prompt, attachments, gitReferenceHeader);
    }
    private string AppendJevFooter(string prompt)
    {
        if (!_targetSettings.EffectiveJudge.Enabled) return prompt;
        try { return prompt + Environment.NewLine + Environment.NewLine + JevContract.LoadFooter(); }
        catch (Exception exception)
        {
            AddTaskMessage("JEV", "footer 로드 실패: " + exception.Message);
            return prompt;
        }
    }
    private async Task<CodexCliResult> RunCodexWithJevFooterAsync(string prompt, string model, string reasoning, string workingDirectory, string? sessionId, bool readOnly, CancellationToken cancellationToken, string purpose = "WEB_FOLLOWUP", string? retryReason = null)
    {
        var fullPrompt = AppendJevFooter(prompt);
        var startedAt = DateTimeOffset.UtcNow;
        var footerBytes = Math.Max(0, Encoding.UTF8.GetByteCount(fullPrompt) - Encoding.UTF8.GetByteCount(prompt));
        var footerText = fullPrompt.Length >= prompt.Length ? fullPrompt[prompt.Length..] : string.Empty;
        CodexCliResult result;
        try { result = await _codexRunner.RunAsync(fullPrompt, model, reasoning, workingDirectory, sessionId, readOnly, cancellationToken); }
        catch (OperationCanceledException)
        {
            AppendCodexFailureTelemetry(startedAt, prompt, fullPrompt, footerText, model, reasoning, purpose, retryReason, "CANCELLED");
            throw;
        }
        catch (Exception ex)
        {
            AppendCodexFailureTelemetry(startedAt, prompt, fullPrompt, footerText, model, reasoning, purpose, retryReason, "ERROR_" + ex.GetType().Name);
            throw;
        }
        UsageTelemetryStore.Append(new ModelCallTelemetry(
            _activeJevJobId, _judgeRound, "CODEX", model, reasoning, purpose,
            result.Usage.InputTokens, result.Usage.CachedInputTokens, result.Usage.OutputTokens, result.Usage.ReasoningOutputTokens, result.Usage.ProviderTotalTokens,
            Encoding.UTF8.GetByteCount(fullPrompt), Encoding.UTF8.GetByteCount(prompt), footerBytes, null, Encoding.UTF8.GetByteCount(result.StandardOutput),
            Math.Max(0, (long)(result.FinishedAt - result.StartedAt).TotalMilliseconds), retryReason ?? (result.ExitCode != 0 ? "CLI_EXIT_" + result.ExitCode : null), result.Usage.UsageKnown,
            null, null, result.FinishedAt, DigestText(prompt), DigestText(footerText), DigestText(fullPrompt)));
        return result;
    }

    private void AppendCodexFailureTelemetry(DateTimeOffset startedAt, string prompt, string fullPrompt, string footer, string model, string reasoning, string purpose, string? retryReason, string error)
    {
        UsageTelemetryStore.Append(new ModelCallTelemetry(
            _activeJevJobId, _judgeRound, "CODEX", model, reasoning, purpose, null, null, null, null, null,
            Encoding.UTF8.GetByteCount(fullPrompt), Encoding.UTF8.GetByteCount(prompt), Math.Max(0, Encoding.UTF8.GetByteCount(fullPrompt) - Encoding.UTF8.GetByteCount(prompt)), null, 0,
            Math.Max(0, (long)(DateTimeOffset.UtcNow - startedAt).TotalMilliseconds), retryReason ?? error, false, null, null, DateTimeOffset.UtcNow,
            DigestText(prompt), DigestText(footer), DigestText(fullPrompt)));
    }

    private static string DigestText(string value) => "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static string BuildGitReferenceHeader(GitReviewCheckpoint checkpoint) =>
        checkpoint.ReviewCommitSha is null
            ? $"[REVIEW_SOURCE=LOCAL]{Environment.NewLine}[GIT_REFERENCE={checkpoint.SyncState}]"
            : $"[REVIEW_SOURCE=GIT]{Environment.NewLine}[REVIEW_COMMIT_SHA={checkpoint.ReviewCommitSha}]{Environment.NewLine}[SYNC_STATE={checkpoint.SyncState}]";
    private static string BuildGitReviewSummary(GitReviewCheckpoint checkpoint) =>
        $"Git reference: {checkpoint.SyncState}{Environment.NewLine}" +
        $"Repository: {checkpoint.RepositoryUrl ?? "unconfigured"}{Environment.NewLine}" +
        $"Branch: {checkpoint.Branch ?? "unknown"}{Environment.NewLine}" +
        $"Working tree: {checkpoint.SyncState switch { "LOCAL_DIRTY" => "DIRTY", "REMOTE_CONFIRMED" or "REMOTE_UNREACHABLE" or "REMOTE_BRANCH_UNKNOWN" or "SYNC_MISMATCH" => "CLEAN", _ => "UNKNOWN" }}{Environment.NewLine}" +
        $"Local HEAD SHA: {checkpoint.LocalHeadSha ?? "unknown"}{Environment.NewLine}" +
        $"Push confirmation: {checkpoint.PushConfirmation}{Environment.NewLine}" +
        $"Remote HEAD SHA: {checkpoint.RemoteHeadSha ?? "unknown"}{Environment.NewLine}" +
        $"Review commit SHA: {checkpoint.ReviewCommitSha ?? "not confirmed"}{Environment.NewLine}" +
        $"Server observed SHA: {checkpoint.ServerObservation}" +
        (string.IsNullOrWhiteSpace(checkpoint.PauseReason) ? string.Empty : $"{Environment.NewLine}{Environment.NewLine}Note: {checkpoint.PauseReason}");
    private void CheckJobInactivity()
    {
        if (_jobTimedOut || (!_awaitingWebResult && _activeTaskCts is null)) return;
        if (DateTimeOffset.UtcNow - _lastActivityAt < JobInactivityTimeout) return;

        _jobTimedOut = true;
        _activeTaskCts?.Cancel();
        _bridgeServer?.CancelActiveTask();
        _awaitingWebResult = false;
        AddTaskMessage("SYSTEM", "Web 또는 Codex 응답이 30분 동안 없어 작업을 종료했습니다.");
        TaskDirection.Text = "TIMEOUT";
        TaskTitle.Text = "30분 무응답으로 작업 종료";
        ResultTitle.Text = "FINISH_TIMEOUT";
        ResultBody.Text = "Web 또는 Codex에서 30분 동안 응답이 없어 작업을 종료했습니다.";
        RunButton.Content = "▶  Run Task";
        SetFlowState(false, false, false);
        ExportTaskTranscript();
    }
    private void ResetTaskState()
    {
        _awaitingWebResult = false;
        _webFollowupStarted = false;
        _actionProtocolEnabled = false;
        _activeReadOnly = false;
        _initialGitReferenceHeader = null;
        _jobTimedOut = false;
        _lastWebTaskId = null;
        _activePrompt = null;
        _judgeReportOnly = false;
        _pendingJevFailure = null;
        _activeWebInstruction = null;
        _activeWorkingDirectory = null;
        _activeSessionId = null;
        _activeCliModel = null;
        _activeReasoning = null;
        _lastWebTask = null;
        TaskDirection.Text = "IDLE";
        TaskTitle.Text = "작업 없음";
        ResultTitle.Text = "Codex 결과 대기 중";
        ResultBody.Text = "새 작업을 실행하면 결과가 이 영역에 표시됩니다.";
        RunButton.Content = "▶  Run Task";
        SetFlowState(false, false, false);
        ActivateResultTab(web: false);
    }
    private void LoadCodexSelections()
    {
        _codexProjects = DiscoverCodexProjects();
        var choices = new List<CodexThreadOption>();
        foreach (var project in _codexProjects)
        {
            choices.Add(new CodexThreadOption("(" + project.Name + ") ＋ 신규 스레드", string.Empty, project.Path));
            choices.AddRange(DiscoverCodexThreads(project.Path));
        }
        choices.Insert(0, new CodexThreadOption("(현재 폴더) ＋ 신규 스레드", string.Empty, AppContext.BaseDirectory));
        CodexThreadCombo.ItemsSource = choices;
        var saved = LoadSavedCodexSelection();
        var savedIndex = saved is null ? -1 : choices.FindIndex(choice => choice.SessionId == saved.Value.SessionId && choice.ProjectPath == saved.Value.ProjectPath);
        CodexThreadCombo.SelectedIndex = savedIndex >= 0 ? savedIndex : 0;
        UpdateCodexSelectionDisplay();
        _loadingCodexSelections = false;
    }
    private async Task RefreshCodexSelectionsAfterCliAsync(string? sessionId, string projectPath)
    {
        // 새 스레드는 CLI가 반환한 session ID를 먼저 저장해야 기존 기본 스레드로 되돌아가지 않는다.
        if (!string.IsNullOrWhiteSpace(sessionId)) SaveCodexSelection(sessionId, projectPath);
        for (var attempt = 0; attempt < 5; attempt++)
        {
            LoadCodexSelections();
            if (attempt < 4) await Task.Delay(TimeSpan.FromMilliseconds(300));
        }
    }
    private void CodexThreadCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loadingCodexSelections) SaveCodexSelection();
        UpdateCodexSelectionDisplay();
        if (_startupConfigurationInitialized) ApplyTargetConfiguration();
    }

    private void PopulateCodexThreads(CodexProjectOption? project)
    {
        var threads = new List<CodexThreadOption>();
        if (project is not null)
        {
            threads.Add(new CodexThreadOption("＋ 신규 스레드", string.Empty, project.Path));
            threads.AddRange(DiscoverCodexThreads(project.Path));
        }
        CodexThreadCombo.ItemsSource = threads;
        CodexThreadCombo.SelectedIndex = threads.Count > 0 ? 0 : -1;
    }

    private void UpdateCodexSelectionDisplay()
    {
        var thread = CodexThreadCombo.SelectedItem as CodexThreadOption;
        CodexConversationText.Text = thread?.Label ?? "Codex 스레드를 선택하세요";
    }

    private static List<CodexProjectOption> DiscoverCodexProjects()
    {
        var projects = new Dictionary<string, CodexProjectOption>(StringComparer.OrdinalIgnoreCase);
        AddProject(projects, Environment.CurrentDirectory);
        AddProject(projects, FindRepositoryRoot(AppContext.BaseDirectory));
        try
        {
            var sessionsRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "sessions");
            foreach (var file in Directory.EnumerateFiles(sessionsRoot, "*.jsonl", SearchOption.AllDirectories))
            {
                var firstLine = File.ReadLines(file).FirstOrDefault();
                if (string.IsNullOrWhiteSpace(firstLine)) continue;
                using var document = JsonDocument.Parse(firstLine);
                if (document.RootElement.TryGetProperty("payload", out var payload) && payload.TryGetProperty("cwd", out var cwd)) AddProject(projects, cwd.GetString());
            }
        }
        catch { }
        return projects.Values.OrderBy(project => project.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private (string SessionId, string ProjectPath)? LoadSavedCodexSelection()
    {
        try
        {
            var path = GetSelectionStatePath();
            if (!File.Exists(path)) return null;
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            var sessionId = root.TryGetProperty("sessionId", out var session) ? session.GetString() : null;
            var projectPath = root.TryGetProperty("projectPath", out var project) ? project.GetString() : null;
            return string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(projectPath) ? null : (sessionId, projectPath);
        }
        catch { return null; }
    }

    private void SaveCodexSelection()
    {
        var selected = CodexThreadCombo.SelectedItem as CodexThreadOption;
        if (selected is not null) SaveCodexSelection(selected.SessionId, selected.ProjectPath);
    }

    private static void SaveCodexSelection(string sessionId, string projectPath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(projectPath)) return;
            var path = GetSelectionStatePath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(new { sessionId, projectPath }));
        }
        catch { }
    }
    private static string GetSelectionStatePath()
        => Path.Combine(WorkerPaths.Config, "worker-selection.json");
    private static string? FindRepositoryRoot(string startPath)
    {
        var directory = new DirectoryInfo(startPath);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, ".git"))) return directory.FullName;
            directory = directory.Parent;
        }
        return null;
    }
    private static void AddProject(Dictionary<string, CodexProjectOption> projects, string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return;
        var fullPath = Path.GetFullPath(path);
        projects.TryAdd(fullPath, new CodexProjectOption(new DirectoryInfo(fullPath).Name, fullPath));
    }

    private static List<CodexThreadOption> DiscoverCodexThreads(string projectPath)
    {
        var indexedNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var matchedNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var indexPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "session_index.jsonl");
            foreach (var line in File.ReadLines(indexPath))
            {
                try
                {
                    using var document = JsonDocument.Parse(line);
                    var root = document.RootElement;
                    var id = root.TryGetProperty("id", out var idElement) ? idElement.GetString() : null;
                    var name = root.TryGetProperty("thread_name", out var nameElement) ? nameElement.GetString() : null;
                    if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(name)) indexedNames[id] = name;
                }
                catch (JsonException) { }
            }

            var sessionsRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "sessions");
            foreach (var file in Directory.EnumerateFiles(sessionsRoot, "*.jsonl", SearchOption.AllDirectories))
            {
                try
                {
                    var firstLine = File.ReadLines(file).FirstOrDefault();
                    if (string.IsNullOrWhiteSpace(firstLine)) continue;
                    using var document = JsonDocument.Parse(firstLine);
                    var payload = document.RootElement.GetProperty("payload");
                    if (!string.Equals(Path.GetFullPath(payload.GetProperty("cwd").GetString() ?? string.Empty), projectPath, StringComparison.OrdinalIgnoreCase)) continue;
                    var sessionId = payload.TryGetProperty("session_id", out var sessionElement) ? sessionElement.GetString() : null;
                    if (!string.IsNullOrWhiteSpace(sessionId) && indexedNames.TryGetValue(sessionId, out var name)) matchedNames[sessionId] = name;
                }
                catch (JsonException) { }
                catch (IOException) { }
            }
        }
        catch { }

        var projectName = new DirectoryInfo(projectPath).Name;
        return matchedNames.Select(pair => new CodexThreadOption("(" + projectName + ") " + pair.Value, pair.Key, projectPath)).ToList();
    }
    private string ResolveWorkingDirectory(CodexThreadOption? selectedThread)
    {
        if (!string.IsNullOrWhiteSpace(selectedThread?.SessionId) && !string.IsNullOrWhiteSpace(selectedThread.ProjectPath) && Directory.Exists(selectedThread.ProjectPath))
            return Path.GetFullPath(selectedThread.ProjectPath);

        var configured = _targetSettings.ManualWorkingDirectory;
        return !string.IsNullOrWhiteSpace(configured) && Directory.Exists(configured)
            ? Path.GetFullPath(configured)
            : AppContext.BaseDirectory;
    }

    private string? ResolveConfiguredGitFolder(CodexThreadOption? selectedThread)
    {
        if (!string.IsNullOrWhiteSpace(selectedThread?.SessionId) && !string.IsNullOrWhiteSpace(selectedThread.ProjectPath) && Directory.Exists(selectedThread.ProjectPath))
            return Path.GetFullPath(selectedThread.ProjectPath);

        return string.IsNullOrWhiteSpace(_targetSettings.ManualWorkingDirectory)
            ? null
            : Path.GetFullPath(_targetSettings.ManualWorkingDirectory);
    }

    private void UpdateWorkingDirectoryControls(CodexThreadOption? selectedThread, string workingDirectory)
    {
        var lockedToThread = !string.IsNullOrWhiteSpace(selectedThread?.SessionId);
        WorkingDirectoryInput.Text = lockedToThread || !string.IsNullOrWhiteSpace(_targetSettings.ManualWorkingDirectory)
            ? workingDirectory
            : string.Empty;
        WorkingDirectoryInput.IsReadOnly = lockedToThread;
        WorkingDirectoryBrowseButton.IsEnabled = !lockedToThread;
    }

    private void BrowseWorkingDirectory_Click(object sender, RoutedEventArgs e)
    {
        if (WorkingDirectoryInput.IsReadOnly) return;
        var settingsWasOpen = StatusPopup.IsOpen;
        SetSettingsPopupOpen(false);
        try
        {
            using var dialog = new Forms.FolderBrowserDialog
            {
                Description = "Choose the working folder for new Codex threads.",
                UseDescriptionForTitle = true,
                InitialDirectory = Directory.Exists(WorkingDirectoryInput.Text) ? WorkingDirectoryInput.Text : AppContext.BaseDirectory
            };
            if (dialog.ShowDialog() == Forms.DialogResult.OK && Directory.Exists(dialog.SelectedPath))
                WorkingDirectoryInput.Text = dialog.SelectedPath;
        }
        finally
        {
            if (settingsWasOpen) SetSettingsPopupOpen(true);
        }
    }
    private void ApplyTargetConfiguration()
    {
        var server = WorkerTargetConfiguration.ResolveServer(_targetSettings);
        _serverBaseUrl = server.Url;
        _serverBaseUrlSource = server.Source;
        var selected = CodexThreadCombo.SelectedItem as CodexThreadOption;
        var workingDirectory = ResolveWorkingDirectory(selected);
        _gitTarget = WorkerTargetConfiguration.ResolveGit(ResolveConfiguredGitFolder(selected) ?? string.Empty, _targetSettings);
        UpdateWorkingDirectoryControls(selected, workingDirectory);
        RepositoryUrlInput.Text = _gitTarget.RepositoryUrl ?? string.Empty;
        ServerUrlInput.Text = _serverBaseUrl;
        TargetGitStateText.Text = _gitTarget.IsRepository
            ? $"Branch: {_gitTarget.Branch ?? "unknown"} · Local HEAD: {_gitTarget.HeadSha?[..Math.Min(12, _gitTarget.HeadSha.Length)] ?? "unknown"}"
            : "Git: UNCONFIGURED";
        TargetPathText.Text = !string.IsNullOrWhiteSpace(selected?.SessionId) ? $"Codex ProjectPath: {selected.ProjectPath}" : $"New thread folder: {workingDirectory}";
        RepositoryNameText.Text = " · " + (_gitTarget.RepositoryUrl ?? "MCP-with-MiniPC");
        ApplyJudgeConfigurationToControls();
    }

    private void ApplyJudgeConfigurationToControls()
    {
        var judge = _targetSettings.EffectiveJudge;
        EnableJudgeCheckBox.IsChecked = judge.Enabled;
        JudgeProviderCombo.SelectedIndex = 0;
        JudgeExecutableInput.Text = judge.ManualExecutableOrEndpoint ?? JevJudgeRunner.DefaultEndpoint;
        JudgeTimeoutInput.Text = judge.TimeoutSeconds.ToString();
        JudgeSettingsStatusText.Text = judge.Enabled ? "Jev · optional fallback to GPT Web" : "Jev · OFF";
        if (!_judgeReviewing) _judgeStatus = judge.Enabled ? "READY" : "OFF";
        UpdateJudgeVisual();
    }

    private void JudgeTimeoutInput_GotFocus(object sender, RoutedEventArgs e)
    {
        JudgeTimeoutInput.SelectAll();
    }

    private void JudgeTimeoutInput_LostFocus(object sender, RoutedEventArgs e)
    {
        JudgeTimeoutInput.Text = ReadJudgeTimeout().ToString();
    }

    private void JudgeTimeoutInput_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = e.Text.Any(character => !char.IsDigit(character));
    }

    private int ReadJudgeTimeout()
    {
        return int.TryParse(JudgeTimeoutInput.Text.Trim(), out var value)
            ? Math.Clamp(value, 10, 600)
            : 120;
    }
    private async void TestJudge_Click(object sender, RoutedEventArgs e)
    {
        var endpoint = string.IsNullOrWhiteSpace(JudgeExecutableInput.Text)
            ? JevJudgeRunner.DefaultEndpoint
            : JudgeExecutableInput.Text.Trim();
        JudgeExecutableInput.Text = endpoint;
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var endpointUri) || endpointUri.Scheme != Uri.UriSchemeHttps)
        {
            JudgeSettingsStatusText.Text = "Jev · invalid HTTPS Endpoint";
            return;
        }

        var timeout = ReadJudgeTimeout();
        JudgeSettingsStatusText.Text = "Jev · testing...";
        var request = new JudgeRequest(
            "ProjectHub JEV Endpoint test",
            1,
            AppContext.BaseDirectory,
            "[NEXT : JEV]" + Environment.NewLine + Environment.NewLine + "[VALIDATION REQUEST]" + Environment.NewLine + Environment.NewLine + "- NOUL | 오늘 비가 올 확률은 몇 퍼센트나 될지 1.00으로 정규화해봐" + Environment.NewLine + "  PASS: YES >= 0.5",
            "- NOUL | 오늘 비가 올 확률은 몇 퍼센트나 될지 1.00으로 정규화해봐" + Environment.NewLine + "  PASS: YES >= 0.5",
            Array.Empty<CodexCliFile>(),
            "LOCAL",
            null);
        try
        {
            var result = await _jevJudgeRunner.ReviewAsync(request, new JudgeSettings(true, "jev", endpoint, timeout), CancellationToken.None);
            AddTaskMessage("JEV TEST", $"{result.Decision}: {result.Message}");
            JudgeSettingsStatusText.Text = result.Decision == JudgeDecision.Error
                ? $"Jev · ERROR · {result.Message}"
                : $"Jev · {result.Decision.ToString().ToUpperInvariant()}";
        }
        catch (Exception exception)
        {
            AddTaskMessage("JEV TEST", $"ERROR: {exception.GetType().Name}");
            JudgeSettingsStatusText.Text = "Jev · test failed";
        }
    }
    private async void AutoDetectTargets_Click(object sender, RoutedEventArgs e)
    {
        _targetSettings = _targetSettings with { ManualRepositoryUrl = null, RepositoryUrlSource = null };
        WorkerTargetConfiguration.Save(_targetSettings);
        ApplyTargetConfiguration();
        _serverOnline = await CheckServerAsync();
        ApplyConnectionStatus();
    }

    private async void SaveTargetSettings_Click(object sender, RoutedEventArgs e)
    {
        var server = string.IsNullOrWhiteSpace(ServerUrlInput.Text) ? WorkerTargetConfiguration.DefaultServerBaseUrl : ServerUrlInput.Text.Trim();
        var selectedThread = CodexThreadCombo.SelectedItem as CodexThreadOption;
        var workingDirectory = string.IsNullOrWhiteSpace(selectedThread?.SessionId) ? WorkingDirectoryInput.Text.Trim() : _targetSettings.ManualWorkingDirectory;
        if (string.IsNullOrWhiteSpace(selectedThread?.SessionId) && !Directory.Exists(workingDirectory))
        {
            WorkingDirectoryInput.ToolTip = "Choose an existing folder before applying settings.";
            return;
        }
        var timeout = ReadJudgeTimeout();
        var provider = GetSelectedContent(JudgeProviderCombo, "Jev").ToLowerInvariant();
        var endpoint = string.IsNullOrWhiteSpace(JudgeExecutableInput.Text) ? JevJudgeRunner.DefaultEndpoint : JudgeExecutableInput.Text.Trim();
        _targetSettings = _targetSettings with
        {
            ManualRepositoryUrl = null, ManualServerBaseUrl = server,
            RepositoryUrlSource = null, ServerBaseUrlSource = "MANUAL",
            ManualWorkingDirectory = workingDirectory,
            Judge = new JudgeSettings(EnableJudgeCheckBox.IsChecked == true, provider, endpoint, timeout)
        };
        WorkerTargetConfiguration.Save(_targetSettings);
        ApplyTargetConfiguration();
        _serverOnline = await CheckServerAsync();
        ApplyConnectionStatus();
        SetSettingsPopupOpen(false);
    }
    private async Task InitializeStartupConfigurationAsync()
    {
        if (_startupConfigurationInitialized) return;
        _startupConfigurationInitialized = true;

        // Repository discovery, Codex login status, and server endpoint resolution are
        // startup configuration work. Do not repeat them from the periodic status timer.
        _targetSettings = WorkerTargetConfiguration.Load();
        LoadCodexSelections();
        ApplyTargetConfiguration();
        _codexAuthenticated = await CheckCodexAuthenticationAsync();
        _serverOnline = await CheckServerAsync();
        ApplyConnectionStatus();
    }

    private async Task RefreshConnectionChecksAsync()
    {
        // Keep the timer lightweight: startup configuration is intentionally one-shot.
        // Bridge/Web state is already updated by heartbeat callbacks and task events.
        await Task.CompletedTask;
        ApplyConnectionStatus();
    }

    private void ApplyConnectionStatus()
    {
        var webOnline = _bridgeServer?.WebConnected == true;
        var webExtensionReady = _bridgeServer?.WebExtensionSynchronized == true;
        var webConversationBound = _bridgeServer?.WebConversationBound == true;
        SetConnectionStatus(ProjectStatusText, _codexAuthenticated ? "READY" : "LOGIN NEEDED", _codexAuthenticated, ProjectStatusDot);
        SetConnectionStatus(WebStatusText, !webOnline ? "WAITING" : !webExtensionReady ? "UPDATE REQUIRED" : !webConversationBound ? "BIND REQUIRED" : "READY", webOnline && webExtensionReady && webConversationBound, waiting: !webOnline, indicator: WebStatusDot);
        WebDescriptionText.Text = !webOnline ? "MCP 프로젝트 진척도 확인" : !webExtensionReady ? "확장 업데이트 필요" : !webConversationBound ? "현재 GPT Web 대화를 연결하세요" : !string.IsNullOrWhiteSpace(_bridgeServer?.WebConversationTitle) ? _bridgeServer.WebConversationTitle : "MCP 프로젝트 진척도 확인";
        RunButton.IsEnabled = !(_userCanceledTask && _activeTaskCts is not null) && (_activeTaskCts is not null || _awaitingWebResult || (webOnline && webExtensionReady && webConversationBound));
        SetConnectionStatus(ServerStatusText, _serverOnline ? "READY" : "OFFLINE", _serverOnline, indicator: ServerStatusDot);
        RepositoryNameText.Foreground = _serverOnline ? FindResource("Muted") as System.Windows.Media.Brush : System.Windows.Media.Brushes.OrangeRed;
        PcNameText.Foreground = _codexAuthenticated ? FindResource("Muted") as System.Windows.Media.Brush : System.Windows.Media.Brushes.OrangeRed;
    }

    private static void SetConnectionStatus(TextBlock target, string value, bool ready, System.Windows.Shapes.Ellipse? indicator = null, bool waiting = false)
    {
        target.Text = value;
        target.Foreground = ready ? System.Windows.Media.Brushes.ForestGreen : waiting ? System.Windows.Media.Brushes.Black : System.Windows.Media.Brushes.OrangeRed;
        if (indicator is not null) indicator.Fill = ready ? System.Windows.Media.Brushes.LimeGreen : waiting ? System.Windows.Media.Brushes.SlateGray : System.Windows.Media.Brushes.OrangeRed;
    }

    private async Task<bool> CheckCodexAuthenticationAsync()
    {
        var executable = _codexRunner.FindExecutable();
        if (executable is null) return false;
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };
        process.StartInfo.ArgumentList.Add("login");
        process.StartInfo.ArgumentList.Add("status");
        try
        {
            if (!process.Start()) return false;
            await process.WaitForExitAsync();
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private async Task<bool> CheckServerAsync()
    {
        try
        {
            using var response = await _connectionClient.GetAsync(_serverBaseUrl.TrimEnd('/') + "/api/status");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
    private static string ResolveServerBaseUrl()
    {
        var configured = Environment.GetEnvironmentVariable("PROJECTHUB_AGENT_SERVER_BASE_URL");
        return string.IsNullOrWhiteSpace(configured) ? "https://projecthub.ornithopter.bid" : configured.Trim();
    }

    private static string GetSelectedContent(System.Windows.Controls.ComboBox combo, string fallback)
        => (combo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? fallback;

    private static string ToCliModel(string model)
        => model.Trim().ToLowerInvariant().Replace(" ", "-");

    private void OnExtensionProgress(ExtensionProgress progress)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            var key = $"{progress.TaskId}|{progress.Stage}|{progress.Detail}|{progress.Attempt}";
            if (string.Equals(_lastExtensionProgressKey, key, StringComparison.Ordinal)) return;
            _lastExtensionProgressKey = key;
            var detail = string.IsNullOrWhiteSpace(progress.Detail) ? string.Empty : $" · {progress.Detail}";
            AddTaskMessage("WEB EXTENSION", $"{progress.Stage}{detail}");
            if (progress.Stage is not "FINISHED" and not "FAILED")
            {
                TaskDirection.Text = "WORKER → GPT WEB";
                TaskTitle.Text = $"Web {progress.Stage}";
                SetFlowState(false, true, true);
            }
        }));
    }

    private void OnBridgeTaskChanged(BridgeTask task)
    {
        Dispatcher.BeginInvoke(new Action(() => HandleBridgeTaskChanged(task)));
    }

    private async void HandleBridgeTaskChanged(BridgeTask task)
    {
        if ((task.Status is "COMPLETED" or "FAILED") && _userCanceledBridgeTaskIds.Remove(task.Id))
        {
            SetFlowState(false, false, false);
            return;
        }

        if (_userCanceledTask && (task.Status is "COMPLETED" or "FAILED"))
        {
            SetFlowState(false, false, false);
            return;
        }

        if (task.Status is "PENDING" or "CLAIMED")
        {
            _lastActivityAt = DateTimeOffset.UtcNow;
            _awaitingWebResult = true;
            RunButton.Content = "■  Cancel";
            TaskDirection.Text = "WORKER → GPT WEB";
            TaskTitle.Text = task.Status == "PENDING" ? "Worker Message 대기 중" : "GPT Web에 메시지 전달 중";
            SetFlowState(false, true, true);
            return;
        }

        if (task.Status is not "COMPLETED" and not "FAILED") return;
        _lastActivityAt = DateTimeOffset.UtcNow;
        var duplicateTerminalEvent = _actionProtocolEnabled && _lastWebTaskId == task.Id;
        _awaitingWebResult = false;
        RunButton.Content = "▶  Run Task";
        if (_jobTimedOut || duplicateTerminalEvent)
        {
            SetFlowState(false, false, false);
            return;
        }
        _lastWebTaskId = task.Id;
        _lastWebTask = task;
        UsageTelemetryStore.Append(new ModelCallTelemetry(
            _activeJevJobId, _judgeRound, "GPT_WEB", "unknown", null, "COORDINATOR_RESPONSE",
            null, null, null, null, null, Encoding.UTF8.GetByteCount(task.Prompt ?? string.Empty), Encoding.UTF8.GetByteCount(task.Prompt ?? string.Empty), 0,
            task.Attachments?.Sum(x => x.Size) ?? 0, Encoding.UTF8.GetByteCount(task.Result ?? string.Empty),
            task.StartedAt is not null && task.CompletedAt is not null ? Math.Max(0, (long)(task.CompletedAt.Value - task.StartedAt.Value).TotalMilliseconds) : 0,
            task.FinishReason, false, null, null, DateTimeOffset.UtcNow));
        AddTaskMessage("GPT WEB", task.Result);
        ResultTitle.Text = task.Status == "COMPLETED" ? "GPT Web 응답 수신 완료" : "GPT Web FAIL";
        ResultBody.Text = task.Result ?? "응답 내용이 없습니다.";
        ActivateResultTab(web: true);

        if (task.Status == "COMPLETED" && (_actionProtocolEnabled || !_webFollowupStarted) && _activeWorkingDirectory is not null && _activeCliModel is not null && _activeReasoning is not null)
        {
            _webFollowupStarted = true;
            await RunWebResponseThroughCodexAsync(task);
            return;
        }

        _awaitingWebResult = false;
        RunButton.Content = "▶  Run Task";
        TaskDirection.Text = "GPT WEB → WORKER";
        TaskTitle.Text = task.Status == "COMPLETED" ? "Web 응답 수신 완료" : "Web 응답 수신 실패";
        SetFlowState(false, true, false);
    }

    private void FinishActionTask(WebAction action, string response)
    {
        _awaitingWebResult = false;
        RunButton.Content = "▶  Run Task";
        TaskDirection.Text = "GPT WEB → WORKER";
        TaskTitle.Text = action.Kind switch
        {
            WebActionKind.End => "Web이 작업 완료를 알림",
            WebActionKind.Pause => "Web이 사용자 판단을 요청함",
            WebActionKind.ProtocolError => "ACTION 프로토콜 오류",
            _ => "Web 결과 처리 종료"
        };
        ResultTitle.Text = action.Kind switch
        {
            WebActionKind.End => "FINISH_SUCCESS",
            WebActionKind.Pause => "FINISH_PAUSED",
            WebActionKind.ProtocolError => "FINISH_PROTOCOL_ERROR",
            _ => "Web 결과"
        };
        ResultBody.Text = action.Kind == WebActionKind.ProtocolError ? (action.Error ?? "ACTION 프로토콜 오류") + Environment.NewLine + Environment.NewLine + response : response;
        ActivateResultTab(web: true);
        ExportTaskTranscript();
        SetFlowState(false, false, false);
    }

    private async Task RunWebResponseThroughCodexAsync(BridgeTask task)
    {
        var workingDirectory = _activeWorkingDirectory;
        var model = _activeCliModel;
        var reasoning = _activeReasoning;
        if (workingDirectory is null || model is null || reasoning is null)
        {
            _awaitingWebResult = false;
            return;
        }

        _lastActivityAt = DateTimeOffset.UtcNow;
        var webResponse = task.Result ?? string.Empty;
        var action = ParseWebAction(webResponse, strict: true);
        string followupPrompt;
        if (_actionProtocolEnabled)
        {
            if (action.Kind is WebActionKind.End or WebActionKind.Pause or WebActionKind.ProtocolError or WebActionKind.None or WebActionKind.Begin)
            {
                FinishActionTask(action, webResponse);
                return;
            }
            followupPrompt = action.Body;
        }
        else
        {
            followupPrompt = "GPT Web 응답을 전달합니다. 원래 작업을 계속 수행해줘." + Environment.NewLine + "작업이 완전히 끝났으면 응답 첫 줄을 [WORKER_DONE]로 시작해줘. 아직 다음 단계가 필요하면 GPT Web에 보낼 다음 요청만 출력해줘." + Environment.NewLine + Environment.NewLine + webResponse;
        }
        _judgeRound = 0;
        _judgeReportOnly = false;
        _pendingJevFailure = null;
        using var cts = new CancellationTokenSource();
        _activeTaskCts = cts;
        _awaitingWebResult = false;
        RunButton.Content = "■  Cancel";
        TaskDirection.Text = "GPT WEB → CODEX";
        TaskTitle.Text = "Web 응답을 Codex에 전달하는 중";
        SetFlowState(true, true, false);

        try
        {
            AddTaskMessage("WORKER -> CODEX", followupPrompt);
            var result = await RunCodexWithJevFooterAsync(followupPrompt, model, reasoning, workingDirectory, _activeSessionId, _activeReadOnly, cts.Token, "WEB_FOLLOWUP");
            if (_userCanceledTask) return;
            _lastActivityAt = DateTimeOffset.UtcNow;
            _activeSessionId = result.SessionId ?? _activeSessionId;
            _lastCodexResult = result;
            AddCliRoundStatus(result);
            CodexThreadArchive.Save(result, followupPrompt, workingDirectory);
            _commandUsage = _commandUsage.Add(result.Usage);
            UpdateUsage(_commandUsage);
            ResultTitle.Text = result.ExitCode == 0 ? $"Codex 중간 결과 · {model}" : $"Codex 후속 처리 실패 · exit {result.ExitCode}";
            ResultBody.Text = BuildRoundtripResultBody(webResponse, result);
            ActivateResultTab(web: false);

            if (result.ExitCode == 0 && (_actionProtocolEnabled || ShouldContinueRoundtrip(result)) && _bridgeServer is not null)
            {
                var nextTask = await RouteCodexResultAsync(result, _activeWebInstruction, includeWebInstruction: false, gitReferenceHeader: null, cancellationToken: cts.Token);
                if (nextTask is not null)
                {
                    _awaitingWebResult = true;
                    RunButton.Content = "■  Cancel";
                    TaskDirection.Text = "WORKER → GPT WEB";
                    TaskTitle.Text = "Codex 결과를 GPT Web에 재전달하는 중";
                    SetFlowState(false, true, true);
                    return;
                }
                if (ResultTitle.Text == "FINISH_PAUSED") return;
            }

            _awaitingWebResult = false;
            TaskDirection.Text = "GPT WEB → CODEX";
            TaskTitle.Text = result.ExitCode == 0 ? "Worker 최종 처리 완료" : "Codex 후속 처리 실패";
            SetFlowState(false, false, false);
        }
        catch (OperationCanceledException)
        {
            if (_jobTimedOut || _userCanceledTask) return;
            TaskDirection.Text = "GPT WEB → CODEX";
            TaskTitle.Text = "Codex 후속 처리 취소";
            ResultTitle.Text = "Codex CANCELED";
            ResultBody.Text = "Web 응답 후속 처리가 취소되었습니다."
 + Environment.NewLine + Environment.NewLine + webResponse;
            SetFlowState(false, false, false);
        }
        catch (Exception ex)
        {
            TaskDirection.Text = "GPT WEB → CODEX";
            TaskTitle.Text = "Codex 후속 처리 실패";
            ResultTitle.Text = "Codex ERROR";
            ResultBody.Text = ex.ToString();
            SetFlowState(false, false, false);
        }
        finally
        {
            _activeTaskCts = null;
            var wasUserCanceled = _userCanceledTask;
            _userCanceledTask = false;
            UpdatePanelLayout(_awaitingWebResult);
            ApplyConnectionStatus();
            if (!_awaitingWebResult || wasUserCanceled) RunButton.Content = "▶  Run Task";
        }
    }

    private static WebAction ParseWebAction(string? response, bool strict = false)
    {
        if (string.IsNullOrWhiteSpace(response))
            return strict
                ? new(WebActionKind.ProtocolError, string.Empty, "ACTION 응답이 비어 있습니다.")
                : new(WebActionKind.None, string.Empty);

        var lines = response.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var nonEmpty = lines.Select((line, index) => (line.Trim(), index)).Where(item => item.Item1.Length > 0).ToList();
        if (nonEmpty.Count == 0)
            return strict
                ? new(WebActionKind.ProtocolError, string.Empty, "ACTION 응답이 비어 있습니다.")
                : new(WebActionKind.None, string.Empty);

        var firstLine = nonEmpty[0].Item1;
        var actionPattern = new System.Text.RegularExpressions.Regex(@"^\[ACTION=(BEGIN|CONTINUE|PAUSE|END)\]$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var match = actionPattern.Match(firstLine);
        if (!match.Success)
            return strict
                ? new(WebActionKind.ProtocolError, string.Empty, "첫 유효행에 유효한 ACTION이 없습니다.")
                : new(WebActionKind.None, string.Empty);

        var kind = match.Groups[1].Value.ToUpperInvariant() switch
        {
            "BEGIN" => WebActionKind.Begin,
            "CONTINUE" => WebActionKind.Continue,
            "PAUSE" => WebActionKind.Pause,
            "END" => WebActionKind.End,
            _ => WebActionKind.ProtocolError
        };
        var body = string.Join(Environment.NewLine, lines.Skip(nonEmpty[0].index + 1)).Trim();
        if ((kind is WebActionKind.Begin or WebActionKind.Continue) && string.IsNullOrWhiteSpace(body))
            return new(WebActionKind.ProtocolError, string.Empty, "BEGIN/CONTINUE 본문이 비어 있습니다.");

        return new(kind, body);
    }
    private string BuildWebPrompt(
        CodexCliResult result,
        string? webInstruction,
        bool includeControlInstructions,
        bool includeWebInstruction)
    {
        var output = string.IsNullOrWhiteSpace(result.FinalMessage) ? result.StandardOutput : result.FinalMessage;
        var control = includeControlInstructions
            ? "반드시 답변 첫 줄을 다음 프로토콜 중에서 선택해줘." + Environment.NewLine
                + "[ACTION=CONTINUE] - 다음 작업을 진행하길 원할 때. 계속 진행해도 문제 없을 때" + Environment.NewLine
                + "[ACTION=PAUSE] - 사용자가 개입해서 테스트해봐야 하는 상황일 때" + Environment.NewLine
                + "[ACTION=END] - 목표에 달성한 상태일 때 혹은 대기 작업이 남아있지 않을 때" + Environment.NewLine
                + Environment.NewLine
            : string.Empty;
        var instruction = includeWebInstruction && !string.IsNullOrWhiteSpace(webInstruction)
            ? Environment.NewLine + Environment.NewLine + webInstruction
            : string.Empty;
        var jevGuidance = includeControlInstructions && _targetSettings.EffectiveJudge.Enabled
            ? Environment.NewLine + Environment.NewLine
                + "JEV 검증 지침: 구현·설계·파일·테스트 결과처럼 의미 있는 검증이 가능한 상태라면 다음 Codex 작업에서 JEV 검증을 우선 요청하도록 안내하세요."
                + Environment.NewLine
                + "Worker는 Codex의 [NEXT : JEV] 및 [VALIDATION REQUEST]를 감지해 JEV로 전달합니다. 검증할 항목이 없을 때만 [NEXT : WEB] 보고를 사용하세요."
            : string.Empty;
        return control + output + instruction + jevGuidance;
    }
    private bool ShouldContinueRoundtrip(CodexCliResult result)
    {
        var text = string.IsNullOrWhiteSpace(result.FinalMessage) ? result.StandardOutput : result.FinalMessage;
        if (text.Contains("[WORKER_DONE]", StringComparison.OrdinalIgnoreCase)) return false;
        var match = System.Text.RegularExpressions.Regex.Match(text, @"(?<!\d)(\d+)\s*/\s*(\d+)(?!\d)");
        return match.Success && int.TryParse(match.Groups[1].Value, out var current) && int.TryParse(match.Groups[2].Value, out var total) && current < total;
    }

    private string BuildNextWebPrompt(CodexCliResult result)
    {
        var original = string.IsNullOrWhiteSpace(_activePrompt) ? "원래 작업" : _activePrompt;
        var codex = string.IsNullOrWhiteSpace(result.FinalMessage) ? result.StandardOutput : result.FinalMessage;
        return original + Environment.NewLine + Environment.NewLine + "Codex 최신 결과를 반영해 다음 단계 작업을 계속 수행해줘." + Environment.NewLine + Environment.NewLine + "Codex 실행 결과:" + Environment.NewLine + codex;
    }

    private void StartTaskTranscript(CodexThreadOption? selectedThread, string command, string webInstruction)
    {
        _taskMessages.Clear();
        _messageLogItems.Clear();
        MessageLogEmptyText.Visibility = Visibility.Visible;
        _taskExported = false;
        _taskStartedAt = DateTimeOffset.Now;
        _taskProjectName = string.IsNullOrWhiteSpace(selectedThread?.ProjectPath)
            ? "UnknownProject"
            : new DirectoryInfo(selectedThread.ProjectPath).Name;
        _taskThreadName = string.IsNullOrWhiteSpace(selectedThread?.SessionId)
            ? "NewThread"
            : selectedThread.Label;
        AddTaskMessage("USER COMMAND", command);
        AddTaskMessage("GPT WEB INSTRUCTION", webInstruction);
    }

    private string BuildTaskStartInfo(string model, string reasoning, string workingDirectory, string? sessionId)
    {
        var executable = _codexRunner.FindExecutable() ?? "찾을 수 없음";
        var session = string.IsNullOrWhiteSpace(sessionId) ? "신규 스레드" : sessionId;
        return $"Model: {model}{Environment.NewLine}" +
               $"Reasoning: {reasoning}{Environment.NewLine}" +
               $"Executable: {executable}{Environment.NewLine}" +
               $"Working directory: {workingDirectory}{Environment.NewLine}" +
               $"Session ID: {session}";
    }

    private void AddCliRoundStatus(CodexCliResult result)
    {
        var outcome = result.ExitCode == 0 ? "PASS" : "FAIL";
        var session = string.IsNullOrWhiteSpace(result.SessionId) ? "없음" : result.SessionId;
        AddTaskMessage("CLI STATUS", $"{outcome} · exit {result.ExitCode} · model {result.Model} · session {session}");
    }

    private void AddTaskMessage(string source, string? content)
    {
        if (string.IsNullOrWhiteSpace(content)) return;
        var timestamp = DateTimeOffset.Now;
        var trimmed = content.Trim();
        _taskMessages.Add(new TaskMessage(timestamp, source, trimmed));
        _messageLogItems.Add($"[{timestamp:HH:mm:ss}] {source}{Environment.NewLine}{trimmed}");
        MessageLogEmptyText.Visibility = Visibility.Collapsed;
        RefreshMessageLog();
    }

    private void RefreshMessageLog()
    {
        if (MessageLogList is null) return;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (MessageLogList.Items.Count > 0)
                MessageLogList.ScrollIntoView(MessageLogList.Items[MessageLogList.Items.Count - 1]);
        }), DispatcherPriority.Background);
    }
    private string? ExportTaskTranscript()
    {
        if (_taskExported || _taskMessages.Count == 0) return null;
        try
        {
            var folderName = $"{SanitizeFilePart(_taskProjectName)}_{SanitizeFilePart(_taskThreadName)}";
            var directory = Path.Combine(WorkerPaths.Task, folderName);
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, $"_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
            var lines = new List<string>
            {
                $"Project: {_taskProjectName}",
                $"Thread: {_taskThreadName}",
                $"Started: {_taskStartedAt:O}",
                $"Finished: {DateTimeOffset.Now:O}",
                string.Empty
            };
            foreach (var message in _taskMessages)
            {
                lines.Add($"[{message.Timestamp:yyyy-MM-dd HH:mm:ss}] {message.Source}");
                lines.Add(message.Content);
                lines.Add(string.Empty);
            }
            File.WriteAllText(path, string.Join(Environment.NewLine, lines), new UTF8Encoding(false));
            _taskExported = true;
            return path;
        }
        catch
        {
            return null;
        }
    }

    private static string SanitizeFilePart(string value)
    {
        var invalid = new string(Path.GetInvalidFileNameChars());
        var sanitized = new string(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "Unnamed" : sanitized;
    }

    private static List<BridgeAttachment> BuildWebAttachments(BridgeServer bridge, IReadOnlyList<CodexCliFile> files)
    {
        return files
            .Select(file =>
            {
                try { return bridge.CreateFileAttachment(file); }
                catch { return null; }
            })
            .Where(file => file is not null)
            .Select(file => file!)
            .ToList();
    }

    private string BuildRoundtripResultBody(string webResponse, CodexCliResult result)
    {
        return "GPT Web 응답:" + Environment.NewLine + Summarize(webResponse, "응답 내용이 없습니다.") + Environment.NewLine + Environment.NewLine + "Codex 후속 결과:" + Environment.NewLine + BuildResultBody(result);
    }

    private string BuildResultBody(CodexCliResult result)
    {
        var message = string.IsNullOrWhiteSpace(result.FinalMessage) ? result.StandardOutput : result.FinalMessage;
        var detail = Summarize(message, "Codex가 결과를 반환하지 않았습니다.");
        var stderr = Summarize(result.StandardError, "없음");
        var session = string.IsNullOrWhiteSpace(result.SessionId) ? "없음(신규 스레드 생성 전/실패)" : result.SessionId;
        var workingDirectory = _activeWorkingDirectory ?? "확인되지 않음";
        return $"Model: {result.Model} · Reasoning: {result.Reasoning}{Environment.NewLine}" +
               $"Exit code: {result.ExitCode}{Environment.NewLine}" +
               $"Executable: {result.ExecutablePath}{Environment.NewLine}" +
               $"Working directory: {workingDirectory}{Environment.NewLine}" +
               $"Session ID: {session}{Environment.NewLine}" +
               $"CLI stderr: {stderr}{Environment.NewLine}{Environment.NewLine}{detail}";
    }
    private static bool IsExplicitReadOnlyRequest(string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt)) return false;
        return Regex.IsMatch(prompt, @"읽기\s*전용|파일(?:을|은|도)?\s*(?:만들|생성|수정|변경)지?\s*말|(?:파일|폴더).{0,20}(?:만들지|생성하지|수정하지|변경하지)\s*말|read[- ]only|(?:do not|without)\s+(?:create|modify|write)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static string Summarize(string? value, string fallback)
    {
        var text = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        return text.Length <= 4000 ? text : text[..4000] + Environment.NewLine + "…";
    }

    private void ActivateResultTab(bool web)
    {
        var active = FindResource("ActiveMessageTab") as System.Windows.Media.Brush;
        var inactive = FindResource("InactiveMessageTab") as System.Windows.Media.Brush;
        CodexTab.Background = web ? inactive : active;
        WebTab.Background = web ? active : inactive;
        CodexTab.FontWeight = web ? FontWeights.Normal : FontWeights.Bold;
        WebTab.FontWeight = web ? FontWeights.Bold : FontWeights.Normal;
    }
    private void UpdateUsage(CodexUsage usage)
    {
        UsageText.Text = $"5시간/주간 제한: CLI 미제공 · 이번 작업 누적: {usage.TotalTokens:N0} 토큰";
    }

    private void CodexTab_Click(object sender, RoutedEventArgs e)
    {
        ResultTitle.Text = "MESSAGE LOG · Codex";
        ActivateResultTab(web: false);
        RefreshMessageLog();
    }

    private void WebTab_Click(object sender, RoutedEventArgs e)
    {
        ResultTitle.Text = "MESSAGE LOG · GPT Web";
        ActivateResultTab(web: true);
        RefreshMessageLog();
    }
}
