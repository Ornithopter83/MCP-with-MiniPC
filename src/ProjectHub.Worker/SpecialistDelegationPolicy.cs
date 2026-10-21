namespace ProjectHub.Worker;

/// <summary>
/// A writable SuperVisor milestone must delegate first. The initial SV turn
/// runs read-only; only a real, safely classified WORK failure permits a
/// separate direct-recovery turn. An unattempted or unsafe delegation never
/// grants direct write access.
/// </summary>
internal static class SpecialistDelegationPolicy
{
    internal const string MissingWorkCode = "SV_WORK_DELEGATION_REQUIRED";
    internal const string UnsafeFailureCode = "SV_WORK_DELEGATION_UNSAFE_TO_FALLBACK";

    internal static CodexSandboxMode DelegationSandbox => CodexSandboxMode.ReadOnly;
    internal static CodexSandboxMode ApprovedRecoverySandbox => CodexSandboxMode.WorkspaceWrite;

    internal static bool CanDirectFallback(
        bool readOnly, int attempted, int successful, int recoverableFailures,
        int unsafeFailures, bool cancellationRequested)
        => !readOnly && !cancellationRequested && attempted > 0 &&
           recoverableFailures > 0 && unsafeFailures == 0 &&
           successful + recoverableFailures <= attempted;

    internal static AiRoleRunResult Enforce(
        AiRoleRunResult result, bool mandatory, int successfulDelegations)
    {
        if (!mandatory || successfulDelegations > 0 || result.ExitCode != 0)
            return result;
        var parsed = RoleTextProtocol.ParseWork(result.FinalMessage);
        if (parsed.IsValid && string.Equals(parsed.Status, "blocked",
                StringComparison.OrdinalIgnoreCase))
            return result;
        return result with
        {
            ExitCode = 1,
            FinalMessage = RoleTextProtocol.BuildResult("blocked", MissingWorkCode),
            StandardError = result.StandardError + Environment.NewLine + MissingWorkCode
        };
    }
}
