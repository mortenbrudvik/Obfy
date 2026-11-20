using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Obfy.UI.Views;

namespace Obfy.UI.Services;

/// <summary>
/// Hosted service that shows the hybrid shell window on the WPF UI thread. The old MainWindow and its view models stay
/// registered (AppModule) until the hybrid UI replaces them, but nothing starts them.
/// </summary>
public sealed class ApplicationHostService : IHostedService
{
    private readonly IServiceProvider _serviceProvider;

    public ApplicationHostService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (Application.Current.Windows.OfType<ShellWindow>().Any())
            return Task.CompletedTask;

        // ThemeController.Start runs when ShellViewModel is built, so the window's first paint is in the right theme.
        var shell = _serviceProvider.GetRequiredService<ShellWindow>();

        // The window is shown cloaked until the page says app.ready (see ShellWindow.Preload).
        shell.Preload();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
