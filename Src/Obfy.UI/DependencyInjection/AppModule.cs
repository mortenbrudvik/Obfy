using Autofac;
using Logging.Core.DependencyInjection;
using Obfy.Core.DependencyInjection;
using Obfy.UI.Services;
using Obfy.UI.ViewModels;
using Obfy.UI.Views;

namespace Obfy.UI.DependencyInjection;

/// <summary>
/// Autofac module for registering UI services, ViewModels, and Views.
/// </summary>
public class AppModule : Module
{
    /// <inheritdoc/>
    protected override void Load(ContainerBuilder builder)
    {
        // Register logging first (required by other services)
        builder.RegisterModule(new LoggingModule("Obfy.UI", enableConsoleOutput: false));

        // Register Obfy.Core services
        builder.RegisterModule<ObfuscationModule>();

        // Register UI Services
        builder.RegisterType<FileDialogService>()
            .As<IFileDialogService>()
            .SingleInstance();

        builder.RegisterType<SettingsService>()
            .As<ISettingsService>()
            .SingleInstance();

        builder.RegisterType<WpfUiDispatcher>()
            .As<IUiDispatcher>()
            .SingleInstance();

        builder.RegisterType<WpfClipboardService>()
            .As<IClipboardService>()
            .SingleInstance();

        builder.RegisterType<WpfUserNotificationService>()
            .As<IUserNotificationService>()
            .SingleInstance();

        // Register child ViewModels
        builder.RegisterType<SettingsViewModel>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<FilesViewModel>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<OutputViewModel>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<ResultsViewModel>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<HelpViewModel>()
            .AsSelf()
            .SingleInstance();

        // Register main ViewModel
        builder.RegisterType<MainViewModel>()
            .AsSelf()
            .SingleInstance();

        // Register Views
        builder.RegisterType<MainWindow>()
            .AsSelf()
            .SingleInstance();
    }
}
