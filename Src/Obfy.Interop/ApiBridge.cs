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

    public async Task<string> InvokeMethod(string serviceName, string methodName, string jsonParams)
    {
        try
        {
            logger.LogRequest(serviceName, methodName, jsonParams);
            var requestType = registry.GetRequestType(serviceName, methodName);
            if (requestType == null)
            {
                logger.LogWarning("Unknown API {Service}.{Method}", serviceName, methodName);
                return Respond(serviceName, methodName, new ApiError(UnknownApiCode, $"Unknown API: {serviceName}.{methodName}"));
            }

            var request = JsonUtils.Deserialize(jsonParams, requestType)
                ?? throw new ArgumentException($"Invalid params for {serviceName}.{methodName}.");

            // Task.Run keeps handlers off the UI thread that WebView2 calls in on. AsTask: Task.Run has no
            // ValueTask overload, so without it the await would yield the ValueTask itself.
            var result = await Task.Run(() => mediator.Send(request).AsTask()).ConfigureAwait(false);
            var json = JsonUtils.Serialize(new { success = true, data = result });
            logger.LogResponse(serviceName, methodName, json);
            return json;
        }
        catch (Exception ex)
        {
            var error = ErrorFor(ex);
            if (error.Code == InternalCode)
                logger.LogError(ex, "API call failed: {Service}.{Method}", serviceName, methodName);
            else
                // The code and the exception's type only: the message can hold a path the user typed or picked, and the log
                // file keeps everything from Debug up, so there is no level at which it is safe to write it.
                logger.LogWarning("API call {Service}.{Method} failed with {Code} ({ExceptionType})", serviceName, methodName, error.Code, ex.GetType().Name);
            return Respond(serviceName, methodName, error);
        }
    }

    internal static ApiError ErrorFor(Exception ex) => ex switch
    {
        ObfyApiException api => new ApiError(api.Code, api.Message),
        IOException or UnauthorizedAccessException or ArgumentException or JsonException => new ApiError(BadRequestCode, ex.Message),
        _ => new ApiError(InternalCode, InternalMessage),
    };

    private string Respond(string serviceName, string methodName, ApiError error)
    {
        var json = JsonUtils.Serialize(new { success = false, error });
        logger.LogResponse(serviceName, methodName, json);
        return json;
    }
}
