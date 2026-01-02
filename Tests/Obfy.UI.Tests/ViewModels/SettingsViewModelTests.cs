using Obfy.Core.Models;
using Obfy.UI.ViewModels;
using Shouldly;

namespace Obfy.UI.Tests.ViewModels;

public class SettingsViewModelTests
{
    #region Preset Tests

    [Fact]
    public void ApplyPreset_Minimal_DisablesStringEncryption()
    {
        // Arrange
        var viewModel = new SettingsViewModel();

        // Act
        viewModel.ApplyPreset(ObfuscationLevel.Minimal);

        // Assert
        viewModel.StringEncryptionEnabled.ShouldBeFalse();
    }

    [Fact]
    public void ApplyPreset_Minimal_DisablesControlFlow()
    {
        // Arrange
        var viewModel = new SettingsViewModel();

        // Act
        viewModel.ApplyPreset(ObfuscationLevel.Minimal);

        // Assert
        viewModel.ControlFlowEnabled.ShouldBeFalse();
    }

    [Fact]
    public void ApplyPreset_Minimal_EnablesSymbolRenaming()
    {
        // Arrange
        var viewModel = new SettingsViewModel();

        // Act
        viewModel.ApplyPreset(ObfuscationLevel.Minimal);

        // Assert
        viewModel.SymbolRenamingEnabled.ShouldBeTrue();
    }

    [Fact]
    public void ApplyPreset_Minimal_DisablesAntiDebug()
    {
        // Arrange
        var viewModel = new SettingsViewModel();

        // Act
        viewModel.ApplyPreset(ObfuscationLevel.Minimal);

        // Assert
        viewModel.AntiDebugEnabled.ShouldBeFalse();
    }

    [Fact]
    public void ApplyPreset_Standard_EnablesStringEncryption()
    {
        // Arrange
        var viewModel = new SettingsViewModel();

        // Act
        viewModel.ApplyPreset(ObfuscationLevel.Standard);

        // Assert
        viewModel.StringEncryptionEnabled.ShouldBeTrue();
    }

    [Fact]
    public void ApplyPreset_Standard_DisablesControlFlow()
    {
        // Arrange
        var viewModel = new SettingsViewModel();

        // Act
        viewModel.ApplyPreset(ObfuscationLevel.Standard);

        // Assert
        viewModel.ControlFlowEnabled.ShouldBeFalse();
    }

    [Fact]
    public void ApplyPreset_Aggressive_EnablesStringEncryption()
    {
        // Arrange
        var viewModel = new SettingsViewModel();

        // Act
        viewModel.ApplyPreset(ObfuscationLevel.Aggressive);

        // Assert
        viewModel.StringEncryptionEnabled.ShouldBeTrue();
    }

    [Fact]
    public void ApplyPreset_Aggressive_EnablesControlFlow()
    {
        // Arrange
        var viewModel = new SettingsViewModel();

        // Act
        viewModel.ApplyPreset(ObfuscationLevel.Aggressive);

        // Assert
        viewModel.ControlFlowEnabled.ShouldBeTrue();
    }

    [Fact]
    public void ApplyPreset_Aggressive_EnablesAntiDebug()
    {
        // Arrange
        var viewModel = new SettingsViewModel();

        // Act
        viewModel.ApplyPreset(ObfuscationLevel.Aggressive);

        // Assert
        viewModel.AntiDebugEnabled.ShouldBeTrue();
    }

    [Fact]
    public void ApplyPreset_Aggressive_EnablesAntiTamper()
    {
        // Arrange
        var viewModel = new SettingsViewModel();

        // Act
        viewModel.ApplyPreset(ObfuscationLevel.Aggressive);

        // Assert
        viewModel.AntiTamperEnabled.ShouldBeTrue();
    }

    [Fact]
    public void ApplyPreset_Aggressive_EnablesAntiDecompiler()
    {
        // Arrange
        var viewModel = new SettingsViewModel();

        // Act
        viewModel.ApplyPreset(ObfuscationLevel.Aggressive);

        // Assert
        viewModel.AntiDecompilerEnabled.ShouldBeTrue();
    }

