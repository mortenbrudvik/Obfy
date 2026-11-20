using Microsoft.Extensions.Logging;

namespace Obfy.Interop;

/// <summary>
/// Logs bridge traffic at Debug only, each request or response payload cut to 200 characters. Pushed events log
/// their name and size only, never the payload. Failures are logged by the bridge at Warning or Error without the payload.
/// </summary>
public class IpcLogger(ILogger<IpcLogger> logger)
{
    private const int MaxLoggedLength = 200;

    public void LogRequest(string service, string method, string? jsonParams) =>
        logger.LogDebug("[IPC ->] {Service}.{Method}({Params})", service, method, Truncate(jsonParams));

    public void LogResponse(string service, string method, string? json) =>
        logger.LogDebug("[IPC <-] {Service}.{Method} -> {Result}", service, method, Truncate(json));

    /// <summary>Logs a pushed event by name and serialized size. The payload (file paths, log lines) is not logged.</summary>
    public void LogEvent(string eventName, int jsonLength) =>
        logger.LogDebug("[IPC v] {EventName} ({Length} chars)", eventName, jsonLength);

    public void LogWarning(string message, params object?[] args) => logger.LogWarning(message, args);

    public void LogError(Exception exception, string message, params object?[] args) =>
        logger.LogError(exception, message, args);

    private static string Truncate(string? value)
    {
        if (value is null)
            return "(null)";
        return value.Length <= MaxLoggedLength ? value : value[..MaxLoggedLength] + "...";
    }
}
