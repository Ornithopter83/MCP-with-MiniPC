using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;

namespace ProjectHub.Worker;

internal sealed record UnityAssetCliResult(int ExitCode, string Output, string Error);
internal sealed record UnityAssetImportResult(bool Success, string Detail, int VerifiedAssets);

/// <summary>
/// Uses the already-running project's official Unity Pipeline command surface.
/// Never starts a second Editor, edits YAML or trusts .meta existence alone.
/// </summary>
internal static class UnityAssetCliImporter
{
    internal static string[] CatalogArguments(string root) =>
        new[] { "command", "--project-path", Path.GetFullPath(root),
            "--query", "eval", "--detail", "full", "--format", "json" };

    internal static string[] ImportArguments(string root, string expression) =>
        new[] { "command", "eval", expression, "--project-path",
            Path.GetFullPath(root), "--format", "json", "--timeout", "180" };

    internal static string BuildImportExpression(IReadOnlyList<AssetFileEvidence> files)
    {
        var paths = string.Join(", ", files.Select(f =>
            JsonSerializer.Serialize(f.RelativePath)));
        // Editor-side return value is the evidence, not a CLI process exit 0.
        // Import and GUID/Importer/Model validity are all checked on Editor main thread.
        return """
            UnityEditor.AssetDatabase.Refresh(UnityEditor.ImportAssetOptions.ForceSynchronousImport);
            var projectHubPaths = new string[] { __PROJECTHUB_FILES__ };
            int projectHubModels = 0;
            foreach (var projectHubPath in projectHubPaths) {
                UnityEditor.AssetDatabase.ImportAsset(projectHubPath,
                    UnityEditor.ImportAssetOptions.ForceSynchronousImport);
                var projectHubImporter = UnityEditor.AssetImporter.GetAtPath(projectHubPath);
                var projectHubGuid = UnityEditor.AssetDatabase.AssetPathToGUID(projectHubPath);
                if (projectHubImporter == null || string.IsNullOrEmpty(projectHubGuid))
                    throw new System.InvalidOperationException("PROJECTHUB_IMPORT_UNVERIFIED:" + projectHubPath);
                if (projectHubPath.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase) ||
                    projectHubPath.EndsWith(".obj", System.StringComparison.OrdinalIgnoreCase) ||
                    projectHubPath.EndsWith(".gltf", System.StringComparison.OrdinalIgnoreCase) ||
                    projectHubPath.EndsWith(".glb", System.StringComparison.OrdinalIgnoreCase)) {
                    if (UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(projectHubPath) == null)
                        throw new System.InvalidOperationException("PROJECTHUB_MODEL_NOT_LOADED:" + projectHubPath);
                    projectHubModels++;
                }
            }
            return "PROJECTHUB_ASSET_IMPORT_OK|" + projectHubPaths.Length + "|" + projectHubModels;
            """.Replace("__PROJECTHUB_FILES__", paths, StringComparison.Ordinal);
    }

    internal static UnityAssetImportResult InterpretImportResult(
        UnityAssetCliResult command, int expectedFiles, int expectedModels)
    {
        if (command.ExitCode != 0)
            return new(false, "UNITY_IMPORT_CLI_EXIT_" + command.ExitCode + ": " +
                Trim(command.Error.Length > 0 ? command.Error : command.Output), 0);
        try
        {
            using var response = JsonDocument.Parse(command.Output);
            if (!response.RootElement.TryGetProperty("success", out var success) ||
                success.ValueKind != JsonValueKind.True)
                return new(false, "UNITY_IMPORT_CLI_REJECTED: " + Trim(command.Output), 0);

            // The marker must be the Editor's JSON result value, not a copy
            // of the source expression that happens to contain the marker.
            if (!response.RootElement.TryGetProperty("data", out var data))
                return new(false, "UNITY_IMPORT_RESULT_MISSING", 0);

            if (!TryFindReturnValue(data, out var result) ||
                !result.StartsWith("PROJECTHUB_ASSET_IMPORT_OK|", StringComparison.Ordinal))
                return new(false, "UNITY_IMPORT_RESULT_UNVERIFIED: " + Trim(data.ToString()), 0);

            var values = result.Split('|');
            if (values.Length != 3 ||
                !int.TryParse(values[1], out var fileCount) ||
                !int.TryParse(values[2], out var modelCount) ||
                fileCount != expectedFiles || modelCount != expectedModels)
                return new(false, "UNITY_IMPORT_EVIDENCE_MISMATCH: " + Trim(result), 0);
            return new(true, "UNITY_IMPORT_VERIFIED: " + fileCount +
                " assets, " + modelCount + " models (Importer/GUID/loaded model)", fileCount);
        }
        catch (JsonException)
        {
            return new(false, "UNITY_IMPORT_INVALID_JSON: " + Trim(command.Output), 0);
        }
    }

