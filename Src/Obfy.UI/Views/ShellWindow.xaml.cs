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
    private readonly ShutdownCoordinator _shutdown;
    private readonly AppReadyCoordinator _ready;
    private readonly ILogger<ShellWindow> _logger;
    private DispatcherTimer? _revealTimer;
    private bool _cloaked;
    private bool _revealed;
    private bool _closed;

    // Internal test seams so unit tests can exercise Preload/Reveal without spawning a live window or WebView2.
    internal Func<nint, bool, bool> CloakWindow { get; set; } = SetCloaked;
    internal Action? ShowWindowOverride { get; set; }
    internal Action? ActivateWindowOverride { get; set; }
    internal Func<bool>? IsWindowVisibleOverride { get; set; }
    internal TimeSpan RevealInterval { get; set; } = RevealTimeout;
    internal bool IsRevealed => _revealed;
    internal bool IsRevealTimerEnabled => _revealTimer?.IsEnabled == true;

    public ShellWindow(
        ShellViewModel viewModel, ThemeController theme, AppReadyCoordinator ready, ShutdownCoordinator shutdown,
        ILogger<ShellWindow> logger)
    {
        _viewModel = viewModel;
        _shutdown = shutdown;
        _ready = ready;
        _logger = logger;
        DataContext = viewModel;
        InitializeComponent();
        theme.Attach(this);

        // The page says it is ready on a pool thread.
        _ready.Ready += OnReady;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;

        Closing += OnClosing;
        Closed += (_, _) =>
        {
            _closed = true;
            _revealed = true;
            _ready.Ready -= OnReady;
            viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _revealTimer?.Stop();
        };
    }

    /// <summary>Shows the window where nobody can see it, so its page loads before anyone asks for it.</summary>
    public void Preload()
    {
        if (_closed || IsWindowVisible())
            return;

        var handle = new WindowInteropHelper(this).EnsureHandle();
        _cloaked = CloakWindow(handle, true);
        if (!_cloaked)
        {
            _logger.LogWarning("Could not cloak the window while its page loads; it shows while loading");
        }
        else
        {
            ShowInTaskbar = false;
            ShowActivated = false;
        }

        ShowWindow();
        StartRevealTimer();

        // The page may have been ready before the subscription.
        if (_ready.IsReady)
            Reveal();
    }

    /// <summary>
    /// After <see cref="RevealTimeout"/> the window shows whatever there is (a window that never reveals is worse than a
    /// blank one), and a page that never loaded at all is reported as a failure so the native failure panel replaces the
    /// blank. A page that loaded but is late to say <c>app.ready</c> is not a failure. Also runs when the window is not
    /// cloaked, where there is nothing to reveal but the failure is just as real.
    /// </summary>
    private void StartRevealTimer()
    {
        var timer = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher) { Interval = RevealInterval };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            _logger.LogWarning("The page did not say it was ready within {Seconds}s; showing the window anyway", RevealTimeout.TotalSeconds);
            Reveal();
            _viewModel.OnRevealTimeout();
        };
        _revealTimer = timer;
        timer.Start();
    }

    /// <summary>Uncovers the window and gives it the focus. Once; later calls do nothing.</summary>
    public void Reveal()
    {
        if (_revealed || _closed)
            return;

        _revealed = true;
        _revealTimer?.Stop();
        if (_cloaked || !IsWindowVisible())
        {
            ShowInTaskbar = true;
            ShowActivated = true;
            ShowWindow();
            if (_cloaked)
            {
                CloakWindow(new WindowInteropHelper(this).Handle, false);
                _cloaked = false;
            }

            ActivateWindow();
        }

        _viewModel.NotifyRevealed();
    }

    private bool IsWindowVisible() => IsWindowVisibleOverride?.Invoke() ?? IsVisible;

    private void ShowWindow()
    {
        if (ShowWindowOverride is { } show)
            show();
        else
            Show();
    }

    private void ActivateWindow()
    {
        if (ActivateWindowOverride is { } activate)
            activate();
        else
            Activate();
    }

    private void OnReady() => Dispatcher.BeginInvoke(Reveal);

    /// <summary>
    /// Closing the window ends the process, and a run executes on background threads that the process would cut off
    /// mid-write. With a run going the close is put on hold: the user is asked, the run is cancelled and waited for, and then
    /// the window closes for real. With no run it closes at once, and from then on no run can start.
    /// </summary>
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_shutdown.TryApproveWithoutAsking())
            return;

        e.Cancel = true;
        _ = CloseWhenReadyAsync();
    }

    private async Task CloseWhenReadyAsync()
    {
        try
        {
            if (await _shutdown.PrepareToCloseAsync())
                Close();
        }
        catch (Exception ex)
        {
            // Nothing awaits this task: a fault here would only surface as an unobserved exception.
            _logger.LogError("Could not close the window after the run stopped ({ExceptionType})", ex.GetType().Name);

            // The window is staying open, so the close that was approved is off: runs must start, and the next close asks again.
            _shutdown.AbandonClose();
        }
    }

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
