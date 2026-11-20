using Autofac;
using Logging.Core.DependencyInjection;
using Obfy.Core.DependencyInjection;
using Obfy.App.Services;

namespace Obfy.UI.DependencyInjection;

/// <summary>
/// Autofac module for the services both the shell and the application layer need: logging, the obfuscation core and the
/// preferences store.
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

        builder.RegisterType<SettingsService>()
            .As<ISettingsService>()
            .SingleInstance();
    }
}
