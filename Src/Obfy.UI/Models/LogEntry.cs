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
}
