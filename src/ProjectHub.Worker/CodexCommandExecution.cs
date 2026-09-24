namespace ProjectHub.Worker;

/// <summary>Observed CLI command output recorded as transcript evidence; no success judgment is implied.</summary>
public sealed record CodexCommandExecution(string Command, int ExitCode, string? Output = null);
