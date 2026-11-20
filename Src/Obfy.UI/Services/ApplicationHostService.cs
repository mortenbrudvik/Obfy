using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Obfy.App.Sessions;
using Obfy.UI.Views;

namespace Obfy.UI.Services;

/// <summary>
/// Hosted service that creates the hybrid shell window on the WPF UI thread and starts it cloaked.
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

    /// <summary>
    /// A last write of the preferences: InputSession saves on a short debounce, and a quit inside that window would
    /// otherwise lose the latest output folder or symbol-map choice.
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        var input = _serviceProvider.GetService<InputSession>();
        if (input is not null)
            await input.PersistPreferencesAsync().ConfigureAwait(false);
    }
}
