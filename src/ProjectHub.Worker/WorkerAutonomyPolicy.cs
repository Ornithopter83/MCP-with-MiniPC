namespace ProjectHub.Worker;

/// <summary>
/// Explicit UnityWorker runtime choice: execute Codex/OpenCode commands with
/// the worker user's OS permissions. This does not grant Windows elevation,
/// skip Unity QA, or assert task completion.
/// </summary>
internal static class WorkerAutonomyPolicy
{
    // Runtime-level setting: avoid compile-time dead-code warnings while
    // preserving the existing always-on FULL_AUTONOMY behavior.
    internal static readonly bool Enabled = true;

    internal static CodexSandboxMode EffectiveSandbox(CodexSandboxMode requested) =>
        Enabled ? CodexSandboxMode.DangerFullAccess : requested;

    internal static IReadOnlyList<string> CodexPermissionArguments(
        CodexSandboxMode requested)
    {
        if (Enabled)
            return new[] { "--dangerously-bypass-approvals-and-sandbox" };
        return new[]
        {
            "--sandbox",
            requested switch
            {
                CodexSandboxMode.ReadOnly => "read-only",
                CodexSandboxMode.WorkspaceWrite => "workspace-write",
                _ => "danger-full-access"
            }
        };
    }
}
