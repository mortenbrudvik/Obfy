namespace Obfy.WebView;

/// <summary>What the bridge needs of the WebView: a way to push a JSON message to the page.</summary>
public interface IWebViewInterop
{
    /// <summary>Posts <paramref name="json"/> to the page; the page receives it as a parsed message.</summary>
    void PostWebMessageAsJson(string json);
}
