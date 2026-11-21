using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Obfy.Core.Models;

namespace Obfy.Console;

/// <summary>
/// Finds keys in an <c>obfy.json</c> that no setting reads. The deserializer ignores unknown keys (and the schema
/// allows them, so editors do not flag them either), so a misspelled technique would otherwise run with its
/// defaults and the run would still report success.
/// </summary>
internal static class ConfigKeyValidator
{
    /// <summary>
    /// The unknown keys as dotted paths in the spelling used in the file (<c>controlFlow.intensty</c>). Names match
    /// case-insensitively, as they do when the file is read. Keys that start with <c>$</c> (<c>$schema</c>,
    /// <c>$comment</c>) are metadata and never reported. Text that is not a JSON object has no keys to report.
    /// </summary>
    public static IReadOnlyList<string> FindUnknownKeys(string json)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            return [];
        }

        var unknown = new List<string>();
        if (root is JsonObject obj)
            Walk(obj, typeof(ObfySettings), prefix: "", unknown);
        return unknown;
    }

    private static void Walk(JsonObject obj, Type type, string prefix, List<string> unknown)
    {
        var settable = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(static p => p.SetMethod is { IsPublic: true }
                && p.GetIndexParameters().Length == 0
                && p.GetCustomAttribute<JsonIgnoreAttribute>() is null)
            .ToList();

        foreach (var (key, value) in obj)
        {
            if (key.StartsWith('$'))
                continue;

            var property = settable.FirstOrDefault(p => string.Equals(
                p.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? p.Name, key, StringComparison.OrdinalIgnoreCase));
            var path = prefix.Length == 0 ? key : $"{prefix}.{key}";
            if (property is null)
            {
                unknown.Add(path);
                continue;
            }

            if (value is JsonObject child && IsSettingsObject(property.PropertyType))
                Walk(child, property.PropertyType, path, unknown);
        }
    }

    // A nested settings class, not a string, collection or dictionary (those take any content).
    private static bool IsSettingsObject(Type type) =>
        type.IsClass
        && type != typeof(string)
        && !typeof(System.Collections.IEnumerable).IsAssignableFrom(type);
}
