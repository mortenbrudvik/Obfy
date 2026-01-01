using System.IO;
using System.Text.Json;
using Obfy.Core.Models;

namespace Obfy.UI.Services;

/// <summary>
/// Implementation of settings service for persisting UI settings.
/// </summary>
public class SettingsService : ISettingsService
{
    private static readonly string PreferencesPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Obfy",
        "ui-preferences.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public string? LastOutputDirectory { get; set; }
    public bool GenerateSymbolMap { get; set; }

    public async Task SaveSettingsAsync(ObfySettings settings, string filePath)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(settings, JsonOptions);
        await File.WriteAllTextAsync(filePath, json);
    }

    public async Task<ObfySettings?> LoadSettingsAsync(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return null;
        }

        var json = await File.ReadAllTextAsync(filePath);
        return JsonSerializer.Deserialize<ObfySettings>(json, JsonOptions);
    }

    public async Task SavePreferencesAsync()
    {
        var directory = Path.GetDirectoryName(PreferencesPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var preferences = new UiPreferences
        {
            LastOutputDirectory = LastOutputDirectory,
            GenerateSymbolMap = GenerateSymbolMap
        };

        var json = JsonSerializer.Serialize(preferences, JsonOptions);
        await File.WriteAllTextAsync(PreferencesPath, json);
    }

    public async Task LoadPreferencesAsync()
    {
        if (!File.Exists(PreferencesPath))
        {
            return;
        }

        try
        {
            var json = await File.ReadAllTextAsync(PreferencesPath);
            var preferences = JsonSerializer.Deserialize<UiPreferences>(json, JsonOptions);
            if (preferences != null)
            {
                LastOutputDirectory = preferences.LastOutputDirectory;
                GenerateSymbolMap = preferences.GenerateSymbolMap;
            }
        }
        catch
        {
            // Ignore corrupted preferences file
        }
    }

    private class UiPreferences
    {
        public string? LastOutputDirectory { get; set; }
        public bool GenerateSymbolMap { get; set; }
    }
}
