using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using NLog;
using NLog.Extensions.Logging;
using NLog.Targets.Wrappers;

[assembly: InternalsVisibleTo("Obfy.UI.Tests")]

namespace Logging.Core.Configuration;

/// <summary>
/// Configures NLog logging with file, debug, and console outputs.
/// </summary>
public static class LoggingConfiguration
{
    private const int AppModelErrorNoPackage = 15700;
    private const int ErrorInsufficientBuffer = 122;

    private static readonly string _logDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Obfy", "Logs");

    /// <summary>
    /// Gets the directory where log files are stored.
    /// </summary>
    public static string LogDirectory => _logDirectory;

    /// <summary>
    /// Resolves the directory File Explorer should open to show log files. Inside an MSIX/Store package, Windows
    /// virtualizes writes under <see cref="LogDirectory"/> into the package's <c>LocalCache\Local\Obfy\Logs</c> folder;
    /// File Explorer runs outside package virtualization, so it must be given the redirected folder when it exists.
    /// </summary>
    public static string ResolveExplorerLogDirectory()
        => OperatingSystem.IsWindows()
            ? ResolveExplorerLogDirectory(
                TryGetCurrentPackageFamilyName(),
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                Directory.Exists)
            : _logDirectory;

    internal static string ResolveExplorerLogDirectory(
        string? packageFamilyName,
        string localAppData,
        Func<string, bool> directoryExists)
    {
        ArgumentNullException.ThrowIfNull(directoryExists);

        if (string.IsNullOrWhiteSpace(packageFamilyName) || string.IsNullOrWhiteSpace(localAppData))
            return _logDirectory;

        var virtualized = Path.Combine(
            localAppData,
            "Packages",
            packageFamilyName,
            "LocalCache",
            "Local",
            "Obfy",
            "Logs");
        return directoryExists(virtualized) ? virtualized : _logDirectory;
    }

    private static string? TryGetCurrentPackageFamilyName()
    {
        try
        {
            uint length = 0;
            var status = GetCurrentPackageFamilyName(ref length, null);
            if (status == AppModelErrorNoPackage || status != ErrorInsufficientBuffer || length == 0)
                return null;

            var buffer = new char[length];
            status = GetCurrentPackageFamilyName(ref length, buffer);
            if (status != 0)
                return null;

            var count = (int)Math.Min(length, (uint)buffer.Length);
            var terminator = Array.IndexOf(buffer, '\0', 0, count);
            if (terminator >= 0)
                count = terminator;

            var familyName = new string(buffer, 0, count);
            return string.IsNullOrWhiteSpace(familyName) ? null : familyName;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, EntryPoint = "GetCurrentPackageFamilyName")]
    private static extern int GetCurrentPackageFamilyName(ref uint packageFamilyNameLength, [Out] char[]? packageFamilyName);

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
