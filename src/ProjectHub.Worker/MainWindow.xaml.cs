using System.ComponentModel;
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
    private readonly DispatcherTimer _flowTimer = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private int _flowFrame;
    private bool _codexArrowActive;
    private bool _webArrowActive;
    private bool _allowClose;
    private const string Placeholder = "작업 지시를 입력하세요...";

    public MainWindow()
    {
        InitializeComponent();
        _flowTimer.Tick += (_, _) => UpdateArrowAnimation();
        _flowTimer.Start();
        SetFlowState(codexActive: false, workerActive: true, webActive: true);
        _trayIcon = new Forms.NotifyIcon
        {
            Text = "ProjectHub Worker · MCP-with-MiniPC",
            Icon = new Drawing.Icon(System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "worker-icon.ico")),
            Visible = true,
            ContextMenuStrip = new Forms.ContextMenuStrip()
        };
        _trayIcon.ContextMenuStrip.Items.Add("Open", null, (_, _) => ShowFromTray());
        _trayIcon.ContextMenuStrip.Items.Add("Pause", null, (_, _) => { });
        _trayIcon.ContextMenuStrip.Items.Add("Exit", null, (_, _) => ExitWorker());
        _trayIcon.DoubleClick += (_, _) => ShowFromTray();
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!_allowClose && (((App)System.Windows.Application.Current).ShutdownRequested || Dispatcher.HasShutdownStarted))
            _allowClose = true;

        if (_allowClose)
        {
            _activeTaskCts?.Cancel();
            _flowTimer.Stop();
            _trayIcon.Visible = false;
            _trayIcon.ContextMenuStrip?.Dispose();
            _trayIcon.Dispose();
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
        if (CommandInput.Text == Placeholder)
        {
            CommandInput.Text = string.Empty;
            CommandInput.Foreground = FindResource("Ink") as System.Windows.Media.Brush;
        }
    }

    private void CommandInput_LostFocus(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(CommandInput.Text))
        {
            CommandInput.Text = Placeholder;
            CommandInput.Foreground = FindResource("Muted") as System.Windows.Media.Brush;
        }
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        if (_activeTaskCts is not null) return;
        CommandInput.Text = Placeholder;
        CommandInput.Foreground = FindResource("Muted") as System.Windows.Media.Brush;
    }

    private void SetFlowState(bool codexActive, bool workerActive, bool webActive)
    {
        CodexInactiveIcon.Visibility = codexActive ? Visibility.Collapsed : Visibility.Visible;
        CodexActiveIcon.Visibility = codexActive ? Visibility.Visible : Visibility.Collapsed;
        WorkerInactiveIcon.Visibility = workerActive ? Visibility.Collapsed : Visibility.Visible;
        WorkerActiveIcon.Visibility = workerActive ? Visibility.Visible : Visibility.Collapsed;
        WebInactiveIcon.Visibility = webActive ? Visibility.Collapsed : Visibility.Visible;
        WebActiveIcon.Visibility = webActive ? Visibility.Visible : Visibility.Collapsed;
        _codexArrowActive = codexActive || (workerActive && !webActive);
        _webArrowActive = workerActive && webActive;
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

        if (string.IsNullOrWhiteSpace(CommandInput.Text) || CommandInput.Text == Placeholder)
            return;

        var prompt = CommandInput.Text.Trim();
        var model = GetSelectedContent(ModelCombo, "GPT-5.6 Terra");
        var reasoning = GetSelectedContent(ReasoningCombo, "Medium").ToLowerInvariant();
        var cliModel = ToCliModel(model);
        var workingDirectory = Environment.CurrentDirectory;
        var cts = new CancellationTokenSource();
        _activeTaskCts = cts;
        RunButton.IsEnabled = true;
        RunButton.Content = "■  Cancel";
        TaskDirection.Text = "CODEX → WORKER";
        TaskTitle.Text = "Codex 작업 실행 중";
        TaskDetail.Text = $"{cliModel} · {reasoning} · Codex CLI 실행 중...";
        SetFlowState(codexActive: true, workerActive: false, webActive: false);

        try
        {
            var result = await _codexRunner.RunAsync(prompt, cliModel, reasoning, workingDirectory, cts.Token);
            _lastCodexResult = result;
            CodexConversationText.Text = result.ConversationTitle;
            SetFlowState(codexActive: false, workerActive: true, webActive: false);
            TaskDirection.Text = "CODEX → WORKER";
            TaskTitle.Text = result.ExitCode == 0 ? "Codex 결과 수신 완료" : "Codex 실행 실패";
            TaskDetail.Text = result.ExitCode == 0 ? "Codex 결과를 Worker가 수신했습니다." : Summarize(result.StandardError, "Codex CLI가 오류를 반환했습니다.");
            ResultTitle.Text = result.ExitCode == 0 ? $"Codex PASS · {cliModel}" : $"Codex FAIL · exit {result.ExitCode}";
            ResultBody.Text = BuildResultBody(result);
        }
        catch (OperationCanceledException)
        {
            TaskTitle.Text = "Codex 실행 취소";
            TaskDetail.Text = "사용자가 실행을 취소했습니다.";
            ResultTitle.Text = "Codex CANCELED";
            ResultBody.Text = "Codex CLI 실행이 취소되었습니다.";
            SetFlowState(codexActive: false, workerActive: false, webActive: false);
        }
        catch (Exception ex)
        {
            TaskTitle.Text = "Codex 실행을 시작하지 못했습니다";
            TaskDetail.Text = ex.Message;
            ResultTitle.Text = "Codex ERROR";
            ResultBody.Text = ex.ToString();
            SetFlowState(codexActive: false, workerActive: false, webActive: false);
        }
        finally
        {
            _activeTaskCts.Dispose();
            _activeTaskCts = null;
            RunButton.Content = "▶  Run Task";
        }
    }

    private static string GetSelectedContent(System.Windows.Controls.ComboBox combo, string fallback)
        => (combo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? fallback;

    private static string ToCliModel(string model)
        => model.Trim().ToLowerInvariant().Replace(" ", "-");

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

    private void CodexTab_Click(object sender, RoutedEventArgs e)
    {
        if (_lastCodexResult is null)
        {
            ResultTitle.Text = "Codex CLI 대기 중";
            ResultBody.Text = "Run Task를 실행하면 Codex CLI 결과가 이 영역에 표시됩니다.";
            return;
        }
        ResultTitle.Text = $"Codex {( _lastCodexResult.ExitCode == 0 ? "PASS" : "FAIL" )} · {_lastCodexResult.Model}";
        ResultBody.Text = BuildResultBody(_lastCodexResult);
    }

    private void WebTab_Click(object sender, RoutedEventArgs e)
    {
        ResultTitle.Text = "REVISE · GPT Web 검토 결과";
        ResultBody.Text = "보호영역 검증이 필요합니다. GPT Web 결과는 Worker bridge 연결 후 이 영역에 표시됩니다.";
    }
}

