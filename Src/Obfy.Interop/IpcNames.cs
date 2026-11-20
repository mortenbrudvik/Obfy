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
    }

    public static class Methods
    {
        public const string AppInfo = "info";
        public const string AppReady = "ready";
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
        public const string LogGet = "get";
        public const string LogClear = "clear";
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
    }
}
