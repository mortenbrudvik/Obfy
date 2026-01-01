using Obfy.Core.Models;

namespace Obfy.UI.Services;

/// <summary>
/// Service for persisting UI settings and obfuscation configurations.
/// </summary>
public interface ISettingsService
{
    /// <summary>
    /// Gets the last used output directory.
    /// </summary>
    string? LastOutputDirectory { get; set; }

    /// <summary>
    /// Gets or sets whether to generate symbol maps by default.
    /// </summary>
    bool GenerateSymbolMap { get; set; }

    /// <summary>
    /// Saves obfuscation settings to a file.
    /// </summary>
    Task SaveSettingsAsync(ObfySettings settings, string filePath);

    /// <summary>
    /// Loads obfuscation settings from a file.
    /// </summary>
    Task<ObfySettings?> LoadSettingsAsync(string filePath);

    /// <summary>
    /// Saves the current UI preferences.
    /// </summary>
    Task SavePreferencesAsync();

    /// <summary>
    /// Loads the UI preferences.
    /// </summary>
    Task LoadPreferencesAsync();
}
