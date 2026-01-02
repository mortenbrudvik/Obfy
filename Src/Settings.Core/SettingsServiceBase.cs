using System.Text.Json;
using Microsoft.Extensions.Logging;
using Settings.Core.Validation;

namespace Settings.Core;

/// <summary>
/// Abstract base class for settings services providing JSON persistence and validation.
/// </summary>
/// <typeparam name="T">The settings type. Must be a class with a parameterless constructor.</typeparam>
public abstract class SettingsServiceBase<T> : ISettingsService<T> where T : class, new()
{
    private readonly string _filePath;
    private readonly ILogger? _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    private T _settings = new();

    /// <inheritdoc/>
    public T Settings => _settings;

    /// <inheritdoc/>
    public event EventHandler? SettingsChanged;

    /// <summary>
    /// Initializes a new instance of the settings service.
    /// </summary>
    /// <param name="filePath">Full path to the settings JSON file.</param>
    /// <param name="logger">Optional logger for diagnostics.</param>
    protected SettingsServiceBase(string filePath, ILogger? logger = null)
    {
#if NET6_0_OR_GREATER
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
#else
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(filePath));
#endif

        _filePath = filePath;
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };
    }

    /// <inheritdoc/>
    public void Load()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                var json = File.ReadAllText(_filePath);
                var loaded = JsonSerializer.Deserialize<T>(json, _jsonOptions);

                if (loaded != null)
                {
                    _settings = loaded;
                    OnSettingsLoaded(_settings);

                    // Validate and fix if needed
                    if (!SettingsValidator.IsValid(_settings))
                    {
                        _logger?.LogWarning("Settings validation failed, applying corrections");
                        ApplyValidationCorrections(_settings);
                        Save(); // Persist corrections
                    }

                    _logger?.LogDebug("Settings loaded from {Path}", _filePath);
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to load settings from {Path}, using defaults", _filePath);
        }

        // Create default settings
        _settings = CreateDefaultSettings();
        OnSettingsLoaded(_settings);
        Save();
        _logger?.LogDebug("Default settings created at {Path}", _filePath);
    }

    /// <inheritdoc/>
    public void Save()
    {
        try
        {
            // Validate before saving
            SettingsValidator.Validate(_settings);

            // Ensure directory exists
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(_settings, _jsonOptions);
            File.WriteAllText(_filePath, json);

            _logger?.LogDebug("Settings saved to {Path}", _filePath);
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to save settings to {Path}", _filePath);
            throw;
        }
    }

    /// <summary>
    /// Creates default settings instance. Override to customize defaults.
    /// </summary>
    /// <returns>New settings instance with default values.</returns>
    protected virtual T CreateDefaultSettings() => new();

    /// <summary>
    /// Called after settings are loaded. Override to perform post-load processing.
    /// </summary>
    /// <param name="settings">The loaded settings instance.</param>
    protected virtual void OnSettingsLoaded(T settings) { }

    /// <summary>
    /// Called when validation fails. Override to fix invalid settings values.
    /// </summary>
    /// <param name="settings">The settings instance with validation errors.</param>
    protected virtual void ApplyValidationCorrections(T settings) { }

    /// <summary>
    /// Gets the settings file path.
    /// </summary>
    protected string FilePath => _filePath;
}
