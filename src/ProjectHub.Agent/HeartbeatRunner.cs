namespace ProjectHub.Agent;

public sealed class HeartbeatRunner(
    HeartbeatSender sender,
    AgentOptions options,
    Action<string> log,
    Action<string> logError)
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        log($"ProjectHub.Agent started. Heartbeat interval: {options.HeartbeatInterval.TotalSeconds:0}s");
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await sender.SendOnceAsync(cancellationToken);
                log("Heartbeat sent successfully.");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
            {
                logError($"Heartbeat failed; will retry: {exception.Message}");
            }

            try
            {
                await Task.Delay(options.HeartbeatInterval, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
        }

        log("ProjectHub.Agent stopped.");
    }
}
