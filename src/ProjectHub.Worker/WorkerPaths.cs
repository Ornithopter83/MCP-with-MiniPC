using System.IO;

namespace ProjectHub.Worker;

public static class WorkerPaths
{
    public static string Root => Path.Combine(AppContext.BaseDirectory, "Worker");

    public static string State => Path.Combine(Root, "state");
    public static string Config => Path.Combine(Root, "config");
    public static string Task => Path.Combine(Root, "Task");
    public static string Attachments => Path.Combine(Root, "attachments");
    public static string Logs => Path.Combine(Root, "logs");
    public static string Extension => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ProjectHub", "GPTWeb-Hub", "extension");

    public static void EnsureCreated()
    {
        foreach (var directory in new[] { Root, State, Config, Task, Attachments, Logs, Extension })
            Directory.CreateDirectory(directory);
    }
}
