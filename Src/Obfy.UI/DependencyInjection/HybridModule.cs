using Autofac;
using Obfy.App.Features.App;
using Obfy.App.Services;
using Obfy.App.Sessions;
using Obfy.Interop;
using Obfy.UI.Services;
using Obfy.UI.ViewModels;
using Obfy.UI.Views;
using Obfy.WebView;

namespace Obfy.UI.DependencyInjection;

/// <summary>Registers the hybrid shell: the bridge, the web view, the theme and the shell window.</summary>
public class HybridModule : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        builder.RegisterType<IpcLogger>().AsSelf().SingleInstance();
        builder.RegisterType<ApiRegistry>().AsSelf().SingleInstance();

        builder.RegisterType<ApiBridge>()
            .AsSelf()
            .As<IApiBridge>()
            .SingleInstance();

        builder.RegisterType<ApiEventAggregator>().AsSelf().SingleInstance();

        // One web view for the whole app: the aggregator pushes events through it and the shell window hosts it.
        builder.RegisterType<WebViewControl>()
            .AsSelf()
            .As<IWebViewInterop>()
            .SingleInstance();

        builder.RegisterType<AppReadyCoordinator>().AsSelf().SingleInstance();

        // The one set of protection settings the page edits. Handlers are transient; this is what they share.
        builder.RegisterType<ProtectionSession>().AsSelf().SingleInstance();
        builder.RegisterType<SettingsEventForwarder>().AsSelf().SingleInstance();

        builder.RegisterType<FileDialogs>()
            .As<IFileDialogs>()
            .SingleInstance();

        builder.RegisterType<ThemeController>().AsSelf().SingleInstance();
        builder.RegisterType<ShellViewModel>().AsSelf().SingleInstance();
        builder.RegisterType<ShellWindow>().AsSelf().SingleInstance();
    }
}
