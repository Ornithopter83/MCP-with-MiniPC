using System.Windows;
using System.Windows.Controls;

namespace ProjectHub.Worker;

public partial class MainWindow
{
    private void SetFlowState(bool codexActive, bool workerActive, bool webActive, TaskStage? explicitStage = null)
    {
        var (left, right) = ResolveFlowPair();
        SetFlowNode(left, isActive: IsNodeActive(left, codexActive, workerActive, webActive), isLeft: true);
        SetFlowNode(right, isActive: IsNodeActive(right, codexActive, workerActive, webActive), isLeft: false);
        _pairArrowActive = codexActive || workerActive || webActive;
        _flowFrame = 0;
        UpdateArrowAnimation();
        var running = codexActive || workerActive || webActive || (!_userCanceledTask && _activeTaskCts is not null);
        _currentTaskStage = !running ? TaskStage.Idle
            : explicitStage ?? (_activeCoordinatorFirst && codexActive ? TaskStage.Coordinator
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
        var initialInputIdle = idle && _dashboardBodyMode == DashboardBodyMode.NewTaskInput && _activeTaskCts is null;
        SetPipelineCard(PipelineCoordinatorCard, PipelineCoordinatorTitle, CoordinatorStageCircle, CoordinatorStageIcon, _coordinatorStageIconAsset, TaskStage.Coordinator, RoleVisuals["Coordinator"], false, initialInputIdle);
        SetPipelineCard(PipelineImplementerCard, PipelineImplementerTitle, ImplementerStageCircle, ImplementerStageIcon, _implementerStageIconAsset, TaskStage.Implementer, RoleVisuals["Implementer"], false, initialInputIdle);

        var resourceDisabled = UnityDesignCardActivationPolicy.IsDisabled(
            _activeCoordinatorFirst,
            _currentMilestoneResourceReserved,
            _currentTaskStage == TaskStage.Resource);
        ResourceStageModelText.Text = resourceDisabled
            ? "미예약"
            : AiProviderCatalog.FormatModel(
                _targetSettings.EffectiveDesign.Provider,
                _targetSettings.EffectiveDesign.Model);
        SetPipelineCard(
            PipelineResourceCard,
            PipelineResourceTitle,
            ResourceStageCircle,
            ResourceStageIcon,
            ProviderVisualCatalog.Resolve(_targetSettings.EffectiveDesign.Provider).ColorAsset,
            TaskStage.Resource,
            RoleVisuals["Resource"],
            resourceDisabled,
            initialInputIdle);

        SetPipelineCard(PipelineHighLevelCard, PipelineHighLevelTitle, HighLevelStageCircle, HighLevelStageIcon, _highLevelStageIconAsset, TaskStage.HighLevel, RoleVisuals["HighLevel"], false, initialInputIdle);
        ManagerStageModelText.Text = AiProviderCatalog.FormatModel(
            _targetSettings.EffectiveImplementer.Provider,
            _targetSettings.EffectiveImplementer.Model);
        SetPipelineCard(PipelineManagerCard, PipelineManagerTitle, ManagerStageCircle, ManagerStageIcon, _managerStageIconAsset, TaskStage.Manager, RoleVisuals["Manager"], false, initialInputIdle);
    }

    private void SetPipelineCard(Border card, TextBlock title, Border iconCircle, System.Windows.Controls.Image icon, string iconAsset, TaskStage stage, RoleVisualPalette palette, bool disabled, bool initialInputIdle)
    {
        // UnityWorker's yellow #0 card is DESIGN, not the retired Web RESOURCE
        // sidecar. A stale resource queue callback must never keep it active.
        var current = _directWorkRunning
            ? !disabled && stage == TaskStage.Implementer
            : !disabled && (stage == TaskStage.Resource
                ? UnityDesignCardActivationPolicy.ShouldHighlight(
                    _currentTaskStage == TaskStage.Resource)
                : _currentTaskStage == stage);
        SetPipelineStageAnimation(stage, current);
        var visual = PipelineCardVisualPolicy.Resolve(initialInputIdle, current, disabled);
        var colored = visual.IsColored;
        SetColor(card, colored ? palette.Background : "#B8C8DA");
        SetColor(iconCircle, colored ? palette.IconBackground : "#526477");
        title.Foreground = colored ? new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(palette.Foreground)) : System.Windows.Media.Brushes.White;
        var selectedName = colored
            ? iconAsset
            : ProviderVisualCatalog.ResolveGrayAsset(iconAsset);
        icon.Source = LoadProviderAsset(selectedName);
        if (card == PipelineCoordinatorCard) CoordinatorStageModelText.Foreground = colored ? new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(palette.Foreground)) : System.Windows.Media.Brushes.White;
        else if (card == PipelineResourceCard) ResourceStageModelText.Foreground = colored ? new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(palette.Foreground)) : System.Windows.Media.Brushes.White;
        else if (card == PipelineHighLevelCard) HighLevelStageModelText.Foreground = colored ? new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(palette.Foreground)) : System.Windows.Media.Brushes.White;
        else if (card == PipelineManagerCard) ManagerStageModelText.Foreground = colored ? new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(palette.Foreground)) : System.Windows.Media.Brushes.White;
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
            TaskStage.HighLevel => (PipelineHighLevelActiveBase, PipelineHighLevelActiveOrbit),
            TaskStage.Manager => (PipelineManagerActiveBase, PipelineManagerActiveOrbit),
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
        _ => (FlowNode.Codex, FlowNode.Worker)
    };

    private bool IsNodeActive(FlowNode node, bool codexActive, bool workerActive, bool webActive) => node switch
    {
        FlowNode.Codex => codexActive,
        FlowNode.Worker => workerActive,
        FlowNode.Web => webActive,
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
        var label = isLeft ? FlowLeftLabel : FlowRightLabel;
        var stage = isLeft ? FlowLeftStage : FlowRightStage;
        SetNodeIcon(codexGray, codexColor, node == FlowNode.Codex, isActive);
        SetNodeIcon(workerGray, workerColor, node == FlowNode.Worker, isActive);
        SetNodeIcon(webGray, webColor, node == FlowNode.Web, isActive);
        var (defaultName, brush) = node switch
        {
            FlowNode.Codex => ("CODEX", System.Windows.Media.Brushes.MidnightBlue),
            FlowNode.Worker => ("WORKER", System.Windows.Media.Brushes.SeaGreen),
            FlowNode.Web => ("GPT WEB", System.Windows.Media.Brushes.RoyalBlue),
            _ => ("WORKER", System.Windows.Media.Brushes.SeaGreen)
        };
        var name = _activeCoordinatorFirst && node == FlowNode.Codex ? "SOL · 관제" :
            _activeCoordinatorFirst && node == FlowNode.Worker ? "LUNA · 작업" : defaultName;
        background.Visibility = Visibility.Visible;
        background.Background = isActive ? brush : System.Windows.Media.Brushes.SlateGray;
        label.Text = name;
        label.Foreground = isActive ? brush : System.Windows.Media.Brushes.SlateGray;
        stage.Text = isActive ? "진행 중" : "대기 중";
        stage.Foreground = isActive ? brush : System.Windows.Media.Brushes.SlateGray;
    }

    private static void SetNodeIcon(System.Windows.Controls.Image gray, System.Windows.Controls.Image color, bool selected, bool active)
    {
        gray.Visibility = selected && !active ? Visibility.Visible : Visibility.Collapsed;
        color.Visibility = selected && active ? Visibility.Visible : Visibility.Collapsed;
    }
}
