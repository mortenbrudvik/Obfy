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
    }
}
