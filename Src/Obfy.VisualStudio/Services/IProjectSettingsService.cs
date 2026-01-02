using System.Threading.Tasks;
using Community.VisualStudio.Toolkit;

namespace Obfy.VisualStudio.Services;

/// <summary>
/// Service for managing per-project obfuscation settings
/// </summary>
public interface IProjectSettingsService
{
    /// <summary>
    /// Load settings for a project. Returns null if no settings exist.
    /// </summary>
    Task<ObfySettings?> LoadSettingsAsync(Project project);

    /// <summary>
    /// Load settings from a specific directory
    /// </summary>
    Task<ObfySettings?> LoadSettingsFromDirectoryAsync(string projectDirectory);

    /// <summary>
    /// Save settings for a project
    /// </summary>
    Task SaveSettingsAsync(Project project, ObfySettings settings);

    /// <summary>
    /// Save settings to a specific directory
    /// </summary>
    Task SaveSettingsToDirectoryAsync(string projectDirectory, ObfySettings settings);

    /// <summary>
    /// Check if a project has obfuscation settings
    /// </summary>
    Task<bool> HasSettingsAsync(Project project);

    /// <summary>
    /// Get the settings file path for a project
    /// </summary>
    string GetSettingsFilePath(string projectDirectory);

    /// <summary>
    /// Check if post-build obfuscation is enabled for a project
    /// </summary>
    Task<bool> IsPostBuildEnabledAsync(Project project);

    /// <summary>
    /// Enable or disable post-build obfuscation for a project
    /// </summary>
    Task SetPostBuildEnabledAsync(Project project, bool enabled);
}
