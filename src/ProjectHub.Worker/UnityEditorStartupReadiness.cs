using System.IO;

namespace ProjectHub.Worker;

/// <summary>
/// A Unity window is not command readiness. Only fresh Editor-main-thread
/// responses prove the approved bridge can execute commands after Import.
/// </summary>
internal static class UnityEditorStartupReadiness
{
    internal sealed record Result(bool Ready, string Code, string Detail);

    internal static bool IsCommandReady(
        string ping, string sceneStatus, out string evidence)
    {
        evidence = "";
        if (!ping.StartsWith(
                "EDITOR_BRIDGE_CONFIRMED: EDITOR_RESPONDED; VALUE=",
                StringComparison.Ordinal))
        {
            evidence = "EDITOR_PING_NOT_CONFIRMED";
            return false;
        }
        const string prefix =
            "EDITOR_BRIDGE_CONFIRMED: EDITOR_SCENE_STATE; VALUE=";
        if (!sceneStatus.StartsWith(prefix, StringComparison.Ordinal))
        {
            evidence = "EDITOR_SCENE_COMMAND_NOT_CONFIRMED";
            return false;
        }
        // Read only the returned VALUE, not text in a diagnostic detail or
        // an echoed request. Unity returns True/False with invariant casing.
        var valueEnd = sceneStatus.IndexOf("; EVIDENCE=", prefix.Length,
            StringComparison.Ordinal);
        if (valueEnd < 0) valueEnd = sceneStatus.Length;
        var value = sceneStatus[prefix.Length..valueEnd];
        var fields = value.Split(';');
        bool Flag(string key, string expected) => fields.Any(field =>
            string.Equals(field, key + "=" + expected,
                StringComparison.Ordinal));
        if (!Flag("ACTIVE_LOADED", "True"))
        {
            evidence = "EDITOR_ACTIVE_SCENE_NOT_LOADED";
            return false;
        }
        if (!Flag("COMPILING", "False") || !Flag("UPDATING", "False"))
        {
            evidence = "EDITOR_COMPILING_OR_IMPORTING";
            return false;
        }
        evidence = "EDITOR_MAIN_THREAD_PING_AND_SCENE_COMMAND_CONFIRMED";
        return true;
    }

    internal static async Task<Result> WaitAsync(
        string projectRoot, CancellationToken cancellationToken,
        TimeSpan? maximumWait = null)
    {
        var timeout = maximumWait ?? TimeSpan.FromMinutes(5);
        var deadline = DateTime.UtcNow.Add(timeout);
        var last = "WAITING_FOR_EDITOR_IMPORT_AND_MAIN_THREAD";
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var ping = await UnityEditorDirectBridge.ExecuteAsync(
                    projectRoot, "ping", null, null, null, null,
                    TimeSpan.FromSeconds(4), cancellationToken).ConfigureAwait(false);
                var status = await UnityEditorDirectBridge.ExecuteAsync(
                    projectRoot, "scene_status", null, null, null, null,
                    TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
                if (IsCommandReady(ping, status, out var evidence))
                    return new(true, "EDITOR_COMMAND_READY",
                        evidence + "; " + status);
                last = evidence;
            }
            catch (Exception ex) when (ex is TimeoutException or IOException or
                UnauthorizedAccessException or InvalidOperationException or
                System.Text.Json.JsonException)
            {
                last = ex.GetType().Name + ": " + Limit(ex.Message);
                if (ex is InvalidOperationException &&
                    (ex.Message.Contains("SOURCE_CHANGED", StringComparison.Ordinal) ||
                     ex.Message.Contains("NOT_INSTALLED", StringComparison.Ordinal)))
                    return new(false, "EDITOR_BRIDGE_INSTALL_BLOCKED", last);
            }
            var remaining = deadline - DateTime.UtcNow;
            if (remaining > TimeSpan.Zero)
                await Task.Delay(remaining < TimeSpan.FromSeconds(2)
                    ? remaining : TimeSpan.FromSeconds(2),
                    cancellationToken).ConfigureAwait(false);
        }
        return new(false, "EDITOR_COMMAND_NOT_READY",
            "Unity GUI may be open but Editor import/compile/bridge command " +
            "did not complete. Last observed: " + last);
    }

    private static string Limit(string text) =>
        text.Length <= 800 ? text : text[..800];
}
