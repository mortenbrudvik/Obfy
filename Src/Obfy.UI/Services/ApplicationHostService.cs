using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Obfy.UI.ViewModels;
using Obfy.UI.Views;
using Wpf.Ui.Appearance;

namespace Obfy.UI.Services;

/// <summary>
/// Hosted service that initializes the main window on the WPF UI thread.
/// </summary>
public sealed class ApplicationHostService : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly FilesViewModel _files;

    public ApplicationHostService(IServiceProvider serviceProvider, FilesViewModel files)
    {
        _serviceProvider = serviceProvider;
        _files = files;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (Application.Current.Windows.OfType<MainWindow>().Any())
        {
            return;
        }

        ApplicationThemeManager.ApplySystemTheme();

        var mainViewModel = _serviceProvider.GetRequiredService<MainViewModel>();
        await mainViewModel.InitializeAsync();

        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        mainWindow.DataContext = mainViewModel;
        mainWindow.Show();
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _files.PersistPreferencesAsync();
    }
}
