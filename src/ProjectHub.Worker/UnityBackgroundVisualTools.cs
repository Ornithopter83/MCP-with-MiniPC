using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace ProjectHub.Worker;

internal sealed record UnityBackgroundVisualPlan(
    string Directory, string BeforeScript, string AfterScript,
    string InputTemplate, string BeforePng, string AfterPng,
    string WorkerScript, string WorkerPng, string ProjectRoot)
{
    internal string AgentGuidance =>
        "Unity 내부 캡처 보조 파일 (연결된 Editor의 eval_file 지원 확인 후 사용):\n" +
        "변경 전: " + BeforeScript + "\n" +
        "변경 후: " + AfterScript + "\n" +
        "입력 템플릿: " + InputTemplate + " (실제 UI 객체 지정 필요)\n" +
        "프로젝트: " + ProjectRoot + "\n" +
        "OS 화면·입력 조작은 하지 않는다. 내부 렌더는 Game View와 픽셀 동일성을 " +
        "입증하지 않으며, CLI 종료 코드 또는 파일 존재만으로 PASS 판정하지 마라.";
}

internal sealed record UnityBackgroundVisualEvidence(
    bool Captured, string Detail, string? ImagePath, bool GameViewParityVerified = false);

/// <summary>
/// Generates Unity Editor C# eval_file scripts and validates the resulting
/// offscreen PNG. No Windows foreground/window capture or OS-global input.
/// Existing Editor only; intended to run while the caller owns the Editor gate.
/// </summary>
internal static class UnityBackgroundVisualTools
{
    private const string FrameMarker = "PROJECTHUB_VISUAL_FRAME|";
    private static readonly byte[] PngSignature =
        { 137, 80, 78, 71, 13, 10, 26, 10 };

    internal static UnityBackgroundVisualPlan Prepare(string root, string jobId, string stage)
    {
        var safeId = SafeSegment(jobId);
        var safeStage = SafeSegment(stage);
        var directory = Path.Combine(Path.GetFullPath(root), "temp", "ProjectHub",
            safeId, "visual", safeStage);
        Directory.CreateDirectory(directory);
        var beforePng = Path.Combine(directory, "before.png");
        var afterPng = Path.Combine(directory, "after.png");
        var before = Path.Combine(directory, "capture-before.cs");
        var after = Path.Combine(directory, "capture-after.cs");
        var input = Path.Combine(directory, "ui-click-template.cs");
        var workerPng = Path.Combine(directory, "worker-verify.png");
        var workerScript = Path.Combine(directory, "capture-worker-verify.cs");
        File.WriteAllText(before, BuildCaptureExpression(beforePng), ProjectHubJson.Utf8NoBom);
        File.WriteAllText(after, BuildCaptureExpression(afterPng), ProjectHubJson.Utf8NoBom);
        File.WriteAllText(workerScript, BuildCaptureExpression(workerPng), ProjectHubJson.Utf8NoBom);
        File.WriteAllText(input, BuildUiClickTemplate(), ProjectHubJson.Utf8NoBom);
        return new(directory, before, after, input, beforePng, afterPng,
            workerScript, workerPng, Path.GetFullPath(root));
    }

