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

await runner.RunAsync(cancellationTokenSource.Token);
return 0;
