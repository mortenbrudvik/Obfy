using System.Text.Json.Nodes;
using Obfy.VisualStudio.Services;
using Obfy.VisualStudio.UI;
using Shouldly;

namespace Obfy.VisualStudio.Tests;

public class ReviewFixesVisualStudioTests
{
    [Theory]
    [InlineData(@"-test\My App.dll", "\"-test\\My App.dll\"")]
    [InlineData(@"-test\My App\", "\"-test\\My App\\\\\"")]
    [InlineData(@"-test ""App""\", "\"-test \\\"App\\\"\\\\\"")]
    [InlineData("-o", "\"-o\"")]
    public void CliArgumentBuilder_Quote_QuotesValuesStartingWithDash(string input, string expected)
    {
        CliArgumentBuilder.Quote(input).ShouldBe(expected);
    }

    [Fact]
    public void CliArgumentBuilder_ToCommandLine_QuotesDashPrefixedPaths_WhileKeepingLiteralFlagsUnquoted()
    {
        var args = CliArgumentBuilder.Build(
            @"-test\My App.dll",
            @"-out\My Out.dll",
            @"-cfg\my obfy.json",
            generateSymbolMap: true);

        var commandLine = CliArgumentBuilder.ToCommandLine(args);

        commandLine.ShouldStartWith("\"-test\\My App.dll\" ");
        commandLine.ShouldContain(" -o \"");
        commandLine.ShouldContain(" -c \"-cfg\\my obfy.json\"");
        commandLine.ShouldContain(" --map \"-test\\My App.map.json\"");
        commandLine.ShouldNotContain("\"-o\"");
        commandLine.ShouldNotContain("\"-c\"");
        commandLine.ShouldNotContain("\"--map\"");
    }

    [Fact]
    public void CliArgumentBuilder_ToCommandLine_QuotesAssemblyEvenIfNamedLikeAFlag()
    {
        var args = CliArgumentBuilder.Build("-o", null, null, ObfuscationLevel.Standard);

        var commandLine = CliArgumentBuilder.ToCommandLine(args);

        commandLine.ShouldBe("\"-o\" -l \"standard\"");
    }

    [Fact]
    public void ObfySettingsJson_Serialize_UpdatesPascalCaseDocumentWithoutDuplicateKeys()
    {
        const string pascalCaseJson = """
            {
              "Level": "minimal",
              "PostBuildEnabled": false,
              "StringEncryption": { "Enabled": false, "CustomNote": "keep-me" },
              "ControlFlow": { "Enabled": false, "Mode": "jump", "Intensity": 25 },
              "SymbolRenaming": { "Enabled": true, "PreservePublicApi": true },
              "ConstantEncryption": { "Enabled": false, "Algorithm": "xor" },
              "ResourceEncryption": { "Enabled": false },
              "Protection": {
                "AntiDebug": false,
                "AntiDump": false,
                "ReferenceProxy": false,
                "AntiTamper": { "Enabled": false },
                "AntiDecompiler": { "Enabled": false },
                "MethodEncryption": false,
                "ProxyExternalCalls": false,
                "ExtraProtectionFlag": true
              },
              "Metadata": {
                "RemoveDebugInfo": false,
                "RemoveAttributes": false,
                "ExtraMeta": 42
              }
            }
            """;

        var updated = ObfySettings.ForLevel(ObfuscationLevel.Aggressive);
        updated.PostBuildEnabled = true;

        var serialized = ObfySettingsJson.Serialize(updated, pascalCaseJson);
        var root = JsonNode.Parse(serialized)!.AsObject();

        AssertNoCaseInsensitiveDuplicates(root);
        root.ContainsKey("Level").ShouldBeFalse();
        root.ContainsKey("PostBuildEnabled").ShouldBeFalse();
        root.ContainsKey("StringEncryption").ShouldBeFalse();
        root.ContainsKey("Protection").ShouldBeFalse();
        root.ContainsKey("Metadata").ShouldBeFalse();

        root["level"]!.GetValue<string>().ShouldBe("aggressive");
        root["postBuildEnabled"]!.GetValue<bool>().ShouldBeTrue();
        root["stringEncryption"]!["enabled"]!.GetValue<bool>().ShouldBeTrue();
        root["stringEncryption"]!["CustomNote"]!.GetValue<string>().ShouldBe("keep-me");
        root["symbolRenaming"]!["PreservePublicApi"]!.GetValue<bool>().ShouldBeTrue();
        root["controlFlow"]!["mode"]!.GetValue<string>().ShouldBe("switch");
        root["controlFlow"]!["intensity"]!.GetValue<int>().ShouldBe(80);
        root["protection"]!["antiDebug"]!.GetValue<bool>().ShouldBeTrue();
        root["protection"]!["methodEncryption"]!.GetValue<bool>().ShouldBeTrue();
        root["protection"]!["ExtraProtectionFlag"]!.GetValue<bool>().ShouldBeTrue();
        root["metadata"]!["removeDebugInfo"]!.GetValue<bool>().ShouldBeTrue();
        root["metadata"]!["removeAttributes"]!.GetValue<bool>().ShouldBeTrue();
        root["metadata"]!["ExtraMeta"]!.GetValue<int>().ShouldBe(42);
    }

