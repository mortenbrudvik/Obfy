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
        ApplyCommandLineInputs();

        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        mainWindow.DataContext = mainViewModel;
        mainWindow.Show();
    }

    /// <summary>
    /// Opens assemblies/source files passed on the command line, e.g.
    /// <c>ObfyUI.exe MyApp.dll -o output</c>. Unknown flags are ignored.
    /// </summary>
    private void ApplyCommandLineInputs()
    {
        var parsed = StartupCommandLine.Parse(Environment.GetCommandLineArgs().Skip(1).ToArray());

        if (parsed.Files.Count > 0)
            _files.HandleFileDrop(parsed.Files.ToArray());

        if (!string.IsNullOrWhiteSpace(parsed.OutputDirectory))
            _files.OutputDirectory = parsed.OutputDirectory;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _files.PersistPreferencesAsync();
    }
}
