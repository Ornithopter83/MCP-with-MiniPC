using System.Diagnostics;
using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class WorkerRepositorySettingsTests
{
    [Fact]
    public void ChangingOnlyRoleModel_DoesNotClearInitiallyUnconfiguredRepositoryField()
    {
        using var fixture = new RepositoryFixture();
        var remote = "https://github.com/example/first-project.git";
        fixture.SetRemote("origin", remote);

        var previous = new WorkerTargetSettings(
            ManualRepositoryUrl: null,
            RepositoryUrlSource: null,
            ManualWorkingDirectory: fixture.Root,
            HighLevel: new WorkerAiRoleSettings(Model: "gpt-6-luna", Reasoning: "high"));

        // The UI read-only URL can be blank after a settings refresh.
        // Saving a new model must instead read the existing Git origin.
        var updatedModel = previous with
        {
            HighLevel = previous.EffectiveHighLevel with { Model = "gpt-6-sol" }
        };
        var saved = WorkerTargetConfiguration.ResolveRepositorySettingsForSave(
            updatedModel, fixture.Root);

        Assert.Equal(remote, saved.ManualRepositoryUrl);
        Assert.Equal("AUTO_GIT_REMOTE", saved.RepositoryUrlSource);
        Assert.Equal("gpt-6-sol", saved.EffectiveHighLevel.Model);
        Assert.Equal(remote, fixture.Git("remote", "get-url", "origin").Trim());

        // Applying another model change must also keep the repository URL.
        var next = WorkerTargetConfiguration.ResolveRepositorySettingsForSave(
            saved with
            {
                Design = new WorkerAiRoleSettings(Model: "gpt-6-luna", Reasoning: "medium")
            }, fixture.Root);
        Assert.Equal(remote, next.ManualRepositoryUrl);
    }

    [Fact]
    public void SwitchingWorkspace_UsesNewOriginsNotOldRepositories()
    {
        using var original = new RepositoryFixture();
        using var selected = new RepositoryFixture();
        original.SetRemote("origin", "https://github.com/example/old.git");
        selected.SetRemote("origin", "https://github.com/example/new.git");
        var current = new WorkerTargetSettings(
            "https://github.com/example/old.git",
            "AUTO_GIT_REMOTE", ManualWorkingDirectory: original.Root);

        var changed = WorkerTargetConfiguration.ResolveRepositorySettingsForSave(
            current, selected.Root);
        Assert.Equal("https://github.com/example/new.git", changed.ManualRepositoryUrl);
        Assert.Equal("AUTO_GIT_REMOTE", changed.RepositoryUrlSource);
        Assert.Equal(selected.Root, changed.ManualWorkingDirectory);
    }

    [Fact]
    public void TemporaryOriginLookupFailure_DoesNotEraseSameWorkspaceSettings()
    {
        using var fixture = new RepositoryFixture(createGit: false);
        var existing = new WorkerTargetSettings(
            "https://github.com/example/saved.git",
            "AUTO_GIT_REMOTE", ManualWorkingDirectory: fixture.Root);

        var saved = WorkerTargetConfiguration.ResolveRepositorySettingsForSave(
            existing with { HighLevel = new WorkerAiRoleSettings(Model: "gpt-6-sol") },
            fixture.Root);

        Assert.Equal(existing.ManualRepositoryUrl, saved.ManualRepositoryUrl);
        Assert.Equal(existing.RepositoryUrlSource, saved.RepositoryUrlSource);
    }

    [Fact]
    public void DifferentFolderWithoutOrigin_DoesNotInheritOldRepository()
    {
        using var fixture = new RepositoryFixture();
        using var different = new RepositoryFixture(createGit: false);
        fixture.SetRemote("origin", "https://github.com/example/old.git");
        var settings = new WorkerTargetSettings(
            "https://github.com/example/old.git",
            "AUTO_GIT_REMOTE", ManualWorkingDirectory: fixture.Root);

        var saved = WorkerTargetConfiguration.ResolveRepositorySettingsForSave(
            settings, different.Root);
        Assert.Null(saved.ManualRepositoryUrl);
        Assert.Null(saved.RepositoryUrlSource);
    }

    [Fact]
    public void LegacyCentralServerSettingsAreIgnoredWithoutLosingGitOrModels()
    {
        const string legacy = """
            {
              "manualRepositoryUrl": "https://github.com/example/repo.git",
              "repositoryUrlSource": "AUTO_GIT_REMOTE",
              "manualServerBaseUrl": "https://projecthub.example.invalid",
              "serverBaseUrlSource": "MANUAL",
              "highLevel": { "model": "gpt-6-sol", "reasoning": "high" },
              "maxConcurrentWork": 24
            }
            """;

        var loaded = System.Text.Json.JsonSerializer
            .Deserialize<WorkerTargetSettings>(legacy);

        Assert.NotNull(loaded);
        Assert.Equal("https://github.com/example/repo.git", loaded.ManualRepositoryUrl);
        Assert.Equal("gpt-6-sol", loaded.EffectiveHighLevel.Model);
        Assert.Equal(24, loaded.EffectiveMaxConcurrentWork);

        var saved = System.Text.Json.JsonSerializer.Serialize(loaded);
        Assert.DoesNotContain("manualServerBaseUrl", saved);
        Assert.DoesNotContain("serverBaseUrlSource", saved);
        Assert.Contains("manualRepositoryUrl", saved);
    }

    private sealed class RepositoryFixture : IDisposable
    {
        public string Root { get; }

        public RepositoryFixture(bool createGit = true)
        {
            Root = Path.Combine(Path.GetTempPath(),
                "projecthub-repo-settings-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            if (createGit)
                Git("init", "-q");
        }

        public void SetRemote(string name, string url) =>
            Git("remote", "add", name, url);

        public string Git(params string[] args)
        {
            var psi = new ProcessStartInfo("git")
            {
                WorkingDirectory = Root,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            foreach (var a in args)
                psi.ArgumentList.Add(a);
            using var process = Process.Start(psi)!;
            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0)
                throw new InvalidOperationException(
                    "git " + string.Join(" ", args) + ": " + stderr);
            return stdout;
        }

        public void Dispose()
        {
            if (!Directory.Exists(Root)) return;
            foreach (var file in Directory.EnumerateFiles(
                         Root, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(Root, recursive: true);
        }
    }
}
