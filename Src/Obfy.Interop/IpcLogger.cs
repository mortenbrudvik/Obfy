using Microsoft.Extensions.Logging;

namespace Obfy.Interop;

/// <summary>
/// Logs bridge traffic at Debug as the call's name and the payload's length, never its text. The log file takes Debug and
/// above, and a payload can hold what the user typed (an output folder, a symbol-map path, a watermark id, a key-file
/// path), so it must not be written there. Pushed events log the same way. Failures are logged by the bridge at Warning
/// or Error without the payload.
/// </summary>
public class IpcLogger(ILogger<IpcLogger> logger)
{
    public void LogRequest(string service, string method, string? jsonParams) =>
        logger.LogDebug("[IPC ->] {Service}.{Method} ({Length} chars)", service, method, jsonParams?.Length ?? 0);

    public void LogResponse(string service, string method, string? json) =>
        logger.LogDebug("[IPC <-] {Service}.{Method} ({Length} chars)", service, method, json?.Length ?? 0);

    /// <summary>Logs a pushed event by name and serialized size. The payload (file paths, log lines) is not logged.</summary>
    public void LogEvent(string eventName, int jsonLength) =>
        logger.LogDebug("[IPC v] {EventName} ({Length} chars)", eventName, jsonLength);

    public void LogWarning(string message, params object?[] args) => logger.LogWarning(message, args);

    public void LogError(Exception exception, string message, params object?[] args) =>
        logger.LogError(exception, message, args);

    /// <summary>An error without the exception object: for a failure whose message may hold what the user typed.</summary>
    public void LogError(string message, params object?[] args) => logger.LogError(message, args);
}
