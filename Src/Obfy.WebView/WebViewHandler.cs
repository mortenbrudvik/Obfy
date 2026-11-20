using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Obfy.WebView;

/// <summary>What <see cref="WebViewHandler"/> does about a WebView2 process that failed.</summary>
public enum ProcessFailureResponse
{
    /// <summary>WebView2 recovers by itself (a frame, the GPU or a helper process): only logged.</summary>
    None,

    /// <summary>The page's renderer exited or hung: the page is reloaded.</summary>
    Reload,

    /// <summary>
    /// The page cannot be brought back in this control: the browser process exited, or the renderer kept failing.
    /// <see cref="WebViewHandler.Unrecoverable"/> is raised.
    /// </summary>
    GiveUp,
}

/// <summary>Configures a <see cref="WebView2"/> once its CoreWebView2 is ready, and hardens it.</summary>
public class WebViewHandler
{
    /// <summary>A renderer that has failed this many times is not reloaded again: it would likely only fail again.</summary>
    public const int MaxReloads = 3;

    /// <summary>The host the bundled page is served from (<see cref="HostWebRootFolder"/>) unless another is given.</summary>
    public const string DefaultHostName = "obfy.localassets.invalid";

    private readonly ILogger _logger;
    private int _reloads;
    private Task? _start;

    public WebViewHandler(WebView2 webview, string initialUrl, string? environmentFolderPath = null, ILogger? logger = null)
    {
        WebView = webview;
        InitialUrl = initialUrl;
        EnvironmentFolderPath = environmentFolderPath;
        _logger = logger ?? NullLogger.Instance;

        // A document or link dragged onto the window would make the page navigate to it (blocked, but a navigation all
        // the same). The page takes no drop.
        WebView.AllowExternalDrop = false;

        // InitializeAsync catches everything itself, so the discarded task cannot fault.
        WebView.Loaded += (_, _) => _ = StartAsync();
    }

    /// <summary>
    /// Starts WebView2 and loads the page, once: later calls return the same task. The control's Loaded event calls
    /// it, and Loaded is raised again whenever the control rejoins a shown window, which must not set WebView2 up a
    /// second time. Call on the UI thread.
    /// </summary>
    public Task StartAsync() => _start ??= InitializeAsync();

    public Action<Exception>? InitializeFailed { get; set; }

    /// <summary>
    /// Raised on the UI thread when a WebView2 process failure leaves the page blank for good (see
    /// <see cref="ProcessFailureResponse.GiveUp"/>). Only a new WebView2, in practice a restart of the app, brings it back.
    /// </summary>
    public Action? Unrecoverable { get; set; }

    public WebView2 WebView { get; }
    public string InitialUrl { get; }
    public string? EnvironmentFolderPath { get; }
    public string HostObjectName { get; init; } = "webview";
    public object? HostObject { get; init; }

    /// <summary>
    /// Debug only: DevTools (F12 or the context menu), the browser's shortcut keys and its error page. DevTools never
    /// open by themselves.
    /// </summary>
    public bool EnableDevTools { get; init; }

    public string? HostWebRootFolder { get; init; }
    public string HostWebHostNameForFolder { get; init; } = DefaultHostName;

    /// <summary>
    /// The origins (scheme, host and port, such as <c>https://obfy.localassets.invalid</c>) the page may navigate to
    /// and fetch from. Everything else is cancelled or answered with 403, so no other page receives the host object.
    /// Empty by default: nothing loads until the origin the app is served from is listed.
    /// </summary>
    public IReadOnlyList<string> AllowedOrigins { get; init; } = [];

    /// <summary>The colour the web area shows before the page paints, instead of white. Read once, as WebView2 is initialized.</summary>
    public System.Drawing.Color? DefaultBackgroundColor { get; init; }

    /// <summary>
    /// Whether a right-click shows the browser's context menu when DevTools are off. Text fields keep it for
    /// Cut/Copy/Paste, and so does selected text for Copy; everywhere else it would only offer Back, Reload, Save as
    /// and Print.
    /// </summary>
    public static bool ShouldShowContextMenu(bool isEditable, bool hasSelection) => isEditable || hasSelection;

    /// <summary>
    /// Whether <paramref name="uri"/> has exactly the scheme, host and port of one of <paramref name="allowed"/>. A URI
    /// with no host (<c>about:blank</c>, <c>data:</c>, <c>file:</c>) is never allowed.
    /// </summary>
    public static bool IsAllowedOrigin(Uri uri, IReadOnlyList<string> allowed)
    {
        var origin = OriginOf(uri);
        if (origin == null)
            return false;

        foreach (var entry in allowed)
        {
            if (Uri.TryCreate(entry, UriKind.Absolute, out var parsed) && OriginOf(parsed) == origin)
                return true;
        }

        return false;
    }

