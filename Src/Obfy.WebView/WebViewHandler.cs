using System.IO;
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
/// <remarks>
/// <para>
/// <b>Origin-isolation threat model:</b> Top-level <see cref="CoreWebView2"/> has no
/// <c>AddHostObjectToScriptWithOrigins</c> API (origin-scoped host-object registration only exists on
/// <see cref="CoreWebView2Frame"/> via <see cref="CoreWebView2Frame.AddHostObjectToScript(string, object, IEnumerable{string})"/>).
/// Any top-level document loaded into the control would therefore have access to <see cref="HostObject"/> unless
/// top-level access is strictly origin-gated.
/// </para>
/// <para>
/// Defense-in-depth layers enforcing origin isolation:
/// <list type="bullet">
/// <item><description><see cref="InitialUrl"/> validation: <see cref="HostObject"/> is not attached and initial navigation is skipped if <see cref="InitialUrl"/> is outside <see cref="AllowedOrigins"/> (including when <see cref="AllowedOrigins"/> is empty); <see cref="CoreWebView2Settings.AreHostObjectsAllowed"/> is disabled when <see cref="HostObject"/> is null.</description></item>
/// <item><description>Navigation and frame guards: <c>NavigationStarting</c>, <c>FrameNavigationStarting</c>, <c>FrameCreated</c> (<see cref="CoreWebView2Frame.NavigationStarting"/>), and <c>NewWindowRequested</c> block disallowed top-level navigations, subframes (never exposing the host object to child frames), and popups.</description></item>
/// <item><description>Lifecycle revocation: <see cref="HostObjectOriginGuard"/> reconciles attachment on <c>NavigationStarting</c>, <c>SourceChanged</c>, and <c>ContentLoading</c>, calling <c>RemoveHostObjectFromScript</c> immediately if the WebView ever lands on <c>about:blank</c>, an error page, or any non-allowed origin.</description></item>
/// <item><description>Network filter: <c>WebResourceRequested</c> answers any request outside <see cref="AllowedOrigins"/> with HTTP 403 before reaching the network.</description></item>
/// <item><description>Web message check: <c>WebMessageReceived</c> ignores any message whose <c>args.Source</c> is outside <see cref="AllowedOrigins"/>.</description></item>
/// <item><description>Release CSP: the bundled frontend enforces <c>default-src 'self'</c>, <c>script-src 'self'</c>, <c>style-src 'self'</c>, <c>object-src 'none'</c>, <c>base-uri 'none'</c>, and <c>form-action 'none'</c>.</description></item>
/// </list>
/// </para>
/// </remarks>
public class WebViewHandler
{
    /// <summary>A renderer that has failed this many times is not reloaded again: it would likely only fail again.</summary>
    public const int MaxReloads = 3;

    /// <summary>The host the bundled page is served from (<see cref="HostWebRootFolder"/>) unless another is given.</summary>
    public const string DefaultHostName = "obfy.localassets.invalid";

    /// <summary>
    /// The one message the page posts with <c>postMessageWithAdditionalObjects</c>; the dropped files travel as the
    /// additional objects. Mirrored in the page (Shell.tsx).
    /// </summary>
    public const string FilesDroppedMessage = "files.dropped";

    private readonly ILogger _logger;
    private readonly ReloadBudget _reloads = new();
    private Task? _start;

