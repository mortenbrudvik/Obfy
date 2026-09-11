using System.Windows;
using System.Windows.Threading;
using Autofac;
using Microsoft.Extensions.Logging;
using Obfy.UI.DependencyInjection;
using Obfy.UI.ViewModels;
using Obfy.UI.Views;

namespace Obfy.UI;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private IContainer? _container;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        try
        {
            var builder = new ContainerBuilder();
            builder.RegisterModule<AppModule>();
            _container = builder.Build();

            var mainViewModel = _container.Resolve<MainViewModel>();
            await mainViewModel.InitializeAsync();

            var mainWindow = _container.Resolve<MainWindow>();
            mainWindow.DataContext = mainViewModel;
            mainWindow.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to start Obfy: {ex.Message}", "Obfy", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        TryLog(ex: e.Exception, "Unhandled UI exception");
        e.Handled = true;
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
            _container?.Resolve<ILogger<App>>().LogError(ex, message);
        }
        catch
        {
            System.Diagnostics.Debug.WriteLine($"{message}: {ex}");
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Dispose MainViewModel if it implements IDisposable
        if (_container != null)
        {
            var mainViewModel = _container.Resolve<MainViewModel>();
            (mainViewModel as IDisposable)?.Dispose();
        }

        _container?.Dispose();
        base.OnExit(e);
    }
}