    /// <summary>As <see cref="IsAllowedOrigin(Uri, IReadOnlyList{string})"/>; an address that does not parse is not allowed.</summary>
    public static bool IsAllowedOrigin(string? uri, IReadOnlyList<string> allowed) =>
        Uri.TryCreate(uri, UriKind.Absolute, out var parsed) && IsAllowedOrigin(parsed, allowed);

    /// <summary>
    /// What a Release log may keep of a blocked address: its scheme ("file", "https"), or "unknown" when it does not
    /// parse. The whole address could name a document or a page the user was working on; it is a Debug line.
    /// </summary>
    public static string LoggableAddress(string? uri) =>
        Uri.TryCreate(uri, UriKind.Absolute, out var parsed) ? parsed.Scheme : "unknown";

    /// <summary>The scheme, host and port of <paramref name="uri"/>, lower case and with the port always written out.</summary>
    private static string? OriginOf(Uri uri)
    {
        if (!uri.IsAbsoluteUri || string.IsNullOrEmpty(uri.Host) || uri.Port < 0)
            return null;
        return $"{uri.Scheme}://{uri.IdnHost}:{uri.Port}".ToLowerInvariant();
    }

    /// <summary>
    /// What to do about a failed process of <paramref name="kind"/>, after <paramref name="reloadsSoFar"/> reloads.
    /// After the browser process exits the control is closed for good. The page's renderer exiting or hanging is
    /// recovered by a reload, up to <see cref="MaxReloads"/> times. Everything else WebView2 recovers by itself.
    /// </summary>
    public static ProcessFailureResponse RespondTo(CoreWebView2ProcessFailedKind kind, int reloadsSoFar) => kind switch
    {
        CoreWebView2ProcessFailedKind.BrowserProcessExited => ProcessFailureResponse.GiveUp,
        CoreWebView2ProcessFailedKind.RenderProcessExited or CoreWebView2ProcessFailedKind.RenderProcessUnresponsive =>
            reloadsSoFar < MaxReloads ? ProcessFailureResponse.Reload : ProcessFailureResponse.GiveUp,
        _ => ProcessFailureResponse.None,
    };

    private void ApplySettings(CoreWebView2Settings settings)
    {
        // DevTools and browser shortcuts (F12, F5/Ctrl+R reload, Ctrl+P, Ctrl+F, zoom keys) follow EnableDevTools,
        // so Debug keeps them and Release does not. Editing keys (Ctrl+C/V/X/A/Z) are not affected.
        settings.AreDevToolsEnabled = EnableDevTools;
        settings.AreBrowserAcceleratorKeysEnabled = EnableDevTools;

        // Kept on so text fields have Cut/Copy/Paste; ContextMenuRequested hides it elsewhere in Release.
        settings.AreDefaultContextMenusEnabled = true;

        settings.AreDefaultScriptDialogsEnabled = false;
        settings.IsStatusBarEnabled = false;
        settings.IsZoomControlEnabled = false;
        // Debug keeps the error page so a stopped Vite dev server says so instead of showing a blank window.
        settings.IsBuiltInErrorPageEnabled = EnableDevTools;
        settings.IsPasswordAutosaveEnabled = false;
        settings.IsGeneralAutofillEnabled = false;
        // A touchpad swipe would go back to the about:blank the control starts on; NavigationStarting cancels that, but
        // there is no history worth swiping through.
        settings.IsSwipeNavigationEnabled = false;
        // SmartScreen would send the addresses the page loads to Microsoft. The page is Obfy's own, from a local folder
        // or the dev server.
        settings.IsReputationCheckingRequired = false;
    }

    private void OnProcessFailed(CoreWebView2ProcessFailedKind kind, CoreWebView2ProcessFailedReason reason, int exitCode)
    {
        _logger.LogError("WebView2 process failed: {Kind} ({Reason}, exit code {ExitCode})", kind, reason, exitCode);
        switch (RespondTo(kind, _reloads))
        {
            case ProcessFailureResponse.Reload:
                _reloads++;
                _logger.LogInformation("Reloading the page ({Reload} of at most {MaxReloads})", _reloads, MaxReloads);
                try
                {
                    WebView.Reload();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Could not reload the page");
                    RaiseSafely(() => Unrecoverable?.Invoke(), nameof(Unrecoverable));
                }

                break;
            case ProcessFailureResponse.GiveUp:
                _logger.LogError("The page cannot be recovered in this window; a restart brings it back");
                RaiseSafely(() => Unrecoverable?.Invoke(), nameof(Unrecoverable));
                break;
        }
    }

