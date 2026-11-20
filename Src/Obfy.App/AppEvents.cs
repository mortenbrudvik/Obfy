namespace Obfy.App;

/// <summary>The names of the events the application layer pushes to the page. The page uses the same strings.</summary>
public static class AppEvents
{
    /// <summary>Pushed with the new settings snapshot after any change to the protection settings.</summary>
    public const string SettingsChanged = "settings.changed";

    /// <summary>Pushed with the new input snapshot (file list, output folder, symbol map) after any change to it.</summary>
    public const string FilesChanged = "files.changed";

    /// <summary>Pushed with the lines added since the last push (a batch of about 100 ms of output).</summary>
    public const string LogAppended = "log.appended";

    /// <summary>Pushed with the empty log snapshot after the log is cleared; the page drops what it shows.</summary>
    public const string LogCleared = "log.cleared";
}
