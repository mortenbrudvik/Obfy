using System.Windows;
using System.Windows.Threading;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;
using Obfy.Interop;
using Obfy.UI.DependencyInjection;
using Obfy.UI.Services;
using Wpf.Ui;
using Wpf.Ui.Extensions;

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

        if (!WebView2RuntimeIsInstalled())
        {
            MessageBox.Show(
                "Obfy needs the Microsoft Edge WebView2 Runtime to draw its window, and it is not installed." + Environment.NewLine + Environment.NewLine +
                $"Download it from {WebView2DownloadUrl}, install it, and start Obfy again.",
                "Obfy", MessageBoxButton.OK, MessageBoxImage.Warning);
            Shutdown(1);
            return;
        }

        try
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
                    services.AddSingleton<ISnackbarService, SnackbarService>();
                    services.AddSingleton<IContentDialogService, ContentDialogService>();
                })
                .Build();

            // Before the host starts the shell: the page may call as soon as it loads.
            ApiRegistrations.RegisterAll(_host.Services.GetRequiredService<ApiRegistry>());

            await _host.StartAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to start Obfy: {ex.Message}", "Obfy", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
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

    private async void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        TryLog(ex: e.Exception, "Unhandled UI exception");
        e.Handled = true;

        try
        {
            var dialog = _host?.Services.GetService<IContentDialogService>();
            if (dialog is not null)
            {
                await dialog.ShowSimpleDialogAsync(new SimpleContentDialogCreateOptions
                {
                    Title = "Obfy",
                    Content = e.Exception.Message,
                    CloseButtonText = "Close"
                });
                return;
            }
        }
        catch (Exception dialogEx)
        {
            TryLog(dialogEx, "Failed to show exception dialog");
        }

        MessageBox.Show(e.Exception.Message, "Obfy", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
            TryLog(ex, "Unhandled domain exception");
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        TryLog(e.Exception, "Unobserved task exception");
        e.SetObserved();
    }

    private void TryLog(Exception ex, string message)
    {
        try
        {
            _host?.Services.GetService<ILogger<App>>()?.LogError(ex, message);
        }
        catch (Exception logEx)
        {
            System.Diagnostics.Debug.WriteLine($"{message}: {ex} (logger failed: {logEx.Message})");
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            try
            {
                await _host.StopAsync();
            }
            catch (Exception ex)
            {
                TryLog(ex, "Failed to stop host");
            }

            try
            {
                _host.Dispose();
            }
            catch (Exception ex)
            {
                TryLog(ex, "Failed to dispose host");
            }

            _host = null;
        }

        base.OnExit(e);
    }
}
