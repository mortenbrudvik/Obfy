using Obfy.Common;
using Obfy.WebView;

namespace Obfy.Interop;

/// <summary>Pushes an event to the page as <c>{eventName,data}</c>.</summary>
public class ApiEventAggregator(IWebViewInterop webView, IpcLogger logger)
{
    public virtual void Publish(string eventName, object? data)
    {
        var json = JsonUtils.Serialize(new { eventName, data });
        logger.LogEvent(eventName, json.Length);
        webView.PostWebMessageAsJson(json);
    }
}
