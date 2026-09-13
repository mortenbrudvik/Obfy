using Obfy.VisualStudio.Services;
using Shouldly;

namespace Obfy.VisualStudio.Tests;

public class ObfySettingsJsonTests
{
    [Fact]
    public void Parse_NestedCoreJson_ReadsProtectionAndEnabledObjects()
    {
        const string json = """
            {
              "level": "aggressive",
              "postBuildEnabled": true,
              "stringEncryption": { "enabled": true },
              "controlFlow": { "enabled": true },
              "symbolRenaming": { "enabled": true },
              "protection": {
                "antiDebug": true,
                "antiDump": false,
                "referenceProxy": true,
                "antiTamper": { "enabled": true },
                "antiDecompiler": { "enabled": false }
              }
            }
            """;

        var settings = ObfySettingsJson.Parse(json);

        settings.Level.ShouldBe(ObfuscationLevel.Aggressive);
        settings.PostBuildEnabled.ShouldBeTrue();
        settings.StringEncryption.ShouldBeTrue();
        settings.ControlFlow.ShouldBeTrue();
        settings.AntiDebug.ShouldBeTrue();
        settings.AntiDump.ShouldBeFalse();
        settings.ReferenceProxy.ShouldBeTrue();
        settings.AntiTamper.ShouldBeTrue();
        settings.AntiDecompiler.ShouldBeFalse();
    }

    [Fact]
    public void Parse_LegacyFlatJson_ReadsRootBooleans()
    {
        const string json = """
            {
              "level": "standard",
              "antiDebug": true,
              "stringEncryption": false
            }
            """;

        var settings = ObfySettingsJson.Parse(json);

        settings.Level.ShouldBe(ObfuscationLevel.Standard);
        settings.AntiDebug.ShouldBeTrue();
        settings.StringEncryption.ShouldBeFalse();
    }

    [Fact]
    public void Serialize_RoundTripsNestedShape()
    {
        var original = ObfySettings.ForLevel(ObfuscationLevel.Minimal);
        original.PostBuildEnabled = true;

        var json = ObfySettingsJson.Serialize(original);
        json.ShouldContain("\"protection\"");
        json.ShouldContain("\"enabled\"");

        var loaded = ObfySettingsJson.Parse(json);
        loaded.Level.ShouldBe(ObfuscationLevel.Minimal);
        loaded.PostBuildEnabled.ShouldBeTrue();
        loaded.StringEncryption.ShouldBeFalse();
        loaded.SymbolRenaming.ShouldBeTrue();
    }

    [Fact]
    public void Serialize_PreservesCoreOnlyFields()
    {
        const string existing = """
            {
              "level": "standard",
              "runtimeProfile": "NativeAot",
              "virtualization": { "enabled": true, "maxMethods": 8 },
              "symbolRenaming": { "enabled": true, "preservePublicApi": true },
              "exclusions": { "namespaces": ["UnityEngine"] }
            }
            """;

        var settings = ObfySettings.ForLevel(ObfuscationLevel.Minimal);
        var json = ObfySettingsJson.Serialize(settings, existing);

        json.ShouldContain("\"runtimeProfile\"");
        json.ShouldContain("NativeAot");
        json.ShouldContain("\"virtualization\"");
        json.ShouldContain("\"preservePublicApi\": true");
        json.ShouldContain("UnityEngine");
        json.ShouldContain("\"level\": \"minimal\"");
    }

    [Fact]
    public void Parse_UnknownLevel_Throws()
    {
        Should.Throw<System.Text.Json.JsonException>(() =>
            ObfySettingsJson.Parse("""{ "level": "aggresive" }"""));
    }
}
