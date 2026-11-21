using Obfy.App;

namespace Obfy.Interop;

/// <summary>The service, method and event names shared by the bridge registrations and the page. Filled in per endpoint.</summary>
public static class IpcNames
{
    public static class Services
    {
        public const string App = "app";
        public const string Settings = "settings";
        public const string Files = "files";
        public const string Output = "output";
        public const string Log = "log";
        public const string Results = "results";
        public const string Preview = "preview";
        public const string Run = "run";
    }

    public static class Methods
    {
        public const string AppInfo = "info";
        public const string AppReady = "ready";
        public const string AppOpenGuide = "openGuide";
        public const string SettingsGet = "get";
        public const string SettingsUpdate = "update";
        public const string SettingsApplyLevel = "applyLevel";
        public const string SettingsOpenConfig = "openConfig";
        public const string SettingsSaveConfig = "saveConfig";
        public const string SettingsPickSigningKey = "pickSigningKey";
        public const string FilesGet = "get";
        public const string FilesPick = "pick";
        public const string FilesRemove = "remove";
        public const string FilesClear = "clear";
        public const string FilesSetIncluded = "setIncluded";
        public const string FilesReveal = "reveal";
        public const string FilesOpenOutput = "openOutput";
        public const string OutputSet = "set";
        public const string OutputPickFolder = "pickFolder";
        public const string OutputSetSymbolMap = "setSymbolMap";
        public const string OutputPickSymbolMap = "pickSymbolMap";
        public const string LogGet = "get";
        public const string LogClear = "clear";
        public const string ResultsSummary = "summary";
        public const string ResultsSymbols = "symbols";
        public const string ResultsExportMap = "exportMap";
        public const string ResultsExportReport = "exportReport";
        public const string PreviewGet = "get";
        public const string RunGet = "get";
        public const string RunStart = "start";
        public const string RunCancel = "cancel";
    }

    public static class Events
    {
        /// <summary>Pushed to the page once the shell has shown the window after <c>app.ready</c>.</summary>
        public const string AppRevealed = "app.revealed";

        /// <summary>Pushed with the new settings snapshot after any change to the protection settings.</summary>
        public const string SettingsChanged = AppEvents.SettingsChanged;

        /// <summary>Pushed with the new input snapshot (file list, output folder, symbol map) after any change to it.</summary>
        public const string FilesChanged = AppEvents.FilesChanged;

        /// <summary>Pushed with the lines added to the run log since the last push.</summary>
        public const string LogAppended = AppEvents.LogAppended;

        /// <summary>Pushed with the empty log snapshot after the log is cleared.</summary>
        public const string LogCleared = AppEvents.LogCleared;

        /// <summary>Pushed with the new results snapshot after any change to the results.</summary>
        public const string ResultsChanged = AppEvents.ResultsChanged;

        /// <summary>Pushed with the new run snapshot when progress, status or what blocks the run changes.</summary>
        public const string RunChanged = AppEvents.RunChanged;

        /// <summary>Pushed with the final run snapshot when a run ends.</summary>
        public const string RunCompleted = AppEvents.RunCompleted;
    }
}
