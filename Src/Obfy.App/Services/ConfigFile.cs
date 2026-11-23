using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Obfy.Common;
using Obfy.Core.Models;
using Obfy.Core.Utilities;

namespace Obfy.App.Services;

/// <summary>The loaded configuration settings and any unknown keys that were ignored.</summary>
public sealed record ConfigLoadResult(ObfySettings Settings, IReadOnlyList<string> UnknownKeys);

/// <summary>
/// Reads and writes an <c>obfy.json</c> configuration the way the command line does: camelCase names, enums as names
/// (numbers are accepted on read), nulls left out on write.
/// </summary>
public static class ConfigFile
{
    /// <summary>The Win32 filter the open and save pickers use.</summary>
    public const string DialogFilter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*";

    private const string SchemaKey = "$schema";

    private static readonly JsonSerializerOptions WriteOptions = new(JsonUtils.Options)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>Like <see cref="WriteOptions"/> but nulls are written: lists every property the typed settings know.</summary>
    private static readonly JsonSerializerOptions KnownOptions = new(JsonUtils.Options)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    /// <summary>The top-level names an Obfy configuration may hold.</summary>
    private static readonly HashSet<string> KnownKeys = new(
        typeof(ObfySettings).GetProperties().Select(static p => p.Name).Append(SchemaKey), StringComparer.OrdinalIgnoreCase);

    /// <summary>The sections a level preset governs. A file that holds none of them has only a level to say about them.</summary>
    private static readonly HashSet<string> PresetSections = new(
    [
        nameof(ObfySettings.StringEncryption), nameof(ObfySettings.ControlFlow), nameof(ObfySettings.SymbolRenaming),
        nameof(ObfySettings.Protection), nameof(ObfySettings.Metadata), nameof(ObfySettings.ResourceEncryption),
        nameof(ObfySettings.ConstantEncryption), nameof(ObfySettings.Virtualization), nameof(ObfySettings.Packing),
        nameof(ObfySettings.Incremental)
    ], StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Reads a configuration. A JSON file that is not an Obfy configuration (an object with none of its names, or not an
    /// object) is refused, so the host never adopts, and later saves over, some other tool's file.
    /// </summary>
    /// <exception cref="ObfyApiException">The file is missing, unreadable or not a configuration.</exception>
    public static async Task<ObfySettings> LoadAsync(string path, CancellationToken cancellationToken = default) =>
        (await LoadWithUnknownKeysAsync(path, cancellationToken).ConfigureAwait(false)).Settings;

    /// <summary>
    /// Reads a configuration along with any unknown JSON keys that the deserializer ignored, so callers can warn the user.
    /// </summary>
    /// <exception cref="ObfyApiException">The file is missing, unreadable or not a configuration.</exception>
    public static async Task<ConfigLoadResult> LoadWithUnknownKeysAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path))
            throw new ObfyApiException("settings.configNotFound", $"The configuration file was not found: {path}");

