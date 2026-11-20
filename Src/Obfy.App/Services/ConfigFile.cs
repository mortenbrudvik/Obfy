using System.Text.Json;
using System.Text.Json.Serialization;
using Obfy.Common;
using Obfy.Core.Models;

namespace Obfy.App.Services;

/// <summary>
/// Reads and writes an <c>obfy.json</c> configuration the way the command line does: camelCase names, enums as names
/// (numbers are accepted on read), nulls left out on write.
/// </summary>
public static class ConfigFile
{
    /// <summary>The Win32 filter the open and save pickers use.</summary>
    public const string DialogFilter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*";

    private static readonly JsonSerializerOptions WriteOptions = new(JsonUtils.Options)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <exception cref="ObfyApiException">The file is missing, unreadable or not a configuration.</exception>
    public static async Task<ObfySettings> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path))
            throw new ObfyApiException("settings.configNotFound", $"The configuration file was not found: {path}");

        try
        {
            var json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            return JsonUtils.Deserialize<ObfySettings>(json)
                ?? throw new ObfyApiException("settings.configInvalid", $"{path} does not hold a configuration.");
        }
        catch (JsonException ex)
        {
            throw new ObfyApiException("settings.configInvalid", $"{path} is not a valid configuration: {ex.Message}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ObfyApiException("settings.configUnreadable", $"The configuration could not be read: {ex.Message}");
        }
    }

    /// <exception cref="ObfyApiException">The file cannot be written.</exception>
    public static async Task SaveAsync(ObfySettings settings, string path, CancellationToken cancellationToken = default)
    {
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            var json = JsonSerializer.Serialize(settings, WriteOptions);
            await File.WriteAllTextAsync(path, json, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ObfyApiException("settings.configNotSaved", $"The configuration could not be saved: {ex.Message}");
        }
    }
}
