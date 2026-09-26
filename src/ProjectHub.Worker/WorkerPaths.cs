using System.IO;

namespace ProjectHub.Worker;

public static class WorkerPaths
{
    public static string Root => Path.Combine(AppContext.BaseDirectory, "Worker");

    public static string State => Path.Combine(Root, "state");
    public static string Config => Path.Combine(Root, "config");
    public static string Task => Path.Combine(Root, "Task");
    public static string Attachments => Path.Combine(Root, "attachments");
    public static string WebResults => Path.Combine(Root, "web-results");
    public static string Logs => Path.Combine(Root, "logs");
    public static string Extension => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ProjectHub", "GPTWeb-Hub", "extension");
    public static string ManagedWebRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ProjectHub", "ManagedWeb");
    public static string ManagedWebBrowserRuntime => Path.Combine(ManagedWebRoot, "BrowserRuntime");
    public static string ManagedWebProfiles => Path.Combine(ManagedWebRoot, "Profiles");
    public static string ManagedWebHqProfile => Path.Combine(ManagedWebProfiles, "HQ");
    public static string ManagedWebResourceProfile => Path.Combine(ManagedWebProfiles, "RESOURCE");

    public static void EnsureCreated()
    {
        foreach (var directory in new[]
        {
            Root,
            State,
            Config,
            Task,
            Attachments,
            WebResults,
            Logs,
            Extension,
            ManagedWebRoot,
            ManagedWebBrowserRuntime,
            ManagedWebProfiles,
            ManagedWebHqProfile,
            ManagedWebResourceProfile
        })
            Directory.CreateDirectory(directory);
    }
}
