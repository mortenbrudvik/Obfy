using Obfy.Core.Models;
using Shouldly;

namespace Obfy.Tests;

public class ObfySettingsTests
{
    [Fact]
    public void ForLevel_Minimal_DisablesStringEncryptionAndControlFlow()
    {
        // Act
        var settings = ObfySettings.ForLevel(ObfuscationLevel.Minimal);

        // Assert
        settings.StringEncryption.Enabled.ShouldBeFalse();
        settings.ControlFlow.Enabled.ShouldBeFalse();
        settings.SymbolRenaming.Enabled.ShouldBeTrue();
        settings.Protection.AntiDebug.ShouldBeFalse();
    }

    [Fact]
    public void ForLevel_Standard_EnablesStringEncryptionAndRenaming()
    {
        // Act
        var settings = ObfySettings.ForLevel(ObfuscationLevel.Standard);

        // Assert
        settings.StringEncryption.Enabled.ShouldBeTrue();
        settings.ControlFlow.Enabled.ShouldBeFalse();
        settings.SymbolRenaming.Enabled.ShouldBeTrue();
        settings.Metadata.RemoveDebugInfo.ShouldBeTrue();
    }

    [Fact]
    public void ForLevel_Aggressive_EnablesAllProtections()
    {
        // Act
        var settings = ObfySettings.ForLevel(ObfuscationLevel.Aggressive);

        // Assert
        settings.StringEncryption.Enabled.ShouldBeTrue();
        settings.ControlFlow.Enabled.ShouldBeTrue();
        settings.ControlFlow.Intensity.ShouldBe(80);
        settings.SymbolRenaming.Enabled.ShouldBeTrue();
        settings.Protection.AntiDebug.ShouldBeTrue();
        settings.Protection.AntiTamper.Enabled.ShouldBeTrue();
        settings.Protection.AntiDump.ShouldBeTrue();
        settings.Protection.ReferenceProxy.ShouldBeTrue();
        settings.Protection.MethodEncryption.ShouldBeTrue();
        settings.RuntimeProfile.ShouldBe(RuntimeProfile.Default);
    }

    [Fact]
    public void ForLevel_Custom_PreservesDefaults()
    {
        // Arrange
        var settings = new ObfySettings
        {
            Level = ObfuscationLevel.Custom,
            StringEncryption = { Enabled = false },
            SymbolRenaming = { Enabled = true }
        };

        // Act
        settings.ApplyLevel();

        // Assert - Custom level should not change settings
        settings.StringEncryption.Enabled.ShouldBeFalse();
        settings.SymbolRenaming.Enabled.ShouldBeTrue();
    }

    [Fact]
    public void DefaultSettings_HasReasonableDefaults()
    {
        // Act
        var settings = new ObfySettings();

        // Assert
        settings.Level.ShouldBe(ObfuscationLevel.Standard);
        settings.StringEncryption.Algorithm.ShouldBe(EncryptionAlgorithm.Aes256);
        settings.StringEncryption.MinStringLength.ShouldBe(3);
        settings.ControlFlow.Intensity.ShouldBe(50);
        settings.SymbolRenaming.Mode.ShouldBe(NamingMode.Unreadable);
        settings.ControlFlow.Enabled.ShouldBeFalse();
        settings.Protection.AntiDebug.ShouldBeFalse();
        settings.Exclusions.Attributes.ShouldContain("JsonPropertyNameAttribute");
        settings.Exclusions.Attributes.ShouldContain("JsonPropertyAttribute");
        settings.Exclusions.Attributes.ShouldContain("XmlElementAttribute");
        settings.Exclusions.Attributes.ShouldContain("XmlAttributeAttribute");
        settings.SymbolRenaming.PreserveXaml.ShouldBeFalse();
    }

    [Fact]
    public void ApplyLevel_Minimal_DisablesAggressiveProtections()
    {
        var settings = ObfySettings.ForLevel(ObfuscationLevel.Aggressive);
        settings.Level = ObfuscationLevel.Minimal;
        settings.ApplyLevel();

        settings.Protection.AntiTamper.Enabled.ShouldBeFalse();
        settings.Protection.AntiDecompiler.Enabled.ShouldBeFalse();
        settings.ResourceEncryption.Enabled.ShouldBeFalse();
        settings.ConstantEncryption.Enabled.ShouldBeFalse();
    }