    private async Task InitializeAsync()
    {
        try
        {
            var environment = await CoreWebView2Environment.CreateAsync(
                userDataFolder: EnvironmentFolderPath,
                options: new CoreWebView2EnvironmentOptions
                {
                    AllowSingleSignOnUsingOSPrimaryAccount = false
                });

            await WebView.EnsureCoreWebView2Async(environment, ControllerOptions(environment));
            var core = WebView.CoreWebView2;

            ApplySettings(core.Settings);

            if (!EnableDevTools)
            {
                core.ContextMenuRequested += (_, args) =>
                {
                    var target = args.ContextMenuTarget;
                    if (!ShouldShowContextMenu(target.IsEditable, target.HasSelection))
                        args.Handled = true;
                };
            }

            if (HostObject != null)
                core.AddHostObjectToScript(HostObjectName, HostObject);

            if (!string.IsNullOrEmpty(HostWebRootFolder))
            {
                // The page is served from this host, and its own requests are same-origin, which CORS does not govern.
                // DenyCors only stops pages from other origins (which the filters below block anyway) reading the files.
                core.SetVirtualHostNameToFolderMapping(
                    HostWebHostNameForFolder,
                    HostWebRootFolder,
                    CoreWebView2HostResourceAccessKind.DenyCors);
            }

            InstallRequestFilter(core);
            InstallNavigationGuards(core);
            core.ProcessFailed += (_, args) => OnProcessFailed(args.ProcessFailedKind, args.Reason, args.ExitCode);

            if (!string.IsNullOrEmpty(InitialUrl))
                WebView.Source = new Uri(InitialUrl);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WebView2 initialization failed");
            RaiseSafely(() => InitializeFailed?.Invoke(ex), nameof(InitializeFailed));
        }
    }

    /// <summary>
    /// Runs a caller-supplied callback, logging and swallowing what it throws: the start task is discarded, so an
    /// exception from a callback would otherwise fault it unobserved.
    /// </summary>
    private void RaiseSafely(Action callback, string name)
    {
        try
        {
            callback();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "The {Callback} callback threw", name);
        }
    }

    /// <summary>
    /// The background colour, given to the browser as it is created so not even its first frame is white, and kept on
    /// the control so the control does not put its own default back. A runtime too old for the option still gets the
    /// control's colour, a moment later. Null when there is no colour to give, or the runtime cannot take it.
    /// </summary>
    private CoreWebView2ControllerOptions? ControllerOptions(CoreWebView2Environment environment)
    {
        if (DefaultBackgroundColor is not { } background)
            return null;

        WebView.DefaultBackgroundColor = background;
        try
        {
            var options = environment.CreateCoreWebView2ControllerOptions();
            options.DefaultBackgroundColor = background;
            return options;
        }
        catch (Exception ex) when (ex is NotImplementedException or InvalidCastException or COMException)
        {
            _logger.LogDebug(ex, "This WebView2 runtime cannot take the background colour at creation");
            return null;
        }
    }

    /// <summary>
    /// Every request from the page, its frames and workers: anything outside the allowed origins gets a 403 without
    /// reaching the network. The bundled CSP already blocks such requests in Release; this also covers Debug, which has
    /// no CSP, and anything the CSP does not govern.
    /// </summary>
    private void InstallRequestFilter(CoreWebView2 core)
    {
        core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All, CoreWebView2WebResourceRequestSourceKinds.All);
        core.WebResourceRequested += (_, args) =>
        {
            if (IsAllowedOrigin(args.Request.Uri, AllowedOrigins))
                return;
            LogBlocked("request to", args.Request.Uri);
            args.Response = core.Environment.CreateWebResourceResponse(null, 403, "Forbidden", "");
        };
    }

    /// <summary>
    /// The page and its frames navigate only within the allowed origins, and never open a window. The initial
    /// about:blank is navigated while the control is created, before these handlers exist.
    /// </summary>
    private void InstallNavigationGuards(CoreWebView2 core)
    {
        core.NavigationCompleted += (_, args) =>
        {
            if (args.IsSuccess)
                _logger.LogDebug("Navigation completed successfully");
            else
                _logger.LogWarning("Navigation failed: {WebErrorStatus}", args.WebErrorStatus);
        };

        core.NavigationStarting += (_, args) =>
        {
            if (!IsAllowedOrigin(args.Uri, AllowedOrigins))
            {
                LogBlocked("navigation to", args.Uri);
                args.Cancel = true;
            }
        };

        core.FrameNavigationStarting += (_, args) =>
        {
            if (!IsAllowedOrigin(args.Uri, AllowedOrigins))
            {
                LogBlocked("frame navigation to", args.Uri);
                args.Cancel = true;
            }
        };

        core.NewWindowRequested += (_, args) =>
        {
            LogBlocked("popup request to", args.Uri);
            args.Handled = true;
        };
    }

    /// <summary>A warning with only the address's scheme (see <see cref="LoggableAddress"/>); the whole address is Debug.</summary>
    private void LogBlocked(string what, string? uri)
    {
        _logger.LogWarning("Blocked {What} a {Scheme} address", what, LoggableAddress(uri));
        _logger.LogDebug("Blocked {What} {Uri}", what, uri);
    }
}