    [Fact]
    public void ApplyPreset_Aggressive_EnablesResourceEncryption()
    {
        // Arrange
        var viewModel = new SettingsViewModel();

        // Act
        viewModel.ApplyPreset(ObfuscationLevel.Aggressive);

        // Assert
        viewModel.ResourceEncryptionEnabled.ShouldBeTrue();
    }

    [Fact]
    public void ApplyPreset_Aggressive_EnablesConstantEncryption()
    {
        // Arrange
        var viewModel = new SettingsViewModel();

        // Act
        viewModel.ApplyPreset(ObfuscationLevel.Aggressive);

        // Assert
        viewModel.ConstantEncryptionEnabled.ShouldBeTrue();
    }

    [Fact]
    public void ApplyPreset_Aggressive_SetsHighControlFlowIntensity()
    {
        // Arrange
        var viewModel = new SettingsViewModel();

        // Act
        viewModel.ApplyPreset(ObfuscationLevel.Aggressive);

        // Assert
        viewModel.ControlFlowIntensity.ShouldBe(80);
    }

    #endregion

    #region Level Change Auto-Apply Tests

    [Fact]
    public void Level_WhenChangedToMinimal_AppliesMinimalPreset()
    {
        // Arrange
        var viewModel = new SettingsViewModel();
        viewModel.StringEncryptionEnabled = true; // Start with it enabled

        // Act
        viewModel.Level = ObfuscationLevel.Minimal;

        // Assert
        viewModel.StringEncryptionEnabled.ShouldBeFalse();
    }

    [Fact]
    public void Level_WhenChangedToAggressive_AppliesAggressivePreset()
    {
        // Arrange
        var viewModel = new SettingsViewModel();

        // Act
        viewModel.Level = ObfuscationLevel.Aggressive;

        // Assert
        viewModel.AntiDebugEnabled.ShouldBeTrue();
    }

    [Fact]
    public void Level_WhenSetToCustom_DoesNotChangeSettings()
    {
        // Arrange
        var viewModel = new SettingsViewModel();
        viewModel.StringEncryptionEnabled = false;
        viewModel.ControlFlowEnabled = true;

        // Act
        viewModel.Level = ObfuscationLevel.Custom;

        // Assert - settings should remain unchanged
        viewModel.StringEncryptionEnabled.ShouldBeFalse();
        viewModel.ControlFlowEnabled.ShouldBeTrue();
    }

    #endregion

    #region ToObfySettings Tests

    [Fact]
    public void ToObfySettings_ConvertsLevel()
    {
        // Arrange
        var viewModel = new SettingsViewModel { Level = ObfuscationLevel.Aggressive };

        // Act
        var settings = viewModel.ToObfySettings();

        // Assert
        settings.Level.ShouldBe(ObfuscationLevel.Aggressive);
    }

    [Fact]
    public void ToObfySettings_ConvertsStringEncryption()
    {
        // Arrange
        var viewModel = new SettingsViewModel
        {
            StringEncryptionEnabled = true,
            StringEncryptionAlgorithm = EncryptionAlgorithm.Aes256,
            MinStringLength = 5
        };

        // Act
        var settings = viewModel.ToObfySettings();

        // Assert
        settings.StringEncryption.Enabled.ShouldBeTrue();
        settings.StringEncryption.Algorithm.ShouldBe(EncryptionAlgorithm.Aes256);
        settings.StringEncryption.MinStringLength.ShouldBe(5);
    }

    [Fact]
    public void ToObfySettings_ConvertsControlFlow()
    {
        // Arrange
        var viewModel = new SettingsViewModel
        {
            ControlFlowEnabled = true,
            ControlFlowMode = ControlFlowMode.Switch,
            ControlFlowIntensity = 75
        };

        // Act
        var settings = viewModel.ToObfySettings();

        // Assert
        settings.ControlFlow.Enabled.ShouldBeTrue();
        settings.ControlFlow.Mode.ShouldBe(ControlFlowMode.Switch);
        settings.ControlFlow.Intensity.ShouldBe(75);
    }

