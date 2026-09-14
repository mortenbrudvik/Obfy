using System.Text.Json;
using Obfy.Core.Models;
using Shouldly;

namespace Obfy.Tests;

public class ObfySchemaContractTests
{
    [Fact]
    public void Schema_CoversEverySerializedObfySettingsProperty()
    {
        var schemaPath = Path.Combine(FindRepoRoot(), "schemas", "obfy.schema.json");
        File.Exists(schemaPath).ShouldBeTrue(schemaPath);

        using var schemaDoc = JsonDocument.Parse(File.ReadAllText(schemaPath));
        var schemaProperties = schemaDoc.RootElement.GetProperty("properties");

        var json = JsonSerializer.Serialize(new ObfySettings(), new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
        });
        using var instanceDoc = JsonDocument.Parse(json);

        AssertInstanceCoveredBySchema(schemaProperties, instanceDoc.RootElement, "ObfySettings");
    }

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
