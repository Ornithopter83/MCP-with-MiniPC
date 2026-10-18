using System.IO;

namespace ProjectHub.Worker;

/// <summary>
/// One read-only capability query surface for MCP Editor commands.
/// An Editor status, an empty catalog and an executable Editor command
/// are deliberately distinct observations.
/// </summary>
internal static class UnityEditorCommandGateway
{
    internal sealed record Capabilities(
        UnityToolchainPreflight.Result Connection,
        string? Catalog,
        bool EvalConfirmed,
        string Evidence);

    internal static async Task<Capabilities> DiscoverAsync(
        string projectRoot, CancellationToken cancellationToken)
    {
        var catalogRun = await UnityCliProcessRunner.RunAsync(projectRoot,
            UnityToolchainPreflight.EditorProbeArguments(projectRoot),
            TimeSpan.FromSeconds(75), "UnityWorker command discovery",
            "UNITY_COMMAND_DISCOVERY_TIMEOUT", cancellationToken).ConfigureAwait(false);
        var connection = UnityToolchainPreflight.InterpretEditorProbe(
            catalogRun.ExitCode, catalogRun.Output, catalogRun.Error);
        var evidence = "CATALOG_EXIT=" + catalogRun.ExitCode +
            "; CATALOG_EVIDENCE=" + catalogRun.EvidencePath +
            "; CONNECTION=" + connection.Code;
        if (!connection.Ready)
            return new(connection, null, false, evidence);

        var catalog = connection.CommandCatalogJson;
        var evalFound = UnityToolchainPreflight.CatalogContainsCommand(catalog, "eval");
        if (!evalFound)
        {
            // The pre-existing Asset Importer also queries this documented
            // CLI capability. Query-only; no Editor expression is executed.
            var detail = await UnityCliProcessRunner.RunAsync(projectRoot,
                UnityAssetCliImporter.CatalogArguments(projectRoot),
                TimeSpan.FromSeconds(75), "UnityWorker eval capability query",
                "UNITY_EVAL_DISCOVERY_TIMEOUT", cancellationToken).ConfigureAwait(false);
            evalFound = UnityToolchainPreflight.DetailedCommandConfirmed(
                detail.ExitCode, detail.Output, "eval");
            evidence += "; EVAL_QUERY_EXIT=" + detail.ExitCode +
                "; EVAL_QUERY_EVIDENCE=" + detail.EvidencePath +
                "; EVAL_CONFIRMED=" + evalFound +
                "; EVAL_RESPONSE=" + Limit(detail.Error + " " + detail.Output, 1000);
        }
        return new(connection, catalog, evalFound, evidence);
    }

    // Common Editor transport entry for explicit specialist-chosen bridge
    // commands. CLI eval uses ExecuteEvalAsync below; unsupported commands
    // stay on their already verified CLI paths instead of being guessed.
    internal static Task<string> ExecuteBridgeAsync(
        string root, string action,
        string? type, string? method, string? menu, string? expected,
        CancellationToken cancellationToken)
    {
        return action switch
        {
            "editor_bridge_ping" => UnityEditorDirectBridge.ExecuteAsync(
                root, "ping", null, null, null, null,
                TimeSpan.FromSeconds(8), cancellationToken),
            "editor_bridge_scene_status" => UnityEditorDirectBridge.ExecuteAsync(
                root, "scene_status", null, null, null, null,
                TimeSpan.FromSeconds(12), cancellationToken),
            "editor_bridge_invoke" => UnityEditorDirectBridge.ExecuteAsync(
                root, "invoke", type, method, null, expected,
                TimeSpan.FromMinutes(10), cancellationToken),
            "editor_bridge_menu" => UnityEditorDirectBridge.ExecuteAsync(
                root, "menu", null, null, menu, expected,
                TimeSpan.FromMinutes(3), cancellationToken),
            _ => throw new ArgumentException("EDITOR_BRIDGE_UNKNOWN_ACTION")
        };
    }

    internal static async Task<string> ExecuteEvalAsync(
        string projectRoot, Capabilities capabilities,
        string expression, string expectedResult, CancellationToken cancellationToken)
    {
        if (!capabilities.Connection.Ready || !capabilities.EvalConfirmed)
            throw new InvalidOperationException(
                "UNITY_EDITOR_EVAL_UNVERIFIED: " + capabilities.Evidence);
        var run = await UnityCliProcessRunner.RunAsync(projectRoot,
            UnityEditorCommandResult.EvalArguments(projectRoot, expression),
            TimeSpan.FromMinutes(10), "UnityWorker verified Editor eval",
            "UNITY_EDITOR_EVAL_TIMEOUT_UNVERIFIED", cancellationToken).ConfigureAwait(false);
        return UnityEditorCommandResult.Verify(run, expectedResult) +
            Environment.NewLine + "EVIDENCE=" + run.EvidencePath;
    }

    private static string Limit(string text, int max) =>
        text.Length <= max ? text.Trim() : text[..max].Trim() + "...";
}
