using System.Windows;
using System.Windows.Media;

namespace ProjectHub.Worker;

public partial class MainWindow
{
    private void OnManagedWebRuntimeStatusChanged(ManagedWebRuntimeStatus status)
    {
        Dispatcher.BeginInvoke(new Action(RefreshManagedWebRuntimePresentation));
    }

    private void RefreshManagedWebRuntimePresentation()
    {
        ApplyManagedWebRuntimeStatus(
            _managedWebRuntimeManager?.GetStatus(ManagedWebRole.Hq),
            CoordinatorManagedWebRuntimeStatusText,
            CoordinatorManagedWebShowButton,
            CoordinatorManagedWebHideButton,
            "HQ");
        ApplyManagedWebRuntimeStatus(
            _managedWebRuntimeManager?.GetStatus(ManagedWebRole.Resource),
            ResourceManagedWebRuntimeStatusText,
            ResourceManagedWebShowButton,
            ResourceManagedWebHideButton,
            "RESOURCE");
    }

    private static void ApplyManagedWebRuntimeStatus(
        ManagedWebRuntimeStatus? status,
        System.Windows.Controls.TextBlock statusText,
        System.Windows.Controls.Button showButton,
        System.Windows.Controls.Button hideButton,
        string role)
    {
        if (status is null)
        {
            statusText.Text = $"{role} 브라우저 관리 사용 안 함";
            statusText.Foreground = Brushes.DarkOrange;
            showButton.IsEnabled = false;
            hideButton.IsEnabled = false;
            return;
        }

        showButton.IsEnabled = true;
        hideButton.IsEnabled = status.Running && !status.Hidden;

        if (!string.IsNullOrWhiteSpace(status.Error))
        {
            statusText.Text = $"{role} 브라우저 오류 · {status.Error}";
            statusText.Foreground = Brushes.OrangeRed;
            statusText.ToolTip = status.Error;
            return;
        }

        statusText.ToolTip =
            $"프로필: {status.ProfilePath}" +
            (string.IsNullOrWhiteSpace(status.ExecutablePath)
                ? string.Empty
                : Environment.NewLine + $"실행 파일: {status.ExecutablePath}");

        if (!status.Running)
        {
            statusText.Text = $"{role} 브라우저 중지됨";
            statusText.Foreground = Brushes.DarkOrange;
            return;
        }

        statusText.Text = status.Hidden
            ? $"{role} 브라우저 숨김 실행 중"
            : $"{role} 브라우저 표시 중 · 로그인/대화 선택 가능";
        statusText.Foreground = status.Hidden
            ? Brushes.ForestGreen
            : (Brush)new BrushConverter().ConvertFromString("#1477E8")!;
    }

    private async Task EnsureManagedWebRuntimesStartedAsync()
    {
        if (_managedWebRuntimeManager is null)
            return;

        CoordinatorManagedWebRuntimeStatusText.Text = "HQ 브라우저 런타임 준비 중…";
        ResourceManagedWebRuntimeStatusText.Text = "RESOURCE 브라우저 런타임 준비 중…";

        var hq = await _managedWebRuntimeManager.StartHiddenAsync(
            ManagedWebRole.Hq,
            _bridgeServer?.GetRoleConversationId("HQ"));
        var resource = await _managedWebRuntimeManager.StartHiddenAsync(
            ManagedWebRole.Resource,
            _bridgeServer?.GetRoleConversationId("RESOURCE"));

        RefreshManagedWebRuntimePresentation();
        ReportManagedWebStartFailure("HQ", hq);
        ReportManagedWebStartFailure("RESOURCE", resource);
    }

    private async void ShowManagedHqWeb_Click(object sender, RoutedEventArgs e)
        => await ShowManagedWebAsync(ManagedWebRole.Hq, "HQ");

    private async void HideManagedHqWeb_Click(object sender, RoutedEventArgs e)
        => await HideManagedWebAsync(ManagedWebRole.Hq, "HQ");

    private async void ShowManagedResourceWeb_Click(object sender, RoutedEventArgs e)
        => await ShowManagedWebAsync(ManagedWebRole.Resource, "RESOURCE");

    private async void HideManagedResourceWeb_Click(object sender, RoutedEventArgs e)
        => await HideManagedWebAsync(ManagedWebRole.Resource, "RESOURCE");

    private async Task ShowManagedWebAsync(ManagedWebRole role, string bindingRole)
    {
        if (_managedWebRuntimeManager is null)
            return;

        var conversationId = _bridgeServer?.GetRoleConversationId(bindingRole);
        var status = await _managedWebRuntimeManager.ShowForLoginAsync(role, conversationId);
        RefreshManagedWebRuntimePresentation();
        ReportManagedWebStartFailure(bindingRole, status);
    }

    private async Task HideManagedWebAsync(ManagedWebRole role, string bindingRole)
    {
        if (_managedWebRuntimeManager is null)
            return;

        var conversationId = _bridgeServer?.GetRoleConversationId(bindingRole);
        var status = await _managedWebRuntimeManager.RestartHiddenAsync(role, conversationId);
        RefreshManagedWebRuntimePresentation();
        ReportManagedWebStartFailure(bindingRole, status);
    }

    private void ReportManagedWebStartFailure(string role, ManagedWebRuntimeStatus status)
    {
        if (string.IsNullOrWhiteSpace(status.Error))
            return;

        AddTaskMessage(
            "WEB RUNTIME",
            $"{role} 브라우저 시작 실패: {status.Error}",
            status: "ERROR",
            includeHistory: false);
    }
}
