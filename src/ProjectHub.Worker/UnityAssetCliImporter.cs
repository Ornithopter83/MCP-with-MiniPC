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

    internal static string[] ImportFileArguments(string root, string file) =>
        new[] { "command", "eval_file", file, "--project-path",
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
        if (files.Count == 0)
            return new(false, "ASSET_IMPORT_NO_FILES", 0);
        execute ??= (args, token) => ExecuteAsync(root, args, token);
        try
        {
            // The catalog query is diagnostic, never a hard gate: some CLI
            // versions list tags only or do not support query flags.
            var catalog = await execute(CatalogArguments(root), cancellationToken)
                .ConfigureAwait(false);
            if (IsTimeout(catalog))
                return new(false,
                    "UNITY_EDITOR_QUERY_TIMEOUT: 에디터 응답이 확인되지 않아 Import를 시작하지 않았습니다. HQ에서 재확인 또는 대체 계획을 판단하세요.",
                    0);

            var expression = BuildImportExpression(files);
            var response = await execute(ImportArguments(root, expression),
                cancellationToken).ConfigureAwait(false);
            // A timed-out mutation may already have imported assets in Editor.
            // Never blindly replay it using eval_file or the same eval command.
            if (IsTimeout(response))
                return new(false,
                    "UNITY_IMPORT_UNCERTAIN_TIMEOUT: Unity CLI 응답 시간초과. Editor 저장·Import 성공 여부가 미확인입니다. 기존 파일과 체크포인트를 보존하고 HQ가 검증 또는 대체를 결정해야 합니다.",
                    0);
            var interpreted = InterpretImportResult(response, files.Count, models);
            if (interpreted.Success || !IsCommandUnavailable(response))
                return interpreted;

            // eval was definitively NOT executed. The official eval_file
            // command is the second supported path (never replay an uncertain
            // timeout, editor disconnect or partial Import).
            var directory = Path.Combine(Path.GetFullPath(root),
                "temp", "ProjectHub", "asset-import");
            Directory.CreateDirectory(directory);
            var file = Path.Combine(directory,
                "eval-" + Guid.NewGuid().ToString("N") + ".cs");
            try
            {
                await File.WriteAllTextAsync(file, expression,
                    cancellationToken).ConfigureAwait(false);
                var fallback = await execute(ImportFileArguments(root, file),
                    cancellationToken).ConfigureAwait(false);
                if (IsTimeout(fallback))
                    return new(false,
                        "UNITY_IMPORT_UNCERTAIN_TIMEOUT: eval_file 응답 시간초과. 실행 여부 미확인으로 재호출하지 않습니다.",
                        0);
                return InterpretImportResult(fallback, files.Count, models);
            }
            finally
            {
                try { File.Delete(file); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
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

    private static bool IsTimeout(UnityAssetCliResult result) =>
        result.Error.Contains("UNITY_IMPORT_CLI_TIMEOUT",
            StringComparison.OrdinalIgnoreCase);

    private static bool IsCommandUnavailable(UnityAssetCliResult result)
    {
        var failure = result.Error + Environment.NewLine + result.Output;
        return (result.ExitCode != 0 ||
                failure.Contains("\"success\":false", StringComparison.OrdinalIgnoreCase)) &&
            (failure.Contains("UNKNOWN_COMMAND", StringComparison.OrdinalIgnoreCase) ||
             failure.Contains("COMMAND_NOT_FOUND", StringComparison.OrdinalIgnoreCase) ||
             failure.Contains("Unrecognized command", StringComparison.OrdinalIgnoreCase));
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
