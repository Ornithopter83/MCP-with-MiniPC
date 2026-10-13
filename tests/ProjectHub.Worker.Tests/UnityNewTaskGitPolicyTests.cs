using System.IO;
using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class UnityNewTaskGitPolicyTests
{
    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(),
            "unity-git-policy-" + Guid.NewGuid().ToString("N"));
        public string Bare => Path.Combine(Root, "remote.git");
        public string Seed => Path.Combine(Root, "seed");
        public string Work => Path.Combine(Root, "Unity3DSample");

        public Fixture()
        {
            Directory.CreateDirectory(Root);
            Directory.CreateDirectory(Seed);
            Directory.CreateDirectory(Work);
            Git(Root, "init", "--bare", "--initial-branch=main", Bare);
            Git(Seed, "init", "--initial-branch=main");
            Git(Seed, "config", "user.name", "Fixture");
            Git(Seed, "config", "user.email", "fixture@example.invalid");
            Write(Seed, "Assets/Scenes/AIDemo.unity", "REMOTE SCENE");
            Write(Seed, "Assets/Rock", "REMOTE ROCK");
            Write(Seed, "keep.txt", "SAME BASE");
            Write(Seed, ".gitignore", "# upstream ignore\n");
            Git(Seed, "add", "-A");
            Git(Seed, "commit", "-m", "Remote baseline");
            Git(Seed, "remote", "add", "origin", Bare);
            Git(Seed, "push", "-u", "origin", "main");
            Git(Work, "init", "--initial-branch=master");
            Git(Work, "config", "user.name", "Fixture");
            Git(Work, "config", "user.email", "fixture@example.invalid");
        }

        public static void Write(string root, string relative, string value)
        {
            var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, value);
        }

        public static string Git(string root, params string[] args)
        {
            var result = new ProcessGitCommandRunner().RunAsync(
                root, args, TimeSpan.FromSeconds(40)).GetAwaiter().GetResult();
            Assert.True(result.ExitCode == 0,
                "git " + string.Join(" ", args) + ": " + result.StandardError +
                " " + result.StandardOutput);
            return result.StandardOutput;
        }

        public void Dispose()
        {
            try { Directory.Delete(Root, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    [Fact]
    public async Task FreshTask_OverwritesUntrackedUnityFiles_WithoutBackupOrUnrelatedDeletion()
    {
        using var f = new Fixture();
        Fixture.Write(f.Work, "Assets/Scenes/AIDemo.unity", "LOCAL OLD SCENE");
        Fixture.Write(f.Work, "Assets/Rock/obsolete.txt", "OLD CONFLICT TREE");
        Fixture.Write(f.Work, "Assets/Rock/nested/old.tmp", "OLD RESIDUE");
        Fixture.Write(f.Work, ".gitignore", "OLD IGNORE FILE");
        Fixture.Write(f.Work, "docs/local-notes.md", "KEEP UNTRACKED");
        Fixture.Write(f.Work, "docs/staged.md", "KEEP STAGED");
        Fixture.Git(f.Work, "add", "docs/staged.md");

        var first = await MilestoneMechanicalExecutor.CheckGitReadyAsync(
            f.Work, "main", true, f.Bare, CancellationToken.None);
        Assert.False(first.Success);
        Assert.Equal("LOCAL OLD SCENE",
            File.ReadAllText(Path.Combine(f.Work, "Assets", "Scenes", "AIDemo.unity")));

        var result = await MilestoneMechanicalExecutor.CheckGitReadyAsync(
            f.Work, "main", true, f.Bare, CancellationToken.None,
            overwriteNewTaskConflicts: true);
        Assert.True(result.Success, result.Summary);
        Assert.Contains("main", Fixture.Git(f.Work, "branch", "--show-current"));
        Assert.Equal("REMOTE SCENE",
            File.ReadAllText(Path.Combine(f.Work, "Assets", "Scenes", "AIDemo.unity")));
        Assert.Equal("REMOTE ROCK", File.ReadAllText(Path.Combine(f.Work, "Assets", "Rock")));
        Assert.False(Directory.Exists(Path.Combine(f.Work, "Assets", "Rock", "nested")));
        Assert.Equal("KEEP UNTRACKED", File.ReadAllText(
            Path.Combine(f.Work, "docs", "local-notes.md")));
        Assert.Contains("docs/staged.md",
            Fixture.Git(f.Work, "diff", "--cached", "--name-only"));
        Assert.Contains("/[Ll]ibrary/", File.ReadAllText(
            Path.Combine(f.Work, ".gitignore")));
        Assert.DoesNotContain(".backup", Directory.GetDirectories(f.Work, "*", SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public async Task FreshTask_OverwritesStagedCollision_ButKeepsIndependentUnstagedEdit()
    {
        using var f = new Fixture();
        Fixture.Write(f.Work, "Assets/Scenes/AIDemo.unity", "OLD TRACKED SCENE");
        Fixture.Write(f.Work, "keep.txt", "SAME BASE");
        Fixture.Git(f.Work, "add", "-A");
        Fixture.Git(f.Work, "commit", "-m", "Old master baseline");
        Fixture.Write(f.Work, "Assets/Scenes/AIDemo.unity", "STAGED DIRTY SCENE");
        Fixture.Git(f.Work, "add", "Assets/Scenes/AIDemo.unity");
        Fixture.Write(f.Work, "keep.txt", "UNSTAGED INDEPENDENT EDIT");

        var result = await MilestoneMechanicalExecutor.CheckGitReadyAsync(
            f.Work, "main", true, f.Bare, CancellationToken.None,
            overwriteNewTaskConflicts: true);
        Assert.True(result.Success, result.Summary);
        Assert.Equal("REMOTE SCENE", File.ReadAllText(
            Path.Combine(f.Work, "Assets", "Scenes", "AIDemo.unity")));
        Assert.Equal("UNSTAGED INDEPENDENT EDIT",
            File.ReadAllText(Path.Combine(f.Work, "keep.txt")));
        Assert.Contains("keep.txt", Fixture.Git(f.Work, "diff", "--name-only"));
    }

    [Fact]
    public void CollisionDiscovery_NeverClassifiesUnrelatedOrRuntimeFiles()
    {
        var collisions = UnityNewTaskGitPolicy.FindUntrackedCollisions(
            new[] { "Assets/Scenes/AIDemo.unity", "Assets/Tree" },
            new[] { "Assets/Scenes/AIDemo.unity", "Assets/Tree/old.txt",
                "docs/my-note.txt", ".projecthub/state.json", "Library/cache" });
        Assert.Equal(new[]
        {
            "Assets/Scenes/AIDemo.unity", "Assets/Tree/old.txt"
        }, collisions);
        Assert.True(UnityNewTaskGitPolicy.IsCheckoutCollision(
            "The following untracked working tree files would be overwritten by checkout:"));
        Assert.False(UnityNewTaskGitPolicy.IsCheckoutCollision(
            "fatal: could not read from remote repository"));
    }
}
