namespace Obfy.App;

/// <summary>The names of the events the application layer pushes to the page. The page uses the same strings.</summary>
public static class AppEvents
{
    /// <summary>Pushed with the new settings snapshot after any change to the protection settings.</summary>
    public const string SettingsChanged = "settings.changed";
}
