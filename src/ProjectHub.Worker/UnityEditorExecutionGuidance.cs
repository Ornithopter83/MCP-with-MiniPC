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
        "- Worker auto-installs the approved Editor bridge at the start of a Unity job, but file installation is NOT proof the running Editor imported or compiled it.\n" +
        "- A NEW user Unity task closes one matching project GUI Editor gracefully and waits for exit, or starts Unity.exe if none is running. A newly observed project GUI is mandatory before HQ is called; CLI/Pipeline exit 6 alone does not block startup. Never force-kill or create duplicate Editors.\n" +
        "- Prefer fixed worker_command.editor_bridge_scene_status (or unity_query editor_state, bridge-first) for read-only scene inspection. Never write ad hoc eval merely to check isPlaying or scene roots.\n" +
        "- Only when the task explicitly requires reloading the CURRENT active scene, choose editor_bridge_sync_scene with its exact scene_path; this first copies the on-disk scene to Library/ProjectHub and backs up unsaved state under Assets/Editor/ProjectHub/SceneBackups/. Do not discard or silently save other scenes.\n" +
        "- Request editor_bridge_refresh_assets separately to invoke AssetDatabase.Refresh and inspect scene/import flags; a domain reload or timeout is UNVERIFIED, never automatic success.\n" +
        "- An empty Unity CLI command catalog does not prove the Editor is unusable.\n" +
        "- Otherwise use proven CLI eval (detailed eval capability query). Do not assume bridge installation, Unity CLI status or a scheduled rebuild proves successful execution.\n" +
        "- Before Native Game View capture, independently verify PlayMode=true and the intended active scene. Request native_capture explicitly; DESIGN completion never triggers auto capture.\n" +
        "- On entering PlayMode, record prior state, perform the delegated action, and restore PlayMode if this invocation entered it; do not toggle another user's session blindly.\n" +
        "- Unity Editor operations require the shared MCP gate; no foreground focus or repeated uncertain mutation.\n" +
        "- For specialist delegate_work choose 1..5 narrow concrete Assets/ or docs/ paths, including its future evidence/report subfolder; do not use top-level docs/ or Assets/.\n" +
        "- On timeout record command ID, evidence path and whether response is uncertain; never retry a possibly executed mutation without independent observation.\n" +
        "- COMPILING/PlayMode/Editor/root objects and BuildReport are distinct observations; report only evidence you actually obtained.";
}
