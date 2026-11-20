using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;
using Obfy.WebView;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace Obfy.UI.Services;

/// <summary>
/// Shows the light or the dark theme, following Windows' app mode, everywhere at once so the frame and the page never
/// disagree: WPF-UI's resources, the shell window's ground and frame, the colour the web area shows before the page
/// paints, and the page's <c>prefers-color-scheme</c>. All of it happens on the UI thread.
/// </summary>
public sealed class ThemeController : IDisposable
{
    // WPF-UI's own window colours, so the window, the web area before first paint and the page ground agree.
    private static readonly System.Drawing.Color DarkGround = System.Drawing.Color.FromArgb(0x20, 0x20, 0x20);
    private static readonly System.Drawing.Color LightGround = System.Drawing.Color.FromArgb(0xFA, 0xFA, 0xFA);

    private readonly ILogger<ThemeController> _logger;
    private readonly WebViewControl _webView;
    private readonly Func<bool> _systemIsDark;
    private readonly bool _processWide;

    // UI thread only. The theme shown is null until Start.
    private bool? _showsDark;
    private Window? _window;
    private bool _started;
    private bool _disposed;

    public ThemeController(ILogger<ThemeController> logger, WebViewControl webView)
        : this(logger, webView, WindowsAppModeIsDark, processWide: true)
    {
    }

    /// <param name="logger">Where the theme's log lines go.</param>
    /// <param name="webView">The web area to theme.</param>
    /// <param name="systemIsDark">Whether Windows is in dark app mode now.</param>
    /// <param name="processWide">Whether to touch what the whole process shares (WPF-UI's theme, Windows' settings broadcasts).</param>
    internal ThemeController(ILogger<ThemeController> logger, WebViewControl webView, Func<bool> systemIsDark, bool processWide)
    {
        _logger = logger;
        _webView = webView;
        _systemIsDark = systemIsDark;
        _processWide = processWide;
    }

    /// <summary>The ground colour of the theme shown (or the system's before <see cref="Start"/>): what the web area shows until the page paints.</summary>
    public System.Drawing.Color Ground => GroundOf(_showsDark ?? _systemIsDark());

    /// <summary>Shows the system theme now, then follows Windows. On the UI thread, before the window is drawn.</summary>
    public void Start()
    {
        if (_started || _disposed)
            return;

        _started = true;
        Show(_systemIsDark());

        // The page's colour scheme can only be set once WebView2 has started.
        _webView.CoreWebView2InitializationCompleted += OnWebViewInitialized;
        // Raised for WM_SETTINGCHANGE, which Windows broadcasts when the app mode changes ("ImmersiveColorSet").
        if (_processWide)
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    /// <summary>
    /// The shell window's ground and frame follow the theme from now on. WPF-UI writes its own background on a
    /// FluentWindow when it gets its handle, and sets the dark frame only on the application's main window, so both are put
    /// right once the window has its handle and after every change of theme.
    /// </summary>
    public void Attach(Window window)
    {
        if (_disposed)
            return;

        _window = window;
        window.SourceInitialized += OnWindowSourceInitialized;
        ShowInWindow();
    }

    /// <summary>Windows may have changed its app mode. From any thread.</summary>
    internal void SystemThemeChanged() => _webView.Dispatcher.BeginInvoke(() =>
    {
        if (!_disposed)
            Show(_systemIsDark());
    });

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        if (_window is not null)
            _window.SourceInitialized -= OnWindowSourceInitialized;
        if (!_started)
            return;

        _webView.CoreWebView2InitializationCompleted -= OnWebViewInitialized;
        if (_processWide)
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
    }

    private void Show(bool dark)
    {
        if (_showsDark == dark)
            return;

        _showsDark = dark;
        _logger.LogDebug("Showing the {Theme} theme", dark ? "dark" : "light");

        // No backdrop: the ground is solid, and WPF-UI's default would put Mica on the window.
        if (_processWide)
            ApplicationThemeManager.Apply(dark ? ApplicationTheme.Dark : ApplicationTheme.Light, WindowBackdropType.None);

        ShowInWindow();
        ShowInWebView();
    }

    private void ShowInWindow()
    {
        if (_window is not { } window || _showsDark is not { } dark)
            return;

        var handle = new WindowInteropHelper(window).Handle;
        if (handle != 0)
        {
            WindowBackgroundManager.UpdateBackground(
                window, dark ? ApplicationTheme.Dark : ApplicationTheme.Light, WindowBackdropType.None);

            // What the window shows before WPF draws into it.
            if (HwndSource.FromHwnd(handle)?.CompositionTarget is { } target)
                target.BackgroundColor = ToMediaColor(GroundOf(dark));
        }

        window.Background = new SolidColorBrush(ToMediaColor(GroundOf(dark)));
    }

    private void OnWindowSourceInitialized(object? sender, EventArgs e)
    {
        if (!_disposed)
            ShowInWindow();
    }

    private void ShowInWebView()
    {
        if (_showsDark is not { } dark)
            return;

        _webView.DefaultBackgroundColor = GroundOf(dark);

        try
        {
            if (_webView.CoreWebView2 is { } core)
                core.Profile.PreferredColorScheme = dark ? CoreWebView2PreferredColorScheme.Dark : CoreWebView2PreferredColorScheme.Light;
        }
        catch (Exception ex) when (ex is NotImplementedException or InvalidCastException or COMException or InvalidOperationException)
        {
            // A runtime too old for the setting, or a WebView2 that has gone away: the page follows Windows instead.
            _logger.LogWarning(ex, "Could not set the page's colour scheme");
        }
    }

    private void OnWebViewInitialized(object? sender, CoreWebView2InitializationCompletedEventArgs e)
    {
        // Also the colour again: WebViewHandler set the one the theme had when the window was built.
        if (e.IsSuccess && !_disposed)
            ShowInWebView();
    }

    private void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e) => SystemThemeChanged();

    private static System.Drawing.Color GroundOf(bool dark) => dark ? DarkGround : LightGround;

    private static Color ToMediaColor(System.Drawing.Color c) => Color.FromArgb(c.A, c.R, c.G, c.B);

    /// <summary>
    /// Whether Windows' app mode (Settings, Personalisation, Colours) is dark. Read from where Windows keeps it rather than
    /// from WPF-UI, whose answer is cached. Light when it cannot be read.
    /// </summary>
    private static bool WindowsAppModeIsDark()
    {
        try
        {
            return Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1) is 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or IOException)
        {
            System.Diagnostics.Debug.WriteLine($"Could not read the Windows app mode: {ex.Message}");
            return false;
        }
    }
}
