using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class MilestoneLateGitPathPolicyTests
{
    [Fact]
    public void AddsOnlyUnityMetaForAnOwnedSource()
    {
        var dirty = new[]
        {
            "Assets/Quiz/NewController.cs.meta",
            "Assets/Generated/Unrelated.prefab",
            "Assets/Generated/Unrelated.prefab.meta",
            "Temp/test.log"
        };
        var initial = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var owned = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Assets/Quiz/NewController.cs"
        };

        Assert.Equal(
            new[] { "Assets/Quiz/NewController.cs.meta" },
            MilestoneLateGitPathPolicy.SelectSidecars(dirty, initial, owned));
    }

    [Fact]
    public void DoesNotReclaimPreexistingMetaOrUnreportedLateSources()
    {
        var dirty = new[]
        {
            "Assets/Quiz/A.cs.meta",
            "Assets/Quiz/A.cs.meta",
            "Assets/Quiz/B.cs.meta",
            "Assets/Quiz/B.cs"
        };
        var initial = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Assets/Quiz/A.cs.meta"
        };
        var owned = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Assets/Quiz/A.cs",
            "Assets/Quiz/C.cs"
        };

        Assert.Empty(MilestoneLateGitPathPolicy.SelectSidecars(dirty, initial, owned));
    }

    [Fact]
    public void MatchingIsCaseInsensitiveAndSidecarsAreDeduplicated()
    {
        var dirty = new[] { "Assets/Quiz/A.prefab.META", "assets/quiz/a.prefab.meta" };
        var initial = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var owned = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Assets/Quiz/A.prefab"
        };

        Assert.Single(MilestoneLateGitPathPolicy.SelectSidecars(dirty, initial, owned));
    }
}