    private static string SafeSegment(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Visual evidence identity is empty.");
        var sanitized = new string(text.Take(64)
            .Select(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' ? c : '_')
            .ToArray());
        if (sanitized.Trim('_').Length == 0)
            throw new ArgumentException("Invalid visual evidence identity.");
        return sanitized;
    }

    internal static string BuildCaptureExpression(string outputPath)
    {
        var literal = JsonSerializer.Serialize(Path.GetFullPath(outputPath));
        // All temporary render targets/canvases are restored in finally.
        // Overlay UI is an APPROXIMATE composition, not a Game View pixel claim.
        return """
            var phOutput = __OUTPUT__;
            var phCamera = UnityEngine.Camera.main;
            if (phCamera == null) {
                var phCameras = UnityEngine.Object.FindObjectsByType<UnityEngine.Camera>(
                    UnityEngine.FindObjectsSortMode.None);
                foreach (var phCandidate in phCameras)
                    if (phCandidate.isActiveAndEnabled) { phCamera = phCandidate; break; }
            }
            if (phCamera == null)
                throw new System.InvalidOperationException("PROJECTHUB_VISUAL_NO_CAMERA");
            const int phWidth = 1280;
            const int phHeight = 720;
            var phRt = new UnityEngine.RenderTexture(phWidth, phHeight, 24,
                UnityEngine.RenderTextureFormat.ARGB32);
            var phSavedTarget = phCamera.targetTexture;
            var phSavedActive = UnityEngine.RenderTexture.active;
            UnityEngine.Texture2D phTexture = null;
            var phCopies = new System.Collections.Generic.List<UnityEngine.GameObject>();
            var phOverlayCount = 0;
            try {
                var phCanvases = UnityEngine.Object.FindObjectsByType<UnityEngine.Canvas>(
                    UnityEngine.FindObjectsSortMode.None);
                foreach (var phCanvas in phCanvases) {
                    if (!phCanvas.isActiveAndEnabled || !phCanvas.isRootCanvas ||
                        phCanvas.renderMode != UnityEngine.RenderMode.ScreenSpaceOverlay)
                        continue;
                    phOverlayCount++;
                    var phCopy = UnityEngine.Object.Instantiate(phCanvas.gameObject);
                    phCopies.Add(phCopy);
                    phCopy.hideFlags = UnityEngine.HideFlags.HideAndDontSave;
                    var phCopyCanvas = phCopy.GetComponent<UnityEngine.Canvas>();
                    phCopyCanvas.renderMode = UnityEngine.RenderMode.ScreenSpaceCamera;
                    phCopyCanvas.worldCamera = phCamera;
                    phCopyCanvas.planeDistance = phCamera.nearClipPlane + 1f;
                    phCopyCanvas.overrideSorting = true;
                    phCopyCanvas.sortingOrder = phCanvas.sortingOrder;
                }
                phRt.Create();
                phCamera.targetTexture = phRt;
                phCamera.Render();
                UnityEngine.RenderTexture.active = phRt;
                phTexture = new UnityEngine.Texture2D(phWidth, phHeight,
                    UnityEngine.TextureFormat.RGB24, false);
                phTexture.ReadPixels(new UnityEngine.Rect(0, 0, phWidth, phHeight), 0, 0);
                phTexture.Apply();
                var phPng = phTexture.EncodeToPNG();
                if (phPng == null || phPng.Length < 64)
                    throw new System.InvalidOperationException("PROJECTHUB_VISUAL_EMPTY_PNG");
                System.IO.Directory.CreateDirectory(
                    System.IO.Path.GetDirectoryName(phOutput));
                System.IO.File.WriteAllBytes(phOutput, phPng);
                return "PROJECTHUB_VISUAL_FRAME|" +
                    (phOverlayCount == 0 ? "OFFSCREEN_CAMERA" : "OFFSCREEN_UI_APPROX") +
                    "|" + phWidth + "|" + phHeight;
            } finally {
                phCamera.targetTexture = phSavedTarget;
                UnityEngine.RenderTexture.active = phSavedActive;
                foreach (var phCopy in phCopies)
                    if (phCopy != null) UnityEngine.Object.DestroyImmediate(phCopy);
                if (phTexture != null) UnityEngine.Object.DestroyImmediate(phTexture);
                phRt.Release();
                UnityEngine.Object.DestroyImmediate(phRt);
            }
            """.Replace("__OUTPUT__", literal, StringComparison.Ordinal);
    }

    internal static string BuildUiClickTemplate() => """
        // Copy and replace the object path before running. No OS mouse input.
        if (!UnityEngine.Application.isPlaying)
            throw new System.InvalidOperationException("PROJECTHUB_UI_INPUT_REQUIRES_PLAY_MODE");
        var phTargetName = "__TARGET_UI_OBJECT__";
        if (phTargetName.Contains("__TARGET_"))
            throw new System.InvalidOperationException("PROJECTHUB_UI_TARGET_NOT_SELECTED");
        var phTarget = UnityEngine.GameObject.Find(phTargetName);
        var phEvents = UnityEngine.EventSystems.EventSystem.current;
        if (phTarget == null || phEvents == null)
            throw new System.InvalidOperationException("PROJECTHUB_UI_TARGET_OR_EVENTSYSTEM_MISSING");
        var phRect = phTarget.GetComponent<UnityEngine.RectTransform>();
        if (phRect == null)
            throw new System.InvalidOperationException("PROJECTHUB_UI_TARGET_NOT_RECT");
        var phCanvas = phTarget.GetComponentInParent<UnityEngine.Canvas>();
        var phCamera = phCanvas != null &&
            phCanvas.renderMode != UnityEngine.RenderMode.ScreenSpaceOverlay
            ? phCanvas.worldCamera : null;
        var phPointer = new UnityEngine.EventSystems.PointerEventData(phEvents);
        phPointer.position = UnityEngine.RectTransformUtility.WorldToScreenPoint(
            phCamera, phRect.TransformPoint(phRect.rect.center));
        var phRaycasts = new System.Collections.Generic.List<UnityEngine.EventSystems.RaycastResult>();
        phEvents.RaycastAll(phPointer, phRaycasts);
        if (phRaycasts.Count == 0 ||
            phRaycasts[0].gameObject != phTarget &&
            !phRaycasts[0].gameObject.transform.IsChildOf(phTarget.transform))
            throw new System.InvalidOperationException("PROJECTHUB_UI_TARGET_OCCLUDED_OR_UNRAYCASTABLE");
        phPointer.pointerCurrentRaycast = phRaycasts[0];
        phPointer.pointerPressRaycast = phRaycasts[0];
        phPointer.button = UnityEngine.EventSystems.PointerEventData.InputButton.Left;
        UnityEngine.EventSystems.ExecuteEvents.ExecuteHierarchy(
            phTarget, phPointer, UnityEngine.EventSystems.ExecuteEvents.pointerEnterHandler);
        var phPress = UnityEngine.EventSystems.ExecuteEvents.ExecuteHierarchy(
            phTarget, phPointer, UnityEngine.EventSystems.ExecuteEvents.pointerDownHandler);
        if (phPress == null)
            throw new System.InvalidOperationException("PROJECTHUB_UI_POINTER_DOWN_UNHANDLED");
        phPointer.pointerPress = phPress;
        UnityEngine.EventSystems.ExecuteEvents.ExecuteHierarchy(
            phTarget, phPointer, UnityEngine.EventSystems.ExecuteEvents.pointerUpHandler);
        UnityEngine.EventSystems.ExecuteEvents.ExecuteHierarchy(
            phTarget, phPointer, UnityEngine.EventSystems.ExecuteEvents.pointerClickHandler);
        return "PROJECTHUB_UI_INTERNAL_CLICK|" + phTargetName;
        """;

    internal static UnityBackgroundVisualEvidence InterpretCapture(
        UnityAssetCliResult response, string expectedPng)
    {
        if (response.ExitCode != 0)
            return new(false, "OFFSCREEN_CAPTURE_CLI_FAILED: " +
                Limit(response.Error + " " + response.Output), null);
        try
        {
            using var json = JsonDocument.Parse(response.Output);
            var root = json.RootElement;
            if (!root.TryGetProperty("success", out var success) ||
                success.ValueKind != JsonValueKind.True ||
                !root.TryGetProperty("data", out var data) ||
                !TryResult(data, out var marker))
                return new(false, "OFFSCREEN_CAPTURE_EDITOR_RESULT_UNVERIFIED", null);
            var parts = marker.Split('|');
            if (parts.Length != 4 || parts[0] != "PROJECTHUB_VISUAL_FRAME" ||
                parts[1] is not ("OFFSCREEN_CAMERA" or "OFFSCREEN_UI_APPROX") ||
                !int.TryParse(parts[2], out var width) || width != 1280 ||
                !int.TryParse(parts[3], out var height) || height != 720)
                return new(false, "OFFSCREEN_CAPTURE_MARKER_INVALID", null);
            if (!File.Exists(expectedPng) || !IsPng(expectedPng))
                return new(false, "OFFSCREEN_CAPTURE_FILE_NOT_VALID_PNG", null);
            return new(true,
                (parts[1] == "OFFSCREEN_UI_APPROX"
                    ? "OFFSCREEN_UI_APPROX: Overlay UI composition is approximate"
                    : "OFFSCREEN_CAMERA: Camera pixels only") +
                "; Game View parity UNVERIFIED; file=" + expectedPng,
                expectedPng);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return new(false, "OFFSCREEN_CAPTURE_INVALID_RESULT: " + ex.Message, null);
        }
    }

    private static bool IsPng(string path)
    {
        using var stream = File.OpenRead(path);
        if (stream.Length < 64) return false;
        var header = new byte[16];
        if (stream.Read(header) != header.Length) return false;
        return header.Take(8).SequenceEqual(PngSignature) &&
            System.Text.Encoding.ASCII.GetString(header, 12, 4) == "IHDR";
    }

    private static bool TryResult(JsonElement data, out string value)
    {
        value = string.Empty;
        if (data.ValueKind == JsonValueKind.String)
        {
            value = data.GetString() ?? "";
            return value.StartsWith(FrameMarker, StringComparison.Ordinal);
        }
        if (data.ValueKind != JsonValueKind.Object) return false;
        foreach (var key in new[] { "result", "value", "output" })
            if (data.TryGetProperty(key, out var child) && TryResult(child, out value))
                return true;
        return false;
    }

    internal static async Task<UnityBackgroundVisualEvidence> CaptureAsync(
        UnityBackgroundVisualPlan plan, CancellationToken cancellationToken,
        Func<IReadOnlyList<string>, CancellationToken, Task<UnityAssetCliResult>>? execute = null)
    {
        try
        {
            // Do not reuse stale artifacts as evidence after a failed attempt.
            if (File.Exists(plan.WorkerPng)) File.Delete(plan.WorkerPng);
            execute ??= (args, token) => RunCliAsync(plan.ProjectRoot, args, token);
            var response = await execute(
                UnityAssetCliImporter.ImportFileArguments(plan.ProjectRoot, plan.WorkerScript),
                cancellationToken).ConfigureAwait(false);
            return InterpretCapture(response, plan.WorkerPng);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
            InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return new(false, "OFFSCREEN_CAPTURE_ERROR: " + ex.Message, null);
        }
    }

    private static async Task<UnityAssetCliResult> RunCliAsync(
        string root, IReadOnlyList<string> args, CancellationToken token)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "unity",
            WorkingDirectory = root,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var arg in args) psi.ArgumentList.Add(arg);
        using var job = new WorkerChildProcessJob("UnityWorker internal visual capture");
        using var child = job.Start(psi, token);
        var stdout = child.StandardOutput!.ReadToEndAsync(token);
        var stderr = child.StandardError!.ReadToEndAsync(token);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, timeout.Token);
        try
        {
            await child.Process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            job.Dispose();
            return new(-1, "", "OFFSCREEN_CAPTURE_TIMEOUT_UNCERTAIN_NO_RETRY");
        }
        return new(child.Process.ExitCode,
            await stdout.ConfigureAwait(false), await stderr.ConfigureAwait(false));
    }

    private static string Limit(string text) =>
        text.Length <= 550 ? text.Trim() : text[..550].Trim() + "...";
}
