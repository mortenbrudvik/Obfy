using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Obfy.Core.Models;

namespace Obfy.UI.Services;

/// <summary>
/// Implementation of settings service for persisting UI settings.
/// </summary>
public class SettingsService : ISettingsService
{
    private readonly string _preferencesPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Obfy",
        "ui-preferences.json");

    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly ILogger<SettingsService> _logger;
    private readonly object _preferencesLock = new();

    public SettingsService(ILogger<SettingsService> logger)
    {
        _logger = logger;
    }

    public string? LastOutputDirectory { get; set; }
    public bool GenerateSymbolMap { get; set; }

    public async Task SaveSettingsAsync(ObfySettings settings, string filePath)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(settings, _jsonOptions);
        await File.WriteAllTextAsync(filePath, json);
    }

    public async Task<ObfySettings?> LoadSettingsAsync(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return null;
        }

        var json = await File.ReadAllTextAsync(filePath);
        return JsonSerializer.Deserialize<ObfySettings>(json, _jsonOptions);
    }

    public async Task SavePreferencesAsync()
    {
        var directory = Path.GetDirectoryName(_preferencesPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var preferences = new UiPreferences
        {
            LastOutputDirectory = LastOutputDirectory,
            GenerateSymbolMap = GenerateSymbolMap
        };

        var json = JsonSerializer.Serialize(preferences, _jsonOptions);
        var tempPath = _preferencesPath + ".tmp";

        try
        {
            await File.WriteAllTextAsync(tempPath, json);
            lock (_preferencesLock)
            {
                File.Move(tempPath, _preferencesPath, overwrite: true);
            }
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Failed to save UI preferences to {Path}", _preferencesPath);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Failed to save UI preferences to {Path}", _preferencesPath);
        }
    }

    public async Task LoadPreferencesAsync()
    {
        if (!File.Exists(_preferencesPath))
        {
            return;
        }

        try
        {
            var json = await File.ReadAllTextAsync(_preferencesPath);
            var preferences = JsonSerializer.Deserialize<UiPreferences>(json, _jsonOptions);
            if (preferences != null)
            {
                LastOutputDirectory = preferences.LastOutputDirectory;
                GenerateSymbolMap = preferences.GenerateSymbolMap;
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to load UI preferences from {Path}", _preferencesPath);
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Failed to load UI preferences from {Path}", _preferencesPath);
        }
    }

    private class UiPreferences
    {
        public string? LastOutputDirectory { get; set; }
        public bool GenerateSymbolMap { get; set; }
    }
}
