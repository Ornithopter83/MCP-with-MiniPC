using System.IO;

namespace ProjectHub.Worker;

public sealed record GitWorkspaceBootstrapState(
    bool Success,
    string? ErrorCode,
    string WorkingDirectory,
    string RepositoryRoot,
    string? Branch,
    string? HeadCommit,
    bool InitializedNow,
    bool IsDirty,
    bool NeedsManagedIgnoreUpdate = false,
    bool NeedsManagedIndexCleanup = false)
{
    public bool HasHead => !string.IsNullOrWhiteSpace(HeadCommit);
    public bool NeedsBaseline =>
        Success &&
        (!HasHead || IsDirty || NeedsManagedIgnoreUpdate || NeedsManagedIndexCleanup);
}

public sealed class GitWorkspaceBootstrapper
{
    private const string ManagedIgnoreStart = "# >>> ProjectHub managed";
    private const string ManagedIgnoreEnd = "# <<< ProjectHub managed";
    private const string ManagedPresetMarker = "# ProjectHub preset:";
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan WriteTimeout = TimeSpan.FromMinutes(2);
    private readonly IGitWorktreeCommandRunner _runner;

    public GitWorkspaceBootstrapper(IGitWorktreeCommandRunner? runner = null)
    {
        _runner = runner ?? new ProcessGitWorktreeCommandRunner();
    }

    public async Task<GitWorkspaceBootstrapState> PrepareAsync(
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(workingDirectory) || !Directory.Exists(workingDirectory))
            return Failure("GIT_BOOTSTRAP_WORKSPACE_MISSING", workingDirectory);

        var workspace = Path.GetFullPath(workingDirectory);
        var rootResult = await RunAsync(
            workspace,
            ReadTimeout,
            cancellationToken,
            "rev-parse",
            "--show-toplevel").ConfigureAwait(false);

        var initializedNow = false;
        if (rootResult.ExitCode != 0 || string.IsNullOrWhiteSpace(rootResult.StandardOutput))
        {
            var initResult = await RunAsync(
                workspace,
                WriteTimeout,
                cancellationToken,
                "init").ConfigureAwait(false);

            if (initResult.ExitCode != 0)
            {
                return Failure(
                    initResult.TimedOut ? "GIT_BOOTSTRAP_INIT_TIMEOUT"
                        : initResult.Canceled ? "GIT_BOOTSTRAP_INIT_CANCELED"
                        : "GIT_BOOTSTRAP_INIT_FAILED",
                    workspace);
            }

            initializedNow = true;
            rootResult = await RunAsync(
                workspace,
                ReadTimeout,
                cancellationToken,
                "rev-parse",
                "--show-toplevel").ConfigureAwait(false);
        }

        if (rootResult.ExitCode != 0 || string.IsNullOrWhiteSpace(rootResult.StandardOutput))
            return Failure("GIT_BOOTSTRAP_ROOT_UNAVAILABLE", workspace);

        var repositoryRoot = Path.GetFullPath(FirstLine(rootResult.StandardOutput));

        var longPathsResult = await RunAsync(
            repositoryRoot,
            WriteTimeout,
            cancellationToken,
            "config",
            "--local",
            "core.longpaths",
            "true").ConfigureAwait(false);

        if (longPathsResult.ExitCode != 0)
        {
            return RepositoryFailure(
                longPathsResult.TimedOut ? "GIT_BOOTSTRAP_LONGPATHS_CONFIG_TIMEOUT"
                    : longPathsResult.Canceled ? "GIT_BOOTSTRAP_LONGPATHS_CONFIG_CANCELED"
                    : "GIT_BOOTSTRAP_LONGPATHS_CONFIG_FAILED",
                workspace,
                repositoryRoot,
                initializedNow);
        }

        bool needsManagedIgnoreUpdate;
        try
        {
            needsManagedIgnoreUpdate = NeedsManagedGitIgnoreUpdate(
                repositoryRoot,
                initializedNow);
        }
        catch (IOException)
        {
            return RepositoryFailure(
                "GIT_IGNORE_INSPECTION_FAILED",
                workspace,
                repositoryRoot,
                initializedNow);
        }
        catch (UnauthorizedAccessException)
        {
            return RepositoryFailure(
                "GIT_IGNORE_INSPECTION_FAILED",
                workspace,
                repositoryRoot,
                initializedNow);
        }

