namespace Obfy.Interop;

/// <summary>The service, method and event names shared by the bridge registrations and the page. Filled in per endpoint.</summary>
public static class IpcNames
{
    public static class Services
    {
        public const string App = "app";
        public const string Settings = "settings";
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
    }

    public static class Events
    {
        /// <summary>Pushed to the page once the shell has shown the window after <c>app.ready</c>.</summary>
        public const string AppRevealed = "app.revealed";

        /// <summary>Pushed with the new settings snapshot after any change to the protection settings.</summary>
        public const string SettingsChanged = "settings.changed";
    }
}
