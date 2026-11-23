using System.Text.Json;
using Microsoft.Extensions.Logging;
using Obfy.Core.Models;

namespace Obfy.App.Services;

/// <summary>
/// Implementation of settings service for persisting UI settings.
/// </summary>
public class SettingsService : ISettingsService
{
    public const string PreferencesPathVariable = "OBFY_UI_PREFERENCES";

    private readonly string _preferencesPath;

    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly ILogger<SettingsService> _logger;
    private readonly SemaphoreSlim _preferencesGate = new(1, 1);

    public SettingsService(ILogger<SettingsService> logger)
        : this(logger, ResolvePreferencesPath())
    {
    }

    public SettingsService(ILogger<SettingsService> logger, string preferencesPath)
    {
        _logger = logger;
        _preferencesPath = preferencesPath;
    }

    public static string ResolvePreferencesPath()
    {
        var overridePath = Environment.GetEnvironmentVariable(PreferencesPathVariable);
        if (!string.IsNullOrWhiteSpace(overridePath))
            return overridePath;

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Obfy",
            "ui-preferences.json");
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
        string? tempPath = null;
        await _preferencesGate.WaitAsync().ConfigureAwait(false);
        try
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
            tempPath = $"{_preferencesPath}.{Guid.NewGuid():N}.tmp";

            await File.WriteAllTextAsync(tempPath, json).ConfigureAwait(false);
            File.Move(tempPath, _preferencesPath, overwrite: true);
            tempPath = null;
        }
        catch (Exception ex) when (ex is PathTooLongException or IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            // Not the exception, not the path: the preferences path holds the Windows profile name, and the log keeps Debug and above.
            _logger.LogWarning("Failed to save UI preferences ({ExceptionType})", ex.GetType().Name);
        }
        finally
        {
            _preferencesGate.Release();
            if (tempPath is not null)
                TryDelete(tempPath);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            // Best-effort cleanup of a leftover temporary file.
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
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // An unreadable file costs the saved preferences, not the start (the start-up files and -o folder come after).
            _logger.LogWarning("Failed to load UI preferences ({ExceptionType})", ex.GetType().Name);
        }
    }

    private class UiPreferences
    {
        public string? LastOutputDirectory { get; set; }
        public bool GenerateSymbolMap { get; set; }
    }
}
