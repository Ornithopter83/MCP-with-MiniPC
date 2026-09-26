using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace ProjectHub.Worker;

public partial class MainWindow
{
    public ObservableCollection<UserAttachmentInput> InitialAttachments { get; } = new();
    public ObservableCollection<UserAttachmentInput> FollowupAttachments { get; } = new();

    private void DashboardInputView_PreviewDragOver(object sender, DragEventArgs e)
        => HandleAttachmentDragOver(e);

    private void DashboardFollowupAttachmentView_PreviewDragOver(object sender, DragEventArgs e)
        => HandleAttachmentDragOver(e);

    private void DashboardInputView_PreviewDrop(object sender, DragEventArgs e)
        => HandleAttachmentDrop(e, InitialAttachments);

    private void DashboardFollowupAttachmentView_PreviewDrop(object sender, DragEventArgs e)
        => HandleAttachmentDrop(e, FollowupAttachments);

    private void DashboardTaskInput_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (TryPasteClipboardImage(e, InitialAttachments))
            e.Handled = true;
    }

    private void DashboardFollowupInput_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (TryPasteClipboardImage(e, FollowupAttachments))
            e.Handled = true;
    }

    private void RemoveInitialAttachment_Click(object sender, RoutedEventArgs e)
        => RemovePendingAttachment(sender, InitialAttachments);

    private void RemoveFollowupAttachment_Click(object sender, RoutedEventArgs e)
        => RemovePendingAttachment(sender, FollowupAttachments);

    private static void HandleAttachmentDragOver(DragEventArgs e)
    {
        var paths = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? e.Data.GetData(DataFormats.FileDrop) as string[]
            : null;
        e.Effects = paths is { Length: > 0 } && paths.All(File.Exists)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void HandleAttachmentDrop(
        DragEventArgs e,
        ObservableCollection<UserAttachmentInput> target)
    {
        e.Handled = true;
        if (!e.Data.GetDataPresent(DataFormats.FileDrop) ||
            e.Data.GetData(DataFormats.FileDrop) is not string[] paths ||
            paths.Length == 0)
            return;

        AddAttachmentFiles(paths, target, "DROP");
    }

    private void AddAttachmentFiles(
        IEnumerable<string> paths,
        ObservableCollection<UserAttachmentInput> target,
        string sourceKind)
    {
        var added = 0;
        var failures = new List<string>();

        foreach (var path in paths)
        {
            if (target.Count >= UserAttachmentTransport.MaxFilesPerMessage)
            {
                failures.Add($"최대 {UserAttachmentTransport.MaxFilesPerMessage}개까지 첨부할 수 있습니다.");
                break;
            }

            if (Directory.Exists(path))
            {
                failures.Add($"{Path.GetFileName(path)}: 폴더는 첨부할 수 없습니다.");
                continue;
            }

            try
            {
                var attachment = UserAttachmentTransport.CacheFile(path, sourceKind);
                if (target.Any(existing =>
                        string.Equals(existing.Sha256, attachment.Sha256, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(existing.FileName, attachment.FileName, StringComparison.OrdinalIgnoreCase)))
                {
                    TryDeleteAttachmentCache(attachment.StoredPath);
                    continue;
                }

                target.Add(attachment);
                added++;
            }
            catch (Exception exception)
            {
                failures.Add($"{Path.GetFileName(path)}: {exception.Message}");
            }
        }

        ShowAttachmentFeedback(added, failures);
    }

    private bool TryPasteClipboardImage(
        KeyEventArgs e,
        ObservableCollection<UserAttachmentInput> target)
    {
        if (e.Key != Key.V ||
            (Keyboard.Modifiers & ModifierKeys.Control) == 0)
            return false;

        try
        {
            if (!System.Windows.Clipboard.ContainsImage())
                return false;

            if (target.Count >= UserAttachmentTransport.MaxFilesPerMessage)
            {
                ShowAttachmentFeedback(
                    0,
                    new[] { $"최대 {UserAttachmentTransport.MaxFilesPerMessage}개까지 첨부할 수 있습니다." });
                return true;
            }

            var bitmap = System.Windows.Clipboard.GetImage();
            if (bitmap is null)
                return false;

            WorkerPaths.EnsureCreated();
            var id = Guid.NewGuid().ToString("N");
            var storedPath = Path.Combine(WorkerPaths.Attachments, id + ".png");
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = new FileStream(
                       storedPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None))
            {
                encoder.Save(stream);
            }

            var fileName =
                $"clipboard-{DateTimeOffset.Now:yyMMdd-HHmmss}.png";
            var attachment = UserAttachmentTransport.RegisterCachedFile(
                id,
                fileName,
                storedPath,
                "image/png",
                "CLIPBOARD");

            if (attachment.Size > UserAttachmentTransport.MaxFileBytes)
            {
                TryDeleteAttachmentCache(storedPath);
                throw new InvalidOperationException("클립보드 이미지는 50MB를 초과할 수 없습니다.");
            }

            target.Add(attachment);
            ShowAttachmentFeedback(1, Array.Empty<string>());
            return true;
        }
        catch (Exception exception)
        {
            ShowAttachmentFeedback(
                0,
                new[] { "클립보드 이미지: " + exception.Message });
            return true;
        }
    }

    private void RemovePendingAttachment(
        object sender,
        ObservableCollection<UserAttachmentInput> target)
    {
        if (sender is not System.Windows.Controls.Button button ||
            button.CommandParameter is not string id)
            return;

        var attachment = target.FirstOrDefault(item =>
            string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));
        if (attachment is null)
            return;

        target.Remove(attachment);
        TryDeleteAttachmentCache(attachment.StoredPath);
        UpdateDashboardRunButtonState();
    }

    private void ShowAttachmentFeedback(
        int added,
        IEnumerable<string> failures)
    {
        var errors = failures.Where(value => !string.IsNullOrWhiteSpace(value)).ToArray();
        if (errors.Length > 0)
        {
            DashboardPreflightText.Text =
                (added > 0 ? $"첨부 {added}개 추가 · " : string.Empty) +
                string.Join(" · ", errors.Take(2));
            DashboardPreflightText.Foreground = System.Windows.Media.Brushes.Firebrick;
        }
        else if (added > 0)
        {
            DashboardPreflightText.Text = $"첨부 {added}개 추가됨";
            DashboardPreflightText.Foreground =
                (System.Windows.Media.Brush)FindResource("Muted");
        }

        UpdateDashboardRunButtonState();
    }

    private static void TryDeleteAttachmentCache(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
        }
    }

    private IReadOnlyList<UserAttachmentInput> SnapshotInitialAttachments()
        => InitialAttachments.ToArray();

    private IReadOnlyList<UserAttachmentInput> SnapshotFollowupAttachments()
        => FollowupAttachments.ToArray();

    private void ConsumePendingAttachments(
        IReadOnlyList<UserAttachmentInput>? attachments)
    {
        if (attachments is null || attachments.Count == 0)
            return;

        var ids = attachments
            .Select(item => item.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var attachment in InitialAttachments.Where(item => ids.Contains(item.Id)).ToArray())
            InitialAttachments.Remove(attachment);
        foreach (var attachment in FollowupAttachments.Where(item => ids.Contains(item.Id)).ToArray())
            FollowupAttachments.Remove(attachment);
    }

    private void ClearPendingAttachments(bool deleteCachedFiles)
    {
        foreach (var attachment in InitialAttachments
                     .Concat(FollowupAttachments)
                     .ToArray())
        {
            if (deleteCachedFiles)
                TryDeleteAttachmentCache(attachment.StoredPath);
        }

        InitialAttachments.Clear();
        FollowupAttachments.Clear();
    }

    private static string FormatAttachmentHistory(
        IReadOnlyList<UserAttachmentInput>? attachments)
    {
        if (attachments is null || attachments.Count == 0)
            return "파일 · 해당 없음";

        var names = string.Join(", ", attachments.Take(3).Select(item => item.FileName));
        if (attachments.Count > 3)
            names += $" 외 {attachments.Count - 3}개";
        return $"파일 · {attachments.Count}개 · {names}";
    }

    private static IReadOnlyList<AiInputAttachment> StageUserAttachments(
        IReadOnlyList<UserAttachmentInput>? attachments,
        string workingDirectory,
        string batchId)
        => UserAttachmentTransport.StageForWorkspace(
            attachments,
            workingDirectory,
            batchId);

    private static List<BridgeAttachment> BuildUserWebAttachments(
        IReadOnlyList<UserAttachmentInput>? attachments)
    {
        if (attachments is null || attachments.Count == 0)
            return new List<BridgeAttachment>();

        return attachments
            .Select(UserAttachmentTransport.CreateBridgeAttachment)
            .ToList();
    }
}
