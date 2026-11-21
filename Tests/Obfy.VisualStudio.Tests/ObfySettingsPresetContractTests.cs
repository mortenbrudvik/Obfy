using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Obfy.VisualStudio.Services;
using Shouldly;
using CoreLevel = Obfy.Core.Models.ObfuscationLevel;
using CoreSettings = Obfy.Core.Models.ObfySettings;

namespace Obfy.VisualStudio.Tests;

/// <summary>
/// The CLI treats a <c>-c</c> file as Custom and uses it as written, while <c>-l</c> applies the Core preset.
/// A file the extension writes for Minimal/Standard/Aggressive must therefore carry every value the Core
/// preset sets, or the same level behaves differently depending on whether obfy.json exists.
/// </summary>
public class ObfySettingsPresetContractTests
{
    private static readonly JsonSerializerOptions CliReadOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private static readonly JsonSerializerOptions CompareOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    // Everything ApplyLevel() assigns. Packing, virtualization and incremental are opt-in features the
    // extension never writes, so they are compared only through the value the CLI reads for an absent key.
    private static readonly string[] PresetSections =
    [
        "stringEncryption", "controlFlow", "symbolRenaming", "protection", "metadata",
        "resourceEncryption", "constantEncryption", "virtualization", "packing", "incremental"
    ];

    [Theory]
    [InlineData(ObfuscationLevel.Minimal, CoreLevel.Minimal)]
    [InlineData(ObfuscationLevel.Standard, CoreLevel.Standard)]
    [InlineData(ObfuscationLevel.Aggressive, CoreLevel.Aggressive)]
    public void FileWrittenForAPreset_BehavesLikeTheLevelFlag(ObfuscationLevel level, CoreLevel coreLevel)
    {
        var json = ObfySettingsJson.Serialize(ObfySettings.ForLevel(level));

        var fromFile = JsonSerializer.Deserialize<CoreSettings>(json, CliReadOptions)!;
        fromFile.Level = CoreLevel.Custom; // what Program.cs does with a config file
        var fromFlag = CoreSettings.ForLevel(coreLevel);

        var file = ToNode(fromFile);
        var flag = ToNode(fromFlag);
        foreach (var section in PresetSections)
        {
            file[section]!.ToJsonString().ShouldBe(flag[section]!.ToJsonString(), $"{level}: {section}");
        }
    }

    [Fact]
    public void FileWrittenForAggressive_CarriesTheAggressiveControlFlowIntensity()
    {
        var json = ObfySettingsJson.Serialize(ObfySettings.ForLevel(ObfuscationLevel.Aggressive));

        var node = JsonNode.Parse(json)!;
        node["controlFlow"]!["intensity"]!.GetValue<int>().ShouldBe(80);
    }

    [Fact]
    public void FileWrittenForMinimal_KeepsAttributes()
    {
        var json = ObfySettingsJson.Serialize(ObfySettings.ForLevel(ObfuscationLevel.Minimal));

        var node = JsonNode.Parse(json)!;
        node["metadata"]!["removeAttributes"]!.GetValue<bool>().ShouldBeFalse();
    }

    [Fact]
    public void CustomLevel_DoesNotOverwriteTheUsersTunedValues()
    {
        const string existing = """
            {
              "level": "custom",
              "controlFlow": { "enabled": true, "intensity": 35 },
              "metadata": { "removeAttributes": false, "removeDebugInfo": true }
            }
            """;
        var settings = ObfySettings.ForLevel(ObfuscationLevel.Custom);
        settings.ControlFlow = true;

        var node = JsonNode.Parse(ObfySettingsJson.Serialize(settings, existing))!;

        node["controlFlow"]!["intensity"]!.GetValue<int>().ShouldBe(35);
        node["metadata"]!["removeAttributes"]!.GetValue<bool>().ShouldBeFalse();
    }

    private static JsonObject ToNode(CoreSettings settings) =>
        JsonSerializer.SerializeToNode(settings, CompareOptions)!.AsObject();
}
