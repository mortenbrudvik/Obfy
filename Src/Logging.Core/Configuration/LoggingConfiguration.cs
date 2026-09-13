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
    {
        Directory.CreateDirectory(_logDirectory);

        // Configure NLog programmatically
        var config = new NLog.Config.LoggingConfiguration();

        // File target - daily rotating logs
        var fileName = string.IsNullOrEmpty(appName)
            ? "${shortdate}.log"
            : $"{appName}-${{shortdate}}.log";

        var fileTarget = new NLog.Targets.FileTarget("file")
        {
            FileName = Path.Combine(_logDirectory, fileName),
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

        var asyncFileTarget = new AsyncTargetWrapper(fileTarget)
        {
            Name = "asyncFile",
            OverflowAction = AsyncTargetWrapperOverflowAction.Grow
        };

        config.AddTarget(asyncFileTarget);
        config.AddTarget(debugTarget);

        // Debug and above to debugger
        config.AddRule(NLog.LogLevel.Debug, NLog.LogLevel.Fatal, debugTarget);
        // Debug and above to file (async so obfuscation/UI threads are not blocked)
        config.AddRule(NLog.LogLevel.Debug, NLog.LogLevel.Fatal, asyncFileTarget);

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

    public static void Shutdown()
    {
        LogManager.Flush();
        LogManager.Shutdown();
    }
}
