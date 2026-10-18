using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class TemporaryWorkScopeTests
{
    [Theory]
    [InlineData("Assets/Quiz/Skin.cs", true)]
    [InlineData("Assets/Quiz/Art/", true)]
    [InlineData("docs/projecthub/plan.md", true)]
    [InlineData("Assets/", false)]
    [InlineData("docs/", false)]
    [InlineData("Assets//Quiz/Skin.cs", false)]
    [InlineData("Assets/Quiz/../Scripts/", false)]
    [InlineData("../Assets/Quiz/Skin.cs", false)]
    [InlineData("C:/Assets/Quiz/Skin.cs", false)]
    [InlineData("ProjectSettings/ProjectVersion.txt", false)]
    public void TemporaryWorkPath_MustBeNarrowProjectRelativeScope(
        string path, bool allowed)
    {
        Assert.Equal(allowed, MainWindow.IsAllowedTemporaryPath(path));
    }
}
