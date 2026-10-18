using System.IO;
using System.Text.Json;

namespace ProjectHub.Worker;

/// <summary>
/// Verifies an Editor-side return value, never source echo or CLI exit alone.
/// The caller (specialist) chooses the requested expression and expected result.
/// </summary>
internal static class UnityEditorCommandResult
{
    internal static string[] EvalArguments(string root, string expression) =>
    [
        "command", "eval", expression,
        "--project-path", Path.GetFullPath(root),
        "--format", "json", "--timeout", "600"
    ];

    internal static string Verify(
        UnityAssetCliResult command, string expectedResult)
    {
        if (command.ExitCode != 0)
            throw new InvalidOperationException(
                "UNITY_EDITOR_COMMAND_EXIT_" + command.ExitCode + ": " +
                Short(command.Error + Environment.NewLine + command.Output));
        try
        {
            using var json = JsonDocument.Parse(command.Output);
            if (!json.RootElement.TryGetProperty("success", out var success) ||
                success.ValueKind != JsonValueKind.True ||
                !json.RootElement.TryGetProperty("data", out var data) ||
                !TryReturnValue(data, out var actual))
                throw new InvalidOperationException(
                    "UNITY_EDITOR_RESULT_UNVERIFIED: " + Short(command.Output));
            if (!string.Equals(actual, expectedResult, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "UNITY_EDITOR_RESULT_MISMATCH: actual=" + Short(actual) +
                    "; expected=" + Short(expectedResult));
            return "UNITY_EDITOR_RESULT_CONFIRMED: " + Short(actual) +
                Environment.NewLine + Short(command.Output);
        }
        catch (JsonException)
        {
            throw new InvalidOperationException(
                "UNITY_EDITOR_INVALID_JSON: " + Short(command.Output));
        }
    }

    private static bool TryReturnValue(JsonElement data, out string value)
    {
        value = string.Empty;
        if (data.ValueKind == JsonValueKind.String)
        {
            value = data.GetString() ?? string.Empty;
            return true;
        }
        if (data.ValueKind != JsonValueKind.Object) return false;
        foreach (var key in new[] { "result", "value", "output" })
        {
            if (data.TryGetProperty(key, out var child) &&
                TryReturnValue(child, out value)) return true;
        }
        return false;
    }

    private static string Short(string text) =>
        text.Length <= 4000 ? text.Trim() : text[..4000].Trim();
}