        var trackedManagedPaths = await RunAsync(
            repositoryRoot,
            ReadTimeout,
            cancellationToken,
            BuildManagedPathScanArguments()).ConfigureAwait(false);

        if (trackedManagedPaths.ExitCode != 0)
        {
            return RepositoryFailure(
                "GIT_BOOTSTRAP_MANAGED_PATH_SCAN_FAILED",
                workspace,
                repositoryRoot,
                initializedNow);
        }

        var needsManagedIndexCleanup =
            !string.IsNullOrWhiteSpace(trackedManagedPaths.StandardOutput);

        var branchResult = await RunAsync(
            repositoryRoot,
            ReadTimeout,
            cancellationToken,
            "symbolic-ref",
            "--quiet",
            "--short",
            "HEAD").ConfigureAwait(false);

        if (branchResult.ExitCode != 0 || string.IsNullOrWhiteSpace(branchResult.StandardOutput))
        {
            return new GitWorkspaceBootstrapState(
                false,
                "GIT_BOOTSTRAP_ATTACHED_BRANCH_REQUIRED",
                workspace,
                repositoryRoot,
                null,
                null,
                initializedNow,
                false,
                needsManagedIgnoreUpdate,
                needsManagedIndexCleanup);
        }

        var branch = FirstLine(branchResult.StandardOutput);

        var headResult = await RunAsync(
            repositoryRoot,
            ReadTimeout,
            cancellationToken,
            "rev-parse",
            "--verify",
            "HEAD").ConfigureAwait(false);

        var headCommit = headResult.ExitCode == 0 && !string.IsNullOrWhiteSpace(headResult.StandardOutput)
            ? FirstLine(headResult.StandardOutput)
            : null;

        var statusResult = await RunAsync(
            repositoryRoot,
            ReadTimeout,
            cancellationToken,
            "status",
            "--porcelain=v1",
            "--untracked-files=all").ConfigureAwait(false);

        if (statusResult.ExitCode != 0)
        {
            return new GitWorkspaceBootstrapState(
                false,
                "GIT_BOOTSTRAP_STATUS_UNAVAILABLE",
                workspace,
                repositoryRoot,
                branch,
                headCommit,
                initializedNow,
                false,
                needsManagedIgnoreUpdate,
                needsManagedIndexCleanup);
        }

