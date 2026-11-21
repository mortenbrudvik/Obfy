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
            settings.MethodEncryption = ReadBool(protection, "methodEncryption", settings.MethodEncryption);
            settings.ProxyExternalCalls = ReadBool(protection, "proxyExternalCalls", settings.ProxyExternalCalls);
        }
        else
        {
            settings.AntiDebug = ReadBool(root, "antiDebug", settings.AntiDebug);
            settings.AntiDump = ReadBool(root, "antiDump", settings.AntiDump);
            settings.ReferenceProxy = ReadBool(root, "referenceProxy", settings.ReferenceProxy);
            settings.AntiTamper = ReadEnabled(root, "antiTamper", settings.AntiTamper);
            settings.AntiDecompiler = ReadEnabled(root, "antiDecompiler", settings.AntiDecompiler);
            settings.MethodEncryption = ReadBool(root, "methodEncryption", settings.MethodEncryption);
            settings.ProxyExternalCalls = ReadBool(root, "proxyExternalCalls", settings.ProxyExternalCalls);
        }

        return settings;
    }

    /// <summary>
    /// Writes VS-owned keys. When <paramref name="existingJson"/> is a JSON object, unknown keys
    /// (runtimeProfile, virtualization, exclusions, preservePublicApi, …) are kept.
    /// </summary>
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
        protection["methodEncryption"] = settings.MethodEncryption;
        protection["proxyExternalCalls"] = settings.ProxyExternalCalls;
        node["protection"] = protection;

        WritePresetValues(node, settings.Level);

        return node.ToJsonString(WriteOptions);
    }

    /// <summary>
    /// The CLI reads a <c>-c</c> file as Custom, so for Minimal/Standard/Aggressive the file has to carry every
    /// value Core's preset (<c>ObfySettings.ApplyLevel</c>) sets beyond the on/off toggles written above;
    /// otherwise the same level behaves differently with and without an obfy.json.
    /// Custom keeps whatever the file already says.
    /// </summary>
    private static void WritePresetValues(JsonObject node, ObfuscationLevel level)
    {
        int intensity;
        bool removeAttributes;
        switch (level)
        {
            case ObfuscationLevel.Minimal:
                intensity = 50;
                removeAttributes = false;
                break;
            case ObfuscationLevel.Standard:
                intensity = 50;
                removeAttributes = true;
                break;
            case ObfuscationLevel.Aggressive:
                intensity = 80;
                removeAttributes = true;
                break;
            default:
                return;
        }

        var controlFlow = (JsonObject)node["controlFlow"]!;
        controlFlow["mode"] = "switch";
        controlFlow["intensity"] = intensity;

        var constants = (JsonObject)node["constantEncryption"]!;
        constants["algorithm"] = "xor";

        var metadata = node["metadata"] as JsonObject ?? new JsonObject();
        metadata["removeDebugInfo"] = true;
        metadata["removeAttributes"] = removeAttributes;
        node["metadata"] = metadata;
    }

    public static string PatchPostBuildEnabled(string existingJson, bool enabled)
    {
        var node = ParseObject(existingJson) ?? new JsonObject();
        node["postBuildEnabled"] = enabled;
        return node.ToJsonString(WriteOptions);
    }

    private static JsonObject? ParseObject(string? json)
    {
        if (json == null || string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            return JsonNode.Parse(json) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
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
            var raw = el.GetString();
            if (string.IsNullOrWhiteSpace(raw))
                return false;
            if (Enum.TryParse(raw, ignoreCase: true, out level))
                return true;
            throw new JsonException($"obfy.json: unknown level '{raw}'.");
        }

        if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var n) && Enum.IsDefined(typeof(ObfuscationLevel), n))
        {
            level = (ObfuscationLevel)n;
            return true;
        }

        throw new JsonException("obfy.json: 'level' must be a string or defined enum number.");
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

        throw new JsonException($"obfy.json: '{name}' must be a boolean or {{ \"enabled\": bool }}.");
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
            _ => throw new JsonException($"obfy.json: '{name}' must be a boolean.")
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
