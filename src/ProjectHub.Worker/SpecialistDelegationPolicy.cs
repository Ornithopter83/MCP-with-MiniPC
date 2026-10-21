namespace ProjectHub.Worker;

/// <summary>
/// Prevent a productive SV turn from claiming implementation success without
/// an actual successful delegate_work call. Blocked/error results are preserved.
/// </summary>
internal static class SpecialistDelegationPolicy
{
    internal const string MissingWorkCode = "SV_WORK_DELEGATION_REQUIRED";

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
