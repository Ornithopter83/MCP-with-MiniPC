using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ProjectHub.Server;

public sealed class ProjectHubDailyFileLoggerProvider : ILoggerProvider
{
    private readonly object gate = new();
    private readonly string logDirectory;
    private StreamWriter? writer;
    private string? writerDate;
    private bool disposed;

    public ProjectHubDailyFileLoggerProvider(string contentRoot)
    {
        logDirectory = Path.Combine(contentRoot, "log");
        Directory.CreateDirectory(logDirectory);
    }

    public ILogger CreateLogger(string categoryName) => new ProjectHubDailyFileLogger(this, categoryName);

    internal bool IsEnabled(string categoryName, LogLevel level) =>
        !disposed && level >= LogLevel.Information &&
        (!categoryName.StartsWith("Microsoft.", StringComparison.Ordinal) || level >= LogLevel.Warning) &&
        (!categoryName.StartsWith("System.Net.Http.HttpClient", StringComparison.Ordinal) || level >= LogLevel.Warning);

    internal void Write<TState>(string categoryName, LogLevel level, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(categoryName, level)) return;
        var message = formatter(state, exception);
        if (string.IsNullOrWhiteSpace(message) && exception is null) return;
        lock (gate)
        {
            if (disposed) return;
            EnsureWriter();
            var levelText = level switch
            {
                LogLevel.Trace => "TRACE",
                LogLevel.Debug => "DEBUG",
                LogLevel.Information => "INFO ",
                LogLevel.Warning => "WARN ",
                LogLevel.Error => "ERROR",
                LogLevel.Critical => "CRIT ",
                _ => "     "
            };
            writer!.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{levelText}] [{categoryName}] {message}");
            if (exception is not null) writer.WriteLine(exception);
            writer.Flush();
        }
    }

    private void EnsureWriter()
    {
        var date = DateTime.Now.ToString("yyyyMMdd");
        if (writer is not null && writerDate == date) return;
        writer?.Dispose();
        writerDate = date;
        writer = new StreamWriter(new FileStream(Path.Combine(logDirectory, date + ".log"), FileMode.Append, FileAccess.Write, FileShare.ReadWrite), new System.Text.UTF8Encoding(false)) { AutoFlush = true };
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            if (writer is not null)
            {
                writer.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [INFO ] [ProjectHub.Server] [SERVER] [-] SERVER_STOPPING [OK]");
                writer.WriteLine();
                writer.Dispose();
                writer = null;
            }
        }
    }

    private sealed class ProjectHubDailyFileLogger(ProjectHubDailyFileLoggerProvider provider, string categoryName) : ILogger
    {
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => provider.IsEnabled(categoryName, logLevel);
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => provider.Write(categoryName, logLevel, eventId, state, exception, formatter);
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose() { }
    }
}
