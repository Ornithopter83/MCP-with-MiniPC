namespace ProjectHub.Worker;

/// <summary>
/// A specialist may judge, plan and verify, but a normal productive turn may
/// not be reported as completed unless one bounded temporary WORK succeeded.
/// A naturally blocked run stays blocked; don't force a retry just to satisfy
/// the experimental delegation policy.
/// </summary>
internal static class SpecialistDelegationPolicy
{
    internal const string MissingWorkCode = "SPECIALIST_WORK_DELEGATION_REQUIRED";

    internal static bool MustBlock(
        bool mandatory, int successfulDelegations, int exitCode, string finalMessage)
    {
        if (!mandatory || successfulDelegations > 0 || exitCode != 0)
            return false;
        var report = RoleTextProtocol.ParseWork(finalMessage);
        // If a role already reported a genuine BLOCKED state (quota / MCP
        // unavailable), preserve it instead of synthesizing another failure.
        return !(report.IsValid &&
                 string.Equals(report.Status, "blocked",
                     StringComparison.OrdinalIgnoreCase));
    }

    internal static AiRoleRunResult Enforce(
        AiRoleRunResult result, bool mandatory, int successfulDelegations)
    {
        if (!MustBlock(mandatory, successfulDelegations, result.ExitCode,
                result.FinalMessage))
            return result;
        // A model claiming completion without a successful MCP WORK cannot
        // pass role validation. Explicitly block rather than launching retries.
        var blocked = "[GOTO=WORKER]" + Environment.NewLine +
            "@@REPORT" + Environment.NewLine +
            "<STATUS>blocked</>" + Environment.NewLine +
            "<SUMMARY>" + MissingWorkCode + "</>" + Environment.NewLine +
            "<ISSUES>성공한 MCP delegate_work 호출이 없어 완료를 인정하지 않습니다. " +
            "부모 역할이 직접 작업을 대행하지 말고 연결 상태를 확인해야 합니다.</>" +
            Environment.NewLine + "[RESPONSE=OK]";
        return result with
        {
            ExitCode = 1,
            FinalMessage = blocked,
            StandardError = result.StandardError + Environment.NewLine + MissingWorkCode
        };
    }
}
