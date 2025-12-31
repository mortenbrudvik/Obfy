namespace Settings.Core;

/// <summary>
/// Generic interface for application settings management.
/// </summary>
/// <typeparam name="T">The settings type. Must be a class with a parameterless constructor.</typeparam>
public interface ISettingsService<T> where T : class, new()
{
    /// <summary>
    /// Gets the current settings instance.
    /// </summary>
    T Settings { get; }

    /// <summary>
    /// Loads settings from persistent storage.
    /// Creates default settings if file doesn't exist or is invalid.
    /// </summary>
    void Load();

    /// <summary>
    /// Saves current settings to persistent storage.
    /// Validates settings before saving.
    /// </summary>
    void Save();

    /// <summary>
    /// Raised after settings are successfully saved.
    /// </summary>
    event EventHandler? SettingsChanged;
}