    [Fact]
    public void ToObfySettings_ConvertsSymbolRenaming()
    {
        // Arrange
        var viewModel = new SettingsViewModel
        {
            SymbolRenamingEnabled = true,
            NamingMode = NamingMode.Unreadable,
            RenameTypes = true,
            RenameMethods = true,
            RenameFields = false,
            PreservePublicApi = true
        };

        // Act
        var settings = viewModel.ToObfySettings();

        // Assert
        settings.SymbolRenaming.Enabled.ShouldBeTrue();
        settings.SymbolRenaming.Mode.ShouldBe(NamingMode.Unreadable);
        settings.SymbolRenaming.RenameTypes.ShouldBeTrue();
        settings.SymbolRenaming.RenameMethods.ShouldBeTrue();
        settings.SymbolRenaming.RenameFields.ShouldBeFalse();
        settings.SymbolRenaming.PreservePublicApi.ShouldBeTrue();
    }

    [Fact]
    public void ToObfySettings_ConvertsProtection()
    {
        // Arrange
        var viewModel = new SettingsViewModel
        {
            AntiDebugEnabled = true,
            AntiTamperEnabled = true,
            AntiDumpEnabled = true
        };

        // Act
        var settings = viewModel.ToObfySettings();

        // Assert
        settings.Protection.AntiDebug.ShouldBeTrue();
        settings.Protection.AntiTamper.Enabled.ShouldBeTrue();
        settings.Protection.AntiDump.ShouldBeTrue();
    }

    [Fact]
    public void ToObfySettings_ConvertsExclusions()
    {
        // Arrange
        var viewModel = new SettingsViewModel();
        viewModel.ExcludedNamespaces.Add("System.Security");
        viewModel.ExcludedTypes.Add("MyApp.Internal");
        viewModel.ExcludedMethods.Add("Main");

        // Act
        var settings = viewModel.ToObfySettings();

        // Assert
        settings.Exclusions.Namespaces.ShouldContain("System.Security");
        settings.Exclusions.Types.ShouldContain("MyApp.Internal");
        settings.Exclusions.Methods.ShouldContain("Main");
    }

    [Fact]
    public void ToObfySettings_ConvertsConstantEncryption()
    {
        // Arrange
        var viewModel = new SettingsViewModel
        {
            ConstantEncryptionEnabled = true,
            ConstantEncryptionAlgorithm = EncryptionAlgorithm.Xor,
            EncryptIntegers = true,
            EncryptLongs = false
        };

        // Act
        var settings = viewModel.ToObfySettings();

        // Assert
        settings.ConstantEncryption.Enabled.ShouldBeTrue();
        settings.ConstantEncryption.Algorithm.ShouldBe(EncryptionAlgorithm.Xor);
        settings.ConstantEncryption.EncryptIntegers.ShouldBeTrue();
        settings.ConstantEncryption.EncryptLongs.ShouldBeFalse();
    }

    #endregion

    #region FromObfySettings Tests

    [Fact]
    public void FromObfySettings_LoadsLevel()
    {
        // Arrange
        var viewModel = new SettingsViewModel();
        var settings = new ObfySettings { Level = ObfuscationLevel.Aggressive };

        // Act
        viewModel.FromObfySettings(settings);

        // Assert
        viewModel.Level.ShouldBe(ObfuscationLevel.Aggressive);
    }

    [Fact]
    public void FromObfySettings_LoadsStringEncryption()
    {
        // Arrange
        var viewModel = new SettingsViewModel();
        var settings = new ObfySettings
        {
            StringEncryption = new StringEncryptionSettings
            {
                Enabled = true,
                Algorithm = EncryptionAlgorithm.Xor,
                MinStringLength = 10
            }
        };

        // Act
        viewModel.FromObfySettings(settings);

        // Assert
        viewModel.StringEncryptionEnabled.ShouldBeTrue();
        viewModel.StringEncryptionAlgorithm.ShouldBe(EncryptionAlgorithm.Xor);
        viewModel.MinStringLength.ShouldBe(10);
    }

