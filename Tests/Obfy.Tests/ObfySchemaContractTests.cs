using System.Text.Json;
using System.Text.Json.Serialization;
using Obfy.Core.Models;
using Shouldly;

namespace Obfy.Tests;

public class ObfySchemaContractTests
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public void Schema_CoversEverySerializedObfySettingsProperty()
    {
        var schemaPath = Path.Combine(FindRepoRoot(), "schemas", "obfy.schema.json");
        File.Exists(schemaPath).ShouldBeTrue(schemaPath);

        using var schemaDoc = JsonDocument.Parse(File.ReadAllText(schemaPath));
        var schemaProperties = schemaDoc.RootElement.GetProperty("properties");

        var json = JsonSerializer.Serialize(new ObfySettings(), SerializerOptions);
        using var instanceDoc = JsonDocument.Parse(json);

        AssertInstanceCoveredBySchema(schemaProperties, instanceDoc.RootElement, "ObfySettings");
    }

    [Fact]
    public void Schema_DisallowsAdditionalPropertiesOnRootAndAllNestedObjects()
    {
        var schemaPath = Path.Combine(FindRepoRoot(), "schemas", "obfy.schema.json");
        using var schemaDoc = JsonDocument.Parse(File.ReadAllText(schemaPath));

        var violations = new List<string>();
        AssertNoAdditionalPropertiesAllowed(schemaDoc.RootElement, "$", violations);
        violations.ShouldBeEmpty();
    }

    [Fact]
    public void Schema_ValidatesSerializedObfySettings_AndRejectsUnknownKey()
    {
        var schemaPath = Path.Combine(FindRepoRoot(), "schemas", "obfy.schema.json");
        using var schemaDoc = JsonDocument.Parse(File.ReadAllText(schemaPath));
        var schemaRoot = schemaDoc.RootElement;

        var json = JsonSerializer.Serialize(new ObfySettings(), SerializerOptions);
        using (var instanceDoc = JsonDocument.Parse(json))
        {
            var errors = ValidateAgainstSchema(schemaRoot, instanceDoc.RootElement);
            errors.ShouldBeEmpty(string.Join("\n", errors));
        }

        using (var badRootDoc = JsonDocument.Parse("""{ "unknownRootKey": true }"""))
        {
            var errors = ValidateAgainstSchema(schemaRoot, badRootDoc.RootElement);
            errors.ShouldContain(e => e.Contains("unknownRootKey", StringComparison.Ordinal));
        }

        using (var badNestedDoc = JsonDocument.Parse("""{ "protection": { "unknownNestedKey": false } }"""))
        {
            var errors = ValidateAgainstSchema(schemaRoot, badNestedDoc.RootElement);
            errors.ShouldContain(e => e.Contains("unknownNestedKey", StringComparison.Ordinal));
        }

        using (var commentsAndLowercaseEnumsDoc = JsonDocument.Parse("""
            {
              "$comment": "top-level note",
              "level": "custom",
              "runtimeProfile": "nativeAot",
              "stringEncryption": {
                "$comment": "nested note",
                "enabled": true,
                "algorithm": "xor"
              },
              "protection": {
                "$comment": "protection note",
                "antiTamper": { "$comment": "tamper note", "enabled": false },
                "antiDecompiler": { "$comment": "decompiler note", "enabled": false }
              }
            }
            """))
        {
            var errors = ValidateAgainstSchema(schemaRoot, commentsAndLowercaseEnumsDoc.RootElement);
            errors.ShouldBeEmpty(string.Join("\n", errors));
        }
    }

    [Fact]
    public void Schema_ValidatesAllRepoConfigFiles()
    {
        var repoRoot = FindRepoRoot();
        var schemaPath = Path.Combine(repoRoot, "schemas", "obfy.schema.json");
        using var schemaDoc = JsonDocument.Parse(File.ReadAllText(schemaPath));
        var schemaRoot = schemaDoc.RootElement;

        var configs = new List<string> { Path.Combine(repoRoot, "build", "self-obfuscation.json") };
        configs.AddRange(Directory.GetFiles(Path.Combine(repoRoot, "examples"), "obfy.json", SearchOption.AllDirectories));
        configs.AddRange(Directory.GetFiles(Path.Combine(repoRoot, "Tests", "Obfy.ScenarioTests", "Fixtures"), "obfy.json", SearchOption.AllDirectories));

        configs.Count.ShouldBeGreaterThan(0);
        foreach (var configPath in configs)
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(configPath));
            var errors = ValidateAgainstSchema(schemaRoot, doc.RootElement);
            errors.ShouldBeEmpty($"{configPath}:\n{string.Join("\n", errors)}");
        }
    }

    private static void AssertNoAdditionalPropertiesAllowed(JsonElement schemaNode, string path, List<string> violations)
    {
        if (schemaNode.TryGetProperty("type", out var typeProp) &&
            typeProp.ValueKind == JsonValueKind.String &&
            typeProp.GetString() == "object")
        {
            if (!schemaNode.TryGetProperty("additionalProperties", out var addProps) ||
                addProps.ValueKind != JsonValueKind.False)
            {
                violations.Add($"{path} does not set additionalProperties: false");
            }
        }

        if (schemaNode.TryGetProperty("properties", out var props) && props.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in props.EnumerateObject())
                AssertNoAdditionalPropertiesAllowed(prop.Value, $"{path}.{prop.Name}", violations);
        }
    }

    private static List<string> ValidateAgainstSchema(JsonElement schemaRoot, JsonElement instance)
    {
        var errors = new List<string>();
        ValidateNode(schemaRoot, schemaRoot, instance, "$", errors);
        return errors;
    }

    private static void ValidateNode(
        JsonElement schemaRoot,
        JsonElement schema,
        JsonElement instance,
        string path,
        List<string> errors)
    {
        if (schema.TryGetProperty("$ref", out var refProp) &&
            refProp.GetString() is { } refStr &&
            refStr.StartsWith("#/$defs/", StringComparison.Ordinal))
        {
            var defName = refStr["#/$defs/".Length..];
            schema = schemaRoot.GetProperty("$defs").GetProperty(defName);
        }

        if (schema.TryGetProperty("type", out var typeProp) && !MatchesType(typeProp, instance))
        {
            errors.Add($"{path}: expected type {typeProp} but got {instance.ValueKind}");
            return;
        }

        if (schema.TryGetProperty("enum", out var enumProp))
        {
            var val = instance.ValueKind == JsonValueKind.String ? instance.GetString() : null;
            if (val is null || !enumProp.EnumerateArray().Any(e => string.Equals(e.GetString(), val, StringComparison.Ordinal)))
            {
                errors.Add($"{path}: value '{instance}' is not in enum");
            }
        }

        if (instance.ValueKind == JsonValueKind.Number && instance.TryGetDouble(out var num))
        {
            if (schema.TryGetProperty("minimum", out var minProp) && num < minProp.GetDouble())
                errors.Add($"{path}: {num} < minimum {minProp.GetDouble()}");
            if (schema.TryGetProperty("maximum", out var maxProp) && num > maxProp.GetDouble())
                errors.Add($"{path}: {num} > maximum {maxProp.GetDouble()}");
        }

        if (instance.ValueKind == JsonValueKind.Array && schema.TryGetProperty("items", out var itemSchema))
        {
            var index = 0;
            foreach (var item in instance.EnumerateArray())
            {
                ValidateNode(schemaRoot, itemSchema, item, $"{path}[{index}]", errors);
                index++;
            }
        }

        if (instance.ValueKind == JsonValueKind.Object)
        {
            var hasProperties = schema.TryGetProperty("properties", out var propsSchema);
            var disallowAdditional = schema.TryGetProperty("additionalProperties", out var addProps)
                && addProps.ValueKind == JsonValueKind.False;

            foreach (var prop in instance.EnumerateObject())
            {
                if (hasProperties && propsSchema.TryGetProperty(prop.Name, out var propSchema))
                {
                    ValidateNode(schemaRoot, propSchema, prop.Value, $"{path}.{prop.Name}", errors);
                }
                else if (disallowAdditional)
                {
                    errors.Add($"{path}.{prop.Name}: unknown property is not allowed");
                }
            }
        }
    }

    private static bool MatchesType(JsonElement typeProp, JsonElement instance)
    {
        if (typeProp.ValueKind == JsonValueKind.String)
            return MatchesSingleType(typeProp.GetString(), instance);

        if (typeProp.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in typeProp.EnumerateArray())
            {
                if (MatchesSingleType(item.GetString(), instance))
                    return true;
            }
        }

        return false;
    }

    private static bool MatchesSingleType(string? expected, JsonElement instance) => expected switch
    {
        "object" => instance.ValueKind == JsonValueKind.Object,
        "array" => instance.ValueKind == JsonValueKind.Array,
        "string" => instance.ValueKind == JsonValueKind.String,
        "boolean" => instance.ValueKind is JsonValueKind.True or JsonValueKind.False,
        "integer" => instance.ValueKind == JsonValueKind.Number && instance.TryGetInt64(out _),
        "number" => instance.ValueKind == JsonValueKind.Number,
        "null" => instance.ValueKind == JsonValueKind.Null,
        _ => true
    };

    private static void AssertInstanceCoveredBySchema(JsonElement schemaProperties, JsonElement instance, string path)
    {
        foreach (var property in instance.EnumerateObject())
        {
            if (property.Name == "$schema")
                continue;

            schemaProperties.TryGetProperty(property.Name, out var schemaProperty)
                .ShouldBeTrue($"{path}.{property.Name} is serialized on ObfySettings but missing from obfy.schema.json");

            if (property.Value.ValueKind != JsonValueKind.Object)
                continue;
            if (!schemaProperty.TryGetProperty("properties", out var nestedSchema))
                continue;

            AssertInstanceCoveredBySchema(nestedSchema, property.Value, path + "." + property.Name);
        }
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Obfy.sln")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new InvalidOperationException($"Could not locate Obfy.sln from {AppContext.BaseDirectory}");
    }
}
