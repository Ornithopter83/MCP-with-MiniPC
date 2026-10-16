using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class UnityBackgroundVisualToolsTests
{
    private sealed class Layout : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(),
            "unity-background-capture-" + Guid.NewGuid().ToString("N"));
        public Layout() => Directory.CreateDirectory(Root);
        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, true);
        }
    }

    private static void WritePngHeader(string path)
    {
        var bytes = new byte[80];
        var prefix = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 };
        prefix.CopyTo(bytes, 0);
        System.Text.Encoding.ASCII.GetBytes("IHDR").CopyTo(bytes, 12);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(
            bytes.AsSpan(16, 4), 1920);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(
            bytes.AsSpan(20, 4), 1080);
        File.WriteAllBytes(path, bytes);
    }

    [Fact]
    public void PreparingScripts_ProvidesInternalInputsWithoutGlobalMouseOrDesktopCapture()
    {
        using var layout = new Layout();
        var plan = UnityBackgroundVisualTools.Prepare(layout.Root,
            "M17", "review-1");
        Assert.True(File.Exists(plan.BeforeScript));
        Assert.True(File.Exists(plan.AfterScript));
        Assert.True(File.Exists(plan.InputTemplate));
        Assert.True(File.Exists(plan.WorkerScript));
        Assert.True(File.Exists(plan.NativeScript));
        Assert.NotEqual(plan.WorkerPng, plan.NativePng);
        var native = File.ReadAllText(plan.NativeScript);
        Assert.Contains("ScreenCapture.CaptureScreenshot", native);
        Assert.Contains("GameView", native);
        Assert.Contains("PROJECTHUB_NATIVE_REQUIRES_PLAY_MODE", native);
        Assert.DoesNotContain("SetForegroundWindow", native);
        Assert.NotEqual(plan.AfterPng, plan.WorkerPng);
        var capture = File.ReadAllText(plan.WorkerScript);
        Assert.Contains("UnityEngine.RenderTexture", capture);
        Assert.Contains("phCamera.Render()", capture);
        Assert.Contains("ReadPixels", capture);
        Assert.Contains("ScreenSpaceCamera", capture);
        Assert.Contains("OFFSCREEN_UI_APPROX", capture);
        Assert.Contains("UnityEngine.Screen.width", capture);
        Assert.Contains("UnityEngine.Screen.height", capture);
        Assert.DoesNotContain("const int phWidth", capture);
        Assert.Contains("finally", capture);
        var input = File.ReadAllText(plan.InputTemplate);
        Assert.Contains("EventSystem.current", input);
        Assert.Contains("ExecuteEvents.pointerClickHandler", input);
        Assert.Contains("RaycastAll", input);
        Assert.Contains("PROJECTHUB_UI_TARGET_NOT_SELECTED", input);
        Assert.DoesNotContain("SendInput", input);
        Assert.DoesNotContain("SetForegroundWindow", capture);
        Assert.DoesNotContain("SetForegroundWindow", input);
        Assert.Contains("eval_file", plan.AgentGuidance);
    }

    [Fact]
    public void CaptureCannotClaimParityOrSuccessFromEchoedScriptOrMissingFile()
    {
        using var layout = new Layout();
        var plan = UnityBackgroundVisualTools.Prepare(layout.Root, "M1", "initial");
        var echo = new UnityAssetCliResult(0,
            """{"success":true,"data":{"expression":"PROJECTHUB_VISUAL_FRAME|OFFSCREEN_CAMERA|1920|1080"}}""", "");
        Assert.False(UnityBackgroundVisualTools.InterpretCapture(echo, plan.WorkerPng).Captured);
        Assert.False(UnityBackgroundVisualTools.InterpretCapture(
            new(0, """{"success":true,"data":{"result":"PROJECTHUB_VISUAL_FRAME|OFFSCREEN_CAMERA|1920|1080"}}""", ""),
            plan.WorkerPng).Captured);
    }

    [Fact]
    public async Task ActualEditorResultAndFile_AreRequiredAndUiParityRemainsUnverified()
    {
        using var layout = new Layout();
        var plan = UnityBackgroundVisualTools.Prepare(layout.Root, "M1", "initial");
        var callCount = 0;
        Task<UnityAssetCliResult> Execute(IReadOnlyList<string> args, CancellationToken token)
        {
            callCount++;
            Assert.Equal("command", args[0]);
            Assert.Equal("eval_file", args[1]);
            Assert.Equal(plan.WorkerScript, args[2]);
            Assert.Contains("--project-path", args);
            WritePngHeader(plan.WorkerPng);
            return Task.FromResult(new UnityAssetCliResult(0,
                """{"success":true,"data":{"result":"PROJECTHUB_VISUAL_FRAME|OFFSCREEN_UI_APPROX|1920|1080"}}""", ""));
        }
        var result = await UnityBackgroundVisualTools.CaptureAsync(
            plan, CancellationToken.None, Execute);
        Assert.Equal(1, callCount);
        Assert.True(result.Captured, result.Detail);
        Assert.False(result.GameViewParityVerified);
        Assert.Contains("APPROX", result.Detail);
        Assert.Equal(plan.WorkerPng, result.ImagePath);
    }

    [Fact]
    public void OffscreenCaptureRejectsDimensionsDifferentFromPng()
    {
        using var layout = new Layout();
        var plan = UnityBackgroundVisualTools.Prepare(layout.Root, "M1", "size-check");
        WritePngHeader(plan.WorkerPng);
        const string response =
            """{"success":true,"data":{"result":"PROJECTHUB_VISUAL_FRAME|OFFSCREEN_CAMERA|1920|1079"}}""";
        var outcome = UnityBackgroundVisualTools.InterpretCapture(
            new UnityAssetCliResult(0, response, ""), plan.WorkerPng);
        Assert.False(outcome.Captured);
        Assert.Contains("SIZE_MISMATCH", outcome.Detail);
    }

    [Fact]
    public async Task NativeScreenshotRequestWithoutActualFileIsUnverified()
    {
        using var layout = new Layout();
        var plan = UnityBackgroundVisualTools.Prepare(layout.Root, "M1", "native");
        Assert.False(File.Exists(plan.NativePng));
        var response = await UnityBackgroundVisualTools.CaptureNativeAsync(
            plan, CancellationToken.None, (args, ct) => Task.FromResult(
                new UnityAssetCliResult(0,
                    """{"success":true,"data":{"result":"PROJECTHUB_NATIVE_REQUESTED|1920|1080"}}""",
                    "")));
        Assert.False(response.Captured);
        Assert.Contains("TIMEOUT", response.Detail);
        Assert.False(response.GameViewParityVerified);
    }

    [Fact]
    public async Task FailedNativeCommandCannotReuseStaleCapture()
    {
        using var layout = new Layout();
        var plan = UnityBackgroundVisualTools.Prepare(layout.Root, "M2", "native");
        WritePngHeader(plan.NativePng);
        var response = await UnityBackgroundVisualTools.CaptureNativeAsync(
            plan, CancellationToken.None, (args, ct) => Task.FromResult(
                new UnityAssetCliResult(-1, "", "editor disconnected")));
        Assert.False(response.Captured);
        Assert.False(File.Exists(plan.NativePng));
    }

    [Fact]
    public async Task StaleFileIsNotCreditedAfterFailedOrTimedOutCommand()
    {
        using var layout = new Layout();
        var plan = UnityBackgroundVisualTools.Prepare(layout.Root, "M1", "initial");
        WritePngHeader(plan.WorkerPng);
        var response = await UnityBackgroundVisualTools.CaptureAsync(
            plan, CancellationToken.None,
            (args, ct) => Task.FromResult(new UnityAssetCliResult(
                -1, "", "OFFSCREEN_CAPTURE_TIMEOUT_UNCERTAIN_NO_RETRY")));
        Assert.False(response.Captured);
        Assert.False(File.Exists(plan.WorkerPng));
    }

    [Fact]
    public void CaptureStagePathsCannotEscapeTempProjectDirectory()
    {
        using var layout = new Layout();
        var plan = UnityBackgroundVisualTools.Prepare(layout.Root,
            "job", "../../evil");
        Assert.True(Path.GetFullPath(plan.Directory)
            .StartsWith(Path.Combine(layout.Root, "temp", "ProjectHub"),
                StringComparison.OrdinalIgnoreCase));
    }
}