    public WebViewHandler(WebView2 webview, string initialUrl, string? environmentFolderPath = null, ILogger? logger = null)
    {
        WebView = webview;
        InitialUrl = initialUrl;
        EnvironmentFolderPath = environmentFolderPath;
        _logger = logger ?? NullLogger.Instance;

        // The web area's render window takes the OS drop, so a native drop never reaches the WPF window. The page
        // therefore accepts the drop itself and posts the dropped files to the host (FilesDropped), which reads their
        // real paths from the posted objects. Navigating to a dropped document is blocked by NavigationStarting, and the
        // page cancels the browser's default handling of the drop.
        WebView.AllowExternalDrop = true;

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

    /// <summary>
    /// Raised on the UI thread when a navigation to an allowed origin ends with the page not loaded (see
    /// <see cref="IsLoadFailure"/>). A later successful load raises <see cref="PageLoaded"/>.
    /// </summary>
    public Action? LoadFailed { get; set; }

    /// <summary>Raised on the UI thread when a navigation to an allowed origin ends with the page loaded.</summary>
    public Action? PageLoaded { get; set; }

    /// <summary>
    /// Raised on the UI thread with the full paths of the files the user dropped onto the page (see
    /// <see cref="FilesDroppedMessage"/>). The page cannot know a file's path; the host reads it from the posted file objects.
    /// </summary>
    public Action<IReadOnlyList<string>>? FilesDropped { get; set; }

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
    public static bool IsAllowedOrigin(Uri uri, IReadOnlyList<string> allowed) =>
        IsAllowedOriginCore(uri, allowed);

    /// <summary>As <see cref="IsAllowedOrigin(Uri, IReadOnlyList{string})"/>; an address that does not parse is not allowed.</summary>
    public static bool IsAllowedOrigin(string? uri, IReadOnlyList<string> allowed) =>
        IsAllowedOriginCore(uri, allowed);

    /// <summary>
    /// Whether a subframe navigation to <paramref name="uri"/> is allowed. Only addresses whose scheme, host and port
    /// match <paramref name="allowedOrigins"/> are permitted; <c>about:blank</c>, <c>about:srcdoc</c>, <c>data:</c>,
    /// <c>javascript:</c>, <c>file:</c>, foreign origins, and empty allowlists are rejected.
    /// </summary>
    internal static bool ShouldAllowSubframeNavigation(string? uri, IReadOnlySet<string> allowedOrigins)
    {
        ArgumentNullException.ThrowIfNull(allowedOrigins);
        return IsAllowedOriginCore(uri, allowedOrigins);
    }

    /// <summary>Normalizes each absolute origin entry via <see cref="OriginOf"/>, discarding invalid or hostless entries.</summary>
    internal static HashSet<string> NormalizeAllowedOrigins(IEnumerable<string> allowed)
    {
        ArgumentNullException.ThrowIfNull(allowed);

        var normalized = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in allowed)
        {
            if (Uri.TryCreate(entry, UriKind.Absolute, out var parsed) && OriginOf(parsed) is { } origin)
                normalized.Add(origin);
        }

        return normalized;
    }

    internal static bool IsAllowedOriginCore(string? uri, IEnumerable<string> allowed) =>
        Uri.TryCreate(uri, UriKind.Absolute, out var parsed) && IsAllowedOriginCore(parsed, allowed);

    private static bool IsAllowedOriginCore(Uri uri, IEnumerable<string> allowed)
    {
        var origin = OriginOf(uri);
        if (origin == null)
            return false;

        if (allowed is IReadOnlySet<string> set && set.Contains(origin))
            return true;

        foreach (var entry in allowed)
        {
            if (Uri.TryCreate(entry, UriKind.Absolute, out var parsed) && OriginOf(parsed) == origin)
                return true;
        }

        return false;
    }

    /// <summary>
    /// What the log may keep of a blocked address: its scheme ("file", "https"), or "unknown" when it does not parse. The
    /// whole address could name a document or a page the user was working on, and the log file keeps everything from
    /// Debug up, so it is never logged.
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

    /// <summary>Whether <paramref name="message"/> is the page's file-drop message (<see cref="FilesDroppedMessage"/>), and nothing else.</summary>
    public static bool IsFilesDroppedMessage(string? message) => string.Equals(message, FilesDroppedMessage, StringComparison.Ordinal);

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

    /// <summary>
    /// Whether a finished top-level navigation left the page unloaded: it failed (cannot connect, aborted, ...) or the server
    /// answered 4xx/5xx (a virtual host answers 404 for a file that is not there, with the navigation itself "successful").
    /// A cancelled navigation is not a failure: <c>NavigationStarting</c> cancels a dropped document or a link out of the app,
    /// and a newer navigation supersedes an older one.
    /// </summary>
    public static bool IsLoadFailure(bool isSuccess, CoreWebView2WebErrorStatus status, int httpStatusCode) =>
        status != CoreWebView2WebErrorStatus.OperationCanceled && (!isSuccess || httpStatusCode >= 400);

