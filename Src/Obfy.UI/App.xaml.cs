using System.Windows;
using System.Windows.Threading;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
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

        try
        {
            _host = Host.CreateDefaultBuilder()
                .UseServiceProviderFactory(new AutofacServiceProviderFactory())
                .ConfigureContainer<ContainerBuilder>(builder => builder.RegisterModule<AppModule>())
                .ConfigureServices(services =>
                {
                    services.AddHostedService<ApplicationHostService>();
                    services.AddSingleton<ISnackbarService, SnackbarService>();
                    services.AddSingleton<IContentDialogService, ContentDialogService>();
                })
                .Build();

            await _host.StartAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to start Obfy: {ex.Message}", "Obfy", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
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