    private static bool TryFindReturnValue(JsonElement data, out string result)
    {
        result = string.Empty;
        if (data.ValueKind == JsonValueKind.String)
        {
            result = data.GetString() ?? string.Empty;
            return result.StartsWith("PROJECTHUB_ASSET_IMPORT_OK|", StringComparison.Ordinal);
        }
        if (data.ValueKind == JsonValueKind.Object)
        {
            // Common CLI response envelopes are data.result, data.value or
            // data.output; reject source/command fields because they echo code.
            foreach (var name in new[] { "result", "value", "output" })
                if (data.TryGetProperty(name, out var value) &&
                    TryFindReturnValue(value, out result)) return true;
        }
        return false;
    }

    internal static async Task<UnityAssetImportResult> RunAsync(
        string root, IReadOnlyList<AssetFileEvidence> files, int models,
        CancellationToken cancellationToken,
        Func<IReadOnlyList<string>, CancellationToken, Task<UnityAssetCliResult>>? execute = null)
    {
        if (files.Count == 0 || models == 0)
            return new(false, "ASSET_IMPORT_NO_MODEL_FILES", 0);
        execute ??= (args, token) => ExecuteAsync(root, args, token);
        try
        {
            // Command listing is diagnostic. Older CLI versions may reject
            // query filters; the actual named eval invocation remains authoritative.
            var catalog = await execute(CatalogArguments(root), cancellationToken)
                .ConfigureAwait(false);
            if (catalog.ExitCode != 0)
                return new(false, "UNITY_PIPELINE_CATALOG_UNAVAILABLE: " +
                    Trim(catalog.Error.Length > 0 ? catalog.Error : catalog.Output), 0);

            var args = ImportArguments(root, BuildImportExpression(files));
            var response = await execute(args, cancellationToken).ConfigureAwait(false);
            return InterpretImportResult(response, files.Count, models);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                 InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return new(false, "UNITY_IMPORT_COMMAND_ERROR: " + ex.Message, 0);
        }
    }

    private static async Task<UnityAssetCliResult> ExecuteAsync(
        string root, IReadOnlyList<string> args, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "unity",
            WorkingDirectory = Path.GetFullPath(root),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var arg in args) psi.ArgumentList.Add(arg);

        using var job = new WorkerChildProcessJob("UnityWorker asset import");
        using var child = job.Start(psi, cancellationToken);
        var output = child.StandardOutput!.ReadToEndAsync(cancellationToken);
        var errors = child.StandardError!.ReadToEndAsync(cancellationToken);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, timeout.Token);
        try
        {
            await child.Process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            job.Dispose();
            return new(-1, string.Empty, "UNITY_IMPORT_CLI_TIMEOUT");
        }
        return new(child.Process.ExitCode,
            await output.ConfigureAwait(false), await errors.ConfigureAwait(false));
    }

    private static string Trim(string value) =>
        value.Length <= 650 ? value.Trim() : value[..650].Trim() + "...";
}
