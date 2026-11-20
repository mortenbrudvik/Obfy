using Obfy.App.Features.App;
using Obfy.App.Features.Settings;

namespace Obfy.Interop;

/// <summary>Every <c>service.method</c> the page may call, and the request each one sends. Called once at startup.</summary>
public static class ApiRegistrations
{
    public static void RegisterAll(ApiRegistry registry)
    {
        registry.Register<AppInfoRequest>(IpcNames.Services.App, IpcNames.Methods.AppInfo);
        registry.Register<ReadyRequest>(IpcNames.Services.App, IpcNames.Methods.AppReady);

        registry.Register<GetSettingsRequest>(IpcNames.Services.Settings, IpcNames.Methods.SettingsGet);
        registry.Register<UpdateSettingsRequest>(IpcNames.Services.Settings, IpcNames.Methods.SettingsUpdate);
        registry.Register<ApplyLevelRequest>(IpcNames.Services.Settings, IpcNames.Methods.SettingsApplyLevel);
        registry.Register<OpenConfigRequest>(IpcNames.Services.Settings, IpcNames.Methods.SettingsOpenConfig);
        registry.Register<SaveConfigRequest>(IpcNames.Services.Settings, IpcNames.Methods.SettingsSaveConfig);
        registry.Register<PickSigningKeyRequest>(IpcNames.Services.Settings, IpcNames.Methods.SettingsPickSigningKey);
    }
}
