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
    private CodexModelCatalogResult _codexModelCatalog = new(Array.Empty<CodexModelCapability>(), "MODEL_CATALOG_NOT_LOADED");
    private bool _loadingRoleControls;
    private bool _syncingRoleThreadSelection;
    private bool _activeCoordinatorFirst;
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
    private sealed record RoleVisualPalette(string Background, string IconBackground, string Foreground, string IconAsset);
    private static readonly IReadOnlyDictionary<string, RoleVisualPalette> RoleVisuals = new Dictionary<string, RoleVisualPalette>(StringComparer.Ordinal)
    {
        ["Coordinator"] = new("#DDEEFF", "#1477E8", "#1267D5", "current-openai.png"),
        ["Implementer"] = new("#DCF5E3", "#168A4A", "#116B39", "current-openai.png"),
        ["HighLevel"] = new("#ECD8E4", "#82194B", "#74133F", "current-openai.png"),
        ["Judge"] = new("#FFF0B8", "#B87900", "#765000", "current-jev.png")
    };
    private readonly List<TaskMessage> _taskMessages = new();
    private readonly ObservableCollection<string> _messageLogItems = new();
    public ObservableCollection<string> MessageLogItems => _messageLogItems;
    public sealed record WorkerHistoryEvent(DateTimeOffset Timestamp, string StageKey, string EventType, string Title, string? Summary, long? SizeBytes, int? ItemCount, int? FileCount, string? Status, string? ReferenceId)
    {
        public string? IconAssetOverride { get; init; }
        public string Role => StageKey switch { "Coordinator" => "설계 관제", "Implementer" => "작업", "HighLevel" => "고수준 작업", "Judge" => "판정", _ => "시스템" };
        public string TimestampText => Timestamp.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss");
        public string Details
        {
            get
            {
                var parts = new List<string>();
                if (Status is not null) parts.Add(Status);
                if (FileCount.HasValue) parts.Add($"{FileCount.Value}개 파일");
                if (ItemCount.HasValue) parts.Add($"{ItemCount.Value}건");
                if (SizeBytes.HasValue) parts.Add(FormatHistorySize(SizeBytes.Value));
                return parts.Count == 0 ? EventType : string.Join(" · ", parts);
            }
        }
        public System.Windows.Media.Brush RowBackground => GetRoleBrush(StageKey, p => p.Background, System.Windows.Media.Color.FromRgb(238, 241, 245));
        public System.Windows.Media.Brush IconBackground => GetRoleBrush(StageKey, p => p.IconBackground, System.Windows.Media.Color.FromRgb(126, 139, 155));
        public System.Windows.Media.Brush RoleForeground => GetRoleBrush(StageKey, p => p.Foreground, System.Windows.Media.Color.FromRgb(112, 128, 144));
        public string IconAssetName => IconAssetOverride ?? (RoleVisuals.TryGetValue(StageKey, out var palette) ? palette.IconAsset : "current-console.png");
        public System.Windows.Media.ImageSource IconSource => new System.Windows.Media.Imaging.BitmapImage(new Uri(
            "pack://application:,,,/ProjectHub.Worker;component/Assets/" + IconAssetName));
        private static System.Windows.Media.Brush GetRoleBrush(string stageKey, Func<RoleVisualPalette, string> selector, System.Windows.Media.Color fallback)
            => new System.Windows.Media.SolidColorBrush(RoleVisuals.TryGetValue(stageKey, out var palette)
                ? (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(selector(palette))
                : fallback);
        private static string FormatHistorySize(long bytes) => bytes >= 1024 * 1024 ? $"{bytes / 1024d / 1024d:0.0} MB" : bytes >= 1024 ? $"{bytes / 1024d:0.0} KB" : $"{bytes} B";
    }
    private readonly ObservableCollection<WorkerHistoryEvent> _historyEvents = new();
    public ObservableCollection<WorkerHistoryEvent> HistoryEvents => _historyEvents;
    private DateTimeOffset _taskStartedAt;
    private string _taskProjectName = "UnknownProject";
    private string _taskThreadName = "NewThread";
    private bool _taskExported;
    private readonly DispatcherTimer _flowTimer = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private readonly DispatcherTimer _connectionTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly DispatcherTimer _jobWatchdogTimer = new() { Interval = TimeSpan.FromSeconds(10) };
    private int _flowFrame;
    private bool _pairArrowActive;
    private enum TaskStage { Idle, Coordinator, Implementer, HighLevel, Judge }
    private enum DashboardBodyMode { NewTaskInput, TaskHistory }
    private DashboardBodyMode _dashboardBodyMode = DashboardBodyMode.NewTaskInput;
    private TaskStage _currentTaskStage = TaskStage.Idle;
    private TaskStage? _nextTaskStage;
    private string _coordinatorStageIconAsset = "current-openai.png";
    private bool _judgeReviewing;
    private string _judgeStatus = "OFF";
    private int _judgeRound;
    private string _activeJevJobId = Guid.NewGuid().ToString("N");
    private bool _allowClose;
    private const string Placeholder = "CLI에 즉시 전달할 작업 지시...";
    private const string WebInstructionPlaceholder = "CLI 답변 뒤에 붙여 GPT Web에 전달할 지침...";
    private const string DashboardPromptPlaceholder = "작업 내용을 입력하세요...";
    private readonly HttpClient _connectionClient = new() { Timeout = TimeSpan.FromSeconds(2) };
    private BridgeServer? _bridgeServer;
    private bool _codexAuthenticated;
    private bool _serverOnline;
    private bool _messageExpanded;
    private bool _startupConfigurationInitialized;
    private Task? _startupConfigurationTask;
    private UIElement? _settingsPopupDragSurface;
    private System.Windows.Point _settingsPopupDragStartScreen;
    private double _settingsPopupDragStartHorizontalOffset;
    private double _settingsPopupDragStartVerticalOffset;
    private string _serverBaseUrl = WorkerTargetConfiguration.DefaultServerBaseUrl;
    private string _serverBaseUrlSource = "DEFAULT";
    private WorkerTargetSettings _targetSettings = new(null, null, null, null);
    private GitTargetSnapshot? _gitTarget;
    private List<CodexProjectOption> _codexProjects = new();
    private bool _loadingCodexSelections;
    private static string WindowPlacementPath => Path.Combine(WorkerPaths.Config, "window-placement.json");

    private enum FlowNode { Codex, Worker, Web, Judge }
    private enum WebActionKind { None, Begin, Continue, Pause, End, Hq, ProtocolError }
    private sealed record WebAction(WebActionKind Kind, string Body, string? Error = null);
    private sealed record CodexProjectOption(string Name, string Path);
    private sealed record TaskLaunchRequest(string Prompt, string? WebInstruction, string WorkingDirectory, string? SessionId);
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
        if (StatusPopup.IsOpen)
        {
            e.Cancel = true;
            SetSettingsPopupOpen(false);
            return;
        }

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

    private async void Settings_Click(object sender, RoutedEventArgs e)
    {
        if (!StatusPopup.IsOpen)
        {
            await InitializeStartupConfigurationAsync();
            ApplyRoleSettingsToControls();
        }
        SetSettingsPopupOpen(!StatusPopup.IsOpen);
    }

    private void CloseSettings_Click(object sender, RoutedEventArgs e)
    {
        ApplyTargetConfiguration();
        SetSettingsPopupOpen(false);
    }

    private void SetSettingsPopupOpen(bool open)
    {
        SettingsDimOverlay.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        StatusPopup.IsOpen = open;
        if (open)
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (!StatusPopup.IsOpen) return;
                CoordinatorWebTabButton.Focus();
                Keyboard.Focus(CoordinatorWebTabButton);
            }, DispatcherPriority.Input);
        }
        else if (IsVisible)
        {
            Dispatcher.BeginInvoke(() => SettingsButton.Focus(), DispatcherPriority.Input);
        }
    }

    private void MainWindow_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (!StatusPopup.IsOpen) return;
        e.Handled = true;
    }

    private void SettingsPopup_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        e.Handled = true;
        SetSettingsPopupOpen(false);
    }

    private void SettingsPopupHeader_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!StatusPopup.IsOpen || sender is not UIElement surface) return;
        _settingsPopupDragSurface = surface;
        _settingsPopupDragStartScreen = surface.PointToScreen(e.GetPosition(surface));
        _settingsPopupDragStartHorizontalOffset = StatusPopup.HorizontalOffset;
        _settingsPopupDragStartVerticalOffset = StatusPopup.VerticalOffset;
        surface.CaptureMouse();
        e.Handled = true;
    }

    private void SettingsPopupHeader_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_settingsPopupDragSurface is not { IsMouseCaptured: true } surface || e.LeftButton != MouseButtonState.Pressed) return;
        var currentScreenPoint = surface.PointToScreen(e.GetPosition(surface));
        StatusPopup.HorizontalOffset = _settingsPopupDragStartHorizontalOffset + currentScreenPoint.X - _settingsPopupDragStartScreen.X;
        StatusPopup.VerticalOffset = _settingsPopupDragStartVerticalOffset + currentScreenPoint.Y - _settingsPopupDragStartScreen.Y;
    }

    private void SettingsPopupHeader_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_settingsPopupDragSurface is not { } surface) return;
        surface.ReleaseMouseCapture();
        _settingsPopupDragSurface = null;
        e.Handled = true;
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

    private void DashboardTaskInput_GotFocus(object sender, RoutedEventArgs e)
        => SetInputFocusState(DashboardTaskInput, DashboardPromptPlaceholder, focused: true);

    private void DashboardTaskInput_LostFocus(object sender, RoutedEventArgs e)
        => SetInputFocusState(DashboardTaskInput, DashboardPromptPlaceholder, focused: false);

    private void DashboardTaskInput_TextChanged(object sender, TextChangedEventArgs e)
        => UpdateDashboardRunButtonState();

    private void UpdateDashboardRunButtonState()
    {
        if (RunButton is null || DashboardTaskInput is null) return;
        var active = _activeTaskCts is not null || _awaitingWebResult;
        var preflightError = GetDashboardPreflightError();
        var executionReady = preflightError is null;
        var hasPrompt = !string.IsNullOrWhiteSpace(DashboardTaskInput.Text) && DashboardTaskInput.Text != DashboardPromptPlaceholder;
        HighLevelPermitCheckBox.Visibility = _targetSettings.IsCoordinatorFirst && _dashboardBodyMode == DashboardBodyMode.NewTaskInput ? Visibility.Visible : Visibility.Collapsed;
        HighLevelPermitCheckBox.IsEnabled = !active && _dashboardBodyMode == DashboardBodyMode.NewTaskInput && executionReady;
        if (active)
        {
            RunButton.Content = "■   취소";
            RunButton.IsEnabled = !(_userCanceledTask && _activeTaskCts is not null);
            DashboardPreflightText.Text = string.Empty;
            return;
        }

        if (_dashboardBodyMode == DashboardBodyMode.TaskHistory)
        {
            RunButton.Content = "＋   새 작업";
            RunButton.IsEnabled = true;
            DashboardPreflightText.Text = string.Empty;
            return;
        }

        RunButton.Content = "▶   실행";
        RunButton.IsEnabled = executionReady && hasPrompt;
        RunButton.Background = RunButton.IsEnabled ? new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#1477E8")) : new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#B8C8DA"));
        RunButton.BorderBrush = RunButton.Background;
        RunButton.Opacity = RunButton.IsEnabled ? 1 : 0.85;
        RunButton.Effect = RunButton.IsEnabled ? new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 16, ShadowDepth = 4, Direction = 270, Opacity = 0.22, Color = System.Windows.Media.Color.FromRgb(20, 119, 232) } : null;
        DashboardPreflightText.Text = preflightError ?? (hasPrompt ? string.Empty : "작업 내용을 입력하세요.");
        DashboardPreflightText.Foreground = preflightError is null ? (System.Windows.Media.Brush)FindResource("Muted") : System.Windows.Media.Brushes.Firebrick;
    }

    private void SetDashboardBodyMode(DashboardBodyMode mode)
    {
        _dashboardBodyMode = mode;
        DashboardInputView.Visibility = mode == DashboardBodyMode.NewTaskInput ? Visibility.Visible : Visibility.Collapsed;
        DashboardHistoryView.Visibility = mode == DashboardBodyMode.TaskHistory ? Visibility.Visible : Visibility.Collapsed;
        UpdatePipelineVisuals();
        UpdateDashboardRunButtonState();
    }

    private string? GetDashboardPreflightError()
    {
        if (_targetSettings.IsCoordinatorFirst)
        {
            return GetCoordinatorFirstPreflightError(
                ResolveWorkingDirectory(CodexThreadCombo.SelectedItem as CodexThreadOption),
                _targetSettings.EffectiveCoordinator,
                _targetSettings.EffectiveImplementer);
        }

        if (!_codexAuthenticated) return "Codex 로그인이 필요합니다.";
        if (_bridgeServer?.WebConnected != true) return "GPT Web 연결을 기다리고 있습니다.";
        if (!_bridgeServer.WebExtensionSynchronized) return "GPT Web 확장 동기화를 기다리고 있습니다.";
        if (!_bridgeServer.WebConversationBound) return "GPT Web 대화를 먼저 연결하세요.";
        return null;
    }

    private void BeginNewDashboardTask()
    {
        _historyEvents.Clear();
        DashboardTaskInput.Text = DashboardPromptPlaceholder;
        DashboardTaskInput.Foreground = FindResource("Muted") as System.Windows.Media.Brush;
        SetDashboardBodyMode(DashboardBodyMode.NewTaskInput);
        DashboardTaskInput.Focus();
    }

    private TaskLaunchRequest? BuildTaskLaunchRequest()
    {
        var prompt = DashboardTaskInput.Text?.Trim();
        if (string.IsNullOrWhiteSpace(prompt) || prompt == DashboardPromptPlaceholder) return null;
        var selectedThread = CodexThreadCombo.SelectedItem as CodexThreadOption;
        var workingDirectory = ResolveWorkingDirectory(selectedThread);
        if (string.IsNullOrWhiteSpace(workingDirectory)) return null;
        var sessionId = string.IsNullOrWhiteSpace(selectedThread?.SessionId) ? null : selectedThread.SessionId;
        return new TaskLaunchRequest(prompt, null, workingDirectory, sessionId);
    }

    private void ResetDashboardTaskInput()
    {
        if (DashboardTaskInput.IsKeyboardFocusWithin)
        {
            DashboardTaskInput.Text = string.Empty;
            DashboardTaskInput.Foreground = FindResource("Ink") as System.Windows.Media.Brush;
            return;
        }
        DashboardTaskInput.Text = DashboardPromptPlaceholder;
        DashboardTaskInput.Foreground = FindResource("Muted") as System.Windows.Media.Brush;
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
        ResetDashboardTaskInput();
        CommandInput.Text = Placeholder;
        CommandInput.Foreground = FindResource("Muted") as System.Windows.Media.Brush;
        WebInstructionInput.Text = WebInstructionPlaceholder;
        WebInstructionInput.Foreground = FindResource("Muted") as System.Windows.Media.Brush;
    }
    private void SetFlowState(bool codexActive, bool workerActive, bool webActive, TaskStage? explicitStage = null, TaskStage? explicitNextStage = null)
    {
        var (left, right) = ResolveFlowPair();
        SetFlowNode(left, isActive: IsNodeActive(left, codexActive, workerActive, webActive), isLeft: true);
        SetFlowNode(right, isActive: IsNodeActive(right, codexActive, workerActive, webActive), isLeft: false);
        _pairArrowActive = codexActive || workerActive || webActive;
        _flowFrame = 0;
        UpdateArrowAnimation();
        var running = codexActive || workerActive || webActive || (!_userCanceledTask && (_activeTaskCts is not null || _awaitingWebResult));
        _currentTaskStage = !running ? TaskStage.Idle
            : explicitStage ?? (_judgeReviewing ? TaskStage.Judge
            : _activeCoordinatorFirst && codexActive ? TaskStage.Coordinator
            : webActive ? TaskStage.Coordinator
            : workerActive || codexActive ? TaskStage.Implementer
            : TaskStage.Idle);
        _nextTaskStage = explicitStage.HasValue ? explicitNextStage : explicitNextStage ?? (_currentTaskStage switch
        {
            TaskStage.Coordinator => TaskStage.Implementer,
            TaskStage.Implementer when _targetSettings.EffectiveJudge.Enabled => TaskStage.Judge,
            TaskStage.HighLevel when _targetSettings.EffectiveJudge.Enabled => TaskStage.Judge,
            _ => null
        });
        UpdatePipelineVisuals();
        if (!running) _messageExpanded = false;
        UpdatePanelLayout(running);
        UpdateDashboardRunButtonState();
    }

    private void UpdatePipelineVisuals()
    {
        var idle = _currentTaskStage == TaskStage.Idle;
        var initialInputIdle = idle && _dashboardBodyMode == DashboardBodyMode.NewTaskInput && _activeTaskCts is null && !_awaitingWebResult;
        SetPipelineCard(PipelineCoordinatorCard, PipelineCoordinatorTitle, CoordinatorStageCircle, CoordinatorStageIcon, _coordinatorStageIconAsset, TaskStage.Coordinator, RoleVisuals["Coordinator"], false, initialInputIdle);
        SetPipelineCard(PipelineImplementerCard, PipelineImplementerTitle, ImplementerStageCircle, ImplementerStageIcon, RoleVisuals["Implementer"].IconAsset, TaskStage.Implementer, RoleVisuals["Implementer"], false, initialInputIdle);
        SetPipelineCard(PipelineHighLevelCard, PipelineHighLevelTitle, HighLevelStageCircle, HighLevelStageIcon, RoleVisuals["HighLevel"].IconAsset, TaskStage.HighLevel, RoleVisuals["HighLevel"], false, initialInputIdle);
        SetPipelineCard(PipelineJudgeCard, PipelineJudgeTitle, JudgeStageCircle, JudgeStageIcon, RoleVisuals["Judge"].IconAsset, TaskStage.Judge, RoleVisuals["Judge"], !_targetSettings.EffectiveJudge.Enabled, initialInputIdle);

        var idleVisual = PipelineIdleCardVisualPolicy.Resolve(idle);
        SetColor(PipelineIdleCard, idleVisual.Background);
        PipelineIdleTitle.Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(idleVisual.Foreground));
        SetColor(PipelineIdleIconCircle, idleVisual.IconBackground);
        PipelineIdleCard.BorderBrush = idleVisual.Border == "Transparent"
            ? System.Windows.Media.Brushes.Transparent
            : new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(idleVisual.Border));
        PipelineIdleCard.BorderThickness = idle ? new Thickness(2) : new Thickness(1);
        PipelineIdleCard.Effect = idle ? CreateCurrentStageShadow() : null;
        UpdatePipelineArrowAnimation();
    }

    private void SetPipelineCard(Border card, TextBlock title, Border iconCircle, System.Windows.Controls.Image icon, string iconAsset, TaskStage stage, RoleVisualPalette palette, bool disabled, bool initialInputIdle)
    {
        var current = !disabled && _currentTaskStage == stage;
        var visual = PipelineCardVisualPolicy.Resolve(initialInputIdle, current, disabled);
        var colored = visual.IsColored;
        SetColor(card, colored ? palette.Background : "#B8C8DA");
        SetColor(iconCircle, colored ? palette.IconBackground : "#526477");
        title.Foreground = colored ? new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(palette.Foreground)) : System.Windows.Media.Brushes.White;
        var selectedName = colored ? iconAsset : iconAsset.Replace(".png", "-gray.png", StringComparison.OrdinalIgnoreCase);
        icon.Source = new System.Windows.Media.Imaging.BitmapImage(new Uri($"pack://application:,,,/ProjectHub.Worker;component/Assets/{selectedName}"));
        if (card == PipelineCoordinatorCard) CoordinatorStageModelText.Foreground = colored ? new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(palette.Foreground)) : System.Windows.Media.Brushes.White;
        else if (card == PipelineImplementerCard) ImplementerStageModelText.Foreground = colored ? new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(palette.Foreground)) : System.Windows.Media.Brushes.White;
        else if (card == PipelineHighLevelCard) HighLevelStageModelText.Foreground = colored ? new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(palette.Foreground)) : System.Windows.Media.Brushes.White;
        else if (card == PipelineJudgeCard) JudgeStageModelText.Foreground = colored ? new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(palette.Foreground)) : System.Windows.Media.Brushes.White;
        card.BorderBrush = current ? new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(palette.Foreground)) : System.Windows.Media.Brushes.Transparent;
        card.BorderThickness = current ? new Thickness(2) : new Thickness(1);
        card.Effect = current ? CreateCurrentStageShadow() : null;
        card.Opacity = visual.Opacity;
    }

    private static void SetColor(Border control, string color)
        => control.Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color));

    private static System.Windows.Media.Effects.Effect CreateCurrentStageShadow() => new System.Windows.Media.Effects.DropShadowEffect
    {
        BlurRadius = 14,
        ShadowDepth = 3,
        Direction = 270,
        Opacity = 0.22,
        Color = System.Windows.Media.Color.FromRgb(20, 119, 232)
    };

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
        var (defaultName, brush) = node switch
        {
            FlowNode.Codex => ("CODEX", System.Windows.Media.Brushes.MidnightBlue),
            FlowNode.Worker => ("WORKER", System.Windows.Media.Brushes.SeaGreen),
            FlowNode.Web => ("GPT WEB", System.Windows.Media.Brushes.RoyalBlue),
            _ => ("JUDGE", System.Windows.Media.Brushes.DarkViolet)
        };
        var name = _activeCoordinatorFirst && node == FlowNode.Codex ? "SOL · 관제" :
            _activeCoordinatorFirst && node == FlowNode.Worker ? "LUNA · 작업" : defaultName;
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
        UpdatePipelineArrowAnimation();
        UpdateJudgeVisual();
    }

    private void UpdatePipelineArrowAnimation()
    {
        var arrows = new[] { PipelineArrow1, PipelineArrow2, PipelineArrow3, PipelineArrow4 };
        var labels = new[] { PipelineArrowText1, PipelineArrowText2, PipelineArrowText3, PipelineArrowText4 };
        var start = (int)_currentTaskStage;
        var end = _nextTaskStage.HasValue ? (int)_nextTaskStage.Value : -1;
        if (!_pairArrowActive || start < 1 || end < 1 || end == start)
        {
            for (var i = 0; i < arrows.Length; i++)
            {
                arrows[i].Opacity = 0.72;
                arrows[i].RenderTransform = new System.Windows.Media.TranslateTransform();
                arrows[i].Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#F1F5FA"));
                labels[i].Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#B8C9DD"));
                labels[i].Text = "››";
            }
            return;
        }

        var routeStart = Math.Min(start, end);
        var routeEnd = Math.Max(start, end);
        var routeLength = routeEnd - routeStart;
        var pulseEdge = routeStart + ((_flowFrame / 2) % routeLength);
        var direction = end > start ? 1 : -1;
        var pulse = (Math.Sin((_flowFrame % 8) * Math.PI / 4) + 1) / 2;
        for (var edge = 1; edge <= arrows.Length; edge++)
        {
            var onRoute = edge >= routeStart && edge < routeEnd;
            var active = onRoute && edge == pulseEdge;
            arrows[edge - 1].Opacity = onRoute ? active ? 0.68 + pulse * 0.32 : 0.82 : 0.55;
            arrows[edge - 1].RenderTransform = new System.Windows.Media.TranslateTransform(active ? direction * pulse * 5 : 0, 0);
            arrows[edge - 1].Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(onRoute ? active ? "#D8EBFF" : "#E6F2FF" : "#F1F5FA"));
            labels[edge - 1].Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(onRoute ? "#1477E8" : "#B8C9DD"));
            labels[edge - 1].Text = onRoute ? direction > 0 ? "›››" : "‹‹‹" : "››";
        }
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
        var phase = active ? frame % arrows.Length : -1;
        for (var index = 0; index < arrows.Length; index++)
        {
            var moving = phase == index;
            arrows[index].Opacity = phase < 0 ? 0.3 : moving ? opacities[0] : opacities[1];
            arrows[index].RenderTransform = new System.Windows.Media.TranslateTransform(moving ? 5 : 0, 0);
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
        if (_dashboardBodyMode == DashboardBodyMode.TaskHistory)
        {
            BeginNewDashboardTask();
            return;
        }
        var highLevelAuthorizedAtLaunch = _targetSettings.IsCoordinatorFirst && HighLevelPermitCheckBox.IsChecked == true;
        HighLevelPermitCheckBox.IsChecked = false;
        await InitializeStartupConfigurationAsync();
        var launchRequest = BuildTaskLaunchRequest();
        if (launchRequest is null) return;
        var selectedThreadForLaunch = CodexThreadCombo.SelectedItem as CodexThreadOption;
        if (_targetSettings.IsCoordinatorFirst)
        {
            var cliWorkingDirectory = launchRequest.WorkingDirectory;
            var coordinator = _targetSettings.EffectiveCoordinator;
            var implementer = _targetSettings.EffectiveImplementer;
            var roleError = GetCoordinatorFirstPreflightError(cliWorkingDirectory, coordinator, implementer);
            if (roleError is not null)
            {
                TaskDirection.Text = "PREFLIGHT";
                TaskTitle.Text = "AI 역할 설정을 확인하세요";
                ResultTitle.Text = "CLI-TO-CLI BLOCKED";
                ResultBody.Text = roleError;
                AiRolesStatusText.Text = roleError;
                SetFlowState(false, false, false);
                return;
            }
            _historyEvents.Clear();
            SetDashboardBodyMode(DashboardBodyMode.TaskHistory);
            await RunCoordinatorFirstJobAsync(launchRequest.Prompt, selectedThreadForLaunch, cliWorkingDirectory, coordinator, implementer, highLevelAuthorizedAtLaunch);
            return;
        }
        if (!_codexAuthenticated || _bridgeServer is null || !_bridgeServer.WebConnected || !_bridgeServer.WebExtensionSynchronized || !_bridgeServer.WebConversationBound)
        {
            TaskDirection.Text = "PREFLIGHT";
            TaskTitle.Text = _bridgeServer?.WebConversationBound == false ? "GPT Web 대화 연결 필요" : "연결 상태 확인 필요";
            SetFlowState(false, false, false);
            return;
        }

        var prompt = launchRequest.Prompt;
        var webInstruction = launchRequest.WebInstruction ?? string.Empty;
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
        var selectedThread = selectedThreadForLaunch;
        _historyEvents.Clear();
        SetDashboardBodyMode(DashboardBodyMode.TaskHistory);
        StartTaskTranscript(selectedThread, cliPrompt, webInstruction);
        var workingDirectory = launchRequest.WorkingDirectory;
        var sessionId = launchRequest.SessionId;
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
        AddTaskMessage("TASK REQUEST", cliPrompt, sizeBytes: Encoding.UTF8.GetByteCount(cliPrompt), itemCount: 1);
        _judgeRound = 0;
        _activeJevJobId = Guid.NewGuid().ToString("N");
        _judgeStatus = _targetSettings.EffectiveJudge.Enabled ? "READY" : "OFF";
        _webFollowupStarted = false;
        _commandUsage = CodexUsage.Empty;
        UpdateUsage(_commandUsage);
        var cts = new CancellationTokenSource();
        _activeTaskCts = cts;
        ResetDashboardTaskInput();
        UpdateDashboardRunButtonState();
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
                    RunButton.Content = "■   취소";
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
            _userCanceledTask = false;
            UpdatePanelLayout(_awaitingWebResult);
            ApplyConnectionStatus();
            UpdateDashboardRunButtonState();
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
        var report = output;
        var directive = _targetSettings.EffectiveJudge.Enabled ? JevContract.ParseNext(output) : new NextDirective(NextRoute.Web, output);
        var protocolError = _targetSettings.EffectiveJudge.Enabled ? JevContract.ValidateStructure(directive, false) : null;
        if (protocolError is not null)
        {
            AddTaskMessage("JEV ROUTE ERROR", protocolError, status: "UNKNOWN");
            report = $"[JEV ROUTE ERROR]\nCODE: {protocolError}\n\n{output}";
        }
        else if (_targetSettings.EffectiveJudge.Enabled && directive.Route == NextRoute.Jev)
        {
            var validation = JevContract.ExtractValidationRequest(directive.Body);
            if (!JevContract.TryParseValidation(validation, out _, out var validationError))
            {
                report = $"[JEV REQUEST ERROR]\nCODE: {validationError}\n\n{output}";
            }
            else
            {
                var request = new JudgeRequest(_activePrompt ?? "Current task", 1, _activeWorkingDirectory ?? AppContext.BaseDirectory, output, validation, result.Files, "GIT", _gitTarget?.HeadSha, _activeJevJobId);
                _judgeReviewing = true;
                _judgeStatus = "REVIEWING";
                TaskDirection.Text = "WORKER → JEV";
                TaskTitle.Text = "JEV 응답을 같은 Codex 세션으로 전달 중";
                SetFlowState(false, true, false, explicitStage: TaskStage.Judge);
                AddTaskMessage("JEV REQUEST", validation);
                JudgeTransportResult judgment;
                try { judgment = await _jevJudgeRunner.ReviewRawAsync(request, _targetSettings.EffectiveJudge, cancellationToken); }
                finally { _judgeReviewing = false; }
                RecordJevTransportTelemetry(_activeJevJobId ?? Guid.NewGuid().ToString("N"), judgment.Telemetry, "LEGACY_JEV");
                report = judgment.ErrorCode is null && judgment.RawResponse is not null
                    ? $"[NEXT: WEB]\n[JEV RAW RESPONSE — SAME CODEX SESSION]\n{judgment.RawResponse}"
                    : $"[NEXT: WEB]\n[JEV TRANSPORT ERROR]\nCODE: {judgment.ErrorCode ?? "JEV_RESPONSE_MISSING"}";
                _judgeStatus = judgment.ErrorCode is null ? "RESPONSE_RECEIVED" : "TRANSPORT_ERROR";
                AddTaskMessage("JEV RAW RESULT → CODEX", report, status: _judgeStatus);
                var followup = AppendJevFooter(report);
                var resumed = await RunCodexWithJevFooterAsync(followup, _activeCliModel!, _activeReasoning!, _activeWorkingDirectory!, _activeSessionId, _activeReadOnly, cancellationToken, "LEGACY_JEV_RAW_RETURN");
                _activeSessionId = resumed.SessionId ?? _activeSessionId;
                _lastCodexResult = resumed;
                AddCliRoundStatus(resumed);
                CodexThreadArchive.Save(resumed, followup, _activeWorkingDirectory!);
                _commandUsage = _commandUsage.Add(resumed.Usage);
                UpdateUsage(_commandUsage);
                return await RouteCodexResultAsync(resumed, webInstruction, includeWebInstruction, gitReferenceHeader, cancellationToken);
            }
        }
        var prompt = BuildWebPrompt(result with { FinalMessage = report }, webInstruction, includeControlInstructions: true, includeWebInstruction);
        var attachments = BuildWebAttachments(_bridgeServer, result.Files);
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
        SetFlowState(false, false, false);
        ExportTaskTranscript();
    }
    private void ResetTaskState()
    {
        _activeCoordinatorFirst = false;
        _awaitingWebResult = false;
        _webFollowupStarted = false;
        _actionProtocolEnabled = false;
        _activeReadOnly = false;
        _initialGitReferenceHeader = null;
        _jobTimedOut = false;
        _lastWebTaskId = null;
        _activePrompt = null;
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
        var manualFolder = _targetSettings.ManualWorkingDirectory;
        var preferredFolderIndex = string.IsNullOrWhiteSpace(manualFolder) ? -1 : choices.FindIndex(choice =>
            string.IsNullOrWhiteSpace(choice.SessionId) && PathsEqual(choice.ProjectPath, manualFolder));
        var savedMatchesManualFolder = savedIndex >= 0 &&
            (string.IsNullOrWhiteSpace(manualFolder) || PathsEqual(choices[savedIndex].ProjectPath, manualFolder));
        CodexThreadCombo.SelectedIndex = savedMatchesManualFolder ? savedIndex : preferredFolderIndex >= 0 ? preferredFolderIndex : savedIndex >= 0 ? savedIndex : 0;
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
        if (!_loadingCodexSelections && !_syncingRoleThreadSelection) SaveCodexSelection();
        UpdateCodexSelectionDisplay();
        if (_startupConfigurationInitialized && !_syncingRoleThreadSelection) ApplyTargetConfiguration();
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
        if (!string.IsNullOrWhiteSpace(configured) && Directory.Exists(configured))
            return Path.GetFullPath(configured);
        if (!string.IsNullOrWhiteSpace(selectedThread?.ProjectPath) && Directory.Exists(selectedThread.ProjectPath))
            return Path.GetFullPath(selectedThread.ProjectPath);
        return AppContext.BaseDirectory;
    }

    private string? ResolveConfiguredGitFolder(CodexThreadOption? selectedThread)
    {
        if (!string.IsNullOrWhiteSpace(selectedThread?.SessionId) && !string.IsNullOrWhiteSpace(selectedThread.ProjectPath) && Directory.Exists(selectedThread.ProjectPath))
            return Path.GetFullPath(selectedThread.ProjectPath);

        if (!string.IsNullOrWhiteSpace(_targetSettings.ManualWorkingDirectory) && Directory.Exists(_targetSettings.ManualWorkingDirectory))
            return Path.GetFullPath(_targetSettings.ManualWorkingDirectory);
        return !string.IsNullOrWhiteSpace(selectedThread?.ProjectPath) && Directory.Exists(selectedThread.ProjectPath)
            ? Path.GetFullPath(selectedThread.ProjectPath)
            : null;
    }

    private void UpdateWorkingDirectoryControls(CodexThreadOption? selectedThread, string workingDirectory)
    {
        var lockedToThread = !string.IsNullOrWhiteSpace(selectedThread?.SessionId);
        WorkingDirectoryInput.Text = lockedToThread || !string.IsNullOrWhiteSpace(_targetSettings.ManualWorkingDirectory) || !string.IsNullOrWhiteSpace(selectedThread?.ProjectPath)
            ? workingDirectory
            : string.Empty;
        WorkingDirectoryInput.IsReadOnly = lockedToThread;
        WorkingDirectoryBrowseButton.IsEnabled = !lockedToThread;
    }

    private void RoleThreadCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingRoleControls || !_startupConfigurationInitialized || _syncingRoleThreadSelection || sender is not System.Windows.Controls.ComboBox roleCombo)
            return;

        if (roleCombo.SelectedItem is not CodexThreadOption selected || string.IsNullOrWhiteSpace(selected.ProjectPath) || !Directory.Exists(selected.ProjectPath))
            return;

        var mainSelection = (CodexThreadCombo.ItemsSource as IEnumerable<CodexThreadOption>)?.FirstOrDefault(option =>
            string.Equals(option.SessionId, selected.SessionId, StringComparison.OrdinalIgnoreCase) && PathsEqual(option.ProjectPath, selected.ProjectPath));
        if (mainSelection is null) return;

        _syncingRoleThreadSelection = true;
        try { CodexThreadCombo.SelectedItem = mainSelection; }
        finally { _syncingRoleThreadSelection = false; }

        _targetSettings = _targetSettings with { ManualWorkingDirectory = selected.ProjectPath };
        var workingDirectory = ResolveWorkingDirectory(mainSelection);
        _loadingRoleControls = true;
        try
        {
            SetRoleThreadOptions(CoordinatorRoleThreadCombo, GetCompatibleThreadSession(CoordinatorRoleThreadCombo, selected.ProjectPath));
            SetRoleThreadOptions(ImplementerRoleThreadCombo, GetCompatibleThreadSession(ImplementerRoleThreadCombo, selected.ProjectPath));
            SetRoleThreadOptions(HighLevelRoleThreadCombo, GetCompatibleThreadSession(HighLevelRoleThreadCombo, selected.ProjectPath));
            roleCombo.SelectedItem = (roleCombo.ItemsSource as IEnumerable<CodexThreadOption>)?.FirstOrDefault(option =>
                string.Equals(option.SessionId, selected.SessionId, StringComparison.OrdinalIgnoreCase) && PathsEqual(option.ProjectPath, selected.ProjectPath));
        }
        finally { _loadingRoleControls = false; }
        UpdateWorkspaceControls(mainSelection, workingDirectory);
    }

    private static string? GetCompatibleThreadSession(System.Windows.Controls.ComboBox combo, string projectPath)
    {
        var selected = combo.SelectedItem as CodexThreadOption;
        return selected is not null && PathsEqual(selected.ProjectPath, projectPath) ? selected.SessionId : null;
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

    private async Task RunCoordinatorFirstJobAsync(string request, CodexThreadOption? selectedThread, string workingDirectory, WorkerAiRoleSettings coordinator, WorkerAiRoleSettings implementer, bool highLevelAuthorizedAtLaunch = false)
    {
        var jobId = Guid.NewGuid().ToString("N");
        using var cts = new CancellationTokenSource();
        _activeTaskCts = cts;
        _activeCoordinatorFirst = true;
        ResetDashboardTaskInput();
        RunButton.Content = "■   취소";
        _userCanceledTask = false;
        _jobTimedOut = false;
        _lastActivityAt = DateTimeOffset.UtcNow;
        StartTaskTranscript(selectedThread, request, string.Empty);
        AddTaskMessage("TASK REQUEST", request, sizeBytes: Encoding.UTF8.GetByteCount(request), itemCount: 1);
        var coordinatorSession = CodexCliRunner.NormalizeSessionId(coordinator.ThreadSessionId);
        var workSession = CodexCliRunner.NormalizeSessionId(implementer.ThreadSessionId);
        var highLevel = _targetSettings.EffectiveHighLevel;
        var highLevelSession = CodexCliRunner.NormalizeSessionId(highLevel.ThreadSessionId);
        var highPermit = new JobHighLevelPermit(highLevelAuthorizedAtLaunch);
        var state = WorkerRoleState.Hq;
        var previousState = WorkerRoleState.Hq;
        try
        {
            var inboundType = "USER_REQUEST";
            var inbound = request;
            var coordinatorHasRun = false;
            var workValidationRequest = string.Empty;
            var workResultForJudge = string.Empty;
            IReadOnlyList<CodexCliFile> workFilesForJudge = Array.Empty<CodexCliFile>();
            IReadOnlyList<CodexCommandExecution> workCommandsForJudge = Array.Empty<CodexCommandExecution>();
            string unknownCode = string.Empty;
            string unknownDetail = string.Empty;

            void RouteUnknown(WorkerRoleState source, string code, string detail)
            {
                state = WorkerRoleState.Unknown;
                previousState = source;
                unknownCode = code;
                unknownDetail = detail;
            }

            while (true)
            {
                cts.Token.ThrowIfCancellationRequested();
                if (state == WorkerRoleState.Unknown)
                {
                    inboundType = "UNKNOWN";
                    inbound = WorkerTranscriptJson.Serialize(new { source_state = previousState.ToString().ToUpperInvariant(), code = unknownCode, detail = unknownDetail });
                    AddTaskMessage("UNKNOWN → HQ", inbound, status: unknownCode);
                    state = WorkerRoleState.Hq;
                }
                try
                {
                    switch (state)
                    {
                        case WorkerRoleState.Hq:
                        {
                            TaskDirection.Text = "설계·관제 AI"; TaskTitle.Text = "다음 단계를 결정하는 중"; ResultTitle.Text = "HQ";
                            SetFlowState(true, false, false, explicitStage: TaskStage.Coordinator);
                            var allowed = highPermit.IsAvailable ? "WORK 또는 HIGH (HIGH는 이번 작업에서 1회 사용 가능)" : "WORK (HIGH permit 없음)";
                            var coordinatorPrompt = $"You are HQ. Interpret the inbound message and choose the next action. Only use ACTION=CONTINUE, PAUSE, or END. For CONTINUE, use exactly one allowed GOTO destination. Allowed destination for this job: {allowed}. Treat all content and evidence as information for your judgment; Worker does not evaluate its meaning.\n\n{JevContract.LoadCoordinatorFooter()}\n\n<inbound_message>\n{WorkerTranscriptJson.Serialize(new { message_type = inboundType, body = inbound })}\n</inbound_message>";
                            var routed = await RunCoordinatorRoleAsync(jobId, "HQ_" + inboundType, coordinatorPrompt, coordinator, workingDirectory, coordinatorSession, null, cts.Token);
                            coordinatorSession = CodexCliRunner.NormalizeSessionId(routed.SessionId) ?? coordinatorSession;
                            coordinatorHasRun = true;
                            if (routed.ExitCode != 0)
                            {
                                RouteUnknown(WorkerRoleState.Hq, "PROCESS_EXIT", WorkerTranscriptJson.Serialize(new { exit_code = routed.ExitCode, stdout = routed.StandardOutput, stderr = routed.StandardError }));
                                continue;
                            }
                            var route = WorkerGotoContract.Parse(WorkerRoleState.Hq, routed.FinalMessage, highPermit.IsAvailable);
                            if (route.Error is not null)
                            {
                                RouteUnknown(WorkerRoleState.Hq, route.Error, routed.FinalMessage);
                                continue;
                            }
                            AddTaskMessage("HQ", routed.FinalMessage, status: route.Action?.ToString());
                            if (route.Action == WorkerAction.End)
                            {
                                ResultTitle.Text = "DONE"; ResultBody.Text = route.Body; TaskTitle.Text = "관제 AI가 작업을 종료했습니다.";
                                AddTaskMessage("TASK RESULT", route.Body, status: "DONE"); SetFlowState(false, false, false); return;
                            }
                            if (route.Action == WorkerAction.Pause)
                            {
                                ResultTitle.Text = "PAUSED"; ResultBody.Text = route.Body; TaskTitle.Text = "사용자 입력 대기";
                                AddTaskMessage("TASK PAUSED", route.Body, status: "PAUSED"); SetFlowState(false, false, false); return;
                            }
                            if (coordinatorHasRun && string.IsNullOrWhiteSpace(coordinatorSession))
                            {
                                RouteUnknown(WorkerRoleState.Hq, "SESSION_RESUME_FAILED", "HQ 응답 후 동일 세션 ID를 받지 못해 새 HQ 세션으로 오류를 전달합니다.");
                                continue;
                            }
                            inboundType = "HQ_INSTRUCTION"; inbound = route.Body; state = route.Target!.Value;
                            break;
                        }
                        case WorkerRoleState.Work:
                        {
                            TaskDirection.Text = "작업 AI"; TaskTitle.Text = "작업 AI가 수행 중"; ResultTitle.Text = "WORK";
                            SetFlowState(false, true, false, explicitStage: TaskStage.Implementer);
                            var footer = "\n\nControl contract for WORK. Begin with exactly [GOTO : HQ] to report, or [GOTO : JUDGE] followed by [VALIDATION REQUEST]. WORK cannot use ACTION or call HIGH. Preserve the response body as the report/request.\n";
                            var result = await RunCoordinatorRoleAsync(jobId, "WORK", inbound + footer, implementer, workingDirectory, workSession, null, cts.Token, CodexSandboxMode.WorkspaceWrite);
                            workSession = CodexCliRunner.NormalizeSessionId(result.SessionId) ?? workSession;
                            if (result.ExitCode != 0)
                            {
                                RouteUnknown(WorkerRoleState.Work, "PROCESS_EXIT", WorkerTranscriptJson.Serialize(new { exit_code = result.ExitCode, stdout = result.StandardOutput, stderr = result.StandardError }));
                                continue;
                            }
                            if (string.IsNullOrWhiteSpace(workSession))
                            {
                                RouteUnknown(WorkerRoleState.Work, "SESSION_RESUME_FAILED", "WORK 응답 후 이어갈 session ID가 없습니다.");
                                continue;
                            }
                            var route = WorkerGotoContract.Parse(WorkerRoleState.Work, result.FinalMessage);
                            if (route.Error is not null)
                            {
                                RouteUnknown(WorkerRoleState.Work, route.Error, result.FinalMessage);
                                continue;
                            }
                            if (route.Target == WorkerRoleState.Hq)
                            {
                                inboundType = "WORK_REPORT"; inbound = route.Body; state = WorkerRoleState.Hq;
                            }
                            else
                            {
                                if (!_targetSettings.EffectiveJudge.Enabled)
                                {
                                    RouteUnknown(WorkerRoleState.Work, "JUDGE_UNAVAILABLE", "WORK requested JUDGE, but no JUDGE provider is enabled.");
                                    continue;
                                }
                                workValidationRequest = JevContract.ExtractValidationRequest(route.Body);
                                if (!JevContract.TryParseValidation(workValidationRequest, out _, out var validationError))
                                {
                                    RouteUnknown(WorkerRoleState.Work, "JUDGE_REQUEST_INVALID", validationError);
                                    continue;
                                }
                                workResultForJudge = result.FinalMessage;
                                workFilesForJudge = result.Files;
                                workCommandsForJudge = result.CommandExecutions ?? Array.Empty<CodexCommandExecution>();
                                state = WorkerRoleState.Judge;
                            }
                            break;
                        }
                        case WorkerRoleState.Judge:
                        {
                            if (string.IsNullOrWhiteSpace(workSession))
                            {
                                RouteUnknown(WorkerRoleState.Judge, "WORK_SESSION_MISSING", "The JEV response cannot be returned because the requesting WORK session is unavailable.");
                                continue;
                            }
                            TaskDirection.Text = "판단 AI"; TaskTitle.Text = "JUDGE 전송 중"; ResultTitle.Text = "JUDGE";
                            SetFlowState(false, true, false, explicitStage: TaskStage.Judge);
                            var judgeRequest = new JudgeRequest(request, 1, workingDirectory, workResultForJudge, workValidationRequest, workFilesForJudge, "GIT", _gitTarget?.HeadSha, jobId, workCommandsForJudge);
                            var transport = await _jevJudgeRunner.ReviewRawAsync(judgeRequest, _targetSettings.EffectiveJudge, cts.Token);
                            RecordJevTransportTelemetry(jobId, transport.Telemetry, "JUDGE");
                            if (transport.ErrorCode is not null || transport.RawResponse is null)
                            {
                                RouteUnknown(WorkerRoleState.Judge, transport.ErrorCode ?? "JEV_RESPONSE_MISSING", transport.ErrorCode ?? "JEV returned no response.");
                                continue;
                            }
                            _judgeStatus = "RESPONSE_RECEIVED";
                            inboundType = "JUDGMENT";
                            inbound = "[GOTO : WORK]\n\n[JUDGMENT]\n" + transport.RawResponse;
                            AddTaskMessage("JUDGE RESULT → WORK", inbound, status: _judgeStatus);
                            state = WorkerRoleState.Work;
                            break;
                        }
                        case WorkerRoleState.High:
                        {
                            if (!highPermit.TryConsume())
                            {
                                RouteUnknown(WorkerRoleState.Hq, "HIGH_NOT_AUTHORIZED", "No unused HIGH permit exists for this job.");
                                continue;
                            }
                            TaskDirection.Text = "고수준 작업 AI"; TaskTitle.Text = "고수준 작업 AI가 수행 중"; ResultTitle.Text = "HIGH";
                            SetFlowState(false, true, false, explicitStage: TaskStage.HighLevel);
                            var footer = "\n\nControl contract for HIGH. Begin with exactly [GOTO : HQ] and provide an opaque report. HIGH cannot use ACTION, WORK, JUDGE, or HIGH routes.\n";
                            var result = await RunCoordinatorRoleAsync(jobId, "HIGH_LEVEL", inbound + footer, highLevel, workingDirectory, highLevelSession, null, cts.Token, CodexSandboxMode.WorkspaceWrite);
                            highLevelSession = CodexCliRunner.NormalizeSessionId(result.SessionId) ?? highLevelSession;
                            if (result.ExitCode != 0)
                            {
                                RouteUnknown(WorkerRoleState.High, "PROCESS_EXIT", WorkerTranscriptJson.Serialize(new { exit_code = result.ExitCode, stdout = result.StandardOutput, stderr = result.StandardError }));
                                continue;
                            }
                            if (string.IsNullOrWhiteSpace(highLevelSession))
                            {
                                RouteUnknown(WorkerRoleState.High, "SESSION_RESUME_FAILED", "HIGH response did not include a resumable session ID.");
                                continue;
                            }
                            var route = WorkerGotoContract.Parse(WorkerRoleState.High, result.FinalMessage);
                            if (route.Error is not null)
                            {
                                RouteUnknown(WorkerRoleState.High, route.Error, result.FinalMessage);
                                continue;
                            }
                            AddTaskMessage("HIGH", result.FinalMessage, status: "RETURNED_TO_HQ");
                            inboundType = "HIGH_REPORT"; inbound = route.Body; state = WorkerRoleState.Hq;
                            break;
                        }
                        default:
                        {
                            RouteUnknown(state, "STATE_INVALID", "Worker entered an unsupported role state.");
                            continue;
                        }
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception exception)
                {
                    RouteUnknown(state, "TRANSPORT_OR_PROTOCOL_ERROR", WorkerTranscriptJson.Serialize(new { error_type = exception.GetType().Name, detail = exception.Message }));
                    continue;
                }
                if (state == WorkerRoleState.Hq) AddTaskMessage("AI HANDOFF", inbound, status: inboundType);
            }
        }
        catch (OperationCanceledException)
        {
            AddTaskMessage("TASK CANCELED", "Coordinator-router 작업이 취소되었습니다."); ResultTitle.Text = "CANCELED"; TaskTitle.Text = "작업이 취소되었습니다."; SetFlowState(false, false, false);
        }
        catch (Exception exception) { AddTaskMessage("TASK ERROR", WorkerTranscriptJson.Serialize(new { error_type = exception.GetType().Name, detail = exception.Message }), status: "UNKNOWN"); }
        finally
        {
            _activeCoordinatorFirst = false; _activeTaskCts = null; _userCanceledTask = false; ExportTaskTranscript(); SetFlowState(false, false, false); ApplyConnectionStatus();
        }
    }
    private async Task<CodexCliResult> RunCoordinatorRoleAsync(string jobId, string purpose, string prompt, WorkerAiRoleSettings role, string workingDirectory, string? sessionId, string? schema, CancellationToken cancellationToken, CodexSandboxMode sandbox = CodexSandboxMode.ReadOnly)
    {
        var started = DateTimeOffset.UtcNow;
        var result = await _codexRunner.RunAsync(prompt, role.Model, role.Reasoning, workingDirectory, sessionId, sandbox == CodexSandboxMode.ReadOnly, cancellationToken, schema, sandbox);
        var roleName = purpose.Contains("HIGH_LEVEL", StringComparison.OrdinalIgnoreCase) || purpose.Contains("HIGHLEVEL", StringComparison.OrdinalIgnoreCase) || purpose.Contains("ASTRA", StringComparison.OrdinalIgnoreCase) ? "HIGH_LEVEL"
            : purpose.Contains("IMPLEMENTER", StringComparison.OrdinalIgnoreCase) || purpose.Contains("LUNA", StringComparison.OrdinalIgnoreCase) ? "LUNA"
            : "COORDINATOR";
        UsageTelemetryStore.Append(new ModelCallTelemetry(jobId, null, roleName, role.Model, role.Reasoning, purpose,
            result.Usage.UsageKnown ? result.Usage.InputTokens : null, result.Usage.UsageKnown ? result.Usage.CachedInputTokens : null,
            result.Usage.UsageKnown ? result.Usage.OutputTokens : null, result.Usage.UsageKnown ? result.Usage.ReasoningOutputTokens : null,
            result.Usage.ProviderTotalTokens, Encoding.UTF8.GetByteCount(prompt), Encoding.UTF8.GetByteCount(prompt), 0,
            null, Encoding.UTF8.GetByteCount(result.FinalMessage), (long)(DateTimeOffset.UtcNow - started).TotalMilliseconds,
            null, result.Usage.UsageKnown, null, null, DateTimeOffset.UtcNow));
        AddTaskMessage($"{roleName} {purpose}", $"exit {result.ExitCode} · model {role.Model} · reasoning {role.Reasoning} · session {result.SessionId ?? "missing"}");
        _lastActivityAt = DateTimeOffset.UtcNow;
        return result;
    }

    private static void RecordJevTransportTelemetry(string jobId, JevCallTelemetry? telemetry, string purpose)
    {
        if (telemetry is null) return;
        UsageTelemetryStore.Append(new ModelCallTelemetry(jobId, null, "JEV", telemetry.Model, null, purpose,
            telemetry.InputTokens, telemetry.CachedInputTokens, telemetry.OutputTokens, telemetry.ReasoningTokens, telemetry.ProviderTotalTokens,
            telemetry.RequestBytes, 0, 0, telemetry.EvidenceBytes, telemetry.ResponseBytes, telemetry.LatencyMs,
            telemetry.ErrorCode, telemetry.UsageKnown, telemetry.QuestionCount, telemetry.QuestionCount, DateTimeOffset.UtcNow, null, null, telemetry.PayloadDigest));
    }

    private void ShowCoordinatorFirstBlocked(string title, string detail)
    {
        TaskDirection.Text = "COORDINATOR-FIRST BLOCKED";
        TaskTitle.Text = title;
        ResultTitle.Text = "BLOCKED";
        ResultBody.Text = detail;
        AddTaskMessage("TASK BLOCKED", title + Environment.NewLine + detail);
        SetFlowState(false, false, false);
    }

    private void ApplyTargetConfiguration()
    {
        var server = WorkerTargetConfiguration.ResolveServer(_targetSettings);
        _serverBaseUrl = server.Url;
        _serverBaseUrlSource = server.Source;
        var selected = CodexThreadCombo.SelectedItem as CodexThreadOption;
        var workingDirectory = ResolveWorkingDirectory(selected);
        UpdateWorkspaceControls(selected, workingDirectory);
        ServerUrlInput.Text = _serverBaseUrl;
        ApplyJudgeConfigurationToControls();
        ApplyRoleSettingsToControls();
        ApplyExecutionModePresentation(_targetSettings.IsCoordinatorFirst);
        UpdateDashboardSummary();
        UpdatePipelineVisuals();
        UpdateDashboardRunButtonState();
    }

    private void UpdateDashboardSummary()
    {
        var configuredFolder = _targetSettings.ManualWorkingDirectory;
        var folder = string.IsNullOrWhiteSpace(configuredFolder) ? WorkingDirectoryInput.Text : configuredFolder;
        DashboardFolderText.Text = string.IsNullOrWhiteSpace(folder) ? "작업 폴더 미설정" : folder;
        DashboardServerUrlText.Text = _serverBaseUrl;
        var repository = _gitTarget?.RepositoryUrl;
        var repositoryName = string.IsNullOrWhiteSpace(repository)
            ? "ProjectHub"
            : repository.TrimEnd('/', '.').Split('/', ':').LastOrDefault(part => !string.IsNullOrWhiteSpace(part))?.Replace(".git", string.Empty, StringComparison.OrdinalIgnoreCase);
        DashboardProjectText.Text = string.IsNullOrWhiteSpace(repositoryName) ? "ProjectHub" : repositoryName;

        var coordinator = _targetSettings.EffectiveCoordinator;
        CoordinatorStageModelText.Text = IsWebTransport(coordinator.Transport) ? "GPT Web" : FormatStageModel(coordinator.Model);
        _coordinatorStageIconAsset = IsWebTransport(coordinator.Transport) ? "current-web.png" : "current-openai.png";
        CoordinatorStageIcon.Source = new System.Windows.Media.Imaging.BitmapImage(new Uri($"pack://application:,,,/ProjectHub.Worker;component/Assets/{_coordinatorStageIconAsset}"));
        ImplementerStageModelText.Text = FormatStageModel(_targetSettings.EffectiveImplementer.Model);
        HighLevelStageModelText.Text = FormatStageModel(_targetSettings.EffectiveHighLevel.Model);
        JudgeStageModelText.Text = "JEV";
    }

    private static bool IsWebTransport(string transport) => string.Equals(transport, "web", StringComparison.OrdinalIgnoreCase);

    private static string FormatStageModel(string model) => model.Trim().ToLowerInvariant() switch
    {
        "gpt-6-sol" => "GPT-6 Sol",
        "gpt-6-luna" => "GPT-6 Luna",
        "gpt-6-astra" => "GPT-6 Astra",
        "gpt-5.6-sol" => "GPT-5.6 Sol",
        "gpt-5.6-luna" => "GPT-5.6 Luna",
        "gpt-5.6-terra" => "GPT-5.6 Terra",
        "gpt-5.5" => "GPT-5.5",
        _ => model
    };

    private void UpdateWorkspaceControls(CodexThreadOption? selected, string workingDirectory)
    {
        _gitTarget = WorkerTargetConfiguration.ResolveGit(ResolveConfiguredGitFolder(selected) ?? string.Empty, _targetSettings);
        UpdateWorkingDirectoryControls(selected, workingDirectory);
        RepositoryUrlInput.Text = _gitTarget.RepositoryUrl ?? string.Empty;
        TargetGitStateText.Text = _gitTarget.IsRepository
            ? $"Branch: {_gitTarget.Branch ?? "unknown"} · Local HEAD: {_gitTarget.HeadSha?[..Math.Min(12, _gitTarget.HeadSha.Length)] ?? "unknown"}"
            : "Git: UNCONFIGURED";
        TargetPathText.Text = !string.IsNullOrWhiteSpace(selected?.SessionId) ? $"Codex ProjectPath: {selected.ProjectPath}" : $"New thread folder: {workingDirectory}";
        RepositoryNameText.Text = " · " + (_gitTarget.RepositoryUrl ?? "MCP-with-MiniPC");
    }

    private async Task RefreshCodexModelCatalogAsync()
    {
        var executable = _codexRunner.FindExecutable();
        _codexModelCatalog = executable is null
            ? new(Array.Empty<CodexModelCapability>(), "CODEX_CLI_NOT_FOUND")
            : await CodexModelCatalog.LoadAsync(executable);
    }

    private void ApplyRoleSettingsToControls()
    {
        _loadingRoleControls = true;
        try
        {
            SelectTag(ExecutionModeCombo, _targetSettings.ExecutionMode, "CLI_TO_CLI");
            PopulateProviderCombo(CoordinatorProviderCombo, _targetSettings.EffectiveCoordinator.Transport);
            PopulateRoleModelCombo(CoordinatorModelCombo, _targetSettings.EffectiveCoordinator.Model);
            PopulateRoleReasoningCombo(CoordinatorReasoningCombo, _targetSettings.EffectiveCoordinator.Model, _targetSettings.EffectiveCoordinator.Reasoning);
            PopulateProviderCombo(ImplementerProviderCombo, _targetSettings.EffectiveImplementer.Provider);
            PopulateRoleModelCombo(ImplementerModelCombo, _targetSettings.EffectiveImplementer.Model);
            PopulateRoleReasoningCombo(ImplementerReasoningCombo, _targetSettings.EffectiveImplementer.Model, _targetSettings.EffectiveImplementer.Reasoning);
            PopulateRoleModelCombo(HighLevelModelCombo, _targetSettings.EffectiveHighLevel.Model);
            PopulateRoleReasoningCombo(HighLevelReasoningCombo, _targetSettings.EffectiveHighLevel.Model, _targetSettings.EffectiveHighLevel.Reasoning);
            SetRoleThreadOptions(CoordinatorRoleThreadCombo, _targetSettings.EffectiveCoordinator.ThreadSessionId);
            SetRoleThreadOptions(ImplementerRoleThreadCombo, _targetSettings.EffectiveImplementer.ThreadSessionId);
            SetRoleThreadOptions(HighLevelRoleThreadCombo, _targetSettings.EffectiveHighLevel.ThreadSessionId);
            UpdateCoordinatorProviderCard();
        }
        finally { _loadingRoleControls = false; }
        UpdateRoleCapabilityPresentation();
    }

    private void SetRoleThreadOptions(System.Windows.Controls.ComboBox combo, string? sessionId)
    {
        var options = (CodexThreadCombo.ItemsSource as IEnumerable<CodexThreadOption>)?.ToArray() ?? Array.Empty<CodexThreadOption>();
        var selectedProject = CodexThreadCombo.SelectedItem as CodexThreadOption;
        var targetPath = !string.IsNullOrWhiteSpace(selectedProject?.SessionId)
            ? selectedProject.ProjectPath
            : !string.IsNullOrWhiteSpace(_targetSettings.ManualWorkingDirectory)
                ? _targetSettings.ManualWorkingDirectory
                : !string.IsNullOrWhiteSpace(WorkingDirectoryInput.Text) ? WorkingDirectoryInput.Text : selectedProject?.ProjectPath;
        combo.ItemsSource = options;
        combo.SelectedItem = options.FirstOrDefault(option =>
                !string.IsNullOrWhiteSpace(sessionId) &&
                string.Equals(option.SessionId, sessionId, StringComparison.OrdinalIgnoreCase) &&
                PathsEqual(option.ProjectPath, targetPath))
            ?? options.FirstOrDefault(option => string.IsNullOrWhiteSpace(option.SessionId) && PathsEqual(option.ProjectPath, targetPath))
            ?? options.FirstOrDefault(option => string.IsNullOrWhiteSpace(option.SessionId))
            ?? options.FirstOrDefault();
    }

    private static bool PathsEqual(string left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right)) return false;
        try { return string.Equals(Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar), Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }

    private void PopulateProviderCombo(System.Windows.Controls.ComboBox combo, string configuredProvider)
    {
        combo.Items.Clear();
        if (ReferenceEquals(combo, CoordinatorProviderCombo))
        {
            combo.Items.Add(new ComboBoxItem { Content = "OpenAI Web", Tag = "web" });
            combo.Items.Add(new ComboBoxItem { Content = "OpenAI Codex CLI", Tag = "codex_cli" });
        }
        else
        {
        combo.Items.Add(new ComboBoxItem { Content = "OpenAI · Codex CLI", Tag = "openai" });
        if (!string.Equals(configuredProvider, "openai", StringComparison.OrdinalIgnoreCase))
            combo.Items.Add(new ComboBoxItem { Content = configuredProvider, Tag = configuredProvider });
        }
        SelectTag(combo, configuredProvider, ReferenceEquals(combo, CoordinatorProviderCombo) ? "web" : "openai");
    }

    private void CoordinatorProviderCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loadingRoleControls) UpdateCoordinatorProviderCard();
    }

    private void CoordinatorWebTab_Click(object sender, RoutedEventArgs e)
    {
        SelectTag(CoordinatorProviderCombo, "web", "web");
        UpdateCoordinatorProviderCard();
    }

    private void CoordinatorCliTab_Click(object sender, RoutedEventArgs e)
    {
        SelectTag(CoordinatorProviderCombo, "codex_cli", "codex_cli");
        UpdateCoordinatorProviderCard();
    }

    private void UpdateCoordinatorProviderCard()
    {
        var isWeb = string.Equals(GetSelectedTag(CoordinatorProviderCombo, "web"), "web", StringComparison.OrdinalIgnoreCase);
        CoordinatorModelCombo.IsEnabled = !isWeb;
        CoordinatorWebCard.Visibility = isWeb ? Visibility.Visible : Visibility.Collapsed;
        CoordinatorCliCard.Visibility = isWeb ? Visibility.Collapsed : Visibility.Visible;
        CoordinatorWebTabButton.IsChecked = isWeb;
        CoordinatorCliTabButton.IsChecked = !isWeb;
        CoordinatorWebTabButton.Background = isWeb ? (System.Windows.Media.Brush)FindResource("ActiveMessageTab") : System.Windows.Media.Brushes.White;
        CoordinatorCliTabButton.Background = isWeb ? System.Windows.Media.Brushes.White : (System.Windows.Media.Brush)FindResource("ActiveMessageTab");
    }

    private void PopulateRoleModelCombo(System.Windows.Controls.ComboBox combo, string configuredModel)
    {
        combo.Items.Clear();
        foreach (var model in CodexServedModels.Current)
            combo.Items.Add(new ComboBoxItem { Content = model.DisplayName, Tag = model.Id });
        SelectTag(combo, configuredModel, CodexServedModels.Current[0].Id);
    }

    private void PopulateRoleReasoningCombo(System.Windows.Controls.ComboBox combo, string modelId, string configuredReasoning)
    {
        combo.Items.Clear();
        var model = CodexServedModels.Find(modelId) ?? CodexServedModels.Current[0];
        var efforts = model.ReasoningDepths.Select(depth => depth.ToString().ToLowerInvariant()).ToArray();
        foreach (var effort in efforts)
            combo.Items.Add(new ComboBoxItem { Content = FormatReasoningLabel(effort), Tag = effort });
        var fallback = model.DefaultReasoning.ToString().ToLowerInvariant();
        SelectTag(combo, configuredReasoning, fallback);
    }

    private static string FormatReasoningLabel(string effort) => effort.Equals("xhigh", StringComparison.OrdinalIgnoreCase)
        ? "XHigh"
        : char.ToUpperInvariant(effort[0]) + effort[1..];

    private static void SelectTag(System.Windows.Controls.ComboBox combo, string? tag, string? fallback)
    {
        combo.SelectedItem = combo.Items.OfType<ComboBoxItem>().FirstOrDefault(item => string.Equals(item.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase))
            ?? combo.Items.OfType<ComboBoxItem>().FirstOrDefault(item => string.Equals(item.Tag?.ToString(), fallback, StringComparison.OrdinalIgnoreCase));
    }

    private void RoleModelCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingRoleControls) return;
        var combo = (System.Windows.Controls.ComboBox)sender;
        var reasoningCombo = ReferenceEquals(combo, CoordinatorModelCombo) ? CoordinatorReasoningCombo
            : ReferenceEquals(combo, ImplementerModelCombo) ? ImplementerReasoningCombo : HighLevelReasoningCombo;
        var fallbackReasoning = ReferenceEquals(combo, CoordinatorModelCombo) ? _targetSettings.EffectiveCoordinator.Reasoning
            : ReferenceEquals(combo, ImplementerModelCombo) ? _targetSettings.EffectiveImplementer.Reasoning : _targetSettings.EffectiveHighLevel.Reasoning;
        var currentReasoning = GetSelectedTag(reasoningCombo, fallbackReasoning);
        _loadingRoleControls = true;
        PopulateRoleReasoningCombo(reasoningCombo, GetSelectedTag(combo, string.Empty), currentReasoning);
        _loadingRoleControls = false;
        UpdateRoleCapabilityPresentation();
    }

    private void ExecutionModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingRoleControls) return;
        var cliMode = string.Equals(GetSelectedTag(ExecutionModeCombo, "CLI_TO_CLI"), "CLI_TO_CLI", StringComparison.OrdinalIgnoreCase);
        AiRolesStatusText.Text = cliMode
            ? "Coordinator-first 실행: Web 연결은 필요하지 않습니다."
            : "Legacy 실행: 기존 Codex → GPT Web 경로를 사용합니다.";
    }

    private static string GetSelectedTag(System.Windows.Controls.ComboBox combo, string fallback) =>
        (combo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? fallback;

    private WorkerAiRoleSettings ReadRoleSettings(System.Windows.Controls.ComboBox providerCombo, System.Windows.Controls.ComboBox modelCombo, System.Windows.Controls.ComboBox reasoningCombo, WorkerAiRoleSettings fallback, System.Windows.Controls.ComboBox? threadCombo = null)
    {
        var thread = threadCombo?.SelectedItem as CodexThreadOption;
        var coordinatorTransport = ReferenceEquals(providerCombo, CoordinatorProviderCombo);
        var provider = coordinatorTransport ? fallback.Provider : GetSelectedTag(providerCombo, fallback.Provider);
        var transport = coordinatorTransport ? GetSelectedTag(providerCombo, fallback.Transport) : fallback.Transport;
        return new(provider, GetSelectedTag(modelCombo, fallback.Model), GetSelectedTag(reasoningCombo, fallback.Reasoning), transport, thread?.SessionId, thread?.ProjectPath);
    }

    private void UpdateRoleCapabilityPresentation()
    {
        var coordinator = _targetSettings.EffectiveCoordinator;
        var implementer = _targetSettings.EffectiveImplementer;
        if (CoordinatorProviderCombo.SelectedItem is ComboBoxItem coordinatorProvider)
            coordinator = coordinator with { Transport = coordinatorProvider.Tag?.ToString() ?? coordinator.Transport };
        if (CoordinatorModelCombo.SelectedItem is ComboBoxItem coordinatorModel)
            coordinator = coordinator with { Model = coordinatorModel.Tag?.ToString() ?? coordinator.Model };
        if (CoordinatorReasoningCombo.SelectedItem is ComboBoxItem coordinatorReasoning)
            coordinator = coordinator with { Reasoning = coordinatorReasoning.Tag?.ToString() ?? coordinator.Reasoning };
        if (ImplementerProviderCombo.SelectedItem is ComboBoxItem implementerProvider)
            implementer = implementer with { Provider = implementerProvider.Tag?.ToString() ?? implementer.Provider };
        if (ImplementerModelCombo.SelectedItem is ComboBoxItem implementerModel)
            implementer = implementer with { Model = implementerModel.Tag?.ToString() ?? implementer.Model };
        if (ImplementerReasoningCombo.SelectedItem is ComboBoxItem implementerReasoning)
            implementer = implementer with { Reasoning = implementerReasoning.Tag?.ToString() ?? implementer.Reasoning };
        AiRolesStatusText.Text = _codexModelCatalog.Status == "READY"
            ? $"Codex CLI capability catalog: {_codexModelCatalog.Models.Count}개 모델"
            : $"Codex CLI 모델 capability를 확인하지 못했습니다 ({_codexModelCatalog.Status}).";
    }

    private static string? GetExecutionModeConfigError(string workingDirectory)
    {
        if (!Directory.Exists(workingDirectory)) return "Working Folder가 없거나 접근할 수 없습니다.";
        return null;
    }

    private string? GetCoordinatorFirstPreflightError(string workingDirectory, WorkerAiRoleSettings coordinator, WorkerAiRoleSettings implementer)
    {
        var basic = GetExecutionModeConfigError(workingDirectory);
        if (basic is not null) return basic;
        if (!string.Equals(coordinator.Transport, "codex_cli", StringComparison.OrdinalIgnoreCase))
            return "설계·관제 AI를 Web으로 설정했지만, Web 관제 실행 경로는 아직 연결되지 않았습니다. CLI 탭으로 바꿔 실행하세요.";
        if (!string.Equals(coordinator.Provider, "openai", StringComparison.OrdinalIgnoreCase) || !string.Equals(implementer.Provider, "openai", StringComparison.OrdinalIgnoreCase) || !string.Equals(implementer.Transport, "codex_cli", StringComparison.OrdinalIgnoreCase))
            return "현재 CLI-to-CLI에서 지원하는 provider는 OpenAI Codex CLI뿐입니다. 자동 provider 대체는 하지 않습니다.";
        if (!_codexAuthenticated) return "Codex CLI 인증을 확인할 수 없습니다. codex login status를 확인하세요.";
        // Model and reasoning are selected from CodexServedModels in the settings UI.
        // Do not gate execution using the separately loaded `codex debug models` catalog:
        // that runtime catalog can lag or differ from the enum and reject a valid UI choice.
        return null;
    }

    private void ApplyExecutionModePresentation(bool coordinatorFirst)
    {
        ModelCombo.IsEnabled = !coordinatorFirst;
        ReasoningCombo.IsEnabled = !coordinatorFirst;
        WebInstructionInput.IsEnabled = !coordinatorFirst;
        HighLevelPermitCheckBox.Visibility = coordinatorFirst && _dashboardBodyMode == DashboardBodyMode.NewTaskInput ? Visibility.Visible : Visibility.Collapsed;
        if (_startupConfigurationInitialized) ApplyConnectionStatus();
    }

    private void ApplyJudgeConfigurationToControls()
    {
        var judge = _targetSettings.EffectiveJudge;
        EnableJudgeCheckBox.IsChecked = judge.Enabled;
        JudgeProviderCombo.SelectedIndex = 0;
        JudgeExecutableInput.Text = judge.ManualExecutableOrEndpoint ?? JevJudgeRunner.DefaultEndpoint;
        JudgeTimeoutInput.Text = judge.TimeoutSeconds.ToString();
        var testSettings = ReadJudgeSettingsFromControls(judge.Enabled);
        var validation = _targetSettings.JudgeEndpointValidation;
        if (WorkerTargetConfiguration.IsJudgeEndpointValidationCurrent(validation, testSettings))
        {
            SetJudgeEndpointTestStatus(
                validation!.Succeeded ? "Endpoint 응답 확인 완료" : "Endpoint 확인 실패",
                validation.Succeeded,
                $"저장된 테스트 결과: {validation.Outcome} ({validation.TestedAtUtc.LocalDateTime:g})");
        }
        else
        {
            JudgeEndpointTestStatusText.Text = string.Empty;
            JudgeEndpointTestStatusText.Foreground = (System.Windows.Media.Brush)FindResource("Muted");
            JudgeEndpointTestStatusText.ToolTip = null;
        }
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

    private JudgeSettings ReadJudgeSettingsFromControls(bool enabled)
    {
        var provider = (JudgeProviderCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString()
            ?? GetSelectedContent(JudgeProviderCombo, "Jev").ToLowerInvariant();
        var endpoint = string.IsNullOrWhiteSpace(JudgeExecutableInput.Text)
            ? JevJudgeRunner.DefaultEndpoint
            : JudgeExecutableInput.Text.Trim();
        return new JudgeSettings(enabled, provider, endpoint, ReadJudgeTimeout());
    }

    private bool PersistJudgeEndpointValidation(JudgeSettings testedSettings, bool succeeded, string outcome)
    {
        var validation = new JudgeEndpointValidation(
            WorkerTargetConfiguration.GetJudgeEndpointFingerprint(testedSettings),
            succeeded,
            outcome,
            DateTimeOffset.UtcNow);
        try
        {
            WorkerTargetConfiguration.SaveJudgeEndpointValidation(validation);
            _targetSettings = _targetSettings with { JudgeEndpointValidation = validation };
            return true;
        }
        catch (Exception exception)
        {
            _targetSettings = _targetSettings with { JudgeEndpointValidation = null };
            AddTaskMessage("JEV TEST", $"테스트 결과 저장 실패: {exception.GetType().Name}");
            return false;
        }
    }

    private async void TestJudge_Click(object sender, RoutedEventArgs e)
    {
        var testSettings = ReadJudgeSettingsFromControls(enabled: true);
        var endpoint = testSettings.ManualExecutableOrEndpoint!;
        JudgeExecutableInput.Text = endpoint;
        JudgeEndpointTestButton.IsEnabled = false;
        SetJudgeEndpointTestStatus("Endpoint 확인 중…", null);
        try
        {
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var endpointUri) || endpointUri.Scheme != Uri.UriSchemeHttps)
            {
                AddTaskMessage("JEV TEST", "Endpoint 확인 실패: HTTPS 주소가 아닙니다.");
                var invalidEndpointSaved = PersistJudgeEndpointValidation(testSettings, false, "INVALID_ENDPOINT");
                SetJudgeEndpointTestStatus(invalidEndpointSaved ? "Endpoint 확인 실패" : "테스트 결과 저장 실패", false, "HTTPS endpoint 주소를 확인하세요.");
                return;
            }

            var request = new JudgeRequest(
                "ProjectHub JEV Endpoint test",
                1,
                AppContext.BaseDirectory,
                "[NEXT : JEV]" + Environment.NewLine + Environment.NewLine + "[VALIDATION REQUEST]" + Environment.NewLine + Environment.NewLine + "- NOUL | 오늘 비가 올 확률은 몇 퍼센트나 될지 1.00으로 정규화해봐" + Environment.NewLine + "  PASS: YES >= 0.5",
                "- NOUL | 오늘 비가 올 확률은 몇 퍼센트나 될지 1.00으로 정규화해봐" + Environment.NewLine + "  PASS: YES >= 0.5",
                Array.Empty<CodexCliFile>(),
                "LOCAL",
                null);
            var result = await _jevJudgeRunner.ReviewRawAsync(request, testSettings, CancellationToken.None);
            AddTaskMessage("JEV TEST", result.ErrorCode is null ? "Endpoint 응답 확인 완료" : $"Endpoint 확인 실패: {result.ErrorCode}");
            var succeeded = result.ErrorCode is null && result.RawResponse is not null;
            var testResultSaved = PersistJudgeEndpointValidation(testSettings, succeeded, succeeded ? "RESPONSE_RECEIVED" : result.ErrorCode ?? "RESPONSE_MISSING");
            SetJudgeEndpointTestStatus(
                testResultSaved ? succeeded ? "Endpoint 응답 확인 완료" : "Endpoint 확인 실패" : "테스트 결과 저장 실패",
                testResultSaved && succeeded,
                succeeded ? "JEV 응답을 받았습니다." : result.ErrorCode ?? "응답을 받지 못했습니다.");
        }
        catch (Exception exception)
        {
            AddTaskMessage("JEV TEST", $"ERROR: {exception.GetType().Name}");
            var exceptionResultSaved = PersistJudgeEndpointValidation(testSettings, false, $"ERROR_{exception.GetType().Name}");
            SetJudgeEndpointTestStatus(exceptionResultSaved ? "Endpoint 확인 실패" : "테스트 결과 저장 실패", false, exception.GetType().Name);
        }
        finally
        {
            JudgeEndpointTestButton.IsEnabled = true;
        }
    }

    private void SetJudgeEndpointTestStatus(string message, bool? succeeded, string? detail = null)
    {
        JudgeEndpointTestStatusText.Text = message;
        JudgeEndpointTestStatusText.Foreground = succeeded switch
        {
            true => (System.Windows.Media.Brush)FindResource("Blue"),
            false => System.Windows.Media.Brushes.Red,
            _ => (System.Windows.Media.Brush)FindResource("Muted")
        };
        JudgeEndpointTestStatusText.ToolTip = detail;
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
        var workingDirectory = !string.IsNullOrWhiteSpace(selectedThread?.SessionId)
            ? ResolveWorkingDirectory(selectedThread)
            : WorkingDirectoryInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(selectedThread?.SessionId) && !Directory.Exists(workingDirectory))
        {
            WorkingDirectoryInput.ToolTip = "Choose an existing folder before applying settings.";
            return;
        }
        var timeout = ReadJudgeTimeout();
        var judgeSettings = ReadJudgeSettingsFromControls(EnableJudgeCheckBox.IsChecked == true) with { TimeoutSeconds = timeout };
        var judgeWarning = WorkerTargetConfiguration.GetJudgeApplyWarning(judgeSettings, _targetSettings.JudgeEndpointValidation);
        if (judgeWarning is not null)
            System.Windows.MessageBox.Show(this, judgeWarning, "판단 AI 설정 확인", MessageBoxButton.OK, MessageBoxImage.Warning);
        _targetSettings = _targetSettings with
        {
            ManualRepositoryUrl = null, ManualServerBaseUrl = server,
            RepositoryUrlSource = null, ServerBaseUrlSource = "MANUAL",
            ManualWorkingDirectory = workingDirectory,
            Judge = judgeSettings,
            ExecutionMode = GetSelectedTag(ExecutionModeCombo, "CLI_TO_CLI"),
            Coordinator = ReadRoleSettings(CoordinatorProviderCombo, CoordinatorModelCombo, CoordinatorReasoningCombo, _targetSettings.EffectiveCoordinator, CoordinatorRoleThreadCombo),
            Implementer = ReadRoleSettings(ImplementerProviderCombo, ImplementerModelCombo, ImplementerReasoningCombo, _targetSettings.EffectiveImplementer, ImplementerRoleThreadCombo),
            HighLevel = ReadRoleSettings(ImplementerProviderCombo, HighLevelModelCombo, HighLevelReasoningCombo, _targetSettings.EffectiveHighLevel, HighLevelRoleThreadCombo)
        };
        SaveCodexSelection();
        WorkerTargetConfiguration.Save(_targetSettings);
        ApplyTargetConfiguration();
        _serverOnline = await CheckServerAsync();
        ApplyConnectionStatus();
        SetSettingsPopupOpen(false);
    }
    private Task InitializeStartupConfigurationAsync()
    {
        if (_startupConfigurationTask is not null) return _startupConfigurationTask;
        if (_startupConfigurationInitialized) return Task.CompletedTask;
        _startupConfigurationInitialized = true;
        _startupConfigurationTask = InitializeStartupConfigurationCoreAsync();
        return _startupConfigurationTask;
    }

    private async Task InitializeStartupConfigurationCoreAsync()
    {
        // Repository discovery, Codex login status, and server endpoint resolution are
        // startup configuration work. Do not repeat them from the periodic status timer.
        _targetSettings = WorkerTargetConfiguration.Load();
        LoadCodexSelections();
        await RefreshCodexModelCatalogAsync();
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
        UpdateDashboardRunButtonState();
        SetConnectionStatus(ServerStatusText, _serverOnline ? "온라인" : "오프라인", _serverOnline, indicator: ServerStatusDot);
        SetConnectionStatus(ServerStatusTextSettings, _serverOnline ? "READY" : "OFFLINE", _serverOnline, indicator: ServerStatusDotSettings);
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
            RunButton.Content = "■   취소";
            TaskDirection.Text = "WORKER → GPT WEB";
            TaskTitle.Text = task.Status == "PENDING" ? "Worker Message 대기 중" : "GPT Web에 메시지 전달 중";
            SetFlowState(false, true, true);
            return;
        }

        if (task.Status is not "COMPLETED" and not "FAILED") return;
        _lastActivityAt = DateTimeOffset.UtcNow;
        var duplicateTerminalEvent = _actionProtocolEnabled && _lastWebTaskId == task.Id;
        _awaitingWebResult = false;
        RunButton.Content = "▶   실행";
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
        AddTaskMessage("GPT WEB", task.Result, sizeBytes: task.Result is null ? null : Encoding.UTF8.GetByteCount(task.Result), status: task.Status == "COMPLETED" ? "RECEIVED" : "FAIL");
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
        RunButton.Content = "▶   실행";
        TaskDirection.Text = "GPT WEB → WORKER";
        TaskTitle.Text = task.Status == "COMPLETED" ? "Web 응답 수신 완료" : "Web 응답 수신 실패";
        SetFlowState(false, true, false);
    }

    private void FinishActionTask(WebAction action, string response)
    {
        _awaitingWebResult = false;
        RunButton.Content = "▶   실행";
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
            if (action.Kind == WebActionKind.Hq)
            {
                var coordinator = _targetSettings.EffectiveCoordinator;
                var typedHandoff = WorkerTranscriptJson.Serialize(new { message_type = "HQ_MESSAGE", body = action.Body });
                AddTaskMessage("ACTION HQ", "현재 설정된 설계·관제 AI에 메시지를 전달합니다.", status: "ROUTING");
                if (IsWebTransport(coordinator.Transport))
                {
                    var webTask = await CreateWebTaskAsync("관제 전달 메시지입니다. message_type에 따라 본문을 해석한 뒤 다음 행동을 정하세요. 첫 줄은 [ACTION=CONTINUE|PAUSE|END|HQ] 중 하나로 시작하세요. CONTINUE면 다음 줄에 [NEXT : IMPLEMENTER|HIGH_LEVEL|JUDGE|COORDINATOR]를 쓰고 본문을 전달하세요.\n" + typedHandoff, task.Attachments ?? new List<BridgeAttachment>());
                    _awaitingWebResult = webTask is not null;
                    RunButton.Content = webTask is null ? "▶   실행" : "■   취소";
                    TaskDirection.Text = "WORKER → GPT WEB (HQ)";
                    TaskTitle.Text = webTask is null ? "관제 전달 실패" : "관제 응답 대기";
                    SetFlowState(false, true, webTask is not null);
                    return;
                }
                await RunCoordinatorFirstJobAsync(typedHandoff, null, _activeWorkingDirectory!, coordinator, _targetSettings.EffectiveImplementer);
                return;
            }
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
        using var cts = new CancellationTokenSource();
        _activeTaskCts = cts;
        _awaitingWebResult = false;
        RunButton.Content = "■   취소";
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
                    RunButton.Content = "■   취소";
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
            _userCanceledTask = false;
            UpdatePanelLayout(_awaitingWebResult);
            ApplyConnectionStatus();
            UpdateDashboardRunButtonState();
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
        var actionPattern = new System.Text.RegularExpressions.Regex(@"^\[ACTION\s*=\s*(BEGIN|CONTINUE|PAUSE|END|HQ)\s*\]$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
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
            "HQ" => WebActionKind.Hq,
            _ => WebActionKind.ProtocolError
        };
        var body = string.Join(Environment.NewLine, lines.Skip(nonEmpty[0].index + 1)).Trim();
        if ((kind is WebActionKind.Begin or WebActionKind.Continue or WebActionKind.Hq) && string.IsNullOrWhiteSpace(body))
            return new(WebActionKind.ProtocolError, string.Empty, "BEGIN/CONTINUE/HQ 본문이 비어 있습니다.");

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
                + "[ACTION=HQ] - 본문을 현재 설정된 관제 역할에 전달할 때. 관제 루틴이 message_type과 본문을 해석합니다." + Environment.NewLine
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
        AddTaskMessage("CLI STATUS", $"{outcome} · exit {result.ExitCode} · model {result.Model} · session {session}", sizeBytes: Encoding.UTF8.GetByteCount(result.FinalMessage), fileCount: result.Files.Count, status: outcome);
    }

    private void AddTaskMessage(string source, string? content, long? sizeBytes = null, int? itemCount = null, int? fileCount = null, string? status = null, string? referenceId = null, string? summary = null)
    {
        if (string.IsNullOrWhiteSpace(content)) return;
        var timestamp = DateTimeOffset.Now;
        var trimmed = content.Trim();
        _taskMessages.Add(new TaskMessage(timestamp, source, trimmed));
        _messageLogItems.Add($"[{timestamp:HH:mm:ss}] {source}{Environment.NewLine}{trimmed}");
        MessageLogEmptyText.Visibility = Visibility.Collapsed;
        var historyEvent = CreateHistoryEvent(timestamp, source, trimmed, sizeBytes, itemCount, fileCount, status, referenceId, summary);
        if (historyEvent is not null)
        {
            if (historyEvent.StageKey == "Coordinator")
                historyEvent = historyEvent with { IconAssetOverride = _coordinatorStageIconAsset };
            _historyEvents.Add(historyEvent);
            while (_historyEvents.Count > 250) _historyEvents.RemoveAt(0);
        }
        RefreshMessageLog();
    }

    private static WorkerHistoryEvent? CreateHistoryEvent(DateTimeOffset timestamp, string source, string content, long? sizeBytes, int? itemCount, int? fileCount, string? explicitStatus, string? referenceId, string? summary)
    {
        var normalized = source.Trim().ToUpperInvariant();
        string stage = normalized.Contains("JEV", StringComparison.Ordinal) || normalized.Contains("JUDGE", StringComparison.Ordinal) ? "Judge"
            : normalized.Contains("ASTRA", StringComparison.Ordinal) || normalized.Contains("HIGH LEVEL", StringComparison.Ordinal) ? "HighLevel"
            : normalized.Contains("LUNA", StringComparison.Ordinal) || normalized.Contains("IMPLEMENT", StringComparison.Ordinal) || normalized.Contains("WORKER", StringComparison.Ordinal) ? "Implementer"
            : normalized.Contains("SOL", StringComparison.Ordinal) || normalized.Contains("CODEX", StringComparison.Ordinal) || normalized.Contains("GPT WEB", StringComparison.Ordinal) ? "Coordinator"
            : "System";
        var bytes = sizeBytes ?? Encoding.UTF8.GetByteCount(content);
        var status = Regex.Match(content, @"\b(PASS|FAIL|ERROR|CANCELED|CANCELLED|BLOCKED|ACCEPTED|REJECTED)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var statusText = explicitStatus ?? (status.Success ? status.Value.ToUpperInvariant() : null);

        if (normalized is "USER COMMAND" or "GPT WEB INSTRUCTION") return null;
        if (normalized.Contains("TASK START", StringComparison.Ordinal)) return null;
        if (normalized == "TASK REQUEST")
            return new(timestamp, "Coordinator", "REQUEST_RECEIVED", "작업 요청", HistorySummary(summary ?? content), bytes, itemCount ?? 1, fileCount, null, referenceId);
        if (normalized.Contains("TASK CANCELED", StringComparison.Ordinal) || normalized.Contains("TASK CANCELLED", StringComparison.Ordinal))
            return new(timestamp, "System", "TASK_FINISHED", "작업이 취소되었습니다", "요청에 따라 실행을 중단했습니다.", null, null, null, "CANCELED", null);
        if (normalized.Contains("TASK BLOCKED", StringComparison.Ordinal) || normalized.Contains("TIMEOUT", StringComparison.Ordinal) || normalized.Contains("FAIL", StringComparison.Ordinal) || normalized.Contains("ERROR", StringComparison.Ordinal) || statusText is "FAIL" or "ERROR" or "BLOCKED")
            return new(timestamp, stage, "TASK_FAILED", "작업을 진행할 수 없습니다", HistorySummary(summary ?? content.Split(Environment.NewLine)[0]), bytes > 0 ? bytes : null, itemCount, fileCount, statusText ?? "BLOCKED", referenceId);
        if (normalized.Contains("TASK RESULT", StringComparison.Ordinal))
            return new(timestamp, "Coordinator", "TASK_FINISHED", "수행 결과", HistorySummary(summary ?? SummaryAfterFirstLine(content)), bytes, itemCount, fileCount, statusText, referenceId);
        if (normalized.Contains("SOL WORK CARD", StringComparison.Ordinal))
            return new(timestamp, "Coordinator", "WORK_PLANNED", "작업 계획", HistorySummary(summary ?? ReadJsonSummary(content, "goal", "title")), bytes, itemCount ?? 1, fileCount, statusText, referenceId);
        if (normalized.Contains("WORKER -> GPT WEB", StringComparison.Ordinal) || normalized.Contains("WORKER -> CODEX", StringComparison.Ordinal))
            return new(timestamp, stage, "REQUEST_RECEIVED", "단계 요청 전달", HistorySummary(summary ?? "구현 결과를 다음 단계에 전달했습니다."), bytes, itemCount ?? 1, fileCount, statusText, referenceId);
        if (normalized.Contains("JEV REQUEST", StringComparison.Ordinal))
            return new(timestamp, "Judge", "VALIDATION_REQUEST", "판정 요청", HistorySummary(summary ?? "원자 질문을 판정 AI에 전달했습니다."), bytes, itemCount ?? 1, fileCount, statusText, referenceId);
        if (normalized.Contains("VALIDATION", StringComparison.Ordinal))
            return new(timestamp, "Judge", "VALIDATION_RECEIVED", "검증 결과", HistorySummary(summary ?? (itemCount.HasValue ? $"{itemCount.Value}개 검증 항목의 실행 결과를 확인했습니다." : "검증 증거를 확인했습니다.")), bytes, itemCount, fileCount, statusText, referenceId);
        if (normalized.Contains("JEV RESULT", StringComparison.Ordinal) || normalized.Contains("JEV TEST", StringComparison.Ordinal))
            return new(timestamp, "Judge", "VALIDATION_RECEIVED", "판정 결과", HistorySummary(summary ?? content), bytes, itemCount, fileCount, statusText, referenceId);
        if (normalized.Contains("LUNA RESULT", StringComparison.Ordinal))
            return new(timestamp, "Implementer", "RESULT_RECEIVED", "구현 결과", HistorySummary(summary ?? ReadJsonSummary(content, "summary")), bytes, itemCount, fileCount, statusText, referenceId);
        if (normalized == "GPT WEB")
            return new(timestamp, "Coordinator", "RESULT_RECEIVED", "GPT Web 결과", HistorySummary(summary ?? content), bytes, itemCount, fileCount, statusText, referenceId);
        if (normalized.Contains("CLI STATUS", StringComparison.Ordinal))
            return new(timestamp, stage, "RESULT_RECEIVED", "Codex 실행 결과", HistorySummary(summary ?? (statusText == "PASS" ? "Codex 명령 실행이 완료되었습니다." : "Codex 명령이 실패했습니다.")), bytes, itemCount, fileCount, statusText, referenceId);
        if (normalized.Contains("SOL REVIEW", StringComparison.Ordinal))
            return new(timestamp, "Coordinator", "REVIEW_RECEIVED", "관제 검토", HistorySummary(summary ?? ReadJsonSummary(content, "summary")), bytes, itemCount, fileCount, statusText, referenceId);
        return null;
    }

    private static string HistorySummary(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var normalized = Regex.Replace(value, @"\s+", " ", RegexOptions.CultureInvariant).Trim();
        if (normalized.StartsWith('{') || normalized.StartsWith('['))
        {
            try
            {
                using var document = JsonDocument.Parse(normalized);
                var root = document.RootElement;
                foreach (var key in new[] { "summary", "message", "decision", "title" })
                    if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty(key, out var field) && field.ValueKind == JsonValueKind.String)
                    {
                        normalized = field.GetString() ?? "구조화 응답을 받았습니다.";
                        break;
                    }
                if (normalized.StartsWith('{') || normalized.StartsWith('[')) normalized = "구조화 응답을 받았습니다.";
            }
            catch (JsonException) { normalized = "응답 내용을 요약해 표시할 수 없습니다."; }
        }
        normalized = Regex.Replace(normalized, @"https?://\S+", "[주소]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        normalized = Regex.Replace(normalized, @"(?i)(api[_ -]?key|token|password|secret)\s*[:=]\s*\S+", "$1=[숨김]", RegexOptions.CultureInvariant);
        return normalized.Length <= 240 ? normalized : normalized[..237] + "…";
    }

    private static string? ReadJsonSummary(string content, params string[] properties)
    {
        try
        {
            using var document = JsonDocument.Parse(content);
            foreach (var property in properties)
                if (document.RootElement.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String)
                    return value.GetString();
        }
        catch (JsonException) { }
        return null;
    }

    private static string SummaryAfterFirstLine(string content)
    {
        var newline = content.IndexOfAny(['\r', '\n']);
        return newline < 0 ? content : content[(newline + 1)..];
    }

    private void RefreshMessageLog()
    {
        if (MessageLogList is null) return;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (DashboardHistoryList.Items.Count > 0)
                DashboardHistoryList.ScrollIntoView(DashboardHistoryList.Items[DashboardHistoryList.Items.Count - 1]);
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
