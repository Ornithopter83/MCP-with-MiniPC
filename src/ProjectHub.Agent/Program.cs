using ProjectHub.Agent;

AgentOptions options;
try
{
    options = AgentOptions.Load(args);
}
catch (InvalidOperationException exception)
{
    Console.Error.WriteLine($"Configuration error: {exception.Message}");
    return 2;
}

if (args.Any(argument => string.Equals(argument, "--mode=batch-sync", StringComparison.OrdinalIgnoreCase)))
{
    var scanner = new LargeDataScanner(options.LargeFileThresholdBytes);
    foreach (var project in options.RegisteredProjects)
    {
        var inventory = await scanner.ScanInventoryAsync(project, CancellationToken.None);
        Console.WriteLine($"Batch snapshot: {project.ProjectId} ({inventory.Count} large files; size/mtime only)");
        foreach (var item in inventory) Console.WriteLine($"{item.RelativePath}\t{item.SizeBytes}\t{item.LastWriteTimeUtc:O}");
    }
    return 0;
}

using var cancellationTokenSource = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellationTokenSource.Cancel();
};

using var httpClient = new HttpClient
{
    BaseAddress = options.ServerBaseUrl,
    Timeout = TimeSpan.FromSeconds(10)
};

var runner = new HeartbeatRunner(
    new HeartbeatSender(httpClient, options),
    options,
    message => Console.WriteLine($"[{DateTimeOffset.Now:O}] {message}"),
    message => Console.Error.WriteLine($"[{DateTimeOffset.Now:O}] {message}"));

var heartbeatTask = runner.RunAsync(cancellationTokenSource.Token);
var projectMonitor = new ProjectActivityMonitor(
    options.RegisteredProjects,
    options.WorkstationId,
    new GitStateCollector(),
    new ProjectStateSender(httpClient),
    message => Console.WriteLine($"[{DateTimeOffset.Now:O}] {message}"),
    message => Console.Error.WriteLine($"[{DateTimeOffset.Now:O}] {message}"));
var projectTask = projectMonitor.RunAsync(cancellationTokenSource.Token);

try
{
    await Task.WhenAll(heartbeatTask, projectTask);
}
catch (OperationCanceledException) when (cancellationTokenSource.IsCancellationRequested)
{
}
return 0;
