using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace ProjectHub.Worker;

internal sealed record UnityBackgroundVisualPlan(
    string Directory, string BeforeScript, string AfterScript,
    string InputTemplate, string BeforePng, string AfterPng, string ProjectRoot)
{
    internal string AgentGuidance =>
        "@@UNITY_INTERNAL_INPUT_RENDER\n" +
        "OS 마우스·키보드/화면 스크린샷 대신 기존 Unity Editor의 내부 렌더와 UI 이벤트를 사용한다.\n" +
        "먼저 실제 연결된 Editor의 eval_file 명령을 카탈로그에서 확인하라.\n" +
        "변경 전 내부 캡처: unity command eval_file \"" + BeforeScript +
            "\" --project-path \"" + ProjectRoot + "\" --format json --timeout 180\n" +
        "변경 후 내부 캡처: unity command eval_file \"" + AfterScript +
            "\" --project-path \"" + ProjectRoot + "\" --format json --timeout 180\n" +
        "UI 상호작용이 필요한 경우 " + InputTemplate +
            "을 별도 파일로 복사하고 __TARGET_UI_OBJECT__를 실제 활성 UI 객체 경로로 바꾼 다음 " +
            "eval_file로 실행한다. 이 템플릿은 Play Mode와 EventSystem을 필요로 한다. " +
            "게임 로직 함수를 직접 호출한 결과를 진짜 포인터 입력 검증으로 표시하지 마라.\n" +
        "캡처는 Camera.Render/RenderTexture/ReadPixels를 사용하며 OS 화면을 보지 않는다. " +
        "Overlay Canvas는 임시 복제 후 ScreenSpaceCamera로 합성하므로 " +
        "Game View와 픽셀 완전 일치 증거가 아니다. UI/후처리/카메라 스태킹 차이는 미검증으로 보고하라. " +
        "캡처·입력이 실패하면 파일 존재나 CLI exit 0만으로 PASS라 하지 말고 다른 코드 작업을 계속하라.\n" +
        "생성된 PNG: " + AfterPng;
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
        File.WriteAllText(before, BuildCaptureExpression(beforePng), ProjectHubJson.Utf8NoBom);
        File.WriteAllText(after, BuildCaptureExpression(afterPng), ProjectHubJson.Utf8NoBom);
        File.WriteAllText(input, BuildUiClickTemplate(), ProjectHubJson.Utf8NoBom);
        return new(directory, before, after, input, beforePng, afterPng,
            Path.GetFullPath(root));
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
                phCamera = phCameras.FirstOrDefault(c => c.isActiveAndEnabled);
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
            if (File.Exists(plan.AfterPng)) File.Delete(plan.AfterPng);
            execute ??= (args, token) => RunCliAsync(plan.ProjectRoot, args, token);
            var response = await execute(
                UnityAssetCliImporter.ImportFileArguments(plan.ProjectRoot, plan.AfterScript),
                cancellationToken).ConfigureAwait(false);
            return InterpretCapture(response, plan.AfterPng);
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
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
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
