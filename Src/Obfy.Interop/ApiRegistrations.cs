using Obfy.App.Features.App;

namespace Obfy.Interop;

/// <summary>Every <c>service.method</c> the page may call, and the request each one sends. Called once at startup.</summary>
public static class ApiRegistrations
{
    public static void RegisterAll(ApiRegistry registry)
    {
        registry.Register<AppInfoRequest>(IpcNames.Services.App, IpcNames.Methods.AppInfo);
        registry.Register<ReadyRequest>(IpcNames.Services.App, IpcNames.Methods.AppReady);
    }
}
