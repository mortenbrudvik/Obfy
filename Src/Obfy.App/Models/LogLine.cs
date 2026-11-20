namespace Obfy.App.Models;

/// <summary>How serious a log line is. Serialized by name; the page colours the line from it.</summary>
public enum LogLevel
{
    Debug,
    Info,
    Warning,
    Error,
    Success
}

/// <summary>One line of the run log.</summary>
/// <param name="Id">Rises with every line and is never reused, not even after a clear; the page drops a line whose id it already holds.</param>
/// <param name="Timestamp">Time of day as <c>HH:mm:ss.fff</c>, local time.</param>
public record LogLine(long Id, string Timestamp, LogLevel Level, string Message);

/// <summary>
/// The whole log. Answers <c>log.get</c> and <c>log.clear</c>, and is pushed with <c>log.cleared</c>.
/// </summary>
/// <param name="Revision">Rises with every change. A pushed event not newer than the revision the page holds is stale and is dropped.</param>
public record LogSnapshot(long Revision, IReadOnlyList<LogLine> Lines);

/// <summary>
/// Pushed with <c>log.appended</c>: the lines added since the last push, a batch of roughly 100 ms of output.
/// </summary>
/// <param name="Revision">The log's revision after the last line of the batch. Drop the batch when the page already holds this revision or a later one.</param>
/// <param name="Lines">New lines, oldest first.</param>
public record LogAppended(long Revision, IReadOnlyList<LogLine> Lines);
