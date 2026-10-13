using System.IO;
using System.Text.Json;

namespace ProjectHub.Worker;

/// <summary>
/// Imports settings from legacy EXE-relative Worker/config locations on first
/// launch. The canonical configuration directory itself never depends on the
/// executable location. Existing canonical files always take precedence.
/// </summary>
internal static class WorkerSettingsMigration
{
    internal static readonly string[] SettingFiles =
    {
        "target-settings.json",
        "worker-selection.json",
        "window-placement.json"
    };

    internal static IReadOnlyList<string> FindLegacyConfigDirectories(string executableDirectory)
    {
        var start = Path.GetFullPath(executableDirectory);
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string root) => found.Add(Path.GetFullPath(
            Path.Combine(root, "Worker", "config")));

        Add(start);

        // Known legacy release layout:
        //   repo/bin/ProjectHub.Worker.exe
        //   repo/../Worker/ProjectHub.Worker.exe
        // Each EXE used to own its own Worker/config folder.
        var current = new DirectoryInfo(start);
        for (var depth = 0; current is not null && depth < 4;
             current = current.Parent, depth++)
        {
            if (!File.Exists(Path.Combine(current.FullName,
                    "src", "ProjectHub.Worker", "ProjectHub.Worker.csproj")))
                continue;

            Add(Path.Combine(current.FullName, "bin"));
            if (current.Parent is not null)
                Add(Path.Combine(current.Parent.FullName, "Worker"));
        }

        // An EXE launched from the sibling deployment directory can recover
        // earlier repo/bin settings even when the repository is not an ancestor.
        if (Path.GetFileName(start).Equals("Worker", StringComparison.OrdinalIgnoreCase))
        {
            var parent = Directory.GetParent(start);
            if (parent is not null)
            {
                try
                {
                    foreach (var sibling in Directory.EnumerateDirectories(parent.FullName))
                    {
                        if (File.Exists(Path.Combine(sibling, "src",
                                "ProjectHub.Worker", "ProjectHub.Worker.csproj")))
                            Add(Path.Combine(sibling, "bin"));
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // The adjacent repository may not exist; the canonical
                    // settings directory remains independent of this scan.
                }
            }
        }

        return found.ToArray();
    }

    internal static IReadOnlyList<string> MigrateKnownLegacySettings() =>
        Migrate(WorkerPaths.Config,
            FindLegacyConfigDirectories(AppContext.BaseDirectory));

    // Isolated directory injection supports repeatable Windows CI tests.
    internal static IReadOnlyList<string> Migrate(
        string canonicalDirectory, IEnumerable<string> legacyDirectories)
    {
        var canonical = Path.GetFullPath(canonicalDirectory);
        Directory.CreateDirectory(canonical);
        var sources = legacyDirectories
            .Select(Path.GetFullPath)
            .Where(dir => !dir.Equals(canonical, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(Directory.Exists)
            .ToArray();

        var imported = new List<string>();
        foreach (var filename in SettingFiles)
        {
            var destination = Path.Combine(canonical, filename);
            if (File.Exists(destination))
                continue; // Do not overwrite user preferences, including malformed JSON.

            // Prefer the newest previous setting if both release and bin
            // locations exist, rather than a stale build directory.
            var existing = sources
                .Select(dir => Path.Combine(dir, filename))
                .Where(File.Exists)
                .Where(path => (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .ToArray();

            foreach (var previous in existing)
            {
                // A partial/invalid legacy JSON file must not mask an older
                // valid one or poison the new canonical settings location.
                string original;
                try
                {
                    original = File.ReadAllText(previous);
                    using var parsed = JsonDocument.Parse(original);
                    if (parsed.RootElement.ValueKind != JsonValueKind.Object)
                        continue;
                }
                catch (JsonException) { continue; }
                catch (IOException) { continue; }
                catch (UnauthorizedAccessException) { continue; }

                var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    File.WriteAllText(temporary, original);
                    File.Move(temporary, destination, overwrite: false);
                    imported.Add(filename);
                    break;
                }
                finally
                {
                    if (File.Exists(temporary))
                        File.Delete(temporary);
                }
            }
        }
        return imported;
    }
}
