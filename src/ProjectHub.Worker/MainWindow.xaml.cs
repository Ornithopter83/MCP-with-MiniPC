using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace ProjectHub.Worker;

public partial class MainWindow : Window
{
    private readonly Forms.NotifyIcon _trayIcon;
    private bool _allowClose;
    private const string Placeholder = "작업 지시를 입력하세요...";

    public MainWindow()
    {
        InitializeComponent();
        _trayIcon = new Forms.NotifyIcon
        {
            Text = "ProjectHub Worker",
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
        if (_allowClose)
        {
            _trayIcon.Visible = false;
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
        Close();
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
    }
    private async void RunTask_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(CommandInput.Text) || CommandInput.Text == Placeholder)
        {
            return;
        }

        RunButton.IsEnabled = false;
        RunButton.Content = "…  Mock 실행 중";
        TaskDirection.Text = "WORKER → GPT WEB";
        TaskTitle.Text = "작업 검토 요청을 준비하는 중";
        TaskDetail.Text = "Worker mock task 실행 중...";
        await Task.Delay(350);
        SetFlowState(codexActive: false, workerActive: true, webActive: false);
        await Task.Delay(350);
        SetFlowState(codexActive: false, workerActive: true, webActive: true);
        await Task.Delay(400);
        TaskTitle.Text = "작업 검토 요청 완료";
        TaskDetail.Text = "사용자 확인 필요";
        ResultTitle.Text = "Mock task 완료 · Worker-A UI PASS";
        ResultBody.Text = "현재는 화면 흐름만 검증했습니다. Codex CLI와 GPT Web bridge는 다음 Worker 단계에서 연결됩니다.";
        RunButton.IsEnabled = true;
        RunButton.Content = "▶  Run Task";
    }

    private void CodexTab_Click(object sender, RoutedEventArgs e)
    {
        ResultTitle.Text = "Build PASS · Test PASS · Commit a5177f8";
        ResultBody.Text = "Force Restore 관련 코드가 수정되었으며, 빌드와 테스트가 모두 성공했습니다. 변경 사항이 커밋되었고 원격 저장소에 푸시되었습니다.";
    }

    private void WebTab_Click(object sender, RoutedEventArgs e)
    {
        ResultTitle.Text = "REVISE · GPT Web 검토 결과";
        ResultBody.Text = "보호영역 검증이 필요합니다. GPT Web 결과는 Worker bridge 연결 후 이 영역에 표시됩니다.";
    }
}