    [Fact]
    public void ApplyLevel_Custom_DoesNotOverwriteFlags()
    {
        var settings = ObfySettings.ForLevel(ObfuscationLevel.Standard);
        settings.Level = ObfuscationLevel.Custom;
        settings.ControlFlow.Enabled = true;

        settings.ApplyLevel();

        settings.ControlFlow.Enabled.ShouldBeTrue();
        settings.StringEncryption.Enabled.ShouldBeTrue();
    }

    [Fact]
    public void ForLevel_Standard_SetsRemoveAttributesAndResetsAlgorithm()
    {
        var settings = ObfySettings.ForLevel(ObfuscationLevel.Aggressive);
        settings.ConstantEncryption.Algorithm.ShouldBe(EncryptionAlgorithm.Xor);

        settings.Level = ObfuscationLevel.Standard;
        settings.ApplyLevel();

        settings.Metadata.RemoveAttributes.ShouldBeTrue();
        settings.ConstantEncryption.Enabled.ShouldBeFalse();
        settings.ConstantEncryption.Algorithm.ShouldBe(EncryptionAlgorithm.Xor);
    }

    [Fact]
    public void Validate_ThrowsOnOutOfRangeIntensity()
    {
        var settings = new ObfySettings { ControlFlow = { Intensity = 101 } };
        Should.Throw<System.ComponentModel.DataAnnotations.ValidationException>(() => settings.Validate());

        settings.ControlFlow.Intensity = -1;
        Should.Throw<System.ComponentModel.DataAnnotations.ValidationException>(() => settings.Validate());
    }

    [Fact]
    public void Validate_AcceptsDefaults()
    {
        new ObfySettings().Validate();
    }

    [Fact]
    public void Clone_IsDeepCopy_AndIndependentAfterApplyLevel()
    {
        var original = new ObfySettings
        {
            Level = ObfuscationLevel.Custom,
            StringEncryption = { Enabled = false, MinStringLength = 7 }
        };

        var clone = original.Clone();
        clone.Level = ObfuscationLevel.Aggressive;
        clone.ApplyLevel();

        original.Level.ShouldBe(ObfuscationLevel.Custom);
        original.StringEncryption.Enabled.ShouldBeFalse();
        original.StringEncryption.MinStringLength.ShouldBe(7);

        clone.StringEncryption.Enabled.ShouldBeTrue();
        clone.ShouldNotBeSameAs(original);
        clone.StringEncryption.ShouldNotBeSameAs(original.StringEncryption);
    }

    [Fact]
    public void Validate_SigningEnabledWithoutKeyFile_Throws()
    {
        var settings = new ObfySettings { Signing = { Enabled = true } };
        Should.Throw<System.ComponentModel.DataAnnotations.ValidationException>(() => settings.Validate())
            .Message.ShouldContain("key file");
    }

    [Fact]
    public void Validate_PfxWithoutPasswordEnv_Throws()
    {
        var settings = new ObfySettings
        {
            Signing = { Enabled = true, KeyFile = "key.pfx" }
        };
        Should.Throw<System.ComponentModel.DataAnnotations.ValidationException>(() => settings.Validate())
            .Message.ShouldContain("PasswordEnvironmentVariable");
    }

    [Fact]
    public void Clone_DoesNotSerializeHasAny_AndKeepsV15Settings()
    {
        var original = new ObfySettings
        {
            RuntimeProfile = RuntimeProfile.NativeAot,
            Signing = { Enabled = true, KeyFile = "a.snk" },
            Inclusions = { Methods = { "OnlyThis" } },
            SymbolRenaming = { PreserveXaml = true }
        };

        var clone = original.Clone();
        clone.RuntimeProfile.ShouldBe(RuntimeProfile.NativeAot);
        clone.Signing.Enabled.ShouldBeTrue();
        clone.Signing.KeyFile.ShouldBe("a.snk");
        clone.Inclusions.Methods.ShouldContain("OnlyThis");
        clone.SymbolRenaming.PreserveXaml.ShouldBeTrue();

        var json = System.Text.Json.JsonSerializer.Serialize(original, new System.Text.Json.JsonSerializerOptions
        {
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
        });
        json.ShouldNotContain("hasAny");
    }

