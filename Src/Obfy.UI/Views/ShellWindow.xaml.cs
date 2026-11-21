using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using Obfy.App.Features.App;
using Obfy.UI.Services;
using Obfy.UI.ViewModels;
using Wpf.Ui.Controls;

namespace Obfy.UI.Views;

/// <summary>
/// Hosts the web UI. WebView2 only starts in a window that has really been shown, so <see cref="Preload"/> shows the window
/// cloaked (Windows and WPF treat it as visible, the page loads and paints, the compositor does not draw it) and
/// <see cref="Reveal"/> uncovers it once the page says <c>app.ready</c>, WebView2 fails, or <see cref="RevealTimeout"/> passes.
/// If cloaking is refused the window is simply visible from the start.
/// </summary>
public partial class ShellWindow : FluentWindow
{
    /// <summary>How long a window waits for <c>app.ready</c> before it shows anyway (a page that never says it is ready).</summary>
    public static readonly TimeSpan RevealTimeout = TimeSpan.FromSeconds(8);

    private const int DwmwaCloak = 13;

    private readonly ShellViewModel _viewModel;
    private readonly AppReadyCoordinator _ready;
    private readonly ILogger<ShellWindow> _logger;
    private DispatcherTimer? _revealTimer;
    private bool _cloaked;
    private bool _revealed;

    public ShellWindow(
        ShellViewModel viewModel, ThemeController theme, AppReadyCoordinator ready, ILogger<ShellWindow> logger)
    {
        _viewModel = viewModel;
        _ready = ready;
        _logger = logger;
        DataContext = viewModel;
        InitializeComponent();
        theme.Attach(this);

        // The page says it is ready on a pool thread.
        _ready.Ready += OnReady;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;

        Closed += (_, _) =>
        {
            _ready.Ready -= OnReady;
            viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _revealTimer?.Stop();
        };
    }

    /// <summary>Shows the window where nobody can see it, so its page loads before anyone asks for it.</summary>
    public void Preload()
    {
        if (IsVisible)
            return;

        var handle = new WindowInteropHelper(this).EnsureHandle();
        _cloaked = SetCloaked(handle, true);
        if (!_cloaked)
        {
            _logger.LogWarning("Could not cloak the window while its page loads; it shows while loading");
            _revealed = true;
            Show();
            return;
        }

        ShowInTaskbar = false;
        ShowActivated = false;
        Show();

        _revealTimer = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher) { Interval = RevealTimeout };
        _revealTimer.Tick += (_, _) =>
        {
            _logger.LogWarning("The page did not say it was ready within {Seconds}s; showing the window anyway", RevealTimeout.TotalSeconds);
            Reveal();
        };
        _revealTimer.Start();

        // The page may have been ready before the subscription.
        if (_ready.IsReady)
            Reveal();
    }

    /// <summary>Uncovers the window and gives it the focus. Once; later calls do nothing.</summary>
    public void Reveal()
    {
        if (_revealed)
            return;

        _revealed = true;
        _revealTimer?.Stop();
        ShowInTaskbar = true;
        ShowActivated = true;
        Show();
        if (_cloaked)
        {
            SetCloaked(new WindowInteropHelper(this).Handle, false);
            _cloaked = false;
        }

        Activate();
        _viewModel.NotifyRevealed();
    }

    private void OnReady() => Dispatcher.BeginInvoke(Reveal);

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // The native failure panel must be seen even though the page never said it was ready.
        if (e.PropertyName == nameof(ShellViewModel.WebViewFailed) && _viewModel.WebViewFailed)
            Reveal();
    }

    private static bool SetCloaked(nint window, bool cloaked)
    {
        var value = cloaked ? 1 : 0;
        return DwmSetWindowAttribute(window, DwmwaCloak, ref value, sizeof(int)) == 0;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
}