    /// <summary>The page the folder must hold for the virtual host to have anything to show.</summary>
    public const string EntryPage = "index.html";

    /// <summary>
    /// Throws when <paramref name="folder"/> has no <see cref="EntryPage"/>: <c>SetVirtualHostNameToFolderMapping</c> does not
    /// look, and the window would only show a blank page. The message names no path, so it can go to the log.
    /// </summary>
    /// <exception cref="FileNotFoundException">The page is missing; the install is damaged.</exception>
    public static void EnsureWebRoot(string folder)
    {
        if (!File.Exists(Path.Combine(folder, EntryPage)))
            throw new FileNotFoundException($"The page Obfy shows ({EntryPage}, in the Assets folder beside the program) is missing.");
    }

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
        // A touchpad pinch would zoom the page, which has no zoom control of its own and is laid out for 100%.
        settings.IsPinchZoomEnabled = false;
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
        // Disable host objects altogether when none is configured; otherwise HostObjectOriginGuard gates access by origin.
        settings.AreHostObjectsAllowed = HostObject != null;
    }

    private void OnProcessFailed(CoreWebView2ProcessFailedKind kind, CoreWebView2ProcessFailedReason reason, int exitCode)
    {
        _logger.LogError("WebView2 process failed: {Kind} ({Reason}, exit code {ExitCode})", kind, reason, exitCode);
        switch (RespondTo(kind, _reloads.Used))
        {
            case ProcessFailureResponse.Reload:
                _reloads.Record();
                _logger.LogInformation("Reloading the page ({Reload} of at most {MaxReloads})", _reloads.Used, MaxReloads);
                try
                {
                    WebView.Reload();
                }
                catch (Exception ex)
                {
                    _logger.LogError("Could not reload the page ({ExceptionType})", ex.GetType().Name);
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

            if (!string.IsNullOrEmpty(HostWebRootFolder))
            {
                EnsureWebRoot(HostWebRootFolder);

                // The page is served from this host, and its own requests are same-origin, which CORS does not govern.
                // DenyCors only stops pages from other origins (which the filters below block anyway) reading the files.
                core.SetVirtualHostNameToFolderMapping(
                    HostWebHostNameForFolder,
                    HostWebRootFolder,
                    CoreWebView2HostResourceAccessKind.DenyCors);
            }

            var allowedOrigins = NormalizeAllowedOrigins(AllowedOrigins);
            var hostObjectGuard = CreateHostObjectGuard(core);

            InstallRequestFilter(core, allowedOrigins);
            InstallNavigationGuards(core, hostObjectGuard, allowedOrigins);
            InstallDefaultDenials(core);
            core.WebMessageReceived += (_, args) => OnWebMessage(args, allowedOrigins);
            core.ProcessFailed += (_, args) => OnProcessFailed(args.ProcessFailedKind, args.Reason, args.ExitCode);

            // Top-level CoreWebView2 has no AddHostObjectToScriptWithOrigins API (only CoreWebView2Frame does), so
            // attach HostObject only when InitialUrl is permitted.
            hostObjectGuard?.ReconcileForUri(InitialUrl, allowedOrigins);

            if (!IsAllowedOriginCore(InitialUrl, allowedOrigins))
            {
                LogBlocked("initial navigation to", InitialUrl);
            }
            else if (!string.IsNullOrEmpty(InitialUrl))
            {
                WebView.Source = new Uri(InitialUrl);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError("WebView2 initialization failed ({ExceptionType})", ex.GetType().Name);
            RaiseSafely(() => InitializeFailed?.Invoke(ex), nameof(InitializeFailed));
        }
    }

    private HostObjectOriginGuard? CreateHostObjectGuard(CoreWebView2 core)
    {
        if (HostObject is not { } hostObject)
            return null;

        return new HostObjectOriginGuard(
            attach: () => core.AddHostObjectToScript(HostObjectName, hostObject),
            detach: () => TryWebViewInterop(() => core.RemoveHostObjectFromScript(HostObjectName), "remove host object"));
    }

    /// <summary>
    /// A message the page posted. Only <see cref="FilesDroppedMessage"/> from an allowed origin is acted on; every other
    /// message is ignored. The dropped files arrive as <see cref="CoreWebView2File"/> objects, whose
    /// <see cref="CoreWebView2File.Path"/> is the real path on disk; anything else among the objects is skipped.
    /// </summary>
    private void OnWebMessage(CoreWebView2WebMessageReceivedEventArgs args, IReadOnlySet<string> allowedOrigins)
    {
        try
        {
            if (!IsAllowedOriginCore(args.Source, allowedOrigins))
                return;

            // Throws when the message is not a string.
            string message;
            try
            {
                message = args.TryGetWebMessageAsString();
            }
            catch (ArgumentException)
            {
                return;
            }

            if (!IsFilesDroppedMessage(message))
                return;

            var paths = new List<string>();
            foreach (var item in args.AdditionalObjects)
            {
                if (item is CoreWebView2File { Path: { Length: > 0 } path })
                    paths.Add(path);
            }

            if (paths.Count > 0)
                RaiseSafely(() => FilesDropped?.Invoke(paths), nameof(FilesDropped));
        }
        catch (Exception ex)
        {
            _logger.LogError("Could not read a message from the page ({ExceptionType})", ex.GetType().Name);
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
            _logger.LogError("The {Callback} callback threw ({ExceptionType})", name, ex.GetType().Name);
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
            _logger.LogDebug("This WebView2 runtime cannot take the background colour at creation ({ExceptionType})", ex.GetType().Name);
            return null;
        }
    }

    /// <summary>
    /// Every request from the page, its frames and workers: anything outside the allowed origins gets a 403 without
    /// reaching the network. The bundled CSP already blocks such requests in Release; this also covers Debug, which has
    /// no CSP, and anything the CSP does not govern.
    /// </summary>
    private void InstallRequestFilter(CoreWebView2 core, IReadOnlySet<string> allowedOrigins)
    {
        core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All, CoreWebView2WebResourceRequestSourceKinds.All);
        core.WebResourceRequested += (_, args) =>
        {
            if (IsAllowedOriginCore(args.Request.Uri, allowedOrigins))
                return;
            LogBlocked("request to", args.Request.Uri);
            args.Response = core.Environment.CreateWebResourceResponse(null, 403, "Forbidden", "");
        };
    }

    /// <summary>
    /// The page and its frames navigate only within the allowed origins, and never open a window. The initial
    /// about:blank is navigated while the control is created, before these handlers exist.
    /// </summary>
    private void InstallNavigationGuards(CoreWebView2 core, HostObjectOriginGuard? hostObjectGuard, IReadOnlySet<string> allowedOrigins)
    {
        core.NavigationCompleted += (_, args) =>
        {
            if (IsLoadFailure(args.IsSuccess, args.WebErrorStatus, args.HttpStatusCode))
            {
                _logger.LogWarning("Navigation failed: {WebErrorStatus}, HTTP status {HttpStatusCode}", args.WebErrorStatus, args.HttpStatusCode);
                RaiseSafely(() => LoadFailed?.Invoke(), nameof(LoadFailed));
            }
            else if (args.IsSuccess)
            {
                _logger.LogDebug("Navigation completed successfully");
                RaiseSafely(() => PageLoaded?.Invoke(), nameof(PageLoaded));
            }
            else
            {
                // Cancelled: a blocked address or a navigation that a newer one replaced.
                _logger.LogDebug("Navigation cancelled");
            }
        };

        core.NavigationStarting += (_, args) =>
        {
            if (!IsAllowedOriginCore(args.Uri, allowedOrigins))
            {
                LogBlocked("navigation to", args.Uri);
                args.Cancel = true;
                return;
            }

            // Re-attach before an allowed page loads if the host object was previously revoked.
            hostObjectGuard?.ReconcileForUri(args.Uri, allowedOrigins);
        };

        // Immediately revoke the top-level host object if the WebView ever lands on about:blank, an error page, or
        // any non-allowed origin (top-level CoreWebView2 lacks AddHostObjectToScriptWithOrigins).
        core.SourceChanged += (_, _) => hostObjectGuard?.ReconcileForUri(core.Source, allowedOrigins);
        core.ContentLoading += (_, args) => hostObjectGuard?.ReconcileForUri(core.Source, allowedOrigins, isErrorPage: args.IsErrorPage);

        core.FrameNavigationStarting += (_, args) =>
        {
            if (!ShouldAllowSubframeNavigation(args.Uri, allowedOrigins))
            {
                LogBlocked("frame navigation to", args.Uri);
                args.Cancel = true;
            }
        };

        TryWebViewInterop(() => core.FrameCreated += (_, args) =>
        {
            TryWebViewInterop(() =>
            {
                var frame = args.Frame;

                // Defense-in-depth alongside FrameNavigationStarting; host objects are never exposed to child frames via
                // CoreWebView2Frame.AddHostObjectToScript.
                void OnFrameNavigationStarting(object? _, CoreWebView2NavigationStartingEventArgs frameArgs)
                {
                    if (!ShouldAllowSubframeNavigation(frameArgs.Uri, allowedOrigins))
                    {
                        LogBlocked("subframe navigation to", frameArgs.Uri);
                        frameArgs.Cancel = true;
                    }
                }

                void OnFrameDestroyed(object? _, object __) =>
                    TryWebViewInterop(() =>
                    {
                        frame.NavigationStarting -= OnFrameNavigationStarting;
                        frame.Destroyed -= OnFrameDestroyed;
                    }, "detach subframe navigation guard");

                frame.NavigationStarting += OnFrameNavigationStarting;
                frame.Destroyed += OnFrameDestroyed;
            }, "attach subframe navigation guard");
        }, "subscribe to FrameCreated");

        core.NewWindowRequested += (_, args) =>
        {
            LogBlocked("popup request to", args.Uri);
            args.Handled = true;
        };
    }

    /// <summary>
    /// Runs a WebView2 COM/event operation, logging and ignoring interop or disposal failures so older WebView2 runtimes
    /// and teardown races do not surface as unhandled exceptions.
    /// </summary>
    private void TryWebViewInterop(Action action, string operation)
    {
        try
        {
            action();
        }
        catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException or COMException or NotImplementedException or InvalidCastException)
        {
            _logger.LogDebug("Could not {Operation} ({ExceptionType})", operation, ex.GetType().Name);
        }
    }

    /// <summary>
    /// The page is Obfy's own and asks for nothing from the browser: no camera, microphone, location, notifications or
    /// clipboard read, no file downloads (exports go through the host's own file dialogs) and no script dialogs. WebView2's
    /// defaults for the page's origin would prompt for the first, start the second and, with default dialogs off, leave
    /// the third to whoever handles the event. Each is refused here, explicitly, so a page that is ever compromised or
    /// changed cannot get more than the host gives it.
    /// </summary>
    private void InstallDefaultDenials(CoreWebView2 core)
    {
        core.PermissionRequested += (_, args) =>
        {
            _logger.LogDebug("Denied a {Kind} permission request", args.PermissionKind);
            args.State = CoreWebView2PermissionState.Deny;
            args.Handled = true;
        };

        core.DownloadStarting += (_, args) =>
        {
            _logger.LogDebug("Cancelled a download");
            args.Cancel = true;
            args.Handled = true;
        };

        // Default script dialogs are off (ApplySettings); a dialog the handler does not accept is dismissed.
        core.ScriptDialogOpening += (_, args) => _logger.LogDebug("Dismissed a {Kind} script dialog", args.Kind);
    }

    /// <summary>A warning with only the address's scheme (see <see cref="LoggableAddress"/>); the whole address is not logged.</summary>
    private void LogBlocked(string what, string? uri)
    {
        _logger.LogWarning("Blocked {What} a {Scheme} address", what, LoggableAddress(uri));
    }
}
