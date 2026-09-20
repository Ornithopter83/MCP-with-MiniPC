using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace ProjectHub.Worker;

public partial class MainWindow : Window
{
    private readonly Forms.NotifyIcon _trayIcon;
    private readonly CodexCliRunner _codexRunner = new();
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
    private DateTimeOffset _lastActivityAt;
    private bool _jobTimedOut;
    private static readonly TimeSpan JobInactivityTimeout = TimeSpan.FromMinutes(30);
    private string? _lastWebTaskId;
    private CodexUsage _commandUsage = CodexUsage.Empty;
    private sealed record TaskMessage(DateTimeOffset Timestamp, string Source, string Content);
    private readonly List<TaskMessage> _taskMessages = new();
    private DateTimeOffset _taskStartedAt;
    private string _taskProjectName = "UnknownProject";
    private string _taskThreadName = "NewThread";
    private bool _taskExported;
    private readonly DispatcherTimer _flowTimer = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private readonly DispatcherTimer _connectionTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly DispatcherTimer _jobWatchdogTimer = new() { Interval = TimeSpan.FromSeconds(10) };
    private int _flowFrame;
    private bool _codexArrowActive;
    private bool _webArrowActive;
    private bool _allowClose;
    private const string Placeholder = "CLI에 즉시 전달할 작업 지시...";
    private const string WebInstructionPlaceholder = "CLI 답변 뒤에 붙여 GPT Web에 전달할 지침...";
    private readonly HttpClient _connectionClient = new() { Timeout = TimeSpan.FromSeconds(2) };
    private BridgeServer? _bridgeServer;
    private bool _codexAuthenticated;
    private bool _serverOnline;
    private List<CodexProjectOption> _codexProjects = new();
    private bool _loadingCodexSelections;

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
        if (bridgeServer is not null) bridgeServer.TaskChanged += OnBridgeTaskChanged;
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
        LoadCodexSelections();
        Loaded += async (_, _) => await RefreshConnectionChecksAsync();
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
        _allowClose = true;
        ((App)System.Windows.Application.Current).RequestShutdown();
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
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
        CodexIconBackground.Background = codexActive ? System.Windows.Media.Brushes.MidnightBlue : System.Windows.Media.Brushes.SlateGray;
        WorkerIconBackground.Background = workerActive ? System.Windows.Media.Brushes.SeaGreen : System.Windows.Media.Brushes.SlateGray;
        WebIconBackground.Background = webActive ? System.Windows.Media.Brushes.RoyalBlue : System.Windows.Media.Brushes.SlateGray;
        CodexLabelText.Foreground = codexActive ? System.Windows.Media.Brushes.MidnightBlue : System.Windows.Media.Brushes.SlateGray;
        WorkerLabelText.Foreground = workerActive ? System.Windows.Media.Brushes.SeaGreen : System.Windows.Media.Brushes.SlateGray;
        WebLabelText.Foreground = webActive ? System.Windows.Media.Brushes.RoyalBlue : System.Windows.Media.Brushes.SlateGray;
        CodexStageText.Foreground = codexActive ? System.Windows.Media.Brushes.MidnightBlue : System.Windows.Media.Brushes.SlateGray;
        WorkerStageText.Foreground = workerActive ? System.Windows.Media.Brushes.SeaGreen : System.Windows.Media.Brushes.SlateGray;
        WebStageText.Foreground = webActive ? System.Windows.Media.Brushes.RoyalBlue : System.Windows.Media.Brushes.SlateGray;
        CodexInactiveIcon.Visibility = codexActive ? Visibility.Collapsed : Visibility.Visible;
        CodexStageText.Text = codexActive ? "실행 중" : "대기 중";
        WorkerStageText.Text = workerActive ? (webActive ? "요청 전달 중" : "결과 처리 중") : "대기 중";
        WebStageText.Text = webActive ? "응답 생성 중" : "대기 중";
        CodexActiveIcon.Visibility = codexActive ? Visibility.Visible : Visibility.Collapsed;
        WorkerInactiveIcon.Visibility = workerActive ? Visibility.Collapsed : Visibility.Visible;
        WorkerActiveIcon.Visibility = workerActive ? Visibility.Visible : Visibility.Collapsed;
        WebInactiveIcon.Visibility = webActive ? Visibility.Collapsed : Visibility.Visible;
        WebActiveIcon.Visibility = webActive ? Visibility.Visible : Visibility.Collapsed;
        _codexArrowActive = codexActive;
        _webArrowActive = webActive;
        _flowFrame = 0;
        UpdateArrowAnimation();
    }

    private void UpdateArrowAnimation()
    {
        var activeIndex = _flowFrame++ % 5;
        var opacities = new[] { 1.0, 0.32, 0.32 };
        SetArrowFrame(new[] { CodexArrow1, CodexArrow2, CodexArrow3 }, _codexArrowActive, activeIndex, opacities);
        SetArrowFrame(new[] { WebArrow1, WebArrow2, WebArrow3 }, _webArrowActive, activeIndex, opacities);
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
        if (_activeTaskCts is not null)
        {
            _activeTaskCts.Cancel();
            return;
        }
        if (_awaitingWebResult)
        {
            _bridgeServer?.CancelActiveTask();
            ResetTaskState();
            return;
        }
        await RefreshConnectionChecksAsync();
        if (!_codexAuthenticated || !_serverOnline || _bridgeServer is null || !_bridgeServer.WebConnected)
        {
            TaskDirection.Text = "PREFLIGHT";
            TaskTitle.Text = "연결 상태 확인 필요";
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
        var model = GetSelectedContent(ModelCombo, "GPT-5.6 Luna");
        var reasoning = GetSelectedContent(ReasoningCombo, "Medium").ToLowerInvariant();
        var cliModel = ToCliModel(model);
        var selectedThread = CodexThreadCombo.SelectedItem as CodexThreadOption;
        StartTaskTranscript(selectedThread, cliPrompt, webInstruction);
        var workingDirectory = string.IsNullOrWhiteSpace(selectedThread?.ProjectPath) ? AppContext.BaseDirectory : selectedThread.ProjectPath;
        var sessionId = string.IsNullOrWhiteSpace(selectedThread?.SessionId) ? null : selectedThread.SessionId;
        _activePrompt = cliPrompt;
        _activeWebInstruction = webInstruction;
        _actionProtocolEnabled = true;
        _jobTimedOut = false;
        _lastActivityAt = DateTimeOffset.UtcNow;
        _lastWebTaskId = null;
        _activeWorkingDirectory = workingDirectory;
        _activeSessionId = sessionId;
        _activeCliModel = cliModel;
        _activeReasoning = reasoning;
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
            var result = await _codexRunner.RunAsync(cliPrompt, cliModel, reasoning, workingDirectory, sessionId, cts.Token);
            _lastActivityAt = DateTimeOffset.UtcNow;
            _lastCodexResult = result;
            AddTaskMessage("CODEX", string.IsNullOrWhiteSpace(result.FinalMessage) ? result.StandardOutput : result.FinalMessage);
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
                var webPrompt = BuildWebPrompt(result, webInstruction, includeControlInstructions: true, includeWebInstruction: true);
                var attachments = BuildWebAttachments(_bridgeServer, result.Files);
                AddTaskMessage("WORKER -> GPT WEB", webPrompt);
                var task = _bridgeServer.CreateTaskForLatestBinding(webPrompt, attachments);
                if (task is null)
                {
                    TaskTitle.Text = "GPT Web 대화 연결 필요";
                    ResultBody.Text += Environment.NewLine + Environment.NewLine + "연결된 GPT Web 대화가 없어 전달하지 못했습니다.";
                    SetFlowState(false, false, false);
                }
                else
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
            if (_jobTimedOut) return;
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
            if (!_awaitingWebResult) RunButton.Content = "▶  Run Task";
        }
    }

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
    private async Task RefreshConnectionChecksAsync()
    {
        var wasCodexAuthenticated = _codexAuthenticated;
        _codexAuthenticated = await CheckCodexAuthenticationAsync();
        if (_codexAuthenticated && !wasCodexAuthenticated) LoadCodexSelections();
        _serverOnline = await CheckServerAsync();
        var webOnline = _bridgeServer?.WebConnected == true;
        SetConnectionStatus(ProjectStatusText, _codexAuthenticated ? "READY" : "LOGIN NEEDED", _codexAuthenticated, ProjectStatusDot);
        SetConnectionStatus(WebStatusText, webOnline ? "READY" : "WAITING", webOnline, waiting: !webOnline, indicator: WebStatusDot);
        WebDescriptionText.Text = webOnline && !string.IsNullOrWhiteSpace(_bridgeServer?.WebConversationTitle) ? _bridgeServer.WebConversationTitle : "MCP 프로젝트 진척도 확인";
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
            var serverBaseUrl = Environment.GetEnvironmentVariable("PROJECTHUB_AGENT_SERVER_BASE_URL");
            if (string.IsNullOrWhiteSpace(serverBaseUrl)) serverBaseUrl = "https://projecthub.ornithopter.bid";
            using var response = await _connectionClient.GetAsync(serverBaseUrl.TrimEnd('/') + "/api/status");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
    private static string GetSelectedContent(System.Windows.Controls.ComboBox combo, string fallback)
        => (combo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? fallback;

    private static string ToCliModel(string model)
        => model.Trim().ToLowerInvariant().Replace(" ", "-");

    private void OnBridgeTaskChanged(BridgeTask task)
    {
        Dispatcher.BeginInvoke(new Action(() => HandleBridgeTaskChanged(task)));
    }

    private async void HandleBridgeTaskChanged(BridgeTask task)
    {
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
        if (_jobTimedOut) return;
        _lastActivityAt = DateTimeOffset.UtcNow;
        if (_actionProtocolEnabled && _lastWebTaskId == task.Id) return;
        _lastWebTaskId = task.Id;
        _lastWebTask = task;
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
            var result = await _codexRunner.RunAsync(followupPrompt, model, reasoning, workingDirectory, _activeSessionId, cts.Token);
            _lastActivityAt = DateTimeOffset.UtcNow;
            _activeSessionId = result.SessionId ?? _activeSessionId;
            _lastCodexResult = result;
            AddTaskMessage("CODEX", string.IsNullOrWhiteSpace(result.FinalMessage) ? result.StandardOutput : result.FinalMessage);
            CodexThreadArchive.Save(result, followupPrompt, workingDirectory);
            _commandUsage = _commandUsage.Add(result.Usage);
            UpdateUsage(_commandUsage);
            ResultTitle.Text = result.ExitCode == 0 ? $"Codex 중간 결과 · {model}" : $"Codex 후속 처리 실패 · exit {result.ExitCode}";
            ResultBody.Text = BuildRoundtripResultBody(webResponse, result);
            ActivateResultTab(web: false);

            if (result.ExitCode == 0 && (_actionProtocolEnabled || ShouldContinueRoundtrip(result)) && _bridgeServer is not null)
            {
                var nextPrompt = BuildWebPrompt(result, _activeWebInstruction, includeControlInstructions: true, includeWebInstruction: false);
                var nextAttachments = BuildWebAttachments(_bridgeServer, result.Files);
                AddTaskMessage("WORKER -> GPT WEB", nextPrompt);
                var nextTask = _bridgeServer.CreateTaskForLatestBinding(nextPrompt, nextAttachments);
                if (nextTask is not null)
                {
                    _awaitingWebResult = true;
                    RunButton.Content = "■  Cancel";
                    TaskDirection.Text = "WORKER → GPT WEB";
                    TaskTitle.Text = "Codex 결과를 GPT Web에 재전달하는 중";
                    SetFlowState(false, true, true);
                    return;
                }
            }

            _awaitingWebResult = false;
            TaskDirection.Text = "GPT WEB → CODEX";
            TaskTitle.Text = result.ExitCode == 0 ? "Worker 최종 처리 완료" : "Codex 후속 처리 실패";
            SetFlowState(false, false, false);
        }
        catch (OperationCanceledException)
        {
            if (_jobTimedOut) return;
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
            if (!_awaitingWebResult) RunButton.Content = "▶  Run Task";
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

        var actionPattern = new System.Text.RegularExpressions.Regex(@"^\[ACTION=(BEGIN|CONTINUE|PAUSE|END)\]$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var actionLines = nonEmpty.Where(item => actionPattern.IsMatch(item.Item1)).ToList();
        if (strict && actionLines.Count != 1)
            return new(WebActionKind.ProtocolError, string.Empty, actionLines.Count == 0 ? "첫 유효행에 유효한 ACTION이 없습니다." : "Web 응답에 ACTION이 여러 개 있습니다.");

        if (strict && actionLines[0].index != nonEmpty[0].index)
            return new(WebActionKind.ProtocolError, string.Empty, "ACTION은 첫 번째 유효행이어야 합니다.");

        if (!strict && actionLines.Count == 0)
            return new(WebActionKind.None, string.Empty);

        var actionLine = actionLines[0];
        var kind = actionLine.Item1.ToUpperInvariant() switch
        {
            "[ACTION=BEGIN]" => WebActionKind.Begin,
            "[ACTION=CONTINUE]" => WebActionKind.Continue,
            "[ACTION=PAUSE]" => WebActionKind.Pause,
            "[ACTION=END]" => WebActionKind.End,
            _ => WebActionKind.ProtocolError
        };
        var body = string.Join(Environment.NewLine, lines.Skip(actionLine.index + 1)).Trim();
        if ((kind is WebActionKind.Begin or WebActionKind.Continue) && string.IsNullOrWhiteSpace(body))
            return new(WebActionKind.ProtocolError, string.Empty, "BEGIN/CONTINUE 본문이 비어 있습니다.");

        return new(kind, body);
    }
    private static string BuildWebPrompt(
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
        return control + output + instruction;
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

    private void AddTaskMessage(string source, string? content)
    {
        if (string.IsNullOrWhiteSpace(content)) return;
        _taskMessages.Add(new TaskMessage(DateTimeOffset.Now, source, content.Trim()));
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

    private static string BuildRoundtripResultBody(string webResponse, CodexCliResult result)
    {
        return "GPT Web 응답:" + Environment.NewLine + Summarize(webResponse, "응답 내용이 없습니다.") + Environment.NewLine + Environment.NewLine + "Codex 후속 결과:" + Environment.NewLine + BuildResultBody(result);
    }

    private static string BuildResultBody(CodexCliResult result)
    {
        var message = string.IsNullOrWhiteSpace(result.FinalMessage) ? result.StandardOutput : result.FinalMessage;
        var detail = Summarize(message, "Codex가 결과를 반환하지 않았습니다.");
        return $"Model: {result.Model} · Reasoning: {result.Reasoning}{Environment.NewLine}Exit code: {result.ExitCode}{Environment.NewLine}{detail}";
    }

    private static string Summarize(string? value, string fallback)
    {
        var text = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        return text.Length <= 4000 ? text : text[..4000] + Environment.NewLine + "…";
    }

    private void ActivateResultTab(bool web)
    {
        var active = FindResource("PaleBlue") as System.Windows.Media.Brush;
        var inactive = System.Windows.Media.Brushes.White;
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
        if (_lastCodexResult is null)
        {
            ResultTitle.Text = "Codex CLI 대기 중";
            ResultBody.Text = "Run Task를 실행하면 Codex CLI 결과가 이 영역에 표시됩니다.";
            ActivateResultTab(web: false);
            return;
        }
        ResultTitle.Text = $"Codex {( _lastCodexResult.ExitCode == 0 ? "PASS" : "FAIL" )} · {_lastCodexResult.Model}";
        ResultBody.Text = BuildResultBody(_lastCodexResult);
        UpdateUsage(_lastCodexResult.Usage);
        ActivateResultTab(web: false);
    }

    private void WebTab_Click(object sender, RoutedEventArgs e)
    {
        ActivateResultTab(web: true);
        if (_lastWebTask is not null)
        {
            ResultTitle.Text = _lastWebTask.Status == "COMPLETED" ? "GPT Web PASS" : "GPT Web FAIL";
            ResultBody.Text = _lastWebTask.Result ?? "응답 내용이 없습니다.";
        }
        else
        {
            ResultTitle.Text = "GPT Web 대기 중";
            ResultBody.Text = "GPT Web 결과가 도착하면 이 영역에 표시됩니다.";
        }
    }
}

