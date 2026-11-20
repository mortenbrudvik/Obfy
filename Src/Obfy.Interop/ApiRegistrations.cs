using Obfy.App.Features.App;
using Obfy.App.Features.Files;
using Obfy.App.Features.Log;
using Obfy.App.Features.Output;
using Obfy.App.Features.Preview;
using Obfy.App.Features.Results;
using Obfy.App.Features.Run;
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

        // files.dropPaths is not here: the window's native drop sends DropPathsRequest, the page never calls it.
        registry.Register<GetFilesRequest>(IpcNames.Services.Files, IpcNames.Methods.FilesGet);
        registry.Register<PickFilesRequest>(IpcNames.Services.Files, IpcNames.Methods.FilesPick);
        registry.Register<RemoveFileRequest>(IpcNames.Services.Files, IpcNames.Methods.FilesRemove);
        registry.Register<ClearFilesRequest>(IpcNames.Services.Files, IpcNames.Methods.FilesClear);
        registry.Register<SetIncludedRequest>(IpcNames.Services.Files, IpcNames.Methods.FilesSetIncluded);
        registry.Register<RevealFileRequest>(IpcNames.Services.Files, IpcNames.Methods.FilesReveal);
        registry.Register<OpenOutputRequest>(IpcNames.Services.Files, IpcNames.Methods.FilesOpenOutput);

        registry.Register<SetOutputRequest>(IpcNames.Services.Output, IpcNames.Methods.OutputSet);
        registry.Register<PickOutputFolderRequest>(IpcNames.Services.Output, IpcNames.Methods.OutputPickFolder);
        registry.Register<SetSymbolMapRequest>(IpcNames.Services.Output, IpcNames.Methods.OutputSetSymbolMap);

        registry.Register<GetLogRequest>(IpcNames.Services.Log, IpcNames.Methods.LogGet);
        registry.Register<ClearLogRequest>(IpcNames.Services.Log, IpcNames.Methods.LogClear);

        registry.Register<GetResultsSummaryRequest>(IpcNames.Services.Results, IpcNames.Methods.ResultsSummary);
        registry.Register<QuerySymbolsRequest>(IpcNames.Services.Results, IpcNames.Methods.ResultsSymbols);
        registry.Register<ExportMapRequest>(IpcNames.Services.Results, IpcNames.Methods.ResultsExportMap);
        registry.Register<ExportReportRequest>(IpcNames.Services.Results, IpcNames.Methods.ResultsExportReport);
        registry.Register<GetPreviewRequest>(IpcNames.Services.Preview, IpcNames.Methods.PreviewGet);

        registry.Register<GetRunRequest>(IpcNames.Services.Run, IpcNames.Methods.RunGet);
        registry.Register<StartRunRequest>(IpcNames.Services.Run, IpcNames.Methods.RunStart);
        registry.Register<CancelRunRequest>(IpcNames.Services.Run, IpcNames.Methods.RunCancel);
    }
}
