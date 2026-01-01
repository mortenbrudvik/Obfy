using System.Windows;
using Autofac;
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

        // Build the Autofac container
        var builder = new ContainerBuilder();
        builder.RegisterModule<AppModule>();
        _container = builder.Build();

        // Initialize MainViewModel
        var mainViewModel = _container.Resolve<MainViewModel>();
        await mainViewModel.InitializeAsync();

        // Resolve and show the main window
        var mainWindow = _container.Resolve<MainWindow>();
        mainWindow.DataContext = mainViewModel;
        mainWindow.Show();
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
