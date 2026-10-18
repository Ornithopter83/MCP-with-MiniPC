namespace ProjectHub.Worker;

/// <summary>
/// Single execution-policy guidance shared by all Unity roles and temporary WORK.
/// Worker never chooses which Editor operation or visual treatment to perform.
/// </summary>
internal static class UnityEditorExecutionGuidance
{
    internal const string Text =
        "UNITY_EDITOR_EXECUTION_POLICY:\n" +
        "- Choose the Editor operation and exact expected result yourself; Worker only transmits and records evidence.\n" +
        "- Use worker_command.editor_bridge_ping/scene_status when a verified direct bridge is installed. An empty Unity CLI command catalog does not prove the Editor is unusable.\n" +
        "- Otherwise use proven CLI eval (detailed eval capability query). Do not assume bridge installation, Unity CLI status or a scheduled rebuild proves successful execution.\n" +
        "- Before Native Game View capture, independently verify PlayMode=true and the intended active scene. Request native_capture explicitly; DESIGN completion never triggers auto capture.\n" +
        "- On entering PlayMode, record prior state, perform the delegated action, and restore PlayMode if this invocation entered it; do not toggle another user's session blindly.\n" +
        "- Unity Editor operations require the shared MCP gate; no foreground focus or repeated uncertain mutation.\n" +
        "- For specialist delegate_work choose 1..5 narrow concrete Assets/ or docs/ paths, including its future evidence/report subfolder; do not use top-level docs/ or Assets/.\n" +
        "- On timeout record command ID, evidence path and whether response is uncertain; never retry a possibly executed mutation without independent observation.\n" +
        "- COMPILING/PlayMode/Editor/root objects and BuildReport are distinct observations; report only evidence you actually obtained.";
}
