using System.Windows;
using System.Windows.Threading;
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
    private readonly Func<Dispatcher> _uiDispatcher;
    private readonly Func<bool> _shellExists;
    private readonly Action<IServiceProvider> _showShell;

    public ApplicationHostService(IServiceProvider serviceProvider)
        : this(serviceProvider, () => Application.Current.Dispatcher, ShellWindowExists, CreateAndPreloadShell)
    {
    }

    /// <param name="serviceProvider">Where the window is built from.</param>
    /// <param name="uiDispatcher">The WPF UI thread's dispatcher.</param>
    /// <param name="shellExists">Whether the window already exists. Asked on the UI thread.</param>
    /// <param name="showShell">Builds the window and starts it cloaked. Runs on the UI thread.</param>
    internal ApplicationHostService(
        IServiceProvider serviceProvider, Func<Dispatcher> uiDispatcher, Func<bool> shellExists, Action<IServiceProvider> showShell)
    {
        _serviceProvider = serviceProvider;
        _uiDispatcher = uiDispatcher;
        _shellExists = shellExists;
        _showShell = showShell;
    }

    /// <summary>
    /// The window is built and shown on the UI thread, wherever the host calls from: it used to be built on the calling
    /// thread, which was the UI thread only because the host's lifetime completes this call synchronously. On the UI thread
    /// already (the normal case) it runs at once, before this returns, as it always has.
    /// </summary>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var dispatcher = _uiDispatcher();
        return dispatcher.CheckAccess() ? Run() : dispatcher.InvokeAsync(Run).Task;

        Task Run()
        {
            if (!_shellExists())
                _showShell(_serviceProvider);
            return Task.CompletedTask;
        }
    }

    private static bool ShellWindowExists() => Application.Current.Windows.OfType<ShellWindow>().Any();

    private static void CreateAndPreloadShell(IServiceProvider services)
    {
        // ThemeController.Start runs when ShellViewModel is built, so the window's first paint is in the right theme.
        var shell = services.GetRequiredService<ShellWindow>();

        // The window is shown cloaked until the page says app.ready (see ShellWindow.Preload).
        shell.Preload();
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
