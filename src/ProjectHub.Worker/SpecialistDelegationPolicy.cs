namespace ProjectHub.Worker;

/// <summary>
/// UnityWorker-style synchronous WORK delegation: parents must receive
/// at least one real successful child result before claiming completion.
/// Unsuccessful delegation never grants direct execution authority.
/// </summary>
internal static class SpecialistDelegationPolicy
{
    internal const string MissingWorkCode = "SV_WORK_DELEGATION_REQUIRED";
    internal const string UnsafeFailureCode = "SV_WORK_DELEGATION_UNSAFE_TO_FALLBACK";

    internal static CodexSandboxMode DelegationSandbox => CodexSandboxMode.ReadOnly;
    internal static bool MustBlockForUnsafeDelegation(DelegationMetrics metrics) =>
        metrics.UnsafeFailures > 0;

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
