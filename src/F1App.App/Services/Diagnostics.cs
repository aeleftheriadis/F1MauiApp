using System.Text;
using Microsoft.Extensions.Logging;

namespace F1App.Services;

/// <summary>
/// Writes everything the app logs to a file, including crashes.
/// </summary>
/// <remarks>
/// The debug logger only reaches the debugger, which means an exception a user sees on screen leaves no
/// trace once the window is closed. This provider appends to <c>logs/app.log</c> in app data, and the
/// global handlers below capture unhandled exceptions from the app domain and from unobserved tasks.
/// </remarks>
public static class Diagnostics
{
    private static readonly Lock Gate = new();
    private static string? _logPath;

    /// <summary>Full path of the log file, once <see cref="Initialize"/> has run.</summary>
    public static string? LogPath => _logPath;

    /// <summary>Starts file logging and installs the crash handlers. Call once, early.</summary>
    /// <param name="directory">Directory for the log file.</param>
    /// <returns>A logger provider to register with the app's logging pipeline.</returns>
    public static ILoggerProvider Initialize(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        var logs = Path.Combine(directory, "logs");
        Directory.CreateDirectory(logs);
        _logPath = Path.Combine(logs, "app.log");

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Write("CRASH", args.ExceptionObject as Exception ?? new Exception(args.ExceptionObject?.ToString() ?? "unknown"));

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Write("UNOBSERVED", args.Exception);
            args.SetObserved();
        };

        Write("INFO", null, "diagnostics started");
        return new FileLoggerProvider(_logPath);
    }

    /// <summary>Appends one entry. Safe to call from any thread.</summary>
    public static void Write(string level, Exception? exception, string? message = null)
    {
        if (_logPath is null)
        {
            return;
        }

        var builder = new StringBuilder()
            .Append(DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture))
            .Append(' ')
            .Append(level)
            .Append(' ');

        if (message is not null)
        {
            builder.AppendLine(message);
        }

        if (exception is not null)
        {
            builder.AppendLine(exception.ToString());
        }

        lock (Gate)
        {
            try
            {
                File.AppendAllText(_logPath, builder.ToString());
            }
            catch (IOException)
            {
                // Logging must never be the reason the app fails.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private sealed class FileLoggerProvider(string path) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new FileLogger(path, categoryName);

        public void Dispose()
        {
        }

        private sealed class FileLogger(string path, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                if (!IsEnabled(logLevel))
                {
                    return;
                }

                var message = $"{category}: {formatter(state, exception)}";
                var entry = $"{DateTimeOffset.UtcNow:O} {logLevel.ToString().ToUpperInvariant()} {message}\n";

                lock (Gate)
                {
                    try
                    {
                        File.AppendAllText(path, exception is null ? entry : entry + exception + Environment.NewLine);
                    }
                    catch (IOException)
                    {
                    }
                    catch (UnauthorizedAccessException)
                    {
                    }
                }
            }
        }
    }
}
