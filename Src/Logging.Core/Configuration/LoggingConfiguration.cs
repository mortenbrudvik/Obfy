using Microsoft.Extensions.Logging;
using NLog;
using NLog.Extensions.Logging;
using NLog.Targets.Wrappers;

namespace Logging.Core.Configuration;

/// <summary>
/// Configures NLog logging with file, debug, and console outputs.
/// </summary>
public static class LoggingConfiguration
{
    private static readonly string _logDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Obfy", "Logs");

    /// <summary>
    /// Gets the directory where log files are stored.
    /// </summary>
    public static string LogDirectory => _logDirectory;

    /// <summary>
    /// Creates and configures the logger factory with NLog.
    /// </summary>
    /// <param name="appName">Optional application name for log file naming.</param>
    /// <param name="enableConsoleOutput">Whether to output logs to console. Default is true.</param>
    /// <returns>A configured ILoggerFactory instance.</returns>
    public static ILoggerFactory CreateLoggerFactory(string? appName = null, bool enableConsoleOutput = true)
        => CreateLoggerFactory(_logDirectory, appName, enableConsoleOutput);

    /// <summary>
    /// Creates a logger factory that writes files under <paramref name="logDirectory"/>.
    /// </summary>
    /// <param name="logDirectory">Directory for rotating log files. If it cannot be created, only debugger/console targets are used.</param>
    /// <param name="appName">Optional application name for log file naming.</param>
    /// <param name="enableConsoleOutput">Whether to output logs to console.</param>
    public static ILoggerFactory CreateLoggerFactory(string logDirectory, string? appName, bool enableConsoleOutput)
    {
        var canWriteFiles = true;
        string? failureType = null;
        try
        {
            Directory.CreateDirectory(logDirectory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            canWriteFiles = false;
            failureType = ex.GetType().Name;
            // Console.Error only reaches someone in a console app; a windowed one reads Status. Debug output has the type
            // alone (the folder is the user's, and its name has no business in a log line).
            if (enableConsoleOutput)
                Console.Error.WriteLine($"Cannot write logs to {logDirectory}: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"Cannot create the log folder ({failureType})");
        }

        Status = new LogFileStatus(canWriteFiles, failureType);

        // Configure NLog programmatically
        var config = new NLog.Config.LoggingConfiguration();

        // File target - daily rotating logs
        var fileName = string.IsNullOrEmpty(appName)
            ? "${shortdate}.log"
            : $"{appName}-${{shortdate}}.log";

        var fileTarget = new NLog.Targets.FileTarget("file")
        {
            FileName = Path.Combine(logDirectory, fileName),
            Layout = "${longdate} [${level:uppercase=true}] ${logger}: ${message}${onexception:inner=${newline}${exception:format=tostring}}",
            ArchiveEvery = NLog.Targets.FileArchivePeriod.Day,
            ArchiveSuffixFormat = "_{1:yyyyMMdd}",
            MaxArchiveFiles = 30
        };

        // Debug target - for IDE output window
        var debugTarget = new NLog.Targets.DebuggerTarget("debugger")
        {
            Layout = "[${level:uppercase=true}] ${logger}: ${message}"
        };

        // Console target - for console applications
        var consoleTarget = new NLog.Targets.ConsoleTarget("console")
        {
            Layout = "${time} [${level:uppercase=true}] ${message}"
        };

        config.AddTarget(debugTarget);

        // Debug and above to debugger
        config.AddRule(NLog.LogLevel.Debug, NLog.LogLevel.Fatal, debugTarget);
        if (canWriteFiles)
        {
            var asyncFileTarget = new AsyncTargetWrapper(fileTarget)
            {
                Name = "asyncFile",
                OverflowAction = AsyncTargetWrapperOverflowAction.Grow
            };
            config.AddTarget(asyncFileTarget);
            config.AddRule(NLog.LogLevel.Debug, NLog.LogLevel.Fatal, asyncFileTarget);
        }

        // Console output only if enabled
        if (enableConsoleOutput)
        {
            config.AddTarget(consoleTarget);
            config.AddRule(NLog.LogLevel.Info, NLog.LogLevel.Fatal, consoleTarget);
        }

        LogManager.Configuration = config;

        return LoggerFactory.Create(builder =>
        {
            builder.ClearProviders();
            builder.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Debug);
            builder.AddNLog();
        });
    }

    public static void Flush() => LogManager.Flush();

    /// <summary>
    /// Writes what the asynchronous file target still holds, waiting at most <paramref name="timeout"/>. For a crash handler:
    /// the process is going down and the log line that says why must be on disk first. Never throws.
    /// </summary>
    public static void Flush(TimeSpan timeout)
    {
        try
        {
            LogManager.Flush(timeout);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Could not flush the log ({ex.GetType().Name})");
        }
    }

    /// <summary>
    /// Whether the log file can be written, as of the last <c>CreateLoggerFactory</c>. A WPF app has no console to report a
    /// log folder it cannot create on, so the shell reads this to say so where the user can see it.
    /// </summary>
    public static LogFileStatus Status { get; private set; } = new(true, null);

    public static void Shutdown()
    {
        LogManager.Flush();
        LogManager.Shutdown();
    }
}

/// <summary>Whether the log file can be written, and if not, the type of the exception that said so (never its message).</summary>
public sealed record LogFileStatus(bool FileLoggingAvailable, string? FailureType);
