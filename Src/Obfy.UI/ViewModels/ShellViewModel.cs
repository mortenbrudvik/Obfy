using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Logging.Core.Configuration;
using Mediator;
using Microsoft.Extensions.Logging;
using Obfy.App.Features.App;
using Obfy.App.Sessions;
using Obfy.Interop;
using Obfy.UI.Services;
using Obfy.WebView;

namespace Obfy.UI.ViewModels;

/// <summary>
/// The hybrid shell: the web UI the window hosts, and the native failure panel shown instead when WebView2 cannot start
/// or fails for good.
/// </summary>
public partial class ShellViewModel : ObservableObject
{
    /// <summary>Mirrored in the page: window.chrome.webview.hostObjects.apibridge.</summary>
    public const string HostObjectName = "apibridge";

    private readonly ILogger<ShellViewModel> _logger;
    private readonly AppReadyCoordinator _ready;
    private readonly ApiEventAggregator _events;
    private readonly ShutdownCoordinator _shutdown;
    private readonly LogFileStatus _logStatus;

    /// <summary>WebView2 could not start, or failed for good: the window shows the native failure panel instead.</summary>
    [ObservableProperty]
    private bool _webViewFailed;

    public ShellViewModel(
        WebViewControl webView, ApiBridge bridge, ThemeController theme, AppReadyCoordinator ready,
        ApiEventAggregator events, ShutdownCoordinator shutdown, IMediator mediator, InputSession input,
        LogFileStatus logStatus, ILogger<ShellViewModel> logger)
    {
        WebView = webView;
        _shutdown = shutdown;
        _logStatus = logStatus;
        _ready = ready;
        _events = events;
        _logger = logger;

        // Before the handler, so DefaultBackgroundColor below is already the theme's.
        theme.Start();

#if DEBUG
        const string origin = "http://localhost:5250";
        var url = origin + "/";
#else
        const string origin = "https://" + WebViewHandler.DefaultHostName;
        var url = origin + "/index.html";
#endif

        // The control's Loaded event keeps the handler alive.
        _ = new WebViewHandler(WebView, url, UserDataFolder, logger)
        {
            HostObject = bridge,
            HostObjectName = HostObjectName,
            // The web area shows the theme's ground until the page paints, instead of white.
            DefaultBackgroundColor = theme.Ground,
            // Only the page's own origin. In Debug that is the Vite dev server, whose hot-reload websocket does not
            // raise WebResourceRequested and so needs no entry.
            AllowedOrigins = [origin],
#if DEBUG
            EnableDevTools = true,
#else
            EnableDevTools = false,
            // BaseDirectory, not CurrentDirectory: the working directory differs for "dotnet run" and a shortcut.
            HostWebRootFolder = Path.Combine(AppContext.BaseDirectory, "Assets"),
#endif
            // Off the UI thread, and a failure is told to the user (see DroppedFilesHandler).
            FilesDropped = new DroppedFilesHandler(mediator, input, logger).Handle,
            // All arrive on the UI thread.
            InitializeFailed = OnInitializeFailed,
            Unrecoverable = OnUnrecoverable,
            LoadFailed = OnLoadFailed,
            PageLoaded = OnPageLoaded,
        };
    }

    /// <summary>
    /// Names the environment variable that moves the page's WebView2 profile. The end-to-end tests give each launch its
    /// own profile, so a running Obfy (whose browser has other arguments) cannot clash with the test's debug port.
    /// </summary>
    public const string UserDataFolderVariable = "OBFY_WEBVIEW2_USER_DATA";

    /// <summary>Where the page's WebView2 profile lives (<see cref="UserDataFolderVariable"/> overrides the default).</summary>
    public static string UserDataFolder { get; } =
        Environment.GetEnvironmentVariable(UserDataFolderVariable) is { Length: > 0 } overridden
            ? overridden
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Obfy", "WebView2");

    /// <summary>Where the logs are, for the failure panel (an instance property: bindings do not reach static ones).</summary>
    public string LogsDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Obfy", "Logs");

    /// <summary>Whether there is a log file to read: false when the log folder could not be created at start-up.</summary>
    public bool LogsAvailable => _logStatus.FileLoggingAvailable;

