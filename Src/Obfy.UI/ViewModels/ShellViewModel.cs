using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Obfy.App.Features.App;
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

    /// <summary>WebView2 could not start, or failed for good: the window shows the native failure panel instead.</summary>
    [ObservableProperty]
    private bool _webViewFailed;

    public ShellViewModel(
        WebViewControl webView, ApiBridge bridge, ThemeController theme, AppReadyCoordinator ready,
        ApiEventAggregator events, ILogger<ShellViewModel> logger)
    {
        WebView = webView;
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
            // Both arrive on the UI thread.
            InitializeFailed = ex =>
            {
                _logger.LogError(ex, "WebView2 could not start");
                WebViewFailed = true;
            },
            Unrecoverable = () =>
            {
                _logger.LogError("WebView2 failed for good");
                WebViewFailed = true;
            },
        };
    }

    /// <summary>Where the page's WebView2 profile lives.</summary>
    public static string UserDataFolder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Obfy", "WebView2");

    /// <summary>Where the logs are, for the failure panel (an instance property: bindings do not reach static ones).</summary>
    public string LogsDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Obfy", "Logs");

    /// <summary>The web UI, which the window shows unless <see cref="WebViewFailed"/>.</summary>
    public WebViewControl WebView { get; }

    /// <summary>Tells the page the window is now shown (<c>app.revealed</c>). Safe from any thread.</summary>
    public void NotifyRevealed() => _events.Publish(IpcNames.Events.AppRevealed, new { ready = _ready.IsReady });

    /// <summary>The failure panel's Restart: starts a new process and closes this one.</summary>
    [RelayCommand]
    private void Restart()
    {
        try
        {
            var path = Environment.ProcessPath;
            if (path is not null)
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not restart");
            return;
        }

        System.Windows.Application.Current.Shutdown();
    }

    [RelayCommand]
    private static void Quit() => System.Windows.Application.Current.Shutdown();

    /// <summary>The failure panel's Open log folder. A command must not throw: a folder that will not open is logged.</summary>
    [RelayCommand]
    private void OpenLogFolder()
    {
        try
        {
            Directory.CreateDirectory(LogsDirectory);
            Process.Start(new ProcessStartInfo(LogsDirectory) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not open the log folder {Path}", LogsDirectory);
        }
    }
}
