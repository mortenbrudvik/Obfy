using System;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Obfy.VisualStudio.Services;

/// <summary>
/// Adapter that saves Core nested obfy.json and loads both nested (Core) and flat (legacy VS) files.
/// Serialize overlays known host fields onto an existing document so Core-only keys are kept.
/// </summary>
public static class ObfySettingsJson
{
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    public static ObfySettings Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var settings = new ObfySettings();

        if (TryReadLevel(root, out var level))
        {
            settings.Level = level;
        }

        settings.PostBuildEnabled = ReadBool(root, "postBuildEnabled", settings.PostBuildEnabled);
        settings.StringEncryption = ReadEnabled(root, "stringEncryption", settings.StringEncryption);
        settings.ControlFlow = ReadEnabled(root, "controlFlow", settings.ControlFlow);
        settings.SymbolRenaming = ReadEnabled(root, "symbolRenaming", settings.SymbolRenaming);
        settings.ConstantEncryption = ReadEnabled(root, "constantEncryption", settings.ConstantEncryption);
        settings.ResourceEncryption = ReadEnabled(root, "resourceEncryption", settings.ResourceEncryption);

        if (TryGetProperty(root, "protection", out var protection) && protection.ValueKind == JsonValueKind.Object)
        {
            settings.AntiDebug = ReadBool(protection, "antiDebug", settings.AntiDebug);
            settings.AntiDump = ReadBool(protection, "antiDump", settings.AntiDump);
            settings.ReferenceProxy = ReadBool(protection, "referenceProxy", settings.ReferenceProxy);
            settings.AntiTamper = ReadEnabled(protection, "antiTamper", settings.AntiTamper);
            settings.AntiDecompiler = ReadEnabled(protection, "antiDecompiler", settings.AntiDecompiler);
        }
        else
        {
            // Legacy flat root properties
            settings.AntiDebug = ReadBool(root, "antiDebug", settings.AntiDebug);
            settings.AntiDump = ReadBool(root, "antiDump", settings.AntiDump);
            settings.ReferenceProxy = ReadBool(root, "referenceProxy", settings.ReferenceProxy);
            settings.AntiTamper = ReadEnabled(root, "antiTamper", settings.AntiTamper);
            settings.AntiDecompiler = ReadEnabled(root, "antiDecompiler", settings.AntiDecompiler);
        }

        return settings;
    }

    public static string Serialize(ObfySettings settings, string? existingJson = null)
    {
        var node = ParseObject(existingJson) ?? new JsonObject();
        node["level"] = settings.Level.ToString().ToLowerInvariant();
        node["postBuildEnabled"] = settings.PostBuildEnabled;
        SetEnabled(node, "stringEncryption", settings.StringEncryption);
        SetEnabled(node, "controlFlow", settings.ControlFlow);
        SetEnabled(node, "symbolRenaming", settings.SymbolRenaming);
        SetEnabled(node, "constantEncryption", settings.ConstantEncryption);
        SetEnabled(node, "resourceEncryption", settings.ResourceEncryption);

        var protection = node["protection"] as JsonObject ?? new JsonObject();
        protection["antiDebug"] = settings.AntiDebug;
        protection["antiDump"] = settings.AntiDump;
        protection["referenceProxy"] = settings.ReferenceProxy;
        SetEnabled(protection, "antiTamper", settings.AntiTamper);
        SetEnabled(protection, "antiDecompiler", settings.AntiDecompiler);
        node["protection"] = protection;

        return node.ToJsonString(WriteOptions);
    }

    public static string PatchPostBuildEnabled(string existingJson, bool enabled)
    {
        var node = ParseObject(existingJson) ?? new JsonObject();
        node["postBuildEnabled"] = enabled;
        return node.ToJsonString(WriteOptions);
    }

    private static JsonObject? ParseObject(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        return JsonNode.Parse(json!) as JsonObject;
    }

    private static void SetEnabled(JsonObject parent, string name, bool enabled)
    {
        if (parent[name] is JsonObject existing)
            existing["enabled"] = enabled;
        else
            parent[name] = new JsonObject { ["enabled"] = enabled };
    }

    private static bool TryReadLevel(JsonElement root, out ObfuscationLevel level)
    {
        level = ObfuscationLevel.Standard;
        if (!TryGetProperty(root, "level", out var el))
        {
            return false;
        }

        if (el.ValueKind == JsonValueKind.String)
        {
            return Enum.TryParse(el.GetString(), ignoreCase: true, out level);
        }

        if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var n) && Enum.IsDefined(typeof(ObfuscationLevel), n))
        {
            level = (ObfuscationLevel)n;
            return true;
        }

        return false;
    }

    private static bool ReadEnabled(JsonElement parent, string name, bool defaultValue)
    {
        if (!TryGetProperty(parent, name, out var el))
        {
            return defaultValue;
        }

        if (el.ValueKind == JsonValueKind.True) return true;
        if (el.ValueKind == JsonValueKind.False) return false;

        if (el.ValueKind == JsonValueKind.Object)
        {
            return ReadBool(el, "enabled", defaultValue);
        }

        return defaultValue;
    }

    private static bool ReadBool(JsonElement parent, string name, bool defaultValue)
    {
        if (!TryGetProperty(parent, name, out var el))
        {
            return defaultValue;
        }

        return el.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => defaultValue
        };
    }

    private static bool TryGetProperty(JsonElement obj, string name, out JsonElement value)
    {
        if (obj.ValueKind != JsonValueKind.Object)
        {
            value = default;
            return false;
        }

        if (obj.TryGetProperty(name, out value))
        {
            return true;
        }

        foreach (var prop in obj.EnumerateObject())
        {
            if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = prop.Value;
                return true;
            }
        }

        value = default;
        return false;
    }
}