    [Fact]
    public void ResourceEncryption_DefaultExcludeIncludesEmbeddedPrefix()
    {
        new ResourceEncryptionSettings().ExcludePatterns.ShouldContain("Obfy.Embedded.*");
        new ResourceEncryptionSettings().ExcludePatterns.ShouldContain("*.resources");
    }

    [Fact]
    public void Clone_KeepsDependencyEmbeddingAndProxyExternal()
    {
        var original = new ObfySettings
        {
            RuntimeProfile = RuntimeProfile.BlazorWasm,
            DependencyEmbedding =
            {
                Enabled = true,
                IncludePatterns = { "Lib*.dll" },
                ExcludePatterns = { "Skip.dll" }
            },
            Protection = { ReferenceProxy = true, ProxyExternalCalls = true }
        };

        var clone = original.Clone();
        clone.RuntimeProfile.ShouldBe(RuntimeProfile.BlazorWasm);
        clone.DependencyEmbedding.Enabled.ShouldBeTrue();
        clone.DependencyEmbedding.IncludePatterns.ShouldContain("Lib*.dll");
        clone.DependencyEmbedding.ExcludePatterns.ShouldContain("Skip.dll");
        clone.Protection.ProxyExternalCalls.ShouldBeTrue();
    }

    [Fact]
    public void ApplyLevel_Standard_ClearsProxyExternalCalls()
    {
        var settings = new ObfySettings
        {
            Protection = { ReferenceProxy = true, ProxyExternalCalls = true },
            Level = ObfuscationLevel.Standard
        };

        settings.ApplyLevel();

        settings.Protection.ReferenceProxy.ShouldBeFalse();
        settings.Protection.ProxyExternalCalls.ShouldBeFalse();
    }

    [Fact]
    public void Validate_NullCoalescesDependencyEmbeddingLists()
    {
        var settings = new ObfySettings();
        settings.DependencyEmbedding.IncludePatterns = null!;
        settings.DependencyEmbedding.ExcludePatterns = null!;

        settings.Validate();

        settings.DependencyEmbedding.IncludePatterns.ShouldNotBeNull();
        settings.DependencyEmbedding.ExcludePatterns.ShouldNotBeNull();
    }

    [Fact]
    public void Validate_WatermarkEnabledWithoutId_Throws()
    {
        var settings = new ObfySettings { Watermark = { Enabled = true } };
        Should.Throw<System.ComponentModel.DataAnnotations.ValidationException>(() => settings.Validate())
            .Message.ShouldContain("id");
    }

    [Fact]
    public void Validate_WatermarkEnabledWithWhitespaceId_Throws()
    {
        var settings = new ObfySettings { Watermark = { Enabled = true, Id = "  " } };
        Should.Throw<System.ComponentModel.DataAnnotations.ValidationException>(() => settings.Validate())
            .Message.ShouldContain("id");
    }

    [Fact]
    public void Clone_KeepsWatermarkAndDecoyFlag()
    {
        var original = new ObfySettings
        {
            Watermark = { Enabled = true, Id = "customer-42" },
            Protection = { AntiDecompiler = { AddDecoyAttributes = false } }
        };

        var clone = original.Clone();
        clone.Watermark.Enabled.ShouldBeTrue();
        clone.Watermark.Id.ShouldBe("customer-42");
        clone.Protection.AntiDecompiler.AddDecoyAttributes.ShouldBeFalse();
        clone.Watermark.ShouldNotBeSameAs(original.Watermark);
    }

    [Fact]
    public void ApplyLevel_DoesNotResetWatermark()
    {
        var settings = new ObfySettings
        {
            Watermark = { Enabled = true, Id = "keep-me" },
            Level = ObfuscationLevel.Aggressive
        };
        settings.ApplyLevel();
        settings.Watermark.Enabled.ShouldBeTrue();
        settings.Watermark.Id.ShouldBe("keep-me");
    }
}
