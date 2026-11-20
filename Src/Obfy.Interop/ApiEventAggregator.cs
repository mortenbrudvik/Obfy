using Obfy.Common;
using Obfy.WebView;

namespace Obfy.Interop;

/// <summary>Pushes an event to the page as <c>{eventName,data}</c>.</summary>
public class ApiEventAggregator(IWebViewInterop webView)
{
    public virtual void Publish(string eventName, object? data)
    {
        var json = JsonUtils.Serialize(new { eventName, data });
        webView.PostWebMessageAsJson(json);
    }
}
