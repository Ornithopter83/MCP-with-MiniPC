using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class ParallelJudgeWorkItemRouterTests
{
    [Fact]
    public async Task RawJudgeResultReturnsToSameWorkItem()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "projecthub-parallel-judge-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var host = new FakeHost();
        var transport = new FakeTransport(new JudgeTransportResult(
            "{\"answer\":\"raw\"}",
            null,
            null));

        try
        {
            await using var router = new ParallelJudgeWorkItemRouter(
                host,
                transport,
                "job-1",
                cts.Token);

            host.Publish(new ParallelWorkExternalBlock(
                "W4",
                "JUDGE_REQUEST",
                "NOUL | QID:q1 구현 판단이 필요한가?",
                "commit-W4",
                "branch-W4",
                root,
                "session-W4",
                "판단이 필요한 기능"));

            var resume = await host.Resume.Task.WaitAsync(cts.Token);

            Assert.Equal("W4", resume.WorkItemId);
            Assert.Equal("JUDGMENT", resume.InputType);
            Assert.Equal("{\"answer\":\"raw\"}", resume.Body);
            Assert.NotNull(transport.LastRequest);
            Assert.Equal(root, transport.LastRequest!.WorkingDirectory);
            Assert.Equal("commit-W4", transport.LastRequest.ReviewCommitSha);
            Assert.Equal("job-1", transport.LastRequest.JobId);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task InvalidJudgeFormReturnsMechanicalFailureWithoutTransportCall()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "projecthub-parallel-judge-invalid-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var host = new FakeHost();
        var transport = new FakeTransport(new JudgeTransportResult("unused", null, null));

        try
        {
            await using var router = new ParallelJudgeWorkItemRouter(
                host,
                transport,
                "job-2",
                cts.Token);

            host.Publish(new ParallelWorkExternalBlock(
                "W5",
                "JUDGE_REQUEST",
                "잘못된 Form",
                null,
                null,
                root,
                "session-W5"));

            var resume = await host.Resume.Task.WaitAsync(cts.Token);

            Assert.Equal("W5", resume.WorkItemId);
            Assert.Equal("JUDGMENT", resume.InputType);
            Assert.Contains("status: FAILED", resume.Body);
            Assert.Contains("JUDGE_REQUEST_", resume.Body);
            Assert.Null(transport.LastRequest);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    private sealed record ResumeCall(string WorkItemId, string InputType, string Body);

    private sealed class FakeHost : IParallelExternalBlockHost
    {
        public event Action<ParallelWorkExternalBlock>? ExternalBlockAvailable;

        public TaskCompletionSource<ResumeCall> Resume { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Publish(ParallelWorkExternalBlock block)
            => ExternalBlockAvailable?.Invoke(block);

        public Task<bool> ResumeExternalWorkItemAsync(
            string workItemId,
            string inputType,
            string body,
            CancellationToken cancellationToken = default)
        {
            Resume.TrySetResult(new ResumeCall(workItemId, inputType, body));
            return Task.FromResult(true);
        }
    }

    private sealed class FakeTransport : IParallelJudgeTransport
    {
        private readonly JudgeTransportResult _result;

        public FakeTransport(JudgeTransportResult result)
        {
            _result = result;
        }

        public JudgeRequest? LastRequest { get; private set; }

        public Task<JudgeTransportResult> ReviewAsync(
            JudgeRequest request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(_result);
        }
    }
}
