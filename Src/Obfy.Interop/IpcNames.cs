namespace Obfy.Interop;

/// <summary>The service, method and event names shared by the bridge registrations and the page. Filled in per endpoint.</summary>
public static class IpcNames
{
    public static class Services
    {
        public const string App = "app";
    }

    public static class Methods
    {
        public const string AppInfo = "info";
        public const string AppReady = "ready";
    }

    public static class Events
    {
        /// <summary>Pushed to the page once the shell has shown the window after <c>app.ready</c>.</summary>
        public const string AppRevealed = "app.revealed";
    }
}