    [Fact]
    public void FromObfySettings_LoadsExclusions()
    {
        // Arrange
        var viewModel = new SettingsViewModel();
        viewModel.ExcludedNamespaces.Add("OldNamespace"); // Should be cleared
        var settings = new ObfySettings
        {
            Exclusions = new ExclusionRules
            {
                Namespaces = new List<string> { "System", "Microsoft" },
                Types = new List<string> { "MyType" },
                Methods = new List<string> { "DoNotObfuscate" }
            }
        };

        // Act
        viewModel.FromObfySettings(settings);

        // Assert
        viewModel.ExcludedNamespaces.Count.ShouldBe(2);
        viewModel.ExcludedNamespaces.ShouldContain("System");
        viewModel.ExcludedNamespaces.ShouldContain("Microsoft");
        viewModel.ExcludedNamespaces.ShouldNotContain("OldNamespace");
        viewModel.ExcludedTypes.ShouldContain("MyType");
        viewModel.ExcludedMethods.ShouldContain("DoNotObfuscate");
    }

    [Fact]
    public void FromObfySettings_RoundTrips()
    {
        // Arrange
        var original = new SettingsViewModel
        {
            Level = ObfuscationLevel.Aggressive,
            StringEncryptionEnabled = true,
            ControlFlowEnabled = true,
            ControlFlowIntensity = 80,
            AntiDebugEnabled = true
        };
        original.ExcludedNamespaces.Add("Test.Namespace");

        // Act
        var settings = original.ToObfySettings();
        var restored = new SettingsViewModel();
        restored.FromObfySettings(settings);

        // Assert
        restored.Level.ShouldBe(original.Level);
        restored.StringEncryptionEnabled.ShouldBe(original.StringEncryptionEnabled);
        restored.ControlFlowEnabled.ShouldBe(original.ControlFlowEnabled);
        restored.ControlFlowIntensity.ShouldBe(original.ControlFlowIntensity);
        restored.AntiDebugEnabled.ShouldBe(original.AntiDebugEnabled);
        restored.ExcludedNamespaces.ShouldContain("Test.Namespace");
    }

    #endregion

    #region Exclusion Collection Tests

    [Fact]
    public void ExcludedNamespaces_CanAddItems()
    {
        // Arrange
        var viewModel = new SettingsViewModel();

        // Act
        viewModel.ExcludedNamespaces.Add("System.Security");
        viewModel.ExcludedNamespaces.Add("System.Reflection");

        // Assert
        viewModel.ExcludedNamespaces.Count.ShouldBe(2);
    }

    [Fact]
    public void ExcludedTypes_CanRemoveItems()
    {
        // Arrange
        var viewModel = new SettingsViewModel();
        viewModel.ExcludedTypes.Add("TypeA");
        viewModel.ExcludedTypes.Add("TypeB");

        // Act
        viewModel.ExcludedTypes.Remove("TypeA");

        // Assert
        viewModel.ExcludedTypes.Count.ShouldBe(1);
        viewModel.ExcludedTypes.ShouldContain("TypeB");
    }

    [Fact]
    public void ExcludedMethods_CanClear()
    {
        // Arrange
        var viewModel = new SettingsViewModel();
        viewModel.ExcludedMethods.Add("Method1");
        viewModel.ExcludedMethods.Add("Method2");

        // Act
        viewModel.ExcludedMethods.Clear();

        // Assert
        viewModel.ExcludedMethods.Count.ShouldBe(0);
    }

    #endregion

    #region Available Options Tests

    [Fact]
    public void AvailableLevels_ContainsAllLevels()
    {
        // Assert
        SettingsViewModel.AvailableLevels.ShouldContain(ObfuscationLevel.Minimal);
        SettingsViewModel.AvailableLevels.ShouldContain(ObfuscationLevel.Standard);
        SettingsViewModel.AvailableLevels.ShouldContain(ObfuscationLevel.Aggressive);
        SettingsViewModel.AvailableLevels.ShouldContain(ObfuscationLevel.Custom);
    }

    [Fact]
    public void AvailableAlgorithms_ContainsCommonAlgorithms()
    {
        // Assert
        SettingsViewModel.AvailableAlgorithms.ShouldContain(EncryptionAlgorithm.Aes256);
        SettingsViewModel.AvailableAlgorithms.ShouldContain(EncryptionAlgorithm.Xor);
    }

    [Fact]
    public void AvailableNamingModes_ContainsAllModes()
    {
        // Assert
        SettingsViewModel.AvailableNamingModes.ShouldContain(NamingMode.Unreadable);
    }

    #endregion
}
