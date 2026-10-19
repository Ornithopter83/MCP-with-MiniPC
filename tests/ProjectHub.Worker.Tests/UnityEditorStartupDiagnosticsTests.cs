using System.Text.Json;
using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class UnityEditorStartupDiagnosticsTests
{
    private sealed class TestContext : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(),
            "projecthub-startup-" + Guid.NewGuid().ToString("N"));
        public string Log { get; }
        public string Queue => UnityEditorDirectBridge.QueueDirectory(Root);

        public TestContext()
        {
            Directory.CreateDirectory(Root);
            Log = Path.Combine(Root, "Editor.log");
            File.WriteAllText(Log, "old error CS1111: ignored before this run\n");
        }

        public void WriteSignal(string status = "BRIDGE_CALLBACK_ACTIVE",
            string? root = null, string? queue = null, string? destination = null)
        {
            var path = destination ?? UnityEditorDirectBridge.StartupSignalPath(Root);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(new
            {
                projectRoot = root ?? Root,
                queueDirectory = queue ?? Queue,
                status,
                processId = 12345,
                utcTicks = DateTime.UtcNow.Ticks
            }));
        }

        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, true);
            if (Directory.Exists(Queue)) Directory.Delete(Queue, true);
        }
    }

    [Fact]
    public void OldCompileErrorsDoNotCountButNewErrorsExplainStartupBlock()
    {
        using var test = new TestContext();
        var start = UnityEditorStartupDiagnostics.Capture(test.Log);
        var old = UnityEditorStartupDiagnostics.Analyze(
            test.Root, start, "PENDING_NOT_CLAIMED");
        Assert.Equal("BRIDGE_NOT_LOADED", old.Code);
        File.AppendAllText(test.Log,
            "Assets/Editor/ProjectHub/ProjectHubEditorBridge.cs(27,1): error CS0246: type not found\n");
        var current = UnityEditorStartupDiagnostics.Analyze(
            test.Root, start, "PENDING_NOT_CLAIMED");
        Assert.Equal("EDITOR_COMPILE_FAILED", current.Code);
        Assert.Contains("CS0246", current.Detail);
        Assert.DoesNotContain("CS1111", current.Detail);
    }

    [Fact]
    public void ActiveReceiverButUnclaimedRequestMeansNoCommandResponse()
    {
        using var test = new TestContext();
        var start = UnityEditorStartupDiagnostics.Capture(test.Log);
        test.WriteSignal();
        var value = UnityEditorStartupDiagnostics.Analyze(
            test.Root, start, "PENDING_NOT_CLAIMED");
        Assert.Equal("BRIDGE_NO_RESPONSE", value.Code);
        Assert.Contains("ExpectedQueue=", value.Detail);
    }

    [Fact]
    public void BusyReceiverIsNotReportedAsCommandReadyOrUnloaded()
    {
        using var test = new TestContext();
        var start = UnityEditorStartupDiagnostics.Capture(test.Log);
        test.WriteSignal("EDITOR_IMPORT_BUSY");
        Assert.Equal("EDITOR_IMPORT_IN_PROGRESS",
            UnityEditorStartupDiagnostics.Analyze(test.Root, start, "timeout").Code);
    }

    [Fact]
    public void MatchingProjectInAnotherFreshQueueMeansKeyMismatch()
    {
        using var test = new TestContext();
        var start = UnityEditorStartupDiagnostics.Capture(test.Log);
        var wrong = Path.Combine(Path.GetDirectoryName(test.Queue)!,
            Path.GetFileName(test.Queue) + "-mismatch", "bridge-active.json");
        try
        {
            test.WriteSignal(destination: wrong, queue: Path.GetDirectoryName(wrong));
            var finding = UnityEditorStartupDiagnostics.Analyze(
                test.Root, start, "PENDING_NOT_CLAIMED");
            Assert.Equal("EDITOR_BRIDGE_QUEUE_MISMATCH", finding.Code);
        }
        finally
        {
            var directory = Path.GetDirectoryName(wrong)!;
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}
