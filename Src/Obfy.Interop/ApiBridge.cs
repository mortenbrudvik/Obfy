using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using Mediator;
using Obfy.App;
using Obfy.Common;

namespace Obfy.Interop;

/// <summary>The host object the page calls (<c>window.chrome.webview.hostObjects.apibridge</c>).</summary>
[ComVisible(true)]
[InterfaceType(ComInterfaceType.InterfaceIsDual)]
public interface IApiBridge
{
    Task<string> InvokeMethod(string serviceName, string methodName, string jsonParams);
}

/// <summary>
/// Looks the call up in <see cref="ApiRegistry"/>, sends the request through Mediator and answers with
/// <c>{"success":true,"data":...}</c> or <c>{"success":false,"error":{"code":...,"message":...}}</c>. It never throws:
/// an exception would reach the page as an unreadable COM error.
/// </summary>
[ComVisible(true)]
[ClassInterface(ClassInterfaceType.None)]
public class ApiBridge(IMediator mediator, ApiRegistry registry, IpcLogger logger) : IApiBridge
{
    public const string UnknownApiCode = "UnknownApi";
    public const string BadRequestCode = "BadRequest";
    public const string InternalCode = "Internal";

    /// <summary>What the page is told when a call fails in a way that is not the user's to fix; the log has the rest.</summary>
    public const string InternalMessage = "Something went wrong. The log has the details.";

    /// <summary>The longest name of a page-supplied service or method the log keeps.</summary>
    private const int MaxLoggedNameLength = 64;

    public async Task<string> InvokeMethod(string serviceName, string methodName, string jsonParams)
    {
        // The names come from the page. A registered one is plain; one that is not may hold anything (a line break would
        // forge a log line), so the log gets a cut, plain-text version of both, never the text as sent.
        var service = LogName(serviceName);
        var method = LogName(methodName);
        try
        {
            logger.LogRequest(service, method, jsonParams);
            var requestType = registry.GetRequestType(serviceName, methodName);
            if (requestType == null)
            {
                logger.LogWarning("Unknown API {Service}.{Method}", service, method);
                return Respond(service, method, new ApiError(UnknownApiCode, $"Unknown API: {serviceName}.{methodName}"));
            }

            // Only reading the page's JSON is the page's mistake. An ArgumentException or JsonException out of a handler
            // is a bug in the host, and falls through to Internal, which is logged as an error.
            object? request;
            try
            {
                request = JsonUtils.Deserialize(jsonParams, requestType);
            }
            catch (Exception ex) when (ex is JsonException or ArgumentException)
            {
                return BadRequest(service, method, ex.Message, ex.GetType().Name);
            }

            if (request is null)
                return BadRequest(service, method, $"Invalid params for {serviceName}.{methodName}.", nameof(ArgumentException));

            // Task.Run keeps handlers off the UI thread that WebView2 calls in on. AsTask: Task.Run has no
            // ValueTask overload, so without it the await would yield the ValueTask itself.
            var result = await Task.Run(() => mediator.Send(request).AsTask()).ConfigureAwait(false);
            var json = JsonUtils.Serialize(new { success = true, data = result });
            logger.LogResponse(service, method, json);
            return json;
        }
        catch (Exception ex)
        {
            // The code and the exception's type only, at every level: the message can hold a path the user typed or picked,
            // and the log file keeps everything from Debug up, so there is no level at which it is safe to write it.
            var error = ErrorFor(ex);
            if (error.Code == InternalCode)
                logger.LogError("API call {Service}.{Method} failed with {Code} ({ExceptionType})", service, method, error.Code, ex.GetType().Name);
            else
                logger.LogWarning("API call {Service}.{Method} failed with {Code} ({ExceptionType})", service, method, error.Code, ex.GetType().Name);
            return Respond(service, method, error);
        }
    }

    internal static ApiError ErrorFor(Exception ex) => ex switch
    {
        ObfyApiException api => new ApiError(api.Code, api.Message),
        IOException or UnauthorizedAccessException => new ApiError(BadRequestCode, ex.Message),
        _ => new ApiError(InternalCode, InternalMessage),
    };

    private string BadRequest(string service, string method, string message, string exceptionType)
    {
        logger.LogWarning("API call {Service}.{Method} failed with {Code} ({ExceptionType})", service, method, BadRequestCode, exceptionType);
        return Respond(service, method, new ApiError(BadRequestCode, message));
    }

    /// <summary>A name as the log may hold it: letters, digits, <c>.</c>, <c>_</c> and <c>-</c>, anything else a <c>?</c>, cut at 64 characters.</summary>
    private static string LogName(string? name)
    {
        if (string.IsNullOrEmpty(name))
            return string.Empty;

        var length = Math.Min(name.Length, MaxLoggedNameLength);
        var plain = string.Create(length, name, static (span, source) =>
        {
            for (var i = 0; i < span.Length; i++)
                span[i] = char.IsAsciiLetterOrDigit(source[i]) || source[i] is '.' or '_' or '-' ? source[i] : '?';
        });
        return name.Length > MaxLoggedNameLength ? plain + "..." : plain;
    }

    private string Respond(string serviceName, string methodName, ApiError error)
    {
        var json = JsonUtils.Serialize(new { success = false, error });
        logger.LogResponse(serviceName, methodName, json);
        return json;
    }
}
