using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class UnityEditorCommandResultTests
{
    [Fact]
    public void EvalTargetsExistingProjectWithoutStartingEditor()
    {
        var root = Path.GetTempPath();
        var args = UnityEditorCommandResult.EvalArguments(
            root, "return QuizStandaloneBuild.BuildQuizStandalone().summary.result.ToString();");
        Assert.Equal("command", args[0]);
        Assert.Equal("eval", args[1]);
        Assert.Contains("--project-path", args);
        Assert.Contains(Path.GetFullPath(root), args);
        Assert.DoesNotContain("open", args);
        Assert.DoesNotContain("batchmode", args);
        Assert.Contains("600", args);
    }

    [Fact]
    public void RequiresEditorReturnedValueNotSourceEcho()
    {
        const string result = """{"success":true,"data":{"result":"Succeeded"}}""";
        Assert.Contains("CONFIRMED", UnityEditorCommandResult.Verify(
            new(0, result, ""), "Succeeded"));
        Assert.Throws<InvalidOperationException>(() =>
            UnityEditorCommandResult.Verify(new(0,
                """{"success":true,"data":{"expression":"return \"Succeeded\";"}}""", ""),
                "Succeeded"));
        Assert.Throws<InvalidOperationException>(() =>
            UnityEditorCommandResult.Verify(new(0, result, ""), "Failed"));
        Assert.Throws<InvalidOperationException>(() =>
            UnityEditorCommandResult.Verify(new(7, result, ""), "Succeeded"));
        Assert.Throws<InvalidOperationException>(() =>
            UnityEditorCommandResult.Verify(new(0, "not json", ""), "Succeeded"));
    }
}
