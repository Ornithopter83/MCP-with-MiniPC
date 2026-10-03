using System.Collections.Concurrent;
using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class ObservationSidecarQueueTests
{
    [Fact]
    public void BuildExecutionPolicyAllowsBuildCommandsOnlyForSlot9()
    {
        Assert.True(BuildExecutionPolicy.IsForbiddenForWorkItem(
            "10",
            "dotnet",
            new[] { "build", "App.sln", "-c", "Release" }));
        Assert.True(BuildExecutionPolicy.IsForbiddenForWorkItem(
            FixedWorkItemSlots.FileManager,
            "dotnet",
            new[] { "restore", "App.sln" }));
        Assert.False(BuildExecutionPolicy.IsForbiddenForWorkItem(
            FixedWorkItemSlots.BuildPublish,
            "dotnet",
            new[] { "build", "App.sln", "-c", "Release" }));
        Assert.False(BuildExecutionPolicy.IsForbiddenForWorkItem(
            "10",
            "dotnet",
            new[] { "--info" }));
    }

    [Fact]
    public void BuildExecutionChecklistDetectionKeepsImplementationTextDistinct()
    {
        Assert.True(BuildExecutionPolicy.ContainsBuildExecutionInstruction(
            "Release 빌드를 실행해 성공을 확인한다."));
        Assert.True(BuildExecutionPolicy.ContainsBuildExecutionInstruction(
            "dotnet restore App.sln"));
        Assert.False(BuildExecutionPolicy.ContainsBuildExecutionInstruction(
            "PSD export 기능을 구현한다."));
        Assert.False(BuildExecutionPolicy.ContainsBuildExecutionInstruction(
            "빌드는 수행하지 않고 정적 검토한다."));
    }


    [Fact]
    public async Task UnrelatedJsonInRequestFolderIsIgnoredWithoutFailure()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "projecthub-observation-sidecar-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        using var cancellation = new CancellationTokenSource();
        var registry = new MechanicalWorkRegistry();
        var queue = new ObservationSidecarQueue(
            root,
            "job",
            registry,
            cancellation.Token);
        var events = new ConcurrentBag<ObservationSidecarEvent>();
        queue.TransportEvent += events.Add;

        try
        {
            var requestDirectory = queue.GetRequestDirectory("16");
            var manifestPath = Path.Combine(requestDirectory, "16-commit-manifest.json");
            await File.WriteAllTextAsync(
                manifestPath,
                """
                {
                  "workItemId": "16",
                  "commit": "abc123",
                  "tree": "tree123",
                  "changedFiles": []
                }
                """);

            await queue.ScanNowAsync(CancellationToken.None);
            await queue.ScanNowAsync(CancellationToken.None);

            Assert.True(File.Exists(manifestPath));
            Assert.Equal(0, registry.OutstandingCount);
            Assert.DoesNotContain(
                events,
                value => string.Equals(
                    value.Source,
                    "OBSERVATION FAILED",
                    StringComparison.Ordinal));
        }
        finally
        {
            cancellation.Cancel();
            await queue.DisposeAsync();
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }
}
