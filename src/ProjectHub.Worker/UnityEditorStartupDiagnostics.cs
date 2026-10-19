using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ProjectHub.Worker;

/// <summary>
/// Explains startup failure without changing Unity state or claiming that a
/// visible GUI, installed C# file, or a heartbeat proves command readiness.
/// </summary>
internal static class UnityEditorStartupDiagnostics
{
    internal sealed record Baseline(
        string LogPath, long LogLength, string Prefix, DateTime StartedUtc);
    internal sealed record Finding(string Code, string Detail);

    internal static Baseline Capture(string? logPath = null)
    {
        var path = logPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Unity", "Editor", "Editor.log");
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            return new(path, stream.Length, ReadPrefix(stream), DateTime.UtcNow);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new(path, 0, "", DateTime.UtcNow);
        }
    }

    private static string ReadPrefix(FileStream stream, int limit = 128)
    {
        stream.Position = 0;
        var bytes = new byte[(int)Math.Min(stream.Length, limit)];
        var read = stream.Read(bytes, 0, bytes.Length);
        return Convert.ToHexString(bytes.AsSpan(0, read));
    }

    internal static Finding Analyze(string projectRoot, Baseline baseline,
        string lastObserved)
    {
        var compileErrors = RecentCompileErrors(baseline);
        var expectedQueue = UnityEditorDirectBridge.QueueDirectory(projectRoot);
        var signalPath = UnityEditorDirectBridge.StartupSignalPath(projectRoot);
        var message = "Last command=" + Limit(lastObserved, 450) +
            "; UnityLog=" + baseline.LogPath +
            "; ExpectedQueue=" + expectedQueue;
        if (compileErrors.Count > 0)
            return new("EDITOR_COMPILE_FAILED", message +
                "; Fresh compile errors: " + string.Join(" | ", compileErrors));

        if (TryReadFreshSignal(signalPath, baseline.StartedUtc, out var signal))
        {
            if (!IsSamePath(signal.ProjectRoot, projectRoot) ||
                !IsSamePath(signal.QueueDirectory, expectedQueue))
                return new("EDITOR_BRIDGE_QUEUE_MISMATCH",
                    message + "; Receiver signaled a different project/queue.");
            if (signal.Status == "EDITOR_IMPORT_BUSY")
                return new("EDITOR_IMPORT_IN_PROGRESS", message +
                    "; Bridge callback loaded but Import/compilation still reported busy.");
            return new("BRIDGE_NO_RESPONSE", message +
                "; Receiver active: " + signal.Status +
                "; PID=" + signal.ProcessId +
                "; lastUtcTicks=" + signal.UtcTicks +
                ". Main-thread request did not complete.");
        }

        // A receiver compiled for the same project but using a different
        // queue key is distinguishable from a receiver never initialized.
        var parent = Path.GetDirectoryName(expectedQueue)!;
        try
        {
            if (Directory.Exists(parent))
            {
                foreach (var directory in Directory.EnumerateDirectories(parent))
                {
                    if (IsSamePath(directory, expectedQueue)) continue;
                    if (TryReadFreshSignal(Path.Combine(directory,
                            "bridge-active.json"), baseline.StartedUtc, out var other) &&
                        IsSamePath(other.ProjectRoot, projectRoot))
                        return new("EDITOR_BRIDGE_QUEUE_MISMATCH",
                            message + "; ActualQueue=" + directory);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }

        return new("BRIDGE_NOT_LOADED", message +
            "; No recent receiver activation signal. Check Unity Console, " +
            "package resolution and script compilation. The file installation " +
            "alone does not establish import or command readiness.");
    }

    internal sealed record Signal(string ProjectRoot, string QueueDirectory,
        string Status, int ProcessId, long UtcTicks);

    private static bool TryReadFreshSignal(string path, DateTime sinceUtc,
        out Signal signal)
    {
        signal = new("", "", "", 0, 0);
        try
        {
            if (!File.Exists(path) ||
                File.GetLastWriteTimeUtc(path) < sinceUtc.AddSeconds(-2))
                return false;
            using var json = JsonDocument.Parse(File.ReadAllText(path));
            var data = json.RootElement;
            if (data.ValueKind != JsonValueKind.Object)
                return false;
            string GetString(string key) => data.TryGetProperty(key, out var item) &&
                item.ValueKind == JsonValueKind.String ? item.GetString() ?? "" : "";
            int GetInt(string key) => data.TryGetProperty(key, out var item) &&
                item.ValueKind == JsonValueKind.Number && item.TryGetInt32(out var value)
                    ? value : 0;
            long GetLong(string key) => data.TryGetProperty(key, out var item) &&
                item.ValueKind == JsonValueKind.Number && item.TryGetInt64(out var value)
                    ? value : 0;
            signal = new(GetString("projectRoot"), GetString("queueDirectory"),
                GetString("status"), GetInt("processId"), GetLong("utcTicks"));
            return signal.UtcTicks >= sinceUtc.AddSeconds(-2).Ticks &&
                signal.ProcessId > 0 &&
                signal.ProjectRoot.Length > 0 && signal.QueueDirectory.Length > 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
            JsonException or ArgumentException) { return false; }
    }

    private static IReadOnlyList<string> RecentCompileErrors(Baseline baseline)
    {
        try
        {
            using var stream = new FileStream(baseline.LogPath, FileMode.Open,
                FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var sameFile = stream.Length >= baseline.LogLength &&
                string.Equals(ReadPrefix(stream, baseline.Prefix.Length / 2),
                    baseline.Prefix, StringComparison.Ordinal);
            // When Unity rotates/truncates Editor.log on restart, scan the
            // new file. Otherwise read only the bytes appended after baseline.
            var offset = sameFile ? baseline.LogLength : 0;
            offset = Math.Max(offset, stream.Length - 256 * 1024);
            stream.Position = offset;
            using var reader = new StreamReader(stream, Encoding.UTF8, true, 4096);
            var segment = reader.ReadToEnd();
            return segment.Split('\n')
                .Select(line => line.Trim())
                .Where(line =>
                    Regex.IsMatch(line, @"\berror CS[0-9]{4}\b",
                        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) ||
                    line.Contains("Compilation failed", StringComparison.OrdinalIgnoreCase))
                .Select(line => Limit(line, 360))
                .TakeLast(3).ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }

    private static bool IsSamePath(string left, string right)
    {
        try
        {
            return string.Equals(
                Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar),
                Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    private static string Limit(string text, int length) =>
        text.Length <= length ? text : text[..length];
}