        return new GitWorkspaceBootstrapState(
            true,
            null,
            workspace,
            repositoryRoot,
            branch,
            headCommit,
            initializedNow,
            !string.IsNullOrWhiteSpace(statusResult.StandardOutput),
            needsManagedIgnoreUpdate,
            needsManagedIndexCleanup);
    }

    public async Task<GitWorkspaceBootstrapState> CreateBaselineAsync(
        GitWorkspaceBootstrapState state,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (!state.Success)
            return state;
        if (!state.NeedsBaseline)
            return state;

        if (state.NeedsManagedIgnoreUpdate)
        {
            try
            {
                EnsureManagedGitIgnore(
                    state.RepositoryRoot,
                    state.InitializedNow || !state.HasHead);
            }
            catch (IOException)
            {
                return state with
                {
                    Success = false,
                    ErrorCode = "GIT_IGNORE_UPDATE_FAILED"
                };
            }
            catch (UnauthorizedAccessException)
            {
                return state with
                {
                    Success = false,
                    ErrorCode = "GIT_IGNORE_UPDATE_FAILED"
                };
            }
        }

        if (state.NeedsManagedIndexCleanup)
        {
            var cleanupResult = await RunAsync(
                state.RepositoryRoot,
                WriteTimeout,
                cancellationToken,
                BuildManagedIndexCleanupArguments()).ConfigureAwait(false);

            if (cleanupResult.ExitCode != 0)
            {
                return state with
                {
                    Success = false,
                    ErrorCode = cleanupResult.TimedOut ? "GIT_MANAGED_INDEX_CLEANUP_TIMEOUT"
                        : cleanupResult.Canceled ? "GIT_MANAGED_INDEX_CLEANUP_CANCELED"
                        : "GIT_MANAGED_INDEX_CLEANUP_FAILED"
                };
            }
        }

        var addResult = await RunAsync(
            state.RepositoryRoot,
            WriteTimeout,
            cancellationToken,
            "add",
            "--all").ConfigureAwait(false);

        if (addResult.ExitCode != 0)
            return state with
            {
                Success = false,
                ErrorCode = addResult.TimedOut ? "GIT_BASELINE_ADD_TIMEOUT"
                    : addResult.Canceled ? "GIT_BASELINE_ADD_CANCELED"
                    : "GIT_BASELINE_ADD_FAILED"
            };

        var message = state.HasHead
            ? "ProjectHub baseline before parallel work"
            : "ProjectHub initial baseline";

        var commitResult = await RunAsync(
            state.RepositoryRoot,
            WriteTimeout,
            cancellationToken,
            "-c",
            "user.name=ProjectHub",
            "-c",
            "user.email=projecthub@local",
            "commit",
            "--allow-empty",
            "--no-gpg-sign",
            "-m",
            message).ConfigureAwait(false);

        if (commitResult.ExitCode != 0)
            return state with
            {
                Success = false,
                ErrorCode = commitResult.TimedOut ? "GIT_BASELINE_COMMIT_TIMEOUT"
                    : commitResult.Canceled ? "GIT_BASELINE_COMMIT_CANCELED"
                    : "GIT_BASELINE_COMMIT_FAILED"
            };

        var headResult = await RunAsync(
            state.RepositoryRoot,
            ReadTimeout,
            cancellationToken,
            "rev-parse",
            "--verify",
            "HEAD").ConfigureAwait(false);

        if (headResult.ExitCode != 0 || string.IsNullOrWhiteSpace(headResult.StandardOutput))
            return state with { Success = false, ErrorCode = "GIT_BASELINE_HEAD_UNAVAILABLE" };

        var statusResult = await RunAsync(
            state.RepositoryRoot,
            ReadTimeout,
            cancellationToken,
            "status",
            "--porcelain=v1",
            "--untracked-files=all").ConfigureAwait(false);

        if (statusResult.ExitCode != 0)
            return state with { Success = false, ErrorCode = "GIT_BASELINE_STATUS_UNAVAILABLE" };
        if (!string.IsNullOrWhiteSpace(statusResult.StandardOutput))
            return state with
            {
                Success = false,
                ErrorCode = "GIT_BASELINE_NOT_CLEAN",
                HeadCommit = FirstLine(headResult.StandardOutput),
                IsDirty = true
            };

        return state with
        {
            Success = true,
            ErrorCode = null,
            HeadCommit = FirstLine(headResult.StandardOutput),
            IsDirty = false,
            NeedsManagedIgnoreUpdate = false,
            NeedsManagedIndexCleanup = false
        };
    }

    private static string[] BuildManagedPathScanArguments()
        => new[]
        {
            "ls-files",
            "--",
            ".projecthub",
            ".verification-appdata",
            ".projecthub-worktrees"
        };

    private static string[] BuildManagedIndexCleanupArguments()
        => new[]
        {
            "rm",
            "-r",
            "--cached",
            "--ignore-unmatch",
            "--",
            ".projecthub",
            ".verification-appdata",
            ".projecthub-worktrees"
        };

    private static bool NeedsManagedGitIgnoreUpdate(
        string repositoryRoot,
        bool initializedNow)
    {
        var path = Path.Combine(repositoryRoot, ".gitignore");
        var existing = File.Exists(path)
            ? File.ReadAllText(path)
            : string.Empty;
        var updated = BuildUpdatedGitIgnore(
            repositoryRoot,
            existing,
            initializedNow);
        return !string.Equals(existing, updated, StringComparison.Ordinal);
    }

    private static void EnsureManagedGitIgnore(
        string repositoryRoot,
        bool initializedNow)
    {
        var path = Path.Combine(repositoryRoot, ".gitignore");
        var existing = File.Exists(path)
            ? File.ReadAllText(path)
            : string.Empty;
        var updated = BuildUpdatedGitIgnore(
            repositoryRoot,
            existing,
            initializedNow);

        if (!string.Equals(existing, updated, StringComparison.Ordinal))
            File.WriteAllText(path, updated);
    }

    private static string BuildUpdatedGitIgnore(
        string repositoryRoot,
        string existing,
        bool initializedNow)
    {
        var normalized = NormalizeNewlines(existing);
        var preserveManagedPresets =
            normalized.Contains(ManagedPresetMarker, StringComparison.Ordinal);
        var block = BuildManagedIgnoreBlock(
            repositoryRoot,
            initializedNow || preserveManagedPresets);

        var start = normalized.IndexOf(ManagedIgnoreStart, StringComparison.Ordinal);
        if (start >= 0)
        {
            var end = normalized.IndexOf(
                ManagedIgnoreEnd,
                start,
                StringComparison.Ordinal);

            if (end >= 0)
            {
                end += ManagedIgnoreEnd.Length;
                var parts = new List<string>();
                var prefix = normalized[..start].Trim('\n');
                var suffix = normalized[end..].Trim('\n');

                if (prefix.Length > 0)
                    parts.Add(prefix);
                parts.Add(block);
                if (suffix.Length > 0)
                    parts.Add(suffix);

                normalized = string.Join("\n\n", parts);
            }
            else
            {
                normalized = normalized.TrimEnd('\n') + "\n\n" + block;
            }
        }
        else
        {
            normalized = normalized.TrimEnd('\n');
            if (normalized.Length > 0)
                normalized += "\n\n";
            normalized += block;
        }

        normalized = normalized.TrimEnd('\n') + "\n";
        return normalized.Replace("\n", Environment.NewLine);
    }

    private static string BuildManagedIgnoreBlock(
        string repositoryRoot,
        bool includeProjectPresets)
    {
        var lines = new List<string>
        {
            ManagedIgnoreStart,
            ".projecthub/",
            ".verification-appdata/",
            ".projecthub-worktrees/",
            "",
            "# OS 임시 파일",
            ".DS_Store",
            "Thumbs.db",
            "Desktop.ini",
            "",
            "# 편집기 임시 파일",
            "*.swp",
            "*.swo",
            "*~"
        };

        if (includeProjectPresets)
        {
            foreach (var preset in DetectProjectPresets(repositoryRoot))
            {
                lines.Add("");
                lines.Add($"{ManagedPresetMarker} {preset.Name}");
                lines.AddRange(preset.Patterns);
            }
        }

        lines.Add(ManagedIgnoreEnd);
        return string.Join("\n", lines);
    }

    private static IReadOnlyList<GitIgnorePreset> DetectProjectPresets(string repositoryRoot)
    {
        var presets = new List<GitIgnorePreset>();

        if (File.Exists(Path.Combine(repositoryRoot, "project.godot")))
            presets.Add(new("Godot", new[] { ".godot/" }));

        if (Directory.Exists(Path.Combine(repositoryRoot, "Assets")) &&
            Directory.Exists(Path.Combine(repositoryRoot, "ProjectSettings")))
        {
            presets.Add(new(
                "Unity",
                new[] { "Library/", "Temp/", "Logs/", "Obj/", "UserSettings/" }));
        }

        if (Directory.EnumerateFiles(repositoryRoot, "*.sln", SearchOption.TopDirectoryOnly).Any() ||
            Directory.EnumerateFiles(repositoryRoot, "*.csproj", SearchOption.TopDirectoryOnly).Any())
        {
            presets.Add(new(".NET", new[] { "bin/", "obj/" }));
        }

        if (File.Exists(Path.Combine(repositoryRoot, "package.json")))
            presets.Add(new("Node", new[] { "node_modules/" }));

        return presets;
    }

    private Task<GitCommandResult> RunAsync(
        string workingDirectory,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        params string[] arguments)
        => _runner.RunAsync(workingDirectory, arguments, timeout, cancellationToken);

    private Task<GitCommandResult> RunAsync(
        string workingDirectory,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        IReadOnlyList<string> arguments)
        => _runner.RunAsync(workingDirectory, arguments, timeout, cancellationToken);

    private static GitWorkspaceBootstrapState Failure(
        string errorCode,
        string? workingDirectory)
    {
        var workspace = string.IsNullOrWhiteSpace(workingDirectory)
            ? string.Empty
            : Path.GetFullPath(workingDirectory);
        return new GitWorkspaceBootstrapState(
            false,
            errorCode,
            workspace,
            workspace,
            null,
            null,
            false,
            false);
    }

    private static GitWorkspaceBootstrapState RepositoryFailure(
        string errorCode,
        string workspace,
        string repositoryRoot,
        bool initializedNow)
        => new(
            false,
            errorCode,
            workspace,
            repositoryRoot,
            null,
            null,
            initializedNow,
            false);

    private static string NormalizeNewlines(string value)
        => (value ?? string.Empty)
            .Replace("\r\n", "\n")
            .Replace('\r', '\n');

    private static string FirstLine(string value)
        => value
            .Replace("\r\n", "\n")
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault()?.Trim() ?? string.Empty;

    private sealed record GitIgnorePreset(
        string Name,
        IReadOnlyList<string> Patterns);
}
