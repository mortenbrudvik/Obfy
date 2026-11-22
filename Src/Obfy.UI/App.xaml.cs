using System.Windows;
using System.Windows.Threading;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using Logging.Core.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;
using Obfy.App.Models;
using Obfy.App.Services;
using Obfy.App.Sessions;
using Obfy.Interop;
using Obfy.UI.DependencyInjection;
using Obfy.UI.Services;

namespace Obfy.UI;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private IHost? _host;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        // The runtime probe is inside the guarded start: it throws DllNotFoundException or COMException when the WebView2
        // loader is broken, and an exception that escapes this async void method is marked handled by
        // OnDispatcherUnhandledException, which would leave an invisible process with no window and no Shutdown.
        var result = await StartupState.StartAsync(
            WebView2RuntimeIsInstalled,
            () => StartHostAsync(e.Args),
            ex =>
            {
                TryLog(ex, "Obfy could not start");
                MessageBox.Show($"Failed to start Obfy: {ex.Message}", "Obfy", MessageBoxButton.OK, MessageBoxImage.Error);
            });

        if (result == StartupState.StartResult.RuntimeMissing)
        {
            MessageBox.Show(
                "Obfy needs the Microsoft Edge WebView2 Runtime to draw its window, and it is not installed." + Environment.NewLine + Environment.NewLine +
                $"Download it from {WebView2DownloadUrl}, install it, and start Obfy again.",
                "Obfy", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        if (result != StartupState.StartResult.Started)
            Shutdown(1);
    }

    private async Task StartHostAsync(string[] args)
    {
        // Mediator's generated registrations go into the host's services; the Autofac factory populates the container
        // from them, so handlers are built by Autofac with everything the modules register.
        _host = Host.CreateDefaultBuilder()
            .UseServiceProviderFactory(new AutofacServiceProviderFactory())
            .ConfigureContainer<ContainerBuilder>(HybridBootstrap.RegisterModules)
            .ConfigureServices(services =>
            {
                HybridBootstrap.AddMediatorServices(services);
                services.AddHostedService<ApplicationHostService>();
            })
            .Build();

        // Before the host starts the shell: the page may call as soon as it loads.
        ApiRegistrations.RegisterAll(_host.Services.GetRequiredService<ApiRegistry>());

        await ApplyStartupStateAsync(args);

        await _host.StartAsync();
    }

    /// <summary>
    /// The last session's preferences, then the files and <c>-o</c> folder the process was started with. Runs on the UI
    /// thread (the awaits resume here) before the shell exists: the file list changes without publishing, and a
    /// configuration among the arguments publishes <c>settings.changed</c> from this thread, so the web view is first
    /// built on the UI thread, as the shell would build it. A failure in either step costs that step's state, not the
    /// start or the other step, and says so as a notice on the file list (see <see cref="StartupState.ApplyAsync"/>).
    /// </summary>
    private async Task ApplyStartupStateAsync(string[] args)
    {
        var services = _host!.Services;

        // Built now so it follows the settings and file sessions from the start (it subscribes to their changes).
        _ = services.GetRequiredService<RunCoordinator>();
        var input = services.GetRequiredService<InputSession>();
        var intake = services.GetRequiredService<InputIntake>();

        await StartupState.ApplyAsync(
            input.InitializeAsync,
            () => intake.ApplyStartupAsync(args),
            (title, message) => input.Notify(NoticeLevel.Warning, title, message),
            services.GetRequiredService<ILogger<App>>());
    }

    /// <summary>Where a user without the WebView2 Runtime gets it.</summary>
    private const string WebView2DownloadUrl = "https://go.microsoft.com/fwlink/p/?LinkId=2124703";

    private static bool WebView2RuntimeIsInstalled()
    {
        try
        {
            return !string.IsNullOrWhiteSpace(CoreWebView2Environment.GetAvailableBrowserVersionString());
        }
        catch (WebView2RuntimeNotFoundException)
        {
            return false;
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        TryLog(ex: e.Exception, "Unhandled UI exception");
        e.Handled = true;

        MessageBox.Show(e.Exception.Message, "Obfy", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    /// <summary>How long a crash waits for the log to be written before the process goes down.</summary>
    private static readonly TimeSpan CrashFlushTimeout = TimeSpan.FromSeconds(3);

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
            TryLog(ex, "Unhandled domain exception");

        // The file target is asynchronous: without this the line that says why the process died is usually still in the queue.
        LoggingConfiguration.Flush(CrashFlushTimeout);
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        TryLog(e.Exception, "Unobserved task exception");
        e.SetObserved();
    }

    private readonly object _fallbackLogsGate = new();
    private ILoggerFactory? _fallbackLogs;

    /// <summary>
    /// Logs what went wrong without throwing, by exception type and stack, not message (see <see cref="CrashLog"/>). The host's
    /// logger is used when there is one; before the host exists (it failed to build), a logger of its own is made.
    /// </summary>
    private void TryLog(Exception ex, string message)
    {
        ILogger? logger = null;
        try
        {
            logger = _host?.Services.GetService<ILogger<App>>();
        }
        catch (Exception)
        {
            // A container that cannot give out a logger: the fallback below.
        }

        try
        {
            if (logger is null)
            {
                lock (_fallbackLogsGate)
                    _fallbackLogs ??= LoggingConfiguration.CreateLoggerFactory("Obfy.UI", enableConsoleOutput: false);
                logger = _fallbackLogs.CreateLogger<App>();
            }
            CrashLog.Error(logger, ex, message);
        }
        catch (Exception logEx)
        {
            System.Diagnostics.Debug.WriteLine($"{message}: {ex.GetType().Name} (logger failed: {logEx.GetType().Name})");
        }
    }

    /// <summary>How long quitting waits for the host to stop (the last preference write runs in StopAsync).</summary>
    private static readonly TimeSpan HostStopTimeout = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Blocks until the host has stopped. An <c>async void</c> OnExit would return to WPF at its first await and let the
    /// process end while the preferences are still being written. <c>Task.Run</c> keeps the stop off the dispatcher, so
    /// nothing in it can deadlock on the UI thread this method is blocking; the wait is bounded so a hung save cannot
    /// keep the process alive.
    /// </summary>
    protected override void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            var host = _host;
            var stopped = false;
            try
            {
                stopped = Task.Run(() => host.StopAsync()).Wait(HostStopTimeout);
                if (!stopped)
                    TryLog(new TimeoutException($"No stop within {HostStopTimeout.TotalSeconds:0} s"), "The host did not stop in time; exiting anyway");
            }
            catch (Exception ex)
            {
                TryLog(ex, "Failed to stop host");
            }

            if (stopped)
            {
                try
                {
                    host.Dispose();
                }
                catch (Exception ex)
                {
                    TryLog(ex, "Failed to dispose host");
                }
            }

            _host = null;
        }

        try
        {
            LoggingConfiguration.Flush(TimeSpan.FromSeconds(2));
        }
        catch (Exception)
        {
            // Best-effort log flush during shutdown.
        }

        base.OnExit(e);
    }
}
