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
    private readonly AiRoleRunnerRegistry _aiRoleRunners;
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
        ["Resource"] = new("#ECD8E4", "#82194B", "#74133F", "current-web.png"),
        ["Judge"] = new("#FFF0B8", "#B87900", "#765000", "current-jev.png"),
        ["Message"] = new("#EEF8F2", "#168A4A", "#116B39", "current-console.png")
    };
    private readonly List<TaskMessage> _taskMessages = new();
    private readonly ObservableCollection<string> _messageLogItems = new();
    public ObservableCollection<string> MessageLogItems => _messageLogItems;
    public sealed record WorkerHistoryEvent(DateTimeOffset Timestamp, string StageKey, string EventType, string Title, string? Summary, long? SizeBytes, int? ItemCount, int? FileCount, string? Status, string? ReferenceId)
    {
        public string? IconAssetOverride { get; init; }
        public long? WorkNumber { get; init; }
        public string FullMessage { get; init; } = string.Empty;
        public string TokenDetails { get; init; } = "토큰 · 해당 없음";
        public string FileDetails { get; init; } = "파일 · 해당 없음";
        public string Role => StageKey switch
        {
            "Coordinator" => "설계·관제",
            "Implementer" when WorkNumber.HasValue => $"작업 (#{WorkNumber.Value})",
            "Implementer" => "작업",
            "Resource" => "리소스",
            "Judge" => "판정",
            "Message" => "메시지",
            _ => "시스템"
        };
        public string TimestampText => Timestamp.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss");
        public Visibility MetricsVisibility => EventType == "ROLE_PROGRESS" ? Visibility.Collapsed : Visibility.Visible;
        public TextWrapping SummaryWrapping => EventType == "ROLE_PROGRESS" ? TextWrapping.Wrap : TextWrapping.NoWrap;
        public TextTrimming SummaryTrimming => TextTrimming.CharacterEllipsis;
        public double SummaryMaxHeight => EventType == "ROLE_PROGRESS" ? 72d : double.PositiveInfinity;
        public double SummaryHeight => EventType == "ROLE_PROGRESS" ? 72d : double.NaN;
        public double CardHeight => EventType == "ROLE_PROGRESS" ? 104d : double.NaN;
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
    private string? _taskTranscriptPath;
    private int _taskTranscriptStartIndex;
    private string? _activeProjectJobId;
    private CoordinatorContinuationState? _continuationState;
    private readonly DispatcherTimer _flowTimer = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private readonly DispatcherTimer _connectionTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly DispatcherTimer _jobWatchdogTimer = new() { Interval = TimeSpan.FromSeconds(10) };
    private int _flowFrame;
    private bool _pairArrowActive;
    private enum TaskStage { Idle, Coordinator, Implementer, Resource, Judge }
    private enum DashboardBodyMode { NewTaskInput, TaskHistory }
    private DashboardBodyMode _dashboardBodyMode = DashboardBodyMode.NewTaskInput;
    private TaskStage _currentTaskStage = TaskStage.Idle;
    private string _coordinatorStageIconAsset = "current-openai.png";
    private string _implementerStageIconAsset = "current-openai.png";
    private string _resourceStageIconAsset = "current-web.png";
    private bool _resourceSidecarActive;
    private int _resourceSidecarQueued;
    private string _resourceSidecarStatus = "ChatGPT Web";
    private bool _judgeReviewing;
    private string _judgeStatus = "OFF";
    private int _judgeRound;
    private string _activeJevJobId = Guid.NewGuid().ToString("N");
    private bool _allowClose;
    private const string Placeholder = "CLI에 즉시 전달할 작업 지시...";
    private const string WebInstructionPlaceholder = "CLI 답변 뒤에 붙여 GPT Web에 전달할 지침...";
    private const string DashboardPromptPlaceholder = "작업 내용을 입력하세요...";
    private const string FollowupPromptPlaceholder = "추가할 작업을 입력하세요...";
    private readonly HttpClient _connectionClient = new() { Timeout = TimeSpan.FromSeconds(2) };
    private BridgeServer? _bridgeServer;
    private ManagedWebRuntimeManager? _managedWebRuntimeManager;
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
    private sealed record CodexProjectOption(string Name, string Path);
    private sealed record TaskLaunchRequest(string Prompt, string? WebInstruction, string WorkingDirectory, string? SessionId);
    private sealed record CodexThreadOption(string Label, string SessionId, string ProjectPath)
    {
        public override string ToString() => Label;
    }

    public MainWindow(
        BridgeServer? bridgeServer = null,
        ManagedWebRuntimeManager? managedWebRuntimeManager = null)
    {
        _aiRoleRunners = AiRoleRunnerRegistry.CreateDefault(_codexRunner);
        InitializeComponent();
        InitializeDirectWorkControls();
        _bridgeServer = bridgeServer;
        _managedWebRuntimeManager = managedWebRuntimeManager;
        if (bridgeServer is not null)
        {
            bridgeServer.TaskChanged += OnBridgeTaskChanged;
            bridgeServer.ExtensionProgressChanged += OnExtensionProgress;
        }
        if (managedWebRuntimeManager is not null)
            managedWebRuntimeManager.StatusChanged += OnManagedWebRuntimeStatusChanged;
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
        Loaded += async (_, _) =>
        {
            RestoreWindowPosition();
            await InitializeStartupConfigurationAsync();
            RefreshManagedWebRuntimePresentation();
        };
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
            if (_managedWebRuntimeManager is not null)
                _managedWebRuntimeManager.StatusChanged -= OnManagedWebRuntimeStatusChanged;
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
            var workArea = SystemParameters.WorkArea;
            SettingsPopupBorder.Height = Math.Clamp(workArea.Height - 90, 560, 850);
            SettingsPopupBorder.MaxHeight = SettingsPopupBorder.Height;
            SettingsPopupBorder.Width = Math.Clamp(workArea.Width - 40, 1040, 1400);
            UpdateWebRoleBindingStatusPresentation();
            Dispatcher.BeginInvoke(() =>
            {
                if (!StatusPopup.IsOpen) return;
                CoordinatorTargetCombo.Focus();
                Keyboard.Focus(CoordinatorTargetCombo);
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

    private void DashboardFollowupInput_GotFocus(object sender, RoutedEventArgs e)
        => SetInputFocusState(DashboardFollowupInput, FollowupPromptPlaceholder, focused: true);

    private void DashboardFollowupInput_LostFocus(object sender, RoutedEventArgs e)
        => SetInputFocusState(DashboardFollowupInput, FollowupPromptPlaceholder, focused: false);

    private void DashboardFollowupInput_TextChanged(object sender, TextChangedEventArgs e)
        => UpdateFollowupButtonState();

    private void UpdateFollowupButtonState()
    {
        if (AddWorkButton is null || DashboardFollowupInput is null) return;
        var inactive = !_gitPreparationInProgress &&
                       _activeTaskCts is null &&
                       !_awaitingWebResult;
        var hasContinuation = IsDirectWorkMode ||
                              (_continuationState is not null &&
                               TaskContinuationContract.IsResumableStatus(_continuationState.Status));
        var hasPrompt = !string.IsNullOrWhiteSpace(DashboardFollowupInput.Text) &&
                        DashboardFollowupInput.Text != FollowupPromptPlaceholder;
        AddWorkButton.Content = _gitPreparationInProgress ? "Git 준비 중..." : "＋   작업 추가";
        AddWorkButton.IsEnabled = inactive && hasContinuation && hasPrompt;
        AddWorkButton.Opacity = AddWorkButton.IsEnabled ? 1 : 0.72;
    }

    private void SetFollowupComposerVisible(bool visible)
    {
        if (DashboardFollowupComposer is null || AddWorkButton is null) return;
        DashboardFollowupComposer.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        AddWorkButton.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (visible)
        {
            if (string.IsNullOrWhiteSpace(DashboardFollowupInput.Text))
            {
                DashboardFollowupInput.Text = FollowupPromptPlaceholder;
                DashboardFollowupInput.Foreground = FindResource("Muted") as System.Windows.Media.Brush;
            }
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (DashboardHistoryList.Items.Count > 0)
                    DashboardHistoryList.ScrollIntoView(DashboardHistoryList.Items[DashboardHistoryList.Items.Count - 1]);
                DashboardFollowupInput.Focus();
            }), DispatcherPriority.Background);
        }
        UpdateFollowupButtonState();
    }

    private void DashboardHistoryList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        var current = e.OriginalSource as DependencyObject;
        while (current is not null && current is not ListBoxItem)
            current = System.Windows.Media.VisualTreeHelper.GetParent(current);
        if (current is not ListBoxItem container ||
            container.DataContext is not WorkerHistoryEvent item ||
            string.IsNullOrWhiteSpace(item.FullMessage))
            return;

        var viewer = new System.Windows.Controls.TextBox
        {
            Text = item.FullMessage,
            IsReadOnly = true,
            AcceptsReturn = true,
            AcceptsTab = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontFamily = new System.Windows.Media.FontFamily("Consolas"),
            FontSize = 14,
            Padding = new Thickness(12),
            Background = System.Windows.Media.Brushes.White,
            Foreground = (System.Windows.Media.Brush)FindResource("Ink")
        };
        var dialog = new Window
        {
            Title = $"{item.Role} · {item.Title} · Full Message",
            Owner = this,
            Width = 980,
            Height = 700,
            MinWidth = 720,
            MinHeight = 480,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = viewer
        };
        dialog.ShowDialog();
    }

    private void AddUserFollowupHistory(string followup)
    {
        var text = followup.Trim();
        var item = new WorkerHistoryEvent(
            DateTimeOffset.Now,
            "Message",
            "USER_FOLLOWUP",
            "추가 작업",
            WorkerHistoryCardFormatter.Preview(text),
            Encoding.UTF8.GetByteCount(text),
            1,
            null,
            "USER_FOLLOWUP",
            null)
        {
            FullMessage = text,
            TokenDetails = "토큰 · 사용자 입력",
            FileDetails = "파일 · 해당 없음"
        };
        _historyEvents.Add(item);
        RefreshMessageLog();
    }

    private async void AddWorkButton_Click(object sender, RoutedEventArgs e)
    {
        if (_gitPreparationInProgress || _activeTaskCts is not null || _awaitingWebResult) return;

        if (IsDirectWorkMode)
        {
            var directPrompt = DashboardFollowupInput.Text ?? string.Empty;
            if (string.IsNullOrWhiteSpace(directPrompt) || directPrompt == FollowupPromptPlaceholder) return;
            await InitializeStartupConfigurationAsync();
            await RunDirectWorkAsync(directPrompt, appendToHistory: true);
            return;
        }

        var continuation = _continuationState;
        if (continuation is null || !TaskContinuationContract.IsResumableStatus(continuation.Status)) return;

        var followup = DashboardFollowupInput.Text?.Trim() ?? string.Empty;
        if (followup.Length == 0 || followup == FollowupPromptPlaceholder) return;

        var preflightError = GetCoordinatorFirstPreflightError(
            continuation.WorkingDirectory,
            continuation.Coordinator,
            continuation.Implementer);
        if (preflightError is not null)
        {
            DashboardPreflightText.Text = preflightError;
            DashboardPreflightText.Foreground = System.Windows.Media.Brushes.Firebrick;
            return;
        }

        var gitReady = await PrepareParallelGitForLaunchAsync(
            continuation.WorkingDirectory);
        if (!gitReady)
            return;

        AddUserFollowupHistory(followup);
        DashboardFollowupInput.Text = FollowupPromptPlaceholder;
        DashboardFollowupInput.Foreground = FindResource("Muted") as System.Windows.Media.Brush;
        SetFollowupComposerVisible(false);
        await RunCoordinatorFirstJobAsync(
            followup,
            null,
            continuation.WorkingDirectory,
            continuation.Coordinator,
            continuation.Implementer,
            continuation);
    }

    private void UpdateDashboardRunButtonState()
    {
        if (RunButton is null || DashboardTaskInput is null) return;

        if (_gitPreparationInProgress)
        {
            RunButton.Content = "Git 준비 중...";
            ApplyRunButtonVisualState(false);
            DashboardPreflightText.Text = "Git 기준점을 준비하는 중입니다.";
            DashboardPreflightText.Foreground =
                (System.Windows.Media.Brush)FindResource("Muted");
            UpdateFollowupButtonState();
            return;
        }

        var active = _activeTaskCts is not null || _awaitingWebResult;
        var preflightError = IsDirectWorkMode ? GetDirectWorkPreflightError() : GetDashboardPreflightError();
        UpdateDirectWorkControlState(active);
        var executionReady = preflightError is null;
        var hasPrompt = !string.IsNullOrWhiteSpace(DashboardTaskInput.Text) && DashboardTaskInput.Text != DashboardPromptPlaceholder;
        if (active)
        {
            RunButton.Content = "■   취소";
            ApplyRunButtonVisualState(!(_userCanceledTask && _activeTaskCts is not null));
            DashboardPreflightText.Text = string.Empty;
            UpdateFollowupButtonState();
            return;
        }

        if (_dashboardBodyMode == DashboardBodyMode.TaskHistory)
        {
            RunButton.Content = "＋   새 작업";
            ApplyRunButtonVisualState(true);
            DashboardPreflightText.Text = string.Empty;
            UpdateFollowupButtonState();
            return;
        }

        RunButton.Content = "▶   실행";
        ApplyRunButtonVisualState(executionReady && hasPrompt);
        DashboardPreflightText.Text = preflightError ?? (hasPrompt ? string.Empty : "작업 내용을 입력하세요.");
        DashboardPreflightText.Foreground = preflightError is null ? (System.Windows.Media.Brush)FindResource("Muted") : System.Windows.Media.Brushes.Firebrick;
        UpdateFollowupButtonState();
    }

    private void ApplyRunButtonVisualState(bool enabled)
    {
        RunButton.IsEnabled = enabled;
        RunButton.Background = new System.Windows.Media.SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(enabled ? "#1477E8" : "#B8C8DA"));
        RunButton.BorderBrush = RunButton.Background;
        RunButton.Opacity = enabled ? 1 : 0.85;
        RunButton.Effect = enabled
            ? new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 16,
                ShadowDepth = 4,
                Direction = 270,
                Opacity = 0.22,
                Color = System.Windows.Media.Color.FromRgb(20, 119, 232)
            }
            : null;
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
            var workingDirectory =
                ResolveWorkingDirectory(CodexThreadCombo.SelectedItem as CodexThreadOption);
            return GetCoordinatorFirstPreflightError(
                workingDirectory,
                _targetSettings.EffectiveCoordinator,
                _targetSettings.EffectiveImplementer);
        }

        if (!_codexAuthenticated) return "Codex 로그인이 필요합니다.";
        if (_bridgeServer is null) return "GPT Web 연결을 기다리고 있습니다.";
        var hqWeb = _bridgeServer.GetRoleBindingStatus("HQ");
        if (!hqWeb.Bound) return "ChatGPT Web 대화를 HQ 역할로 연결하세요.";
        if (!hqWeb.Connected) return "HQ ChatGPT Web 대화의 heartbeat를 기다리고 있습니다.";
        if (!hqWeb.ExtensionSynchronized) return "HQ GPT Web 확장 동기화를 기다리고 있습니다.";
        return null;
    }

    private void BeginNewDashboardTask()
    {
        ExportTaskTranscript();
        ProjectWorkspacePersistence.ClearContinuation(_activeWorkingDirectory);
        _continuationState = null;
        _activeProjectJobId = null;
        SetFollowupComposerVisible(false);
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
        return new TaskLaunchRequest(prompt, null, workingDirectory, null);
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
    private void SetFlowState(bool codexActive, bool workerActive, bool webActive, TaskStage? explicitStage = null)
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
        SetPipelineCard(PipelineImplementerCard, PipelineImplementerTitle, ImplementerStageCircle, ImplementerStageIcon, _implementerStageIconAsset, TaskStage.Implementer, RoleVisuals["Implementer"], false, initialInputIdle);
        SetPipelineCard(PipelineResourceCard, PipelineResourceTitle, ResourceStageCircle, ResourceStageIcon, _resourceStageIconAsset, TaskStage.Resource, RoleVisuals["Resource"], false, initialInputIdle);
        ResourceStageModelText.Text = _resourceSidecarActive
            ? (_resourceSidecarQueued > 0 ? $"{_resourceSidecarStatus} · 대기 {_resourceSidecarQueued}" : _resourceSidecarStatus)
            : "ChatGPT Web";
        SetPipelineCard(PipelineJudgeCard, PipelineJudgeTitle, JudgeStageCircle, JudgeStageIcon, RoleVisuals["Judge"].IconAsset, TaskStage.Judge, RoleVisuals["Judge"], !_targetSettings.EffectiveJudge.Enabled, initialInputIdle);

        var idleVisual = PipelineIdleCardVisualPolicy.Resolve(idle);
        SetColor(PipelineIdleCard, idleVisual.Background);
        PipelineIdleTitle.Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(idleVisual.Foreground));
        SetColor(PipelineIdleIconCircle, idleVisual.IconBackground);
        PipelineIdleCard.BorderBrush = System.Windows.Media.Brushes.Transparent;
        PipelineIdleCard.BorderThickness = new Thickness(0);
        PipelineIdleCard.Effect = null;
    }

    private void SetPipelineCard(Border card, TextBlock title, Border iconCircle, System.Windows.Controls.Image icon, string iconAsset, TaskStage stage, RoleVisualPalette palette, bool disabled, bool initialInputIdle)
    {
        var current = _directWorkRunning
            ? !disabled && stage == TaskStage.Implementer
            : !disabled && (_currentTaskStage == stage || (stage == TaskStage.Resource && _resourceSidecarActive));
        SetPipelineStageAnimation(stage, current);
        var visual = PipelineCardVisualPolicy.Resolve(initialInputIdle, current, disabled);
        var colored = visual.IsColored;
        SetColor(card, colored ? palette.Background : "#B8C8DA");
        SetColor(iconCircle, colored ? palette.IconBackground : "#526477");
        title.Foreground = colored ? new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(palette.Foreground)) : System.Windows.Media.Brushes.White;
        var selectedName = colored ? iconAsset : iconAsset.Replace(".png", "-gray.png", StringComparison.OrdinalIgnoreCase);
        icon.Source = new System.Windows.Media.Imaging.BitmapImage(new Uri($"pack://application:,,,/ProjectHub.Worker;component/Assets/{selectedName}"));
        if (card == PipelineCoordinatorCard) CoordinatorStageModelText.Foreground = colored ? new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(palette.Foreground)) : System.Windows.Media.Brushes.White;
        else if (card == PipelineResourceCard) ResourceStageModelText.Foreground = colored ? new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(palette.Foreground)) : System.Windows.Media.Brushes.White;
        else if (card == PipelineJudgeCard) JudgeStageModelText.Foreground = colored ? new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(palette.Foreground)) : System.Windows.Media.Brushes.White;
        card.BorderBrush = System.Windows.Media.Brushes.Transparent;
        card.BorderThickness = new Thickness(1);
        card.Effect = null;
        card.Opacity = visual.Opacity;
    }

    private void SetPipelineStageAnimation(TaskStage stage, bool active)
    {
        var (baseOutline, orbit) = stage switch
        {
            TaskStage.Coordinator => (PipelineCoordinatorActiveBase, PipelineCoordinatorActiveOrbit),
            TaskStage.Implementer => (PipelineImplementerActiveBase, PipelineImplementerActiveOrbit),
            TaskStage.Resource => (PipelineResourceActiveBase, PipelineResourceActiveOrbit),
            TaskStage.Judge => (PipelineJudgeActiveBase, PipelineJudgeActiveOrbit),
            _ => throw new ArgumentOutOfRangeException(nameof(stage))
        };

        baseOutline.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        if (!active)
        {
            orbit.BeginAnimation(System.Windows.Shapes.Shape.StrokeDashOffsetProperty, null);
            orbit.StrokeDashOffset = 0;
            orbit.Visibility = Visibility.Collapsed;
            return;
        }

        if (orbit.Visibility == Visibility.Visible) return;
        orbit.Visibility = Visibility.Visible;
        orbit.BeginAnimation(System.Windows.Shapes.Shape.StrokeDashOffsetProperty,
            new System.Windows.Media.Animation.DoubleAnimation(0, -24, TimeSpan.FromSeconds(2))
            {
                RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever
            });
    }

    private static void SetColor(Border control, string color)
        => control.Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color));

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
        if (_gitPreparationInProgress)
            return;

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
        await InitializeStartupConfigurationAsync();
        if (IsDirectWorkMode)
        {
            await RunDirectWorkFromDashboardAsync();
            return;
        }

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

            var gitReady = await PrepareParallelGitForLaunchAsync(
                cliWorkingDirectory);
            if (!gitReady)
                return;

            _historyEvents.Clear();
            SetDashboardBodyMode(DashboardBodyMode.TaskHistory);
            await RunCoordinatorFirstJobAsync(launchRequest.Prompt, selectedThreadForLaunch, cliWorkingDirectory, coordinator, implementer);
            return;
        }
        var legacyHqWeb = _bridgeServer?.GetRoleBindingStatus("HQ");
        if (!_codexAuthenticated || legacyHqWeb is null || !legacyHqWeb.Bound || !legacyHqWeb.Connected || !legacyHqWeb.ExtensionSynchronized)
        {
            TaskDirection.Text = "PREFLIGHT";
            TaskTitle.Text = legacyHqWeb is null || !legacyHqWeb.Bound ? "HQ GPT Web 대화 연결 필요" : "HQ GPT Web 연결 상태 확인 필요";
            SetFlowState(false, false, false);
            return;
        }

        var prompt = launchRequest.Prompt;
        var webInstruction = launchRequest.WebInstruction ?? string.Empty;
        var seedAction = LegacyWebActionContract.Parse(prompt);
        if (seedAction.Kind is LegacyWebActionKind.ProtocolError or LegacyWebActionKind.Continue or LegacyWebActionKind.Pause or LegacyWebActionKind.End)
        {
            TaskTitle.Text = "잘못된 ACTION 시작 형식";
            ResultBody.Text = seedAction.Error ?? "[ACTION=BEGIN] 뒤에 작업 지시를 입력해야 합니다.";
            return;
        }
        var cliPrompt = prompt;
        var model = GetSelectedContent(ModelCombo, "GPT-6 Luna");
        var reasoning = GetSelectedContent(ReasoningCombo, "Medium").ToLowerInvariant();
        var cliModel = ToCliModel(model);
        var selectedThread = selectedThreadForLaunch;
        var workingDirectory = launchRequest.WorkingDirectory;
        _activeWorkingDirectory = workingDirectory;
        _activeJevJobId = Guid.NewGuid().ToString("N");
        _activeProjectJobId = _activeJevJobId;
        _historyEvents.Clear();
        SetDashboardBodyMode(DashboardBodyMode.TaskHistory);
        StartTaskTranscript(selectedThread, cliPrompt, webInstruction);
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
        _activeSessionId = sessionId;
        _activeCliModel = cliModel;
        _activeReasoning = reasoning;
        AddTaskMessage("TASK START", BuildTaskStartInfo(cliModel, reasoning, workingDirectory, sessionId));
        AddTaskMessage("TASK REQUEST", cliPrompt, sizeBytes: Encoding.UTF8.GetByteCount(cliPrompt), itemCount: 1);
        _judgeRound = 0;
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
        return Task.FromResult(_bridgeServer?.CreateTaskForRole("HQ", prompt, attachments));
    }

    private async Task<BridgeTask?> RouteCodexResultAsync(CodexCliResult result, string? webInstruction, bool includeWebInstruction, string? gitReferenceHeader, CancellationToken cancellationToken)
    {
        if (result.ExitCode != 0 || _bridgeServer is null) return null;
        var output = string.IsNullOrWhiteSpace(result.FinalMessage) ? result.StandardOutput : result.FinalMessage;
        var report = output;
        var directive = _targetSettings.EffectiveJudge.Enabled ? LegacyWebJevContract.ParseNext(output) : new NextDirective(NextRoute.Web, output);
        var protocolError = _targetSettings.EffectiveJudge.Enabled ? LegacyWebJevContract.ValidateStructure(directive) : null;
        if (protocolError is not null)
        {
            AddTaskMessage("JEV ROUTE ERROR", protocolError, status: "UNKNOWN");
            report = $"[JEV ROUTE ERROR]\nCODE: {protocolError}\n\n{output}";
        }
        else if (_targetSettings.EffectiveJudge.Enabled && directive.Route == NextRoute.Jev)
        {
            var validation = JudgeTransportContract.ExtractRequest(directive.Body);
            if (!JudgeTransportContract.TryParse(validation, out _, out var validationError))
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
        try { return prompt + Environment.NewLine + Environment.NewLine + LegacyWebJevContract.LoadFooter(); }
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

    private void RunOnUi(Action action)
    {
        if (Dispatcher.CheckAccess()) action();
        else Dispatcher.Invoke(action);
    }

    private void OnResourceSidecarStateChanged(ResourceSidecarQueueState state)
    {
        RunOnUi(() =>
        {
            _resourceSidecarActive = state.OutstandingCount > 0;
            _resourceSidecarQueued = state.QueuedCount;
            _resourceSidecarStatus = state.Stage switch
            {
                "GENERATING" => "생성·다운로드 중",
                "QUEUED" => state.Running ? "생성·다운로드 중" : "대기 중",
                _ => "ChatGPT Web"
            };
            UpdatePipelineVisuals();
        });
    }

    private void OnResourceSidecarTransportEvent(ResourceSidecarTransportEvent transport)
    {
        RunOnUi(() =>
            AddTaskMessage(
                transport.Source,
                transport.Content,
                sizeBytes: Encoding.UTF8.GetByteCount(transport.Content),
                status: transport.Status,
                includeHistory: false));
    }

    private void OnResourceSidecarCompletion(ResourceSidecarCompletion completion)
    {
        RunOnUi(() =>
        {
            if (!completion.Success)
            {
                AddTaskMessage(
                    "RESOURCE FAILED",
                    $"request {completion.RequestId} · type {completion.Type}\n{completion.Message}",
                    status: completion.ErrorCode ?? "RESOURCE_FAILED",
                    includeHistory: false);
                return;
            }

            var files = completion.SavedPaths
                .Where(File.Exists)
                .Select(path =>
                {
                    var info = new FileInfo(path);
                    return new CodexCliFile(info.FullName, info.Name, GetResourceMimeType(info.Extension), info.Length);
                })
                .ToArray();
            AddTaskMessage(
                "RESOURCE SAVED",
                $"request {completion.RequestId} · type {completion.Type}\n{completion.Message}",
                fileCount: files.Length,
                status: "SAVED",
                includeHistory: false);
            AddRoleResponseHistory(WorkerRoleState.Resource, "리소스 저장", completion.Message, files: files, status: "SAVED");
        });
    }

    private void OnObservationSidecarEvent(ObservationSidecarEvent observation)
    {
        RunOnUi(() =>
        {
            _lastActivityAt = DateTimeOffset.UtcNow;
            AddTaskMessage(
                observation.Source,
                observation.Content,
                sizeBytes: Encoding.UTF8.GetByteCount(observation.Content),
                status: observation.Status,
                includeHistory: false);
        });
    }

    private static string FormatCompletionMode(MechanicalWorkCompletionMode mode)
        => mode == MechanicalWorkCompletionMode.WorkResultRequired ? "WORK_RESULT_REQUIRED" : "FINALIZE_ONLY";

    private Task RunCoordinatorFirstJobAsync(
        string request,
        CodexThreadOption? selectedThread,
        string workingDirectory,
        WorkerAiRoleSettings coordinator,
        WorkerAiRoleSettings implementer,
        CoordinatorContinuationState? continuation = null)
        => RunParallelCoordinatorFirstJobAsync(
            request,
            selectedThread,
            workingDirectory,
            coordinator,
            implementer,
            continuation);

    private async Task<AiRoleRunResult> RunHqRoleAsync(
        string jobId,
        string purpose,
        string prompt,
        WorkerAiRoleSettings role,
        string workingDirectory,
        string? sessionId,
        CancellationToken cancellationToken,
        Action<string>? sessionStarted = null)
    {
        if (!IsWebTransport(role.Transport))
            return await RunCoordinatorRoleAsync(
                jobId,
                purpose,
                prompt,
                role,
                workingDirectory,
                sessionId,
                null,
                cancellationToken,
                CodexSandboxMode.ReadOnly,
                sessionStarted);
        return await RunWebRoleAsync(jobId, "HQ", purpose, prompt, cancellationToken);
    }

    private async Task<AiRoleRunResult> RunWebRoleAsync(string jobId, string roleName, string purpose, string prompt, CancellationToken cancellationToken)
    {
        var bridgeServer = _bridgeServer;
        var webStatus = bridgeServer?.GetRoleBindingStatus(roleName);
        if (bridgeServer is null || webStatus is null || !webStatus.Bound || !webStatus.Connected || !webStatus.ExtensionSynchronized)
            throw new InvalidOperationException($"{roleName}_WEB_UNAVAILABLE");

        var started = DateTimeOffset.UtcNow;
        AddTaskMessage($"WORKER → {roleName} WEB", prompt, sizeBytes: Encoding.UTF8.GetByteCount(prompt), status: "SENDING", includeHistory: false);
        var task = bridgeServer.CreateTaskForRole(roleName, prompt)
            ?? throw new InvalidOperationException($"{roleName}_WEB_TASK_CREATE_FAILED");
        var completed = await bridgeServer.WaitForTaskCompletionAsync(task.Id, cancellationToken)
            ?? throw new InvalidOperationException($"{roleName}_WEB_TASK_MISSING");
        var message = completed.Result ?? string.Empty;
        var success = completed.Status == "COMPLETED";
        UsageTelemetryStore.Append(new ModelCallTelemetry(
            jobId, null, roleName, "chatgpt-web", null, purpose,
            null, null, null, null, null,
            Encoding.UTF8.GetByteCount(prompt), Encoding.UTF8.GetByteCount(prompt), 0,
            completed.Attachments?.Sum(item => item.Size) ?? 0,
            Encoding.UTF8.GetByteCount(message),
            Math.Max(0, (long)(DateTimeOffset.UtcNow - started).TotalMilliseconds),
            success ? null : completed.FinishReason,
            false, null, null, DateTimeOffset.UtcNow));
        _lastActivityAt = DateTimeOffset.UtcNow;
        return new AiRoleRunResult("web", "chatgpt-web", string.Empty, null, success ? 0 : 1,
            message, success ? string.Empty : message, message, Array.Empty<CodexCliFile>(), CodexUsage.Empty, Array.Empty<CodexCommandExecution>());
    }

    private static string GetResourceFailureCode(BridgeTask? task) => task?.FinishReason switch
    {
        "resource_not_generated" or "resource_image_not_generated" => "RESOURCE_NOT_GENERATED",
        "resource_capture_failed" or "resource_image_capture_failed" => "RESOURCE_CAPTURE_FAILED",
        "resource_download_failed" or "resource_image_download_failed" => "RESOURCE_DOWNLOAD_FAILED",
        "resource_save_failed" => "RESOURCE_SAVE_FAILED",
        "send_failed" => "RESOURCE_WEB_DELIVERY_FAILED",
        "resource_timeout" => "RESOURCE_TIMEOUT",
        _ => "RESOURCE_RESULT_MISSING"
    };

    private static string GetResourceMimeType(string extension) => extension.ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".webp" => "image/webp",
        ".gif" => "image/gif",
        ".svg" => "image/svg+xml",
        ".mp3" => "audio/mpeg",
        ".wav" => "audio/wav",
        ".ogg" => "audio/ogg",
        ".flac" => "audio/flac",
        ".m4a" => "audio/mp4",
        ".mp4" => "video/mp4",
        ".webm" => "video/webm",
        ".pdf" => "application/pdf",
        ".zip" => "application/zip",
        ".json" => "application/json",
        ".txt" => "text/plain",
        ".md" => "text/markdown",
        ".csv" => "text/csv",
        ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        _ => "application/octet-stream"
    };

    private async Task<AiRoleRunResult> RunCoordinatorRoleAsync(string jobId, string purpose, string prompt, WorkerAiRoleSettings role, string workingDirectory, string? sessionId, string? schema, CancellationToken cancellationToken, CodexSandboxMode sandbox = CodexSandboxMode.ReadOnly, Action<string>? sessionStarted = null)
    {
        var started = DateTimeOffset.UtcNow;
        var roleName = purpose.Contains("IMPLEMENTER", StringComparison.OrdinalIgnoreCase) || purpose.Contains("LUNA", StringComparison.OrdinalIgnoreCase) || purpose == "WORK" ? "WORK" : "COORDINATOR";
        var outboundRole = roleName == "WORK" ? "WORK" : "HQ";
        AddTaskMessage($"WORKER → {outboundRole} CLI", prompt, sizeBytes: Encoding.UTF8.GetByteCount(prompt), status: "SENDING", includeHistory: false);
        var runner = _aiRoleRunners.Resolve(role)
            ?? throw new InvalidOperationException($"PROVIDER_RUNNER_UNAVAILABLE: {role.Provider}");
        var progressRole = roleName == "WORK" ? WorkerRoleState.Work : WorkerRoleState.Hq;
        Action<string>? progress = string.Equals(role.Transport, "codex_cli", StringComparison.OrdinalIgnoreCase)
            ? message => RunOnUi(() =>
            {
                _lastActivityAt = DateTimeOffset.UtcNow;
                AddRoleProgressHistory(progressRole, message, role.Provider);
            })
            : null;
        var result = await runner.RunAsync(new AiRoleRunRequest(prompt, role, workingDirectory, sessionId, sandbox, cancellationToken, schema, progress, sessionStarted));
        UsageTelemetryStore.Append(new ModelCallTelemetry(jobId, null, roleName, role.Model, role.Reasoning, purpose,
            result.Usage.UsageKnown ? result.Usage.InputTokens : null, result.Usage.UsageKnown ? result.Usage.CachedInputTokens : null,
            result.Usage.UsageKnown ? result.Usage.OutputTokens : null, result.Usage.UsageKnown ? result.Usage.ReasoningOutputTokens : null,
            result.Usage.ProviderTotalTokens, Encoding.UTF8.GetByteCount(prompt), Encoding.UTF8.GetByteCount(prompt), 0,
            null, Encoding.UTF8.GetByteCount(result.FinalMessage), (long)(DateTimeOffset.UtcNow - started).TotalMilliseconds,
            null, result.Usage.UsageKnown, null, null, DateTimeOffset.UtcNow));
        var transcriptSource = roleName == "WORK" && purpose == "WORK" ? "WORK CLI" : $"{roleName} {purpose}";
        AddTaskMessage(transcriptSource, $"exit {result.ExitCode} · provider {role.Provider} · model {role.Model} · reasoning {role.Reasoning} · session {result.SessionId ?? "missing"}");
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
        var implementer = _targetSettings.EffectiveImplementer;
        CoordinatorStageModelText.Text = IsWebTransport(coordinator.Transport) ? "ChatGPT Web" : AiProviderCatalog.FormatModel(coordinator.Provider, coordinator.Model);
        ImplementerWorkGaugeText.Text = FormatActiveWorkItemGauge(0);
        ResourceStageModelText.Text = "ChatGPT Web";
        JudgeStageModelText.Text = "JEV";

        _coordinatorStageIconAsset = IsWebTransport(coordinator.Transport)
            ? "current-web.png"
            : ProviderVisualCatalog.Resolve(coordinator.Provider).ColorAsset;
        _implementerStageIconAsset = ProviderVisualCatalog.Resolve(implementer.Provider).ColorAsset;
        _resourceStageIconAsset = "current-web.png";
        CoordinatorStageIcon.Source = LoadProviderAsset(_coordinatorStageIconAsset);
        ImplementerStageIcon.Source = LoadProviderAsset(_implementerStageIconAsset);
        ResourceStageIcon.Source = LoadProviderAsset(_resourceStageIconAsset);
    }

    private static string FormatActiveWorkItemGauge(int activeCount)
    {
        const int gaugeSlots = 8;
        var active = Math.Clamp(activeCount, 0, gaugeSlots);
        return new string('■', active) + new string('□', gaugeSlots - active);
    }

    private static bool IsWebTransport(string transport) => string.Equals(transport, "web", StringComparison.OrdinalIgnoreCase);

    private static System.Windows.Media.ImageSource LoadProviderAsset(string asset) =>
        new System.Windows.Media.Imaging.BitmapImage(new Uri($"pack://application:,,,/ProjectHub.Worker;component/Assets/{asset}"));

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

            var coordinator = _targetSettings.EffectiveCoordinator;
            SelectTag(CoordinatorTargetCombo, IsWebTransport(coordinator.Transport) ? "web" : "cli", "cli");
            PopulateProviderCombo(CoordinatorProviderCombo, coordinator.Provider);
            PopulateRoleModelCombo(CoordinatorModelCombo, coordinator.Provider, coordinator.Model);
            PopulateRoleReasoningCombo(CoordinatorReasoningCombo, coordinator.Provider, coordinator.Model, coordinator.Reasoning);

            var implementer = _targetSettings.EffectiveImplementer;
            PopulateProviderCombo(ImplementerProviderCombo, implementer.Provider);
            PopulateRoleModelCombo(ImplementerModelCombo, implementer.Provider, implementer.Model);
            PopulateRoleReasoningCombo(ImplementerReasoningCombo, implementer.Provider, implementer.Model, implementer.Reasoning);
            SelectTag(MaxConcurrentWorkCombo, _targetSettings.EffectiveMaxConcurrentWork.ToString(), "1");

            SetRoleThreadOptions(CoordinatorRoleThreadCombo, coordinator.ThreadSessionId);
            SetRoleThreadOptions(ImplementerRoleThreadCombo, implementer.ThreadSessionId);
            ApplyRoleSessionCapability(CoordinatorProviderCombo, CoordinatorRoleThreadCombo);
            ApplyRoleSessionCapability(ImplementerProviderCombo, ImplementerRoleThreadCombo);
            UpdateRoleProviderVisuals();
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
        foreach (var provider in AiProviderCatalog.Current)
        {
            var suffix = provider.ExecutionConfigured ? string.Empty : " · 미연결";
            combo.Items.Add(new ComboBoxItem { Content = provider.DisplayName + suffix, Tag = provider.WireId });
        }

        if (!string.IsNullOrWhiteSpace(configuredProvider) && AiProviderCatalog.Find(configuredProvider) is null)
            combo.Items.Add(new ComboBoxItem { Content = configuredProvider + " · 지원되지 않음", Tag = configuredProvider });

        SelectTag(combo, configuredProvider, string.IsNullOrWhiteSpace(configuredProvider) ? "openai" : null);
    }

    private void CoordinatorTargetCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingRoleControls) return;
        UpdateCoordinatorProviderCard();
        UpdateRoleCapabilityPresentation();
        UpdateDashboardRunButtonState();
    }

    private void RoleProviderCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingRoleControls || sender is not System.Windows.Controls.ComboBox providerCombo) return;
        RebindRoleProvider(providerCombo);
    }

    private void RebindRoleProvider(System.Windows.Controls.ComboBox providerCombo)
    {
        var (modelCombo, reasoningCombo, threadCombo, _) = GetRoleControls(providerCombo);
        var providerId = GetSelectedTag(providerCombo, string.Empty);
        _loadingRoleControls = true;
        try
        {
            PopulateRoleModelCombo(modelCombo, providerId, string.Empty);
            PopulateRoleReasoningCombo(reasoningCombo, providerId, GetSelectedTag(modelCombo, string.Empty), string.Empty);
            ApplyRoleSessionCapability(providerCombo, threadCombo);
            UpdateRoleProviderVisuals();
            if (ReferenceEquals(providerCombo, CoordinatorProviderCombo)) UpdateCoordinatorProviderCard();
        }
        finally { _loadingRoleControls = false; }
        UpdateRoleCapabilityPresentation();
    }

    private (System.Windows.Controls.ComboBox Model, System.Windows.Controls.ComboBox Reasoning, System.Windows.Controls.ComboBox Thread, System.Windows.Controls.Image Icon) GetRoleControls(System.Windows.Controls.ComboBox providerCombo)
    {
        if (ReferenceEquals(providerCombo, CoordinatorProviderCombo))
            return (CoordinatorModelCombo, CoordinatorReasoningCombo, CoordinatorRoleThreadCombo, CoordinatorProviderIcon);
        if (ReferenceEquals(providerCombo, ImplementerProviderCombo))
            return (ImplementerModelCombo, ImplementerReasoningCombo, ImplementerRoleThreadCombo, ImplementerProviderIcon);
        throw new ArgumentException("Unknown role provider control.", nameof(providerCombo));
    }

    private System.Windows.Controls.ComboBox GetProviderComboForModel(System.Windows.Controls.ComboBox modelCombo) =>
        ReferenceEquals(modelCombo, CoordinatorModelCombo) ? CoordinatorProviderCombo
        : ReferenceEquals(modelCombo, ImplementerModelCombo) ? ImplementerProviderCombo
        : throw new ArgumentException("Unknown model control.", nameof(modelCombo));

    private void ApplyRoleSessionCapability(System.Windows.Controls.ComboBox providerCombo, System.Windows.Controls.ComboBox threadCombo)
    {
        var descriptor = AiProviderCatalog.Find(GetSelectedTag(providerCombo, string.Empty));
        threadCombo.IsEnabled = descriptor?.SupportsSessions == true;
        threadCombo.ToolTip = descriptor?.SupportsSessions == true ? null : "이 Provider의 session/resume 실행은 아직 연결되지 않았습니다.";
    }

    private void UpdateRoleProviderVisuals()
    {
        if (string.Equals(GetSelectedTag(CoordinatorTargetCombo, "cli"), "web", StringComparison.OrdinalIgnoreCase))
        {
            CoordinatorProviderIconCircle.Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#EAF4FF"));
            CoordinatorProviderIcon.Source = LoadProviderAsset("current-web.png");
            CoordinatorProviderIcon.ToolTip = "ChatGPT Web";
        }
        else
        {
            CoordinatorProviderIconCircle.Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#1477E8"));
            SetProviderIcon(CoordinatorProviderIcon, GetSelectedTag(CoordinatorProviderCombo, _targetSettings.EffectiveCoordinator.Provider));
        }
        SetProviderIcon(ImplementerProviderIcon, GetSelectedTag(ImplementerProviderCombo, _targetSettings.EffectiveImplementer.Provider));
    }

    private static void SetProviderIcon(System.Windows.Controls.Image image, string providerWireId)
    {
        image.Source = LoadProviderAsset(ProviderVisualCatalog.Resolve(providerWireId).ColorAsset);
        image.ToolTip = ProviderVisualCatalog.Resolve(providerWireId).DisplayName;
    }

    private void UpdateCoordinatorProviderCard()
    {
        var web = string.Equals(GetSelectedTag(CoordinatorTargetCombo, IsWebTransport(_targetSettings.EffectiveCoordinator.Transport) ? "web" : "cli"), "web", StringComparison.OrdinalIgnoreCase);
        CoordinatorCliOptionsPanel.Visibility = web ? Visibility.Collapsed : Visibility.Visible;
        CoordinatorWebCard.Visibility = web ? Visibility.Visible : Visibility.Collapsed;
        CoordinatorCliCard.Visibility = web ? Visibility.Collapsed : Visibility.Visible;
        CoordinatorRoleThreadCombo.IsEnabled = !web && AiProviderCatalog.Find(GetSelectedTag(CoordinatorProviderCombo, string.Empty))?.SupportsSessions == true;
        if (web)
        {
            CoordinatorProviderIconCircle.Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#EAF4FF"));
            CoordinatorProviderIcon.Source = LoadProviderAsset("current-web.png");
            CoordinatorProviderIcon.ToolTip = "ChatGPT Web";
        }
        else
        {
            CoordinatorProviderIconCircle.Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#1477E8"));
            ApplyRoleSessionCapability(CoordinatorProviderCombo, CoordinatorRoleThreadCombo);
            SetProviderIcon(CoordinatorProviderIcon, GetSelectedTag(CoordinatorProviderCombo, _targetSettings.EffectiveCoordinator.Provider));
        }
    }

    private void PopulateRoleModelCombo(System.Windows.Controls.ComboBox combo, string providerId, string configuredModel)
    {
        combo.Items.Clear();
        var provider = AiProviderCatalog.Find(providerId);
        if (provider is null || provider.Models.Count == 0)
        {
            var preserved = configuredModel?.Trim() ?? string.Empty;
            combo.Items.Add(new ComboBoxItem
            {
                Content = string.IsNullOrWhiteSpace(preserved) ? "(연결 후 모델 로드)" : preserved + " · 미연결",
                Tag = preserved
            });
            combo.SelectedIndex = 0;
            combo.IsEnabled = false;
            return;
        }

        foreach (var model in provider.Models)
            combo.Items.Add(new ComboBoxItem { Content = model.DisplayName, Tag = model.Id });
        SelectTag(combo, configuredModel, provider.Models[0].Id);
        combo.IsEnabled = true;
    }

    private void PopulateRoleReasoningCombo(System.Windows.Controls.ComboBox combo, string providerId, string modelId, string configuredReasoning)
    {
        combo.Items.Clear();
        var provider = AiProviderCatalog.Find(providerId);
        var model = provider?.FindModel(modelId);
        if (model is null)
        {
            var preserved = configuredReasoning?.Trim() ?? string.Empty;
            combo.Items.Add(new ComboBoxItem
            {
                Content = string.IsNullOrWhiteSpace(preserved) ? "(연결 후 추론 옵션 로드)" : FormatReasoningLabel(preserved) + " · 미연결",
                Tag = preserved
            });
            combo.SelectedIndex = 0;
            combo.IsEnabled = false;
            return;
        }

        foreach (var effort in model.ReasoningOptions)
            combo.Items.Add(new ComboBoxItem { Content = FormatReasoningLabel(effort), Tag = effort });
        SelectTag(combo, configuredReasoning, model.DefaultReasoning);
        combo.IsEnabled = true;
    }

    private static string FormatReasoningLabel(string effort)
    {
        if (string.IsNullOrWhiteSpace(effort)) return string.Empty;
        return effort.Equals("xhigh", StringComparison.OrdinalIgnoreCase)
            ? "XHigh"
            : char.ToUpperInvariant(effort[0]) + effort[1..];
    }

    private static void SelectTag(System.Windows.Controls.ComboBox combo, string? tag, string? fallback)
    {
        combo.SelectedItem = combo.Items.OfType<ComboBoxItem>().FirstOrDefault(item => string.Equals(item.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase))
            ?? combo.Items.OfType<ComboBoxItem>().FirstOrDefault(item => !string.IsNullOrWhiteSpace(fallback) && string.Equals(item.Tag?.ToString(), fallback, StringComparison.OrdinalIgnoreCase));
    }

    private void RoleModelCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingRoleControls || sender is not System.Windows.Controls.ComboBox modelCombo) return;
        var providerCombo = GetProviderComboForModel(modelCombo);
        var reasoningCombo = ReferenceEquals(modelCombo, CoordinatorModelCombo) ? CoordinatorReasoningCombo : ImplementerReasoningCombo;
        var fallbackReasoning = ReferenceEquals(modelCombo, CoordinatorModelCombo) ? _targetSettings.EffectiveCoordinator.Reasoning : _targetSettings.EffectiveImplementer.Reasoning;
        var currentReasoning = GetSelectedTag(reasoningCombo, fallbackReasoning);
        _loadingRoleControls = true;
        PopulateRoleReasoningCombo(reasoningCombo, GetSelectedTag(providerCombo, string.Empty), GetSelectedTag(modelCombo, string.Empty), currentReasoning);
        _loadingRoleControls = false;
        UpdateRoleCapabilityPresentation();
    }

    private void ExecutionModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingRoleControls) return;
        var cliMode = string.Equals(GetSelectedTag(ExecutionModeCombo, "CLI_TO_CLI"), "CLI_TO_CLI", StringComparison.OrdinalIgnoreCase);
        AiRolesStatusText.Text = cliMode
            ? "Coordinator-first 실행: HQ는 ChatGPT Web 또는 CLI Provider, WORK는 CLI, RESOURCE는 Web 고정입니다."
            : "Legacy 실행: 기존 Codex → GPT Web 경로를 사용합니다.";
    }

    private static string GetSelectedTag(System.Windows.Controls.ComboBox combo, string fallback) =>
        (combo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? fallback;

    private WorkerAiRoleSettings ReadCoordinatorSettings()
    {
        var fallback = _targetSettings.EffectiveCoordinator;
        if (string.Equals(GetSelectedTag(CoordinatorTargetCombo, "cli"), "web", StringComparison.OrdinalIgnoreCase))
            return fallback with { Transport = "web", ThreadSessionId = null, ThreadProjectPath = null };
        return ReadRoleSettings(CoordinatorProviderCombo, CoordinatorModelCombo, CoordinatorReasoningCombo, fallback, CoordinatorRoleThreadCombo);
    }

    private WorkerAiRoleSettings ReadRoleSettings(System.Windows.Controls.ComboBox providerCombo, System.Windows.Controls.ComboBox modelCombo, System.Windows.Controls.ComboBox reasoningCombo, WorkerAiRoleSettings fallback, System.Windows.Controls.ComboBox? threadCombo = null)
    {
        var providerId = GetSelectedTag(providerCombo, fallback.Provider);
        var descriptor = AiProviderCatalog.Find(providerId);
        var thread = descriptor?.SupportsSessions == true ? threadCombo?.SelectedItem as CodexThreadOption : null;
        var transport = descriptor?.DefaultTransport ?? fallback.Transport;
        return new(providerId, GetSelectedTag(modelCombo, fallback.Model), GetSelectedTag(reasoningCombo, fallback.Reasoning), transport, thread?.SessionId, thread?.ProjectPath);
    }

    private void UpdateRoleCapabilityPresentation()
    {
        var selectedProviders = new List<string>
        {
            GetSelectedTag(ImplementerProviderCombo, _targetSettings.EffectiveImplementer.Provider)
        };
        if (!string.Equals(GetSelectedTag(CoordinatorTargetCombo, "cli"), "web", StringComparison.OrdinalIgnoreCase))
            selectedProviders.Add(GetSelectedTag(CoordinatorProviderCombo, _targetSettings.EffectiveCoordinator.Provider));
        var unresolved = selectedProviders
            .Select(AiProviderCatalog.Find)
            .Where(provider => provider is null || !provider.ExecutionConfigured)
            .Select(provider => provider?.DisplayName ?? "지원되지 않는 Provider")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        AiRolesStatusText.Text = unresolved.Length == 0
            ? $"Provider 구조 준비 완료 · OpenAI 모델 {AiProviderCatalog.Get(AiServiceProvider.OpenAI).Models.Count}개"
            : $"Provider 구조 준비 완료 · 실행 미연결: {string.Join(", ", unresolved)}";
    }

    private static string? GetExecutionModeConfigError(string workingDirectory)
    {
        if (!Directory.Exists(workingDirectory)) return "Working Folder가 없거나 접근할 수 없습니다.";
        return null;
    }

    private string? GetParallelGitPreflightError(string workingDirectory)
    {
        var result = ParallelWorkGitPreflight.Validate(
            WorkerTargetConfiguration.ResolveGit(
                workingDirectory,
                _targetSettings));
        return result.Success ? null : result.Message;
    }

    private string? GetCoordinatorFirstPreflightError(string workingDirectory, WorkerAiRoleSettings coordinator, WorkerAiRoleSettings implementer)
    {
        var basic = GetExecutionModeConfigError(workingDirectory);
        if (basic is not null) return basic;

        if (IsWebTransport(coordinator.Transport))
        {
            var hqWeb = _bridgeServer?.GetRoleBindingStatus("HQ");
            if (hqWeb is null || !hqWeb.Bound) return "ChatGPT Web 대화를 HQ 역할로 연결하세요.";
            if (!hqWeb.Connected) return "HQ ChatGPT Web 대화의 heartbeat를 기다리고 있습니다.";
            if (!hqWeb.ExtensionSynchronized) return "HQ GPT Web 확장 동기화를 기다리고 있습니다.";
        }
        else
        {
            var coordinatorError = _aiRoleRunners.GetPreflightError(coordinator, workingDirectory, _codexAuthenticated);
            if (coordinatorError is not null) return coordinatorError;
        }

        return _aiRoleRunners.GetPreflightError(implementer, workingDirectory, _codexAuthenticated);
    }

    private void ApplyExecutionModePresentation(bool coordinatorFirst)
    {
        ModelCombo.IsEnabled = !coordinatorFirst;
        ReasoningCombo.IsEnabled = !coordinatorFirst;
        WebInstructionInput.IsEnabled = !coordinatorFirst;
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
        var maxConcurrentWork = int.TryParse(
            GetSelectedTag(MaxConcurrentWorkCombo, "1"),
            out var parsedMaxConcurrentWork)
            ? Math.Clamp(parsedMaxConcurrentWork, WorkGraph.MinimumConcurrency, WorkGraph.MaximumConcurrency)
            : 1;
        if (judgeWarning is not null)
            System.Windows.MessageBox.Show(this, judgeWarning, "판정 AI 설정 확인", MessageBoxButton.OK, MessageBoxImage.Warning);
        _targetSettings = _targetSettings with
        {
            ManualRepositoryUrl = null, ManualServerBaseUrl = server,
            RepositoryUrlSource = null, ServerBaseUrlSource = "MANUAL",
            ManualWorkingDirectory = workingDirectory,
            Judge = judgeSettings,
            ExecutionMode = GetSelectedTag(ExecutionModeCombo, "CLI_TO_CLI"),
            Coordinator = ReadCoordinatorSettings(),
            Implementer = ReadRoleSettings(ImplementerProviderCombo, ImplementerModelCombo, ImplementerReasoningCombo, _targetSettings.EffectiveImplementer, ImplementerRoleThreadCombo),
            MaxConcurrentWork = maxConcurrentWork
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

    private void UpdateWebRoleBindingStatusPresentation()
    {
        ApplyWebRoleBindingStatus(
            _bridgeServer?.GetRoleBindingStatus("HQ"),
            CoordinatorWebBindingStatusText,
            CoordinatorWebBindingDetailText,
            "HQ");
        ApplyWebRoleBindingStatus(
            _bridgeServer?.GetRoleBindingStatus("RESOURCE"),
            ResourceWebBindingStatusText,
            ResourceWebBindingDetailText,
            "RESOURCE");
    }

    private static void ApplyWebRoleBindingStatus(WebRoleBindingStatus? status, TextBlock statusText, TextBlock detailText, string role)
    {
        if (status is null || !status.Bound)
        {
            statusText.Text = $"{role} Web 미연결";
            statusText.Foreground = System.Windows.Media.Brushes.OrangeRed;
            detailText.Text = $"브라우저 확장에서 {role} 역할로 연결하세요.";
            return;
        }

        var id = status.ConversationId ?? string.Empty;
        var shortId = id.Length > 12 ? id[..12] + "…" : id;
        var label = !string.IsNullOrWhiteSpace(status.ConversationTitle) ? status.ConversationTitle : shortId;
        if (!status.Connected)
        {
            statusText.Text = $"{role} Web 연결 대기";
            statusText.Foreground = System.Windows.Media.Brushes.DarkOrange;
            detailText.Text = string.IsNullOrWhiteSpace(label) ? "연결된 대화의 heartbeat를 기다리고 있습니다." : $"{label} · heartbeat 대기";
            return;
        }

        if (!status.ExtensionSynchronized)
        {
            statusText.Text = $"{role} Web 확장 업데이트 필요";
            statusText.Foreground = System.Windows.Media.Brushes.OrangeRed;
            detailText.Text = string.IsNullOrWhiteSpace(label) ? "확장 버전을 확인하세요." : label;
            return;
        }

        statusText.Text = $"{role} Web 연결됨";
        statusText.Foreground = System.Windows.Media.Brushes.ForestGreen;
        detailText.Text = string.IsNullOrWhiteSpace(label) ? "연결된 대화가 활성 상태입니다." : label;
    }

    private void ApplyConnectionStatus()
    {
        UpdateWebRoleBindingStatusPresentation();
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
            var progressTask = _bridgeServer?.GetTaskSnapshot(progress.TaskId);
            if (progressTask is not null && progressTask.Owner.Equals("RESOURCE", StringComparison.OrdinalIgnoreCase))
            {
                _resourceSidecarStatus = progress.Stage switch
                {
                    "CLAIMED" or "WAIT_SEND_READY" or "TEXT_INSERT" or "SEND_BUTTON_FIND" or "SEND_CONFIRM" => "Web 전송 확인 중",
                    "WAIT_RESPONSE" or "RESPONSE_START" => "생성 결과 대기",
                    "RESOURCE_DETECTED" => "생성 결과 확인",
                    "RESOURCE_READY" => "다운로드 준비",
                    "DOWNLOAD_START" or "DOWNLOAD_PROGRESS" => "다운로드 중",
                    "RESULT_POST" or "RESULT_POST_RETRY" => "Worker 전달 중",
                    "RESOURCE_RESCAN" => "결과 다시 수집",
                    "RESOURCE_NO_FILE" => "생성 파일 미확인",
                    "FINISHED" => "저장 완료",
                    "FAILED" => "실패",
                    _ => _resourceSidecarStatus
                };
                UpdatePipelineVisuals();
            }
            if (progress.Stage is not "FINISHED" and not "FAILED")
            {
                if (_activeCoordinatorFirst && _currentTaskStage == TaskStage.Resource)
                {
                    TaskDirection.Text = "리소스 AI";
                    TaskTitle.Text = $"RESOURCE Web {progress.Stage}";
                    SetFlowState(false, true, true, explicitStage: TaskStage.Resource);
                }
                else if (_activeCoordinatorFirst && _currentTaskStage == TaskStage.Coordinator)
                {
                    TaskDirection.Text = "설계·관제 AI";
                    TaskTitle.Text = $"HQ Web {progress.Stage}";
                    SetFlowState(true, false, true, explicitStage: TaskStage.Coordinator);
                }
                else if (_awaitingWebResult)
                {
                    TaskDirection.Text = "WORKER → GPT WEB";
                    TaskTitle.Text = $"Web {progress.Stage}";
                    SetFlowState(false, true, true);
                }
            }
        }));
    }

    private void OnBridgeTaskChanged(BridgeTask task)
    {
        Dispatcher.BeginInvoke(new Action(() => HandleBridgeTaskChanged(task)));
    }

    private async void HandleBridgeTaskChanged(BridgeTask task)
    {
        if (task.Owner.Equals("HQ", StringComparison.OrdinalIgnoreCase) || task.Owner.Equals("RESOURCE", StringComparison.OrdinalIgnoreCase))
            return;

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

    private void FinishActionTask(LegacyWebAction action, string response)
    {
        _awaitingWebResult = false;
        RunButton.Content = "▶   실행";
        TaskDirection.Text = "GPT WEB → WORKER";
        TaskTitle.Text = action.Kind switch
        {
            LegacyWebActionKind.End => "Web이 작업 완료를 알림",
            LegacyWebActionKind.Pause => "Web이 사용자 판단을 요청함",
            LegacyWebActionKind.ProtocolError => "ACTION 프로토콜 오류",
            _ => "Web 결과 처리 종료"
        };
        ResultTitle.Text = action.Kind switch
        {
            LegacyWebActionKind.End => "FINISH_SUCCESS",
            LegacyWebActionKind.Pause => "FINISH_PAUSED",
            LegacyWebActionKind.ProtocolError => "FINISH_PROTOCOL_ERROR",
            _ => "Web 결과"
        };
        ResultBody.Text = action.Kind == LegacyWebActionKind.ProtocolError ? (action.Error ?? "ACTION 프로토콜 오류") + Environment.NewLine + Environment.NewLine + response : response;
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
        var action = LegacyWebActionContract.Parse(webResponse, strict: true);
        string followupPrompt;
        if (_actionProtocolEnabled)
        {
            if (action.Kind is LegacyWebActionKind.End or LegacyWebActionKind.Pause or LegacyWebActionKind.ProtocolError or LegacyWebActionKind.None)
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

    private string BuildWebPrompt(
        CodexCliResult result,
        string? webInstruction,
        bool includeControlInstructions,
        bool includeWebInstruction)
    {
        var output = string.IsNullOrWhiteSpace(result.FinalMessage) ? result.StandardOutput : result.FinalMessage;
        var control = includeControlInstructions ? LegacyWebActionContract.BuildInstructions(_targetSettings.EffectiveJudge.Enabled) + Environment.NewLine + Environment.NewLine : string.Empty;
        var instruction = includeWebInstruction && !string.IsNullOrWhiteSpace(webInstruction)
            ? Environment.NewLine + Environment.NewLine + webInstruction
            : string.Empty;
        var jevGuidance = includeControlInstructions && _targetSettings.EffectiveJudge.Enabled
            ? Environment.NewLine + Environment.NewLine
                + "JEV 검증 지침: 관측 사실 자체를 다시 확인하지 말고, 현재 근거만으로 기계적으로 확정할 수 없는 판단이 다음 작업이나 완료 결과에 영향을 줄 때 JEV 판정을 요청하세요."
                + Environment.NewLine
                + "이미 판정한 판단의 근거가 의미 있게 바뀌면 새 근거로 다시 요청하고, 기계적으로 확인 가능한 사실만 남았으면 JEV 없이 보고하세요."
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
        var projectPath = !string.IsNullOrWhiteSpace(selectedThread?.ProjectPath)
            ? selectedThread.ProjectPath
            : _activeWorkingDirectory;
        _taskProjectName = string.IsNullOrWhiteSpace(projectPath)
            ? "UnknownProject"
            : new DirectoryInfo(projectPath).Name;
        _taskThreadName = string.IsNullOrWhiteSpace(selectedThread?.SessionId)
            ? "NewThread"
            : selectedThread.Label;
        StartCommandTranscript();
        AddTaskMessage("USER COMMAND", command);
        AddTaskMessage("GPT WEB INSTRUCTION", webInstruction);
    }

    private void StartCommandTranscript()
    {
        _taskExported = false;
        _taskTranscriptPath = null;
        _taskStartedAt = DateTimeOffset.Now;
        _taskTranscriptStartIndex = _taskMessages.Count;
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

    private void AddTaskMessage(string source, string? content, long? sizeBytes = null, int? itemCount = null, int? fileCount = null, string? status = null, string? referenceId = null, string? summary = null, bool includeHistory = true)
    {
        if (string.IsNullOrWhiteSpace(content)) return;
        var timestamp = DateTimeOffset.Now;
        var trimmed = content.Trim();
        var eventId = ProjectWorkspacePersistence.AppendEvent(
            _activeWorkingDirectory,
            _activeProjectJobId,
            timestamp,
            source,
            trimmed,
            status,
            referenceId,
            sizeBytes,
            itemCount,
            fileCount);
        var effectiveReferenceId = referenceId ?? eventId;
        _taskExported = false;
        _taskMessages.Add(new TaskMessage(timestamp, source, trimmed));
        _messageLogItems.Add($"[{timestamp:HH:mm:ss}] {source}{Environment.NewLine}{trimmed}");
        MessageLogEmptyText.Visibility = Visibility.Collapsed;
        if (includeHistory)
        {
            var historyEvent = CreateHistoryEvent(timestamp, source, trimmed, sizeBytes, itemCount, fileCount, status, effectiveReferenceId, summary);
            if (historyEvent is not null)
            {
                historyEvent = historyEvent with { FullMessage = trimmed };
                if (historyEvent.StageKey == "Coordinator")
                    historyEvent = historyEvent with { IconAssetOverride = _coordinatorStageIconAsset };
                _historyEvents.Add(historyEvent);
            }
        }
        RefreshMessageLog();
    }

    private void AddRoleProgressHistory(
        WorkerRoleState role,
        string? body,
        string? providerWireId = null,
        long? workNumber = null,
        string? referenceId = null)
    {
        var text = body?.Trim() ?? string.Empty;
        if (text.Length == 0) return;

        var stage = role switch
        {
            WorkerRoleState.Hq => "Coordinator",
            WorkerRoleState.Work => "Implementer",
            _ => "System"
        };
        var item = new WorkerHistoryEvent(
            DateTimeOffset.Now,
            stage,
            "ROLE_PROGRESS",
            "작업 진행",
            WorkerHistoryCardFormatter.ProgressPreview(text),
            Encoding.UTF8.GetByteCount(text),
            null,
            null,
            "RUNNING",
            referenceId)
        {
            WorkNumber = workNumber,
            FullMessage = text,
            TokenDetails = string.Empty,
            FileDetails = string.Empty
        };
        if (!string.IsNullOrWhiteSpace(providerWireId))
            item = item with { IconAssetOverride = ProviderVisualCatalog.Resolve(providerWireId).ColorAsset };
        else if (item.StageKey == "Coordinator")
            item = item with { IconAssetOverride = _coordinatorStageIconAsset };

        _historyEvents.Add(item);
        AddTaskMessage(role == WorkerRoleState.Work ? "WORK PROGRESS" : "HQ PROGRESS", text, status: "RUNNING", includeHistory: false);
        if (DashboardHistoryList.Items.Count > 0)
            DashboardHistoryList.ScrollIntoView(DashboardHistoryList.Items[DashboardHistoryList.Items.Count - 1]);
        RefreshMessageLog();
    }

    private void AddRoleResponseHistory(
        WorkerRoleState role,
        string title,
        string? body,
        CodexUsage? usage = null,
        IReadOnlyList<CodexCliFile>? files = null,
        JevCallTelemetry? judgeTelemetry = null,
        string? status = null,
        string? providerWireId = null,
        string? fullMessage = null,
        long? workNumber = null,
        string? referenceId = null)
    {
        var stage = role switch
        {
            WorkerRoleState.Hq => "Coordinator",
            WorkerRoleState.Work => "Implementer",
            WorkerRoleState.Resource => "Resource",
            WorkerRoleState.Judge => "Judge",
            _ => "System"
        };
        var text = body?.Trim() ?? string.Empty;
        var fullText = string.IsNullOrWhiteSpace(fullMessage) ? text : fullMessage.Trim();
        var item = new WorkerHistoryEvent(
            DateTimeOffset.Now,
            stage,
            "ROLE_RESPONSE",
            title,
            WorkerHistoryCardFormatter.Preview(text),
            string.IsNullOrEmpty(text) ? null : Encoding.UTF8.GetByteCount(text),
            null,
            files?.Count,
            status,
            referenceId)
        {
            WorkNumber = workNumber,
            FullMessage = fullText,
            TokenDetails = judgeTelemetry is not null
                ? WorkerHistoryCardFormatter.TokenLine(judgeTelemetry)
                : WorkerHistoryCardFormatter.TokenLine(usage),
            FileDetails = WorkerHistoryCardFormatter.FileLine(files)
        };
        if (!string.IsNullOrWhiteSpace(providerWireId))
            item = item with { IconAssetOverride = ProviderVisualCatalog.Resolve(providerWireId).ColorAsset };
        else if (item.StageKey == "Coordinator")
            item = item with { IconAssetOverride = _coordinatorStageIconAsset };
        _historyEvents.Add(item);
        RefreshMessageLog();
    }

    private static WorkerHistoryEvent? CreateHistoryEvent(DateTimeOffset timestamp, string source, string content, long? sizeBytes, int? itemCount, int? fileCount, string? explicitStatus, string? referenceId, string? summary)
    {
        var normalized = source.Trim().ToUpperInvariant();
        string stage = normalized.Contains("JEV", StringComparison.Ordinal) || normalized.Contains("JUDGE", StringComparison.Ordinal) ? "Judge"
            : normalized.Contains("RESOURCE", StringComparison.Ordinal) ? "Resource"
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
        if (_taskExported) return _taskTranscriptPath;
        var commandMessages = _taskMessages
            .Skip(Math.Clamp(_taskTranscriptStartIndex, 0, _taskMessages.Count))
            .ToArray();
        if (commandMessages.Length == 0) return null;

        try
        {
            var hasProjectTranscript = !string.IsNullOrWhiteSpace(_activeWorkingDirectory) &&
                                       !string.IsNullOrWhiteSpace(_activeProjectJobId) &&
                                       Directory.Exists(_activeWorkingDirectory);
            var folderName = $"{SanitizeFilePart(_taskProjectName)}_{SanitizeFilePart(_taskThreadName)}";
            var directory = hasProjectTranscript
                ? ProjectWorkspacePersistence.TranscriptDirectory(_activeWorkingDirectory!)
                : Path.Combine(WorkerPaths.Task, folderName);
            Directory.CreateDirectory(directory);

            var path = _taskTranscriptPath ??
                       (hasProjectTranscript
                           ? ProjectWorkspacePersistence.CommandTranscriptPath(
                               _activeWorkingDirectory!,
                               _taskStartedAt)
                           : CreateStandaloneCommandTranscriptPath(
                               directory,
                               _taskStartedAt));

            var lines = new List<string>
            {
                $"Project: {_taskProjectName}",
                $"Thread: {_taskThreadName}",
                $"Started: {_taskStartedAt:O}",
                $"Finished: {DateTimeOffset.Now:O}",
                string.Empty
            };

            foreach (var message in commandMessages)
            {
                lines.Add($"[{message.Timestamp:yyyy-MM-dd HH:mm:ss}] {message.Source}");
                lines.Add(message.Content);
                lines.Add(string.Empty);
            }

            File.WriteAllText(
                path,
                string.Join(Environment.NewLine, lines),
                new UTF8Encoding(false));
            _taskTranscriptPath = path;
            _taskExported = true;
            return path;
        }
        catch
        {
            return null;
        }
    }

    private static string CreateStandaloneCommandTranscriptPath(
        string directory,
        DateTimeOffset startedAt)
    {
        var stem = startedAt.ToString(
            "yyMMdd-HHmmss",
            System.Globalization.CultureInfo.InvariantCulture);
        var candidate = Path.Combine(directory, stem + ".txt");
        if (!File.Exists(candidate))
            return candidate;

        for (var suffix = 2; suffix <= 99; suffix++)
        {
            candidate = Path.Combine(directory, $"{stem}-{suffix:00}.txt");
            if (!File.Exists(candidate))
                return candidate;
        }

        return Path.Combine(
            directory,
            $"{stem}-{Guid.NewGuid():N}"[..(stem.Length + 5)] + ".txt");
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
