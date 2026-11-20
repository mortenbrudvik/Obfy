using Autofac;
using Autofac.Extensions.DependencyInjection;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Obfy.App.Features.App;
using Obfy.Interop;

namespace Obfy.UI.DependencyInjection;

/// <summary>
/// The registrations App.xaml.cs and the wiring tests share, so a test builds the same container the app does.
/// </summary>
public static class HybridBootstrap
{
    /// <summary>
    /// Adds Mediator's generated registrations. They go into the host's service collection (the Autofac service provider
    /// factory populates the container from it), so handlers are built by Autofac with everything the modules register.
    /// </summary>
    public static void AddMediatorServices(IServiceCollection services) =>
        services.AddMediator((MediatorOptions options) =>
        {
            // Per call: handlers are stateless. Mediator's own default is singleton.
            options.ServiceLifetime = ServiceLifetime.Transient;
            // Every assembly that holds requests or handlers: Obfy.App and Obfy.Interop. One missing here is not an error
            // at build time; its handlers are simply never found. Must be compile-time constants.
            options.Assemblies = [typeof(AppInfoRequest), typeof(ApiBridge)];
        });

    /// <summary>The container modules, in the order they must load (logging first, inside AppModule).</summary>
    public static void RegisterModules(ContainerBuilder builder)
    {
        builder.RegisterModule<AppModule>();
        builder.RegisterModule<HybridModule>();
    }

    /// <summary>The whole container without a host: what the wiring tests build.</summary>
    public static IContainer BuildContainer()
    {
        var services = new ServiceCollection();
        AddMediatorServices(services);

        var builder = new ContainerBuilder();
        builder.Populate(services);
        RegisterModules(builder);
        return builder.Build();
    }
}