    /// <summary>
    /// Where the details are, for the failure panel. When the log folder could not be created there are none, and saying
    /// "the log has the details" would send the user to a folder that is not there.
    /// </summary>
    public string LogsNote => LogsAvailable
        ? $"The log is in {ResolveDisplayLogDirectory()}."
        : "Obfy could not create its log folder, so there is no log to read.";

    /// <summary>What to try if restarting does not help; mentions the log only when there is one.</summary>
    public string FailureAdvice => LogsAvailable
        ? "If it doesn't, the log explains what went wrong, and the Microsoft Edge WebView2 Runtime may need repairing."
        : "If it doesn't, the Microsoft Edge WebView2 Runtime may need repairing.";

    /// <summary>The web UI, which the window shows unless <see cref="WebViewFailed"/>.</summary>
    public WebViewControl WebView { get; }

    /// <summary>Whether the page has finished loading (a successful top-level navigation to the app's own origin).</summary>
    public bool PageLoaded { get; private set; }

    // The panel is up for one of two kinds of reason. Some are for good: WebView2 did not start, or its browser process
    // is gone, and only a restart brings the page back. The others are only "the page is not there (yet)": a navigation
    // that failed, or a start that is slower than the reveal timeout. A page that loads after all takes those back.
    private bool _failedForGood;
    private bool _pageNotLoaded;

    private void ShowFailurePanelIfNeeded() => WebViewFailed = _failedForGood || _pageNotLoaded;

    /// <summary>The page loaded: the panel goes away unless the failure was for good.</summary>
    internal void OnPageLoaded()
    {
        PageLoaded = true;
        _pageNotLoaded = false;
        ShowFailurePanelIfNeeded();
    }

    /// <summary>A navigation to the app's own page ended without the page: a failure, or an HTTP error such as a missing file.</summary>
    internal void OnLoadFailed()
    {
        _logger.LogError("The page did not load");
        _pageNotLoaded = true;
        ShowFailurePanelIfNeeded();
    }

    /// <summary>
    /// The window waited <see cref="Views.ShellWindow.RevealTimeout"/> for the page. Only a page that never loaded is a
    /// failure here: one that has loaded and is merely late to say <c>app.ready</c> is shown as it is, and one that did say
    /// it is ready has loaded, whatever NavigationCompleted has reported yet.
    /// </summary>
    internal void OnRevealTimeout()
    {
        if (PageLoaded || _ready.IsReady)
            return;

        _logger.LogError("The page had not loaded {Seconds} s after the window was created", Views.ShellWindow.RevealTimeout.TotalSeconds);
        _pageNotLoaded = true;
        ShowFailurePanelIfNeeded();
    }

    /// <summary>WebView2 could not be started. Logs the exception's type only: its message may name the profile folder.</summary>
    internal void OnInitializeFailed(Exception ex)
    {
        _logger.LogError("WebView2 could not start ({ExceptionType})", ex.GetType().Name);
        _failedForGood = true;
        ShowFailurePanelIfNeeded();
    }

    internal void OnUnrecoverable()
    {
        _logger.LogError("WebView2 failed for good");
        _failedForGood = true;
        ShowFailurePanelIfNeeded();
    }

    /// <summary>Tells the page the window is now shown (<c>app.revealed</c>). Safe from any thread.</summary>
    public void NotifyRevealed() => _events.Publish(IpcNames.Events.AppRevealed, new { ready = _ready.IsReady });

    /// <summary>The failure panel's Restart: starts a new process and closes this one (after asking, when a run is going).</summary>
    [RelayCommand]
    private Task RestartAsync() => _shutdown.RestartAsync();

    /// <summary>The failure panel's Quit (after asking, when a run is going).</summary>
    [RelayCommand]
    private Task QuitAsync() => _shutdown.QuitAsync();

    private string ResolveDisplayLogDirectory() =>
        LogsDirectory.Equals(LoggingConfiguration.LogDirectory, StringComparison.OrdinalIgnoreCase)
            ? LoggingConfiguration.ResolveExplorerLogDirectory()
            : LogsDirectory;

    /// <summary>The failure panel's Open log folder. A command must not throw: a folder that will not open is logged.</summary>
    [RelayCommand(CanExecute = nameof(LogsAvailable))]
    private void OpenLogFolder()
    {
        try
        {
            Directory.CreateDirectory(LogsDirectory);
            Process.Start(new ProcessStartInfo(ResolveDisplayLogDirectory()) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Could not open the log folder ({ExceptionType})", ex.GetType().Name);
        }
    }
}
