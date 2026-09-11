using System;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Obfy.VisualStudio.Services;

/// <summary>
/// Adapter that saves Core nested obfy.json and loads both nested (Core) and flat (legacy VS) files.
/// </summary>
internal static class ObfySettingsJson
{
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

    public static string Serialize(ObfySettings settings)
    {
        var node = new JsonObject
        {
            ["level"] = settings.Level.ToString().ToLowerInvariant(),
            ["postBuildEnabled"] = settings.PostBuildEnabled,
            ["stringEncryption"] = new JsonObject { ["enabled"] = settings.StringEncryption },
            ["controlFlow"] = new JsonObject { ["enabled"] = settings.ControlFlow },
            ["symbolRenaming"] = new JsonObject
            {
                ["enabled"] = settings.SymbolRenaming,
                ["preservePublicApi"] = false
            },
            ["protection"] = new JsonObject
            {
                ["antiDebug"] = settings.AntiDebug,
                ["antiDump"] = settings.AntiDump,
                ["referenceProxy"] = settings.ReferenceProxy,
                ["antiTamper"] = new JsonObject { ["enabled"] = settings.AntiTamper },
                ["antiDecompiler"] = new JsonObject { ["enabled"] = settings.AntiDecompiler }
            },
            ["constantEncryption"] = new JsonObject { ["enabled"] = settings.ConstantEncryption },
            ["resourceEncryption"] = new JsonObject { ["enabled"] = settings.ResourceEncryption }
        };

        return node.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
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
