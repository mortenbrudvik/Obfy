using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Web.WebView2.Wpf;

namespace Obfy.WebView;

/// <summary>The WebView2 host control, and the <see cref="IWebViewInterop"/> the bridge pushes events through.</summary>
public class WebViewControl : WebView2, IWebViewInterop
{
    private readonly ILogger _logger;

    /// <summary>Whether the page has been found unreachable since the last message that reached it. UI thread only.</summary>
    private bool _unreachableLogged;

    public WebViewControl() : this(NullLogger.Instance)
    {
    }

    /// <summary>For tests: the control with its log lines sent to <paramref name="logger"/>.</summary>
    protected WebViewControl(ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Always queued to the UI thread, even when called on it. Messages are posted from pool threads and the UI thread;
    /// one delivered at once on the UI thread would overtake those already queued from the others, so the page could end
    /// on an older state. One queue for all keeps the order they were posted in.
    /// </summary>
    public void PostWebMessageAsJson(string json) => Dispatcher.BeginInvoke(() => Deliver(json));

    /// <summary>
    /// Hands <paramref name="message"/> to the page, on the UI thread, or drops it while the page is not there. After the
    /// browser process exits (or the control is disposed) every post fails: the first failure is logged, the rest are
    /// dropped quietly until a message gets through again.
    /// </summary>
    private void Deliver(string message)
    {
        try
        {
            if (!PostToPage(message))
            {
                _logger.LogDebug("Message dropped: CoreWebView2 not initialized");
                return;
            }

            _unreachableLogged = false;
        }
        catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException or COMException)
        {
            if (!_unreachableLogged)
            {
                _unreachableLogged = true;
                _logger.LogWarning(ex, "Messages to the page dropped: WebView2 cannot be reached");
            }
        }
    }

    /// <summary>
    /// Posts <paramref name="message"/> to the page; false while the core is not initialized. Throws what the core throws
    /// once it cannot be reached (its getter as well as the post). Overridable so a test can stand in for a WebView2.
    /// </summary>
    protected virtual bool PostToPage(string message)
    {
        var core = CoreWebView2;
        if (core == null)
            return false;

        core.PostWebMessageAsJson(message);
        return true;
    }
}