        try
        {
            var json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            if (JsonNode.Parse(json) is not JsonObject root)
                throw new ObfyApiException("settings.configInvalid", $"{path} does not hold a configuration.");

            if (root.Count > 0 && !root.Any(static p => KnownKeys.Contains(p.Key)))
                throw new ObfyApiException("settings.configInvalid", $"{path} is not an Obfy configuration: it has none of the settings an Obfy configuration holds.");

            var settings = root.Deserialize<ObfySettings>(JsonUtils.Options)
                ?? throw new ObfyApiException("settings.configInvalid", $"{path} does not hold a configuration.");

            // A file that names only a level means that preset. The engine would apply it on every run, so the settings
            // read from the file hold the preset's flags: what the Protection screen shows is then what runs.
            if (settings.Level != ObfuscationLevel.Custom && !root.Any(static p => PresetSections.Contains(p.Key)))
                settings.ApplyLevel();

            var unknownKeys = ConfigKeyValidator.FindUnknownKeys(json);
            return new ConfigLoadResult(settings, unknownKeys);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        {
            // ArgumentException: JsonNode.Parse refuses an object that repeats a name.
            throw new ObfyApiException("settings.configInvalid", $"{path} is not a valid configuration: {ex.Message}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ObfyApiException("settings.configUnreadable", $"The configuration could not be read: {ex.Message}");
        }
    }

    /// <summary>Prefix shared by unknown-configuration-keys notices so callers can replace or clear them.</summary>
    public const string UnknownKeysNoticePrefix = "Unknown configuration keys ignored:";

    /// <summary>Formats ignored configuration keys as a notice message containing only key names.</summary>
    public static string FormatUnknownKeysNotice(IReadOnlyList<string> unknownKeys) =>
        $"{UnknownKeysNoticePrefix} {string.Join(", ", unknownKeys)}.";

    /// <summary>
    /// Writes the settings through a temporary file in the same folder and swaps it in, so a failed or interrupted save
    /// leaves the old file whole.
    /// </summary>
    /// <param name="preserveExisting">
    /// The file already there is the configuration these settings came from: keep what the typed settings do not hold (the
    /// <c>$schema</c> line the command line writes, a field from a newer version). False writes the settings alone, as for
    /// a file the user chose to overwrite.
    /// </param>
    /// <exception cref="ObfyApiException">The file cannot be written.</exception>
    public static async Task SaveAsync(
        ObfySettings settings, string path, CancellationToken cancellationToken = default, bool preserveExisting = false)
    {
        string? temp = null;
        try
        {
            var full = Path.GetFullPath(path);
            var directory = Path.GetDirectoryName(full)!;
            Directory.CreateDirectory(directory);

            var json = await BuildJsonAsync(settings, full, preserveExisting, cancellationToken).ConfigureAwait(false);
            temp = Path.Combine(directory, $".{Path.GetFileName(full)}.{Guid.NewGuid():N}.tmp");
            await File.WriteAllTextAsync(temp, json, cancellationToken).ConfigureAwait(false);

            if (File.Exists(full))
                File.Replace(temp, full, destinationBackupFileName: null);
            else
                File.Move(temp, full);
            temp = null;
        }
        catch (Exception ex) when (ex is PathTooLongException or IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            throw new ObfyApiException("settings.configNotSaved", $"The configuration could not be saved: {ex.Message}");
        }
        finally
        {
            if (temp is not null)
                TryDelete(temp);
        }
    }

    private static async Task<string> BuildJsonAsync(ObfySettings settings, string fullPath, bool preserveExisting, CancellationToken cancellationToken)
    {
        var fresh = JsonSerializer.SerializeToNode(settings, WriteOptions)!.AsObject();
        if (preserveExisting && File.Exists(fullPath))
        {
            try
            {
                var text = await File.ReadAllTextAsync(fullPath, cancellationToken).ConfigureAwait(false);
                if (JsonNode.Parse(text) is JsonObject existing)
                {
                    var known = JsonSerializer.SerializeToNode(settings, KnownOptions)!.AsObject();
                    Merge(existing, fresh, known);
                    fresh = existing;
                }
            }
            catch (Exception ex) when (ex is JsonException or ArgumentException)
            {
                // Not readable as JSON (or duplicate object keys): there is nothing worth keeping, and the settings are written alone.
            }
        }

        return fresh.ToJsonString(WriteOptions);
    }

    /// <summary>
    /// Puts <paramref name="fresh"/> into <paramref name="existing"/>: every name the typed settings know takes the new value
    /// (or goes, when the settings hold none); every other name stays as the file has it, inside objects too.
    /// </summary>
    private static void Merge(JsonObject existing, JsonObject fresh, JsonObject known)
    {
        foreach (var key in existing.Select(static p => p.Key).ToList())
        {
            var knownKey = known.Select(static p => p.Key).FirstOrDefault(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase));
            if (knownKey is null)
                continue;

            var old = existing[key];
            existing.Remove(key);
            if (old is JsonObject oldObject && known[knownKey] is JsonObject knownObject && fresh[knownKey] is JsonObject freshObject)
            {
                Merge(oldObject, freshObject, knownObject);
                fresh[knownKey] = oldObject;
            }
        }

        foreach (var (key, value) in fresh.ToList())
        {
            fresh.Remove(key);
            existing[key] = value;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A leftover temporary file is not worth failing over.
        }
    }
}
