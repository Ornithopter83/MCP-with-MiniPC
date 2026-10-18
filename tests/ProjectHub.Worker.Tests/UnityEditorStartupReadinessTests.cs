using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class UnityEditorStartupReadinessTests
{
    private const string Ping =
        "EDITOR_BRIDGE_CONFIRMED: EDITOR_RESPONDED; VALUE=6000.6.5f1; EVIDENCE=C:\\temp\\ping.json";
    private const string ReadyScene =
        "EDITOR_BRIDGE_CONFIRMED: EDITOR_SCENE_STATE; VALUE=" +
        "ACTIVE=Assets/Quiz/Scenes/QuizScene.unity;" +
        "ACTIVE_LOADED=True;ACTIVE_DIRTY=False;ACTIVE_ROOTS=1;" +
        "LOADED_ROOTS=1;PLAYING=False;COMPILING=False;UPDATING=False;" +
        " [scene=Assets/Quiz/Scenes/QuizScene.unity;loaded=True;dirty=False;roots=1]" +
        "; EVIDENCE=C:\\temp\\scene.json";

    [Fact]
    public void FreshEditorPingAndLoadedSceneConfirmCommandReadiness()
    {
        Assert.True(UnityEditorStartupReadiness.IsCommandReady(
            Ping, ReadyScene, out var code));
        Assert.Equal("EDITOR_MAIN_THREAD_PING_AND_SCENE_COMMAND_CONFIRMED", code);
    }

    [Theory]
    [InlineData("ACTIVE_LOADED=False", "EDITOR_ACTIVE_SCENE_NOT_LOADED")]
    [InlineData("COMPILING=True", "EDITOR_COMPILING_OR_IMPORTING")]
    [InlineData("UPDATING=True", "EDITOR_COMPILING_OR_IMPORTING")]
    public void ImportCompilingAndSceneLoadingCannotStartHq(
        string changed, string expectedCode)
    {
        var original = changed.Split('=')[0] + "=False";
        if (changed.StartsWith("ACTIVE_LOADED", StringComparison.Ordinal))
            original = "ACTIVE_LOADED=True";
        Assert.False(UnityEditorStartupReadiness.IsCommandReady(
            Ping, ReadyScene.Replace(original, changed, StringComparison.Ordinal),
            out var code));
        Assert.Equal(expectedCode, code);
    }

    [Fact]
    public void GuiOrCliResponseWithoutActualEditorCommandsIsNotReady()
    {
        Assert.False(UnityEditorStartupReadiness.IsCommandReady(
            "UNITY_GUI_READY", ReadyScene, out _));
        Assert.False(UnityEditorStartupReadiness.IsCommandReady(
            Ping, "UNITY_EDITOR_CONNECTED", out _));
        Assert.False(UnityEditorStartupReadiness.IsCommandReady(
            Ping, "EDITOR_BRIDGE_CONFIRMED: EDITOR_SCENE_STATE; VALUE=" +
                "ACTIVE_LOADED=True;COMPILING=True;UPDATING=False; " +
                "diagnostic=COMPILING=False; EVIDENCE=x", out _));
    }

    [Fact]
    public void StartupFailsClosedWhenSourceIsMissing()
    {
        Assert.False(UnityEditorStartupReadiness.IsCommandReady(
            Ping, "EDITOR_BRIDGE_NOT_INSTALLED", out _));
    }
}
