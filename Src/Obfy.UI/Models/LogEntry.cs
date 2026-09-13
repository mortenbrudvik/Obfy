namespace Obfy.UI.Models;

/// <summary>
/// Represents the severity level of a log entry.
/// </summary>
public enum LogLevel
{
    Debug,
    Info,
    Warning,
    Error,
    Success
}

/// <summary>
/// Represents a single log entry in the output panel.
/// </summary>
public class LogEntry
{
    /// <summary>
    /// Gets the timestamp when this log entry was created.
    /// </summary>
    public DateTime Timestamp { get; init; } = DateTime.Now;

    /// <summary>
    /// Gets the severity level of this log entry.
    /// </summary>
    public LogLevel Level { get; init; } = LogLevel.Info;

    /// <summary>
    /// Gets the log message.
    /// </summary>
    public string Message { get; init; } = string.Empty;

    public override string ToString() => $"{Level}: {Message}";

    /// <summary>
    /// Creates a new log entry with the specified message and level.
    /// </summary>
    public static LogEntry Create(string message, LogLevel level = LogLevel.Info)
    {
        return new LogEntry
        {
            Timestamp = DateTime.Now,
            Level = level,
            Message = message
        };
    }

    /// <summary>
    /// Creates an info log entry.
    /// </summary>
    public static LogEntry Info(string message) => Create(message, LogLevel.Info);

    /// <summary>
    /// Creates a warning log entry.
    /// </summary>
    public static LogEntry Warning(string message) => Create(message, LogLevel.Warning);

    /// <summary>
    /// Creates an error log entry.
    /// </summary>
    public static LogEntry Error(string message) => Create(message, LogLevel.Error);

    /// <summary>
    /// Creates a success log entry.
    /// </summary>
    public static LogEntry Success(string message) => Create(message, LogLevel.Success);

    /// <summary>
    /// Creates a debug log entry.
    /// </summary>
    public static LogEntry Debug(string message) => Create(message, LogLevel.Debug);
}
