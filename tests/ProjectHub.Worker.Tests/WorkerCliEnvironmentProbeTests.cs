using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class WorkerCliEnvironmentProbeTests
{
    [Theory]
    [InlineData(@"C:\Users\me\AppData\Local\Microsoft\WindowsApps\python.exe", true)]
    [InlineData(@"C:\Program Files\Python313\python.exe", false)]
    [InlineData(@"C:\Program Files\nodejs\node.exe", false)]
    public void WindowsAppAliasIsNotAnInstalledInterpreter(string path, bool expected)
    {
        Assert.Equal(expected, WorkerCliEnvironmentProbe.IsWindowsAppAlias(path));
    }

    [Fact]
    public void AvailabilityRequiresAnExecutableAndSuccessfulVersionProbe()
    {
        Assert.Equal("UNAVAILABLE", WorkerCliEnvironmentProbe.FormatStatus(null, true));
        Assert.Equal("UNAVAILABLE",
            WorkerCliEnvironmentProbe.FormatStatus(
                @"C:\Users\me\AppData\Local\Microsoft\WindowsApps\python.exe", true));
        Assert.Equal("UNAVAILABLE",
            WorkerCliEnvironmentProbe.FormatStatus(@"C:\Tools\rg.exe", false));
        Assert.Equal("READY",
            WorkerCliEnvironmentProbe.FormatStatus(@"C:\Tools\rg.exe", true));
    }
}