    [Fact]
    public void ObfySettingsJson_PatchPostBuildEnabled_ReplacesPascalCaseKeyWithoutDuplicate()
    {
        const string pascalCaseJson = """
            {
              "PostBuildEnabled": false,
              "Virtualization": { "Enabled": true }
            }
            """;

        var patched = ObfySettingsJson.PatchPostBuildEnabled(pascalCaseJson, true);
        var root = JsonNode.Parse(patched)!.AsObject();

        AssertNoCaseInsensitiveDuplicates(root);
        root.ContainsKey("PostBuildEnabled").ShouldBeFalse();
        root["postBuildEnabled"]!.GetValue<bool>().ShouldBeTrue();
        root["Virtualization"]!["Enabled"]!.GetValue<bool>().ShouldBeTrue();
    }

    [Theory]
    [InlineData("{ not valid json")]
    [InlineData("[]")]
    [InlineData("42")]
    public void ObfySettingsJson_SerializeAndPatch_ThrowOnMalformedOrNonObjectExistingJson(string invalidExisting)
    {
        var settings = ObfySettings.ForLevel(ObfuscationLevel.Standard);

        Should.Throw<InvalidOperationException>(() => ObfySettingsJson.Serialize(settings, invalidExisting));
        Should.Throw<InvalidOperationException>(() => ObfySettingsJson.PatchPostBuildEnabled(invalidExisting, true));
    }

    [Fact]
    public void SettingsDialogViewModel_SwitchingAggressiveToCustom_PreservesAllToggles()
    {
        var vm = new SettingsDialogViewModel(ObfySettings.ForLevel(ObfuscationLevel.Aggressive), "TestProject");

        vm.Level = ObfuscationLevel.Custom;

        var saved = vm.GetSettings();
        saved.Level.ShouldBe(ObfuscationLevel.Custom);
        saved.StringEncryption.ShouldBeTrue();
        saved.SymbolRenaming.ShouldBeTrue();
        saved.ControlFlow.ShouldBeTrue();
        saved.AntiDebug.ShouldBeTrue();
        saved.AntiDump.ShouldBeTrue();
        saved.ReferenceProxy.ShouldBeTrue();
        saved.AntiTamper.ShouldBeTrue();
        saved.AntiDecompiler.ShouldBeTrue();
        saved.ConstantEncryption.ShouldBeTrue();
        saved.ResourceEncryption.ShouldBeTrue();
        saved.MethodEncryption.ShouldBeTrue();
        saved.ProxyExternalCalls.ShouldBeFalse();
    }

    [Fact]
    public void SettingsDialogViewModel_SwitchingMinimalToCustom_DoesNotResetStringEncryptionToStandard()
    {
        var vm = new SettingsDialogViewModel(ObfySettings.ForLevel(ObfuscationLevel.Minimal), "TestProject");
        vm.StringEncryption.ShouldBeFalse();

        vm.Level = ObfuscationLevel.Custom;

        var saved = vm.GetSettings();
        saved.Level.ShouldBe(ObfuscationLevel.Custom);
        saved.StringEncryption.ShouldBeFalse();
        saved.SymbolRenaming.ShouldBeTrue();
        saved.ControlFlow.ShouldBeFalse();
    }

    private static void AssertNoCaseInsensitiveDuplicates(JsonObject obj)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in obj)
        {
            seen.Add(prop.Key).ShouldBeTrue($"Duplicate key differing only by case: '{prop.Key}'");
            if (prop.Value is JsonObject child)
            {
                AssertNoCaseInsensitiveDuplicates(child);
            }
        }
    }
}
