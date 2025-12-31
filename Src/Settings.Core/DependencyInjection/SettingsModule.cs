using Autofac;

namespace Settings.Core.DependencyInjection;

/// <summary>
/// Autofac module for registering settings services.
/// </summary>
/// <typeparam name="TSettings">The settings type.</typeparam>
/// <typeparam name="TService">The concrete settings service type.</typeparam>
public class SettingsModule<TSettings, TService> : Module
    where TSettings : class, new()
    where TService : class, ISettingsService<TSettings>
{
    /// <inheritdoc/>
    protected override void Load(ContainerBuilder builder)
    {
        builder.RegisterType<TService>()
            .As<ISettingsService<TSettings>>()
            .SingleInstance();
    }
}
