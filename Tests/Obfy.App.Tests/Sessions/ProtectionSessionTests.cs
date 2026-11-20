using Obfy.App;
using Obfy.App.Sessions;
using Obfy.Common;
using Obfy.Core.Models;
using Shouldly;

namespace Obfy.App.Tests.Sessions;

/// <summary>
/// The WPF <c>SettingsViewModelTests</c> ported to <see cref="ProtectionSession"/> (56 tests), plus the tests for the
/// three logic defects fixed in the lift (UI-039, UI-040, UI-055) and for the patch and snapshot the page uses.
/// </summary>
public class ProtectionSessionTests
{
    private static ObfySettings Settings(ProtectionSession session) => session.ToObfySettings();

    private static Dictionary<string, object?> Patch(params (string Key, object? Value)[] items) =>
        items.ToDictionary(i => i.Key, i => i.Value);

    #region Preset Tests

    [Fact]
    public void ApplyPreset_Minimal_DisablesStringEncryption()
    {
        var session = new ProtectionSession();
        session.ApplyLevel(ObfuscationLevel.Minimal);
        Settings(session).StringEncryption.Enabled.ShouldBeFalse();
    }

    [Fact]
    public void ApplyPreset_Minimal_DisablesControlFlow()
    {
        var session = new ProtectionSession();
        session.ApplyLevel(ObfuscationLevel.Minimal);
        Settings(session).ControlFlow.Enabled.ShouldBeFalse();
    }

    [Fact]
    public void ApplyPreset_Minimal_EnablesSymbolRenaming()
    {
        var session = new ProtectionSession();
        session.ApplyLevel(ObfuscationLevel.Minimal);
        Settings(session).SymbolRenaming.Enabled.ShouldBeTrue();
    }

    [Fact]
    public void ApplyPreset_Minimal_DisablesAntiDebug()
    {
        var session = new ProtectionSession();
        session.ApplyLevel(ObfuscationLevel.Minimal);
        Settings(session).Protection.AntiDebug.ShouldBeFalse();
    }

    [Fact]
    public void ApplyPreset_Standard_EnablesStringEncryption()
    {
        var session = new ProtectionSession();
        session.ApplyLevel(ObfuscationLevel.Standard);
        Settings(session).StringEncryption.Enabled.ShouldBeTrue();
    }

    [Fact]
    public void ApplyPreset_Standard_DisablesControlFlow()
    {
        var session = new ProtectionSession();
        session.ApplyLevel(ObfuscationLevel.Standard);
        Settings(session).ControlFlow.Enabled.ShouldBeFalse();
    }

    [Fact]
    public void ApplyPreset_Aggressive_EnablesStringEncryption()
    {
        var session = new ProtectionSession();
        session.ApplyLevel(ObfuscationLevel.Aggressive);
        Settings(session).StringEncryption.Enabled.ShouldBeTrue();
    }

    [Fact]
    public void ApplyPreset_Aggressive_EnablesControlFlow()
    {
        var session = new ProtectionSession();
        session.ApplyLevel(ObfuscationLevel.Aggressive);
        Settings(session).ControlFlow.Enabled.ShouldBeTrue();
    }

    [Fact]
    public void ApplyPreset_Aggressive_EnablesAntiDebug()
    {
        var session = new ProtectionSession();
        session.ApplyLevel(ObfuscationLevel.Aggressive);
        Settings(session).Protection.AntiDebug.ShouldBeTrue();
    }

    [Fact]
    public void ApplyPreset_Aggressive_EnablesAntiTamper()
    {
        var session = new ProtectionSession();
        session.ApplyLevel(ObfuscationLevel.Aggressive);
        Settings(session).Protection.AntiTamper.Enabled.ShouldBeTrue();
    }

    [Fact]
    public void ApplyPreset_Aggressive_EnablesAntiDecompiler()
    {
        var session = new ProtectionSession();
        session.ApplyLevel(ObfuscationLevel.Aggressive);
        Settings(session).Protection.AntiDecompiler.Enabled.ShouldBeTrue();
    }

    [Fact]
    public void ApplyPreset_Aggressive_EnablesResourceEncryption()
    {
        var session = new ProtectionSession();
        session.ApplyLevel(ObfuscationLevel.Aggressive);
        Settings(session).ResourceEncryption.Enabled.ShouldBeTrue();
    }

    [Fact]
    public void ApplyPreset_Aggressive_EnablesConstantEncryption()
    {
        var session = new ProtectionSession();
        session.ApplyLevel(ObfuscationLevel.Aggressive);
        Settings(session).ConstantEncryption.Enabled.ShouldBeTrue();
    }

    [Fact]
    public void ApplyPreset_Aggressive_SetsHighControlFlowIntensity()
    {
        var session = new ProtectionSession();
        session.ApplyLevel(ObfuscationLevel.Aggressive);
        Settings(session).ControlFlow.Intensity.ShouldBe(80);
    }

    [Fact]
    public void ApplyPreset_Aggressive_EnablesAntiDumpReferenceProxyAndMethodEncryption()
    {
        var session = new ProtectionSession();

        session.ApplyLevel(ObfuscationLevel.Aggressive);

        var settings = Settings(session);
        settings.Protection.AntiDump.ShouldBeTrue();
        settings.Protection.ReferenceProxy.ShouldBeTrue();
        settings.Protection.MethodEncryption.ShouldBeTrue();
        settings.Metadata.RemoveAttributes.ShouldBeTrue();
        settings.ControlFlow.Intensity.ShouldBe(80);
        settings.ConstantEncryption.Algorithm.ShouldBe(EncryptionAlgorithm.Xor);
    }

    [Fact]
    public void ApplyPreset_Minimal_ClearsAggressiveProtectionsAndResetsIntensity()
    {
        var session = new ProtectionSession();
        session.ApplyLevel(ObfuscationLevel.Aggressive);

        session.ApplyLevel(ObfuscationLevel.Minimal);

        var settings = Settings(session);
        settings.Protection.AntiDebug.ShouldBeFalse();
        settings.Protection.AntiTamper.Enabled.ShouldBeFalse();
        settings.Protection.AntiDecompiler.Enabled.ShouldBeFalse();
        settings.Protection.AntiDump.ShouldBeFalse();
        settings.Protection.ReferenceProxy.ShouldBeFalse();
        settings.Protection.MethodEncryption.ShouldBeFalse();
        settings.Metadata.RemoveAttributes.ShouldBeFalse();
        settings.ControlFlow.Intensity.ShouldBe(50);
        settings.ConstantEncryption.Enabled.ShouldBeFalse();
        settings.ResourceEncryption.Enabled.ShouldBeFalse();
    }

    [Fact]
    public void ApplyPreset_Standard_ClearsAggressiveProtectionsAndStripsAttributes()
    {
        var session = new ProtectionSession();
        session.ApplyLevel(ObfuscationLevel.Aggressive);

        session.ApplyLevel(ObfuscationLevel.Standard);

        var settings = Settings(session);
        settings.StringEncryption.Enabled.ShouldBeTrue();
        settings.Protection.AntiDebug.ShouldBeFalse();
        settings.Protection.AntiTamper.Enabled.ShouldBeFalse();
        settings.Protection.AntiDecompiler.Enabled.ShouldBeFalse();
        settings.Protection.AntiDump.ShouldBeFalse();
        settings.Protection.ReferenceProxy.ShouldBeFalse();
        settings.Protection.MethodEncryption.ShouldBeFalse();
        settings.Metadata.RemoveAttributes.ShouldBeTrue();
        settings.ControlFlow.Intensity.ShouldBe(50);
    }

    [Fact]
    public void AddExcludedNamespace_AddsTrimmedValue()
    {
        var session = new ProtectionSession();

        session.UpdateSetting("exclusions.namespaces", new[] { "  MyApp.Internal  " });

        Settings(session).Exclusions.Namespaces.ShouldBe(new[] { "MyApp.Internal" });
    }

    [Fact]
    public void AddExcludedNamespace_IgnoresBlankAndDuplicates()
    {
        var session = new ProtectionSession();

        session.UpdateSetting("exclusions.namespaces", new[] { "System", "  ", "System", " System " });

        Settings(session).Exclusions.Namespaces.Count.ShouldBe(1);
    }

    #endregion

    #region Level Change Auto-Apply Tests

    [Fact]
    public void Level_WhenChangedToMinimal_AppliesMinimalPreset()
    {
        var session = new ProtectionSession();
        session.UpdateSetting("stringEncryption.enabled", true);

        session.ApplyLevel(ObfuscationLevel.Minimal);

        Settings(session).StringEncryption.Enabled.ShouldBeFalse();
    }

    [Fact]
    public void Level_WhenChangedToAggressive_AppliesAggressivePreset()
    {
        var session = new ProtectionSession();
        session.ApplyLevel(ObfuscationLevel.Aggressive);
        Settings(session).Protection.AntiDebug.ShouldBeTrue();
    }

    [Fact]
    public void Level_WhenSetToCustom_DoesNotChangeSettings()
    {
        var session = new ProtectionSession();
        session.UpdateSetting("stringEncryption.enabled", false);
        session.UpdateSetting("controlFlow.enabled", true);

        session.ApplyLevel(ObfuscationLevel.Custom);

        var settings = Settings(session);
        settings.StringEncryption.Enabled.ShouldBeFalse();
        settings.ControlFlow.Enabled.ShouldBeTrue();
    }

    #endregion

    #region ToObfySettings Tests

    [Fact]
    public void TogglingFlag_SetsLevelToCustom()
    {
        var session = new ProtectionSession();
        session.ApplyLevel(ObfuscationLevel.Standard);

        session.UpdateSetting("controlFlow.enabled", true);

        session.Level.ShouldBe(ObfuscationLevel.Custom);
    }

    [Fact]
    public void ChangingAnExclusion_KeepsTheSelectedPreset()
    {
        var session = new ProtectionSession();
        session.ApplyLevel(ObfuscationLevel.Standard);

        session.Update(Patch(
            ("exclusions.namespaces", new[] { "MyApp.Generated" }),
            ("exclusions.types", new[] { "Secret" }),
            ("exclusions.methods", new[] { "Hidden" })));

        session.Level.ShouldBe(ObfuscationLevel.Standard);
        Settings(session).StringEncryption.Enabled.ShouldBeTrue();
    }

    [Fact]
    public void SelectingCustom_RestoresChoicesFromBeforeThePreset()
    {
        var session = new ProtectionSession();
        session.UpdateSetting("controlFlow.enabled", true);

        session.ApplyLevel(ObfuscationLevel.Minimal);
        Settings(session).ControlFlow.Enabled.ShouldBeFalse();
        Settings(session).StringEncryption.Enabled.ShouldBeFalse();

        session.ApplyLevel(ObfuscationLevel.Standard);
        session.ApplyLevel(ObfuscationLevel.Custom);

        Settings(session).ControlFlow.Enabled.ShouldBeTrue();
        Settings(session).StringEncryption.Enabled.ShouldBeTrue();
    }

    [Fact]
    public void ToObfySettings_ConvertsLevel()
    {
        var session = new ProtectionSession();
        session.ApplyLevel(ObfuscationLevel.Aggressive);
        Settings(session).Level.ShouldBe(ObfuscationLevel.Aggressive);
    }

    [Fact]
    public void ToObfySettings_ConvertsStringEncryption()
    {
        var session = new ProtectionSession();
        session.Update(Patch(
            ("stringEncryption.enabled", true),
            ("stringEncryption.algorithm", "Aes256"),
            ("stringEncryption.minStringLength", 5)));

        var settings = Settings(session);

        settings.StringEncryption.Enabled.ShouldBeTrue();
        settings.StringEncryption.Algorithm.ShouldBe(EncryptionAlgorithm.Aes256);
        settings.StringEncryption.MinStringLength.ShouldBe(5);
    }

    [Fact]
    public void ToObfySettings_ConvertsControlFlow()
    {
        var session = new ProtectionSession();
        session.Update(Patch(
            ("controlFlow.enabled", true),
            ("controlFlow.mode", "Switch"),
            ("controlFlow.intensity", 75)));

        var settings = Settings(session);

        settings.ControlFlow.Enabled.ShouldBeTrue();
        settings.ControlFlow.Mode.ShouldBe(ControlFlowMode.Switch);
        settings.ControlFlow.Intensity.ShouldBe(75);
    }

    [Fact]
    public void ToObfySettings_ConvertsSymbolRenaming()
    {
        var session = new ProtectionSession();
        session.Update(Patch(
            ("symbolRenaming.enabled", true),
            ("symbolRenaming.mode", "Unreadable"),
            ("symbolRenaming.renameTypes", true),
            ("symbolRenaming.renameMethods", true),
            ("symbolRenaming.renameFields", false),
            ("symbolRenaming.preservePublicApi", true)));

        var settings = Settings(session);

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
        var session = new ProtectionSession();
        session.Update(Patch(
            ("antiDebug.enabled", true),
            ("antiTamper.enabled", true),
            ("antiDump.enabled", true)));

        var settings = Settings(session);

        settings.Protection.AntiDebug.ShouldBeTrue();
        settings.Protection.AntiTamper.Enabled.ShouldBeTrue();
        settings.Protection.AntiDump.ShouldBeTrue();
    }

    [Fact]
    public void ToObfySettings_ConvertsProxyExternalAndEmbedding()
    {
        var session = new ProtectionSession();
        session.Update(Patch(
            ("referenceProxy.enabled", true),
            ("referenceProxy.proxyExternalCalls", true),
            ("dependencyEmbedding.enabled", true)));

        var settings = Settings(session);

        settings.Protection.ReferenceProxy.ShouldBeTrue();
        settings.Protection.ProxyExternalCalls.ShouldBeTrue();
        settings.DependencyEmbedding.Enabled.ShouldBeTrue();
    }

    [Fact]
    public void FromObfySettings_RoundTripsProxyExternalAndEmbedding()
    {
        var settings = new ObfySettings
        {
            Protection = { ReferenceProxy = true, ProxyExternalCalls = true },
            DependencyEmbedding =
            {
                Enabled = true,
                IncludePatterns = { "Lib.dll" },
                ExcludePatterns = { "Skip.dll" }
            }
        };

        var session = new ProtectionSession();
        session.FromObfySettings(settings);
        var roundTripped = Settings(session);

        roundTripped.Protection.ProxyExternalCalls.ShouldBeTrue();
        roundTripped.DependencyEmbedding.Enabled.ShouldBeTrue();
        roundTripped.DependencyEmbedding.IncludePatterns.ShouldContain("Lib.dll");
        roundTripped.DependencyEmbedding.ExcludePatterns.ShouldContain("Skip.dll");
    }

    [Fact]
    public void FromObfySettings_RoundTripsAssemblyMerge()
    {
        var settings = new ObfySettings { AssemblyMerge = { Enabled = true, Internalize = false } };

        var session = new ProtectionSession();
        session.FromObfySettings(settings);
        var roundTripped = Settings(session);

        roundTripped.AssemblyMerge.Enabled.ShouldBeTrue();
        roundTripped.AssemblyMerge.Internalize.ShouldBeFalse();
    }

    [Fact]
    public void ApplyPreset_Standard_ClearsProxyExternalCalls()
    {
        var session = new ProtectionSession();
        session.Update(Patch(("referenceProxy.enabled", true), ("referenceProxy.proxyExternalCalls", true)));
        session.Level.ShouldBe(ObfuscationLevel.Custom);

        session.ApplyLevel(ObfuscationLevel.Standard);

        var settings = Settings(session);
        settings.Protection.ReferenceProxy.ShouldBeFalse();
        settings.Protection.ProxyExternalCalls.ShouldBeFalse();
    }

    [Fact]
    public void ToObfySettings_ConvertsExclusions()
    {
        var session = new ProtectionSession();
        session.Update(Patch(
            ("exclusions.namespaces", new[] { "System.Security" }),
            ("exclusions.types", new[] { "MyApp.Internal" }),
            ("exclusions.methods", new[] { "Main" })));

        var settings = Settings(session);

        settings.Exclusions.Namespaces.ShouldContain("System.Security");
        settings.Exclusions.Types.ShouldContain("MyApp.Internal");
        settings.Exclusions.Methods.ShouldContain("Main");
    }

    [Fact]
    public void ToObfySettings_ConvertsConstantEncryption()
    {
        var session = new ProtectionSession();
        session.Update(Patch(
            ("constantEncryption.enabled", true),
            ("constantEncryption.algorithm", "Xor"),
            ("constantEncryption.encryptIntegers", true),
            ("constantEncryption.encryptLongs", false)));

        var settings = Settings(session);

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
        var session = new ProtectionSession();
        session.FromObfySettings(new ObfySettings { Level = ObfuscationLevel.Aggressive });
        session.Level.ShouldBe(ObfuscationLevel.Aggressive);
    }

    [Fact]
    public void FromObfySettings_LoadsStringEncryption()
    {
        var session = new ProtectionSession();
        var settings = new ObfySettings
        {
            StringEncryption = new StringEncryptionSettings
            {
                Enabled = true,
                Algorithm = EncryptionAlgorithm.Xor,
                MinStringLength = 10
            }
        };

        session.FromObfySettings(settings);

        var loaded = Settings(session);
        loaded.StringEncryption.Enabled.ShouldBeTrue();
        loaded.StringEncryption.Algorithm.ShouldBe(EncryptionAlgorithm.Xor);
        loaded.StringEncryption.MinStringLength.ShouldBe(10);
    }

    [Fact]
    public void FromObfySettings_LoadsExclusions()
    {
        var session = new ProtectionSession();
        session.UpdateSetting("exclusions.namespaces", new[] { "OldNamespace" }); // Should be cleared
        var settings = new ObfySettings
        {
            Exclusions = new ExclusionRules
            {
                Namespaces = new List<string> { "System", "Microsoft" },
                Types = new List<string> { "MyType" },
                Methods = new List<string> { "DoNotObfuscate" }
            }
        };

        session.FromObfySettings(settings);

        var loaded = Settings(session);
        loaded.Exclusions.Namespaces.Count.ShouldBe(2);
        loaded.Exclusions.Namespaces.ShouldContain("System");
        loaded.Exclusions.Namespaces.ShouldContain("Microsoft");
        loaded.Exclusions.Namespaces.ShouldNotContain("OldNamespace");
        loaded.Exclusions.Types.ShouldContain("MyType");
        loaded.Exclusions.Methods.ShouldContain("DoNotObfuscate");
    }

    [Fact]
    public void FromObfySettings_RoundTrips()
    {
        var original = new ProtectionSession();
        original.ApplyLevel(ObfuscationLevel.Aggressive);
        original.Update(Patch(
            ("stringEncryption.enabled", true),
            ("controlFlow.enabled", true),
            ("controlFlow.intensity", 80),
            ("antiDebug.enabled", true),
            ("exclusions.namespaces", new[] { "Test.Namespace" })));

        var settings = original.ToObfySettings();
        var restored = new ProtectionSession();
        restored.FromObfySettings(settings);
        var after = restored.ToObfySettings();

        restored.Level.ShouldBe(original.Level);
        after.StringEncryption.Enabled.ShouldBe(settings.StringEncryption.Enabled);
        after.ControlFlow.Enabled.ShouldBe(settings.ControlFlow.Enabled);
        after.ControlFlow.Intensity.ShouldBe(settings.ControlFlow.Intensity);
        after.Protection.AntiDebug.ShouldBe(settings.Protection.AntiDebug);
        after.Exclusions.Namespaces.ShouldContain("Test.Namespace");
    }

    [Fact]
    public void ToObfySettings_ConvertsPreserveXamlRuntimeProfileSigningAndInclusions()
    {
        var session = new ProtectionSession();
        session.Update(Patch(
            ("symbolRenaming.preserveXaml", true),
            ("runtimeProfile", "NativeAot"),
            ("signing.enabled", true),
            ("signing.keyFile", "key.snk"),
            ("signing.passwordEnvironmentVariable", "OBFY_PFX"),
            ("inclusions.methods", new[] { "OnlyThis" })));

        var settings = Settings(session);

        settings.SymbolRenaming.PreserveXaml.ShouldBeTrue();
        settings.RuntimeProfile.ShouldBe(RuntimeProfile.NativeAot);
        settings.Signing.Enabled.ShouldBeTrue();
        settings.Signing.KeyFile.ShouldBe("key.snk");
        settings.Signing.PasswordEnvironmentVariable.ShouldBe("OBFY_PFX");
        settings.Inclusions.Methods.ShouldContain("OnlyThis");
    }

    [Fact]
    public void FromObfySettings_RoundTripsRuntimeProfileSigningAndInclusions()
    {
        var settings = new ObfySettings
        {
            RuntimeProfile = RuntimeProfile.UnityIl2Cpp,
            Signing = { Enabled = true, KeyFile = @"C:\keys\lib.snk" },
            Inclusions = { Types = { "OnlyThis" }, Methods = { "Secret*" } },
            SymbolRenaming = { PreserveXaml = true }
        };

        var session = new ProtectionSession();
        session.FromObfySettings(settings);
        var roundTripped = Settings(session);

        roundTripped.RuntimeProfile.ShouldBe(RuntimeProfile.UnityIl2Cpp);
        roundTripped.Signing.Enabled.ShouldBeTrue();
        roundTripped.Signing.KeyFile.ShouldBe(@"C:\keys\lib.snk");
        roundTripped.SymbolRenaming.PreserveXaml.ShouldBeTrue();
        roundTripped.Inclusions.Types.ShouldContain("OnlyThis");
        roundTripped.Inclusions.Methods.ShouldContain("Secret*");
        session.GetSnapshot().RuntimeProfile.ShouldBe("UnityIl2Cpp");
    }

    [Fact]
    public void FromObfySettings_RoundTripsWatermarkAndDecoyAttributes()
    {
        var settings = new ObfySettings
        {
            Watermark = { Enabled = true, Id = "customer-42" },
            Protection = { AntiDecompiler = { AddDecoyAttributes = false } }
        };

        var session = new ProtectionSession();
        session.FromObfySettings(settings);
        var roundTripped = Settings(session);

        roundTripped.Watermark.Enabled.ShouldBeTrue();
        roundTripped.Watermark.Id.ShouldBe("customer-42");
        roundTripped.Protection.AntiDecompiler.AddDecoyAttributes.ShouldBeFalse();
    }

    [Fact]
    public void ToObfySettings_ConvertsPackingEnabled()
    {
        var session = new ProtectionSession();
        session.UpdateSetting("packing.enabled", true);

        var settings = Settings(session);

        settings.Packing.Enabled.ShouldBeTrue();
        settings.Packing.Rid.ShouldBe("win-x64");
    }

    [Fact]
    public void ToObfySettings_ConvertsVirtualizationAndIncremental()
    {
        var session = new ProtectionSession();
        session.Update(Patch(("virtualization.enabled", true), ("incremental.enabled", true)));

        var settings = Settings(session);

        settings.Virtualization.Enabled.ShouldBeTrue();
        settings.Incremental.Enabled.ShouldBeTrue();
    }

    [Fact]
    public void FromObfySettings_RoundTripsPackingEnabled()
    {
        var session = new ProtectionSession();
        session.FromObfySettings(new ObfySettings { Packing = { Enabled = true } });
        Settings(session).Packing.Enabled.ShouldBeTrue();
    }

    [Fact]
    public void FromObfySettings_RoundTripsPackingRidPortable()
    {
        var session = new ProtectionSession();
        session.FromObfySettings(new ObfySettings { Packing = { Enabled = true, Rid = "portable" } });

        var roundTripped = Settings(session);

        roundTripped.Packing.Enabled.ShouldBeTrue();
        roundTripped.Packing.Rid.ShouldBe("portable");
    }

    [Fact]
    public void ApplyPreset_Aggressive_ResetsPackingVirtualizationAndIncremental()
    {
        var session = new ProtectionSession();
        session.Update(Patch(("packing.enabled", true), ("virtualization.enabled", true), ("incremental.enabled", true)));

        session.ApplyLevel(ObfuscationLevel.Aggressive);

        var settings = Settings(session);
        settings.Packing.Enabled.ShouldBeFalse();
        settings.Virtualization.Enabled.ShouldBeFalse();
        settings.Incremental.Enabled.ShouldBeFalse();
    }

    [Fact]
    public void FromObfySettings_RoundTripsFieldsThePanelDoesNotEdit()
    {
        var settings = new ObfySettings
        {
            Level = ObfuscationLevel.Custom,
            PostBuildEnabled = true,
            StringEncryption = new StringEncryptionSettings
            {
                EncryptConstantStrings = false,
                EncryptResourceStrings = false,
                MinStringLength = 4
            },
            ConstantEncryption = new ConstantEncryptionSettings { LongThreshold = 40, IntegerThreshold = 7 },
            Virtualization = new VirtualizationSettings { Enabled = true, MaxMethods = 9 },
            ResourceEncryption = new ResourceEncryptionSettings
            {
                Enabled = true,
                IncludePatterns = new List<string> { "App.*" },
                ExcludePatterns = new List<string> { "skip.*" }
            },
            AssemblyMerge = new AssemblyMergeSettings
            {
                Enabled = true,
                Internalize = false,
                ExcludePatterns = new List<string> { "System.*" },
                SearchDirectories = new List<string> { @"C:\libs" },
                PreserveDebugInfo = true
            },
            Exclusions = new ExclusionRules
            {
                Attributes = new List<string> { "MyExcludeAttribute" },
                Namespaces = new List<string> { "Kept.Ns" }
            }
        };

        var session = new ProtectionSession();
        session.FromObfySettings(settings);
        session.UpdateSetting("stringEncryption.minStringLength", 8);
        var roundTripped = Settings(session);

        roundTripped.PostBuildEnabled.ShouldBeTrue();
        roundTripped.StringEncryption.EncryptConstantStrings.ShouldBeFalse();
        roundTripped.StringEncryption.EncryptResourceStrings.ShouldBeFalse();
        roundTripped.StringEncryption.MinStringLength.ShouldBe(8);
        roundTripped.ConstantEncryption.LongThreshold.ShouldBe(40);
        roundTripped.ConstantEncryption.IntegerThreshold.ShouldBe(7);
        roundTripped.Virtualization.Enabled.ShouldBeTrue();
        roundTripped.Virtualization.MaxMethods.ShouldBe(9);
        roundTripped.ResourceEncryption.IncludePatterns.ShouldBe(new[] { "App.*" });
        roundTripped.ResourceEncryption.ExcludePatterns.ShouldBe(new[] { "skip.*" });
        roundTripped.AssemblyMerge.Enabled.ShouldBeTrue();
        roundTripped.AssemblyMerge.Internalize.ShouldBeFalse();
        roundTripped.AssemblyMerge.ExcludePatterns.ShouldBe(new[] { "System.*" });
        roundTripped.AssemblyMerge.SearchDirectories.ShouldBe(new[] { @"C:\libs" });
        roundTripped.AssemblyMerge.PreserveDebugInfo.ShouldBeTrue();
        roundTripped.Exclusions.Attributes.ShouldBe(new[] { "MyExcludeAttribute" });
        roundTripped.Exclusions.Namespaces.ShouldBe(new[] { "Kept.Ns" });
    }

    [Fact]
    public void ApplyPreset_DoesNotResetLoadedPortableRid()
    {
        var session = new ProtectionSession();
        session.FromObfySettings(new ObfySettings { Packing = { Enabled = true, Rid = "portable" } });
        session.ApplyLevel(ObfuscationLevel.Aggressive);
        session.UpdateSetting("packing.enabled", true);

        Settings(session).Packing.Rid.ShouldBe("portable");
    }

    #endregion

    #region Exclusion Collection Tests

    [Fact]
    public void ExcludedNamespaces_CanAddItems()
    {
        var session = new ProtectionSession();

        session.UpdateSetting("exclusions.namespaces", new[] { "System.Security", "System.Reflection" });

        Settings(session).Exclusions.Namespaces.Count.ShouldBe(2);
    }

    [Fact]
    public void ExcludedTypes_CanRemoveItems()
    {
        var session = new ProtectionSession();
        session.UpdateSetting("exclusions.types", new[] { "TypeA", "TypeB" });

        session.UpdateSetting("exclusions.types", new[] { "TypeB" });

        var types = Settings(session).Exclusions.Types;
        types.Count.ShouldBe(1);
        types.ShouldContain("TypeB");
    }

    [Fact]
    public void ExcludedMethods_CanClear()
    {
        var session = new ProtectionSession();
        session.UpdateSetting("exclusions.methods", new[] { "Method1", "Method2" });

        session.UpdateSetting("exclusions.methods", Array.Empty<string>());

        Settings(session).Exclusions.Methods.Count.ShouldBe(0);
    }

    #endregion

    #region Available Options Tests

    [Fact]
    public void AvailableLevels_ContainsAllLevels()
    {
        var levels = new ProtectionSession().GetSnapshot().Choices.Levels;

        levels.ShouldContain("Minimal");
        levels.ShouldContain("Standard");
        levels.ShouldContain("Aggressive");
        levels.ShouldContain("Custom");
    }

    [Fact]
    public void AvailableAlgorithms_ContainsCommonAlgorithms()
    {
        var algorithms = new ProtectionSession().GetSnapshot().Choices.Algorithms;

        algorithms.ShouldContain("Aes256");
        algorithms.ShouldContain("Xor");
    }

    [Fact]
    public void AvailableNamingModes_ContainsAllModes()
    {
        new ProtectionSession().GetSnapshot().Choices.NamingModes.ShouldContain("Unreadable");
    }

    #endregion

    #region Logic defects fixed in the lift

    [Fact]
    public void Level_StaysNonCustom_WhenProfileOrWatermarkChanges()
    {
        var session = new ProtectionSession();
        session.ApplyLevel(ObfuscationLevel.Standard);

        session.UpdateSetting("runtimeProfile", "NativeAot");
        session.UpdateSetting("watermark.enabled", true);
        session.UpdateSetting("watermark.id", "customer-42");
        session.UpdateSetting("signing.enabled", true);
        session.UpdateSetting("assemblyMerge.enabled", true);
        session.UpdateSetting("dependencyEmbedding.enabled", true);
        session.UpdateSetting("inclusions.types", new[] { "OnlyThis" });
        session.UpdateSetting("exclusions.methods", new[] { "Skip" });

        session.Level.ShouldBe(ObfuscationLevel.Standard);
        session.GetSnapshot().Level.ShouldBe("Standard");
    }

    [Theory]
    [InlineData("virtualization.enabled")]
    [InlineData("packing.enabled")]
    [InlineData("incremental.enabled")]
    public void Level_FlipsToCustom_WhenAnOptionThePresetsTurnOffIsEnabled(string key)
    {
        var session = new ProtectionSession();
        session.ApplyLevel(ObfuscationLevel.Standard);

        session.UpdateSetting(key, true);

        session.Level.ShouldBe(ObfuscationLevel.Custom);
    }

    [Fact]
    public void Packing_EnabledAtAPreset_MakesTheLevelCustom_SoTheEnginePacks()
    {
        var session = new ProtectionSession();
        session.ApplyLevel(ObfuscationLevel.Standard);

        session.UpdateSetting("packing.enabled", true);

        // The engine re-applies a non-Custom level on every run and would turn packing off again.
        var settings = session.ToObfySettings();
        settings.Level.ShouldBe(ObfuscationLevel.Custom);
        settings.Packing.Enabled.ShouldBeTrue();
    }

    [Fact]
    public void Incremental_EnabledAtAPreset_MakesTheLevelCustom_SoTheEngineCaches()
    {
        var session = new ProtectionSession();
        session.ApplyLevel(ObfuscationLevel.Standard);

        session.UpdateSetting("incremental.enabled", true);

        var settings = session.ToObfySettings();
        settings.Level.ShouldBe(ObfuscationLevel.Custom);
        settings.Incremental.Enabled.ShouldBeTrue();
    }

    [Fact]
    public void CustomRestore_BringsBackPackingAndIncrementalChosenInCustom()
    {
        var session = new ProtectionSession();
        session.Update(Patch(("packing.enabled", true), ("incremental.enabled", true))); // Custom, remembered
        session.ApplyLevel(ObfuscationLevel.Minimal); // a preset turns them off
        Settings(session).Packing.Enabled.ShouldBeFalse();

        session.ApplyLevel(ObfuscationLevel.Custom);

        Settings(session).Packing.Enabled.ShouldBeTrue();
        Settings(session).Incremental.Enabled.ShouldBeTrue();
    }

    [Fact]
    public void CustomRestore_KeepsExclusions()
    {
        var session = new ProtectionSession();
        session.UpdateSetting("controlFlow.enabled", true); // Custom, remembered
        session.ApplyLevel(ObfuscationLevel.Minimal);
        session.Update(Patch(
            ("exclusions.types", new[] { "Keep.Me" }),
            ("inclusions.namespaces", new[] { "OnlyHere" }),
            ("runtimeProfile", "BlazorWasm"),
            ("watermark.enabled", true),
            ("watermark.id", "build-7"),
            ("signing.enabled", true),
            ("signing.keyFile", "my.snk"),
            ("signing.passwordEnvironmentVariable", "OBFY_PFX"),
            ("assemblyMerge.enabled", true),
            ("assemblyMerge.internalize", false),
            ("dependencyEmbedding.enabled", true),
            ("dependencyEmbedding.includePatterns", new[] { "Lib.dll" }),
            ("dependencyEmbedding.excludePatterns", new[] { "Skip.dll" })));
        session.Level.ShouldBe(ObfuscationLevel.Minimal);

        session.ApplyLevel(ObfuscationLevel.Custom);

        var settings = Settings(session);
        settings.ControlFlow.Enabled.ShouldBeTrue(); // the remembered choice is back
        settings.Exclusions.Types.ShouldBe(new[] { "Keep.Me" });
        settings.Inclusions.Namespaces.ShouldBe(new[] { "OnlyHere" });
        settings.RuntimeProfile.ShouldBe(RuntimeProfile.BlazorWasm);
        settings.Watermark.Enabled.ShouldBeTrue();
        settings.Watermark.Id.ShouldBe("build-7");
        settings.Signing.Enabled.ShouldBeTrue();
        settings.Signing.KeyFile.ShouldBe("my.snk");
        settings.Signing.PasswordEnvironmentVariable.ShouldBe("OBFY_PFX");
        settings.AssemblyMerge.Enabled.ShouldBeTrue();
        settings.AssemblyMerge.Internalize.ShouldBeFalse();
        settings.DependencyEmbedding.Enabled.ShouldBeTrue();
        settings.DependencyEmbedding.IncludePatterns.ShouldBe(new[] { "Lib.dll" });
        settings.DependencyEmbedding.ExcludePatterns.ShouldBe(new[] { "Skip.dll" });
    }

    [Theory]
    [InlineData("packing")]
    [InlineData("incremental")]
    [InlineData("virtualization")]
    public void FromObfySettings_PresetConfigWithAnOptionThePresetsTurnOff_BecomesCustom_SoTheRunHonoursIt(string option)
    {
        var settings = new ObfySettings { Level = ObfuscationLevel.Standard };
        switch (option)
        {
            case "packing": settings.Packing.Enabled = true; break;
            case "incremental": settings.Incremental.Enabled = true; break;
            default: settings.Virtualization.Enabled = true; break;
        }

        var session = new ProtectionSession();
        session.FromObfySettings(settings, @"C:\cfg\obfy.json");

        session.Level.ShouldBe(ObfuscationLevel.Custom);
        var run = session.ToObfySettings();
        run.Level.ShouldBe(ObfuscationLevel.Custom);
        run.Packing.Enabled.ShouldBe(option == "packing");
        run.Incremental.Enabled.ShouldBe(option == "incremental");
        run.Virtualization.Enabled.ShouldBe(option == "virtualization");
        session.GetSnapshot().Dirty.ShouldBeFalse();
    }

    [Fact]
    public void FromObfySettings_PresetConfigWithoutThoseOptions_KeepsItsLevel()
    {
        var session = new ProtectionSession();

        session.FromObfySettings(new ObfySettings { Level = ObfuscationLevel.Aggressive });

        session.Level.ShouldBe(ObfuscationLevel.Aggressive);
    }

    [Fact]
    public void FromObfySettings_PromotedToCustom_RestoresItsChoicesAfterAPreset()
    {
        var session = new ProtectionSession();
        session.FromObfySettings(new ObfySettings { Level = ObfuscationLevel.Standard, Packing = { Enabled = true } });

        session.ApplyLevel(ObfuscationLevel.Minimal);
        Settings(session).Packing.Enabled.ShouldBeFalse();
        session.ApplyLevel(ObfuscationLevel.Custom);

        Settings(session).Packing.Enabled.ShouldBeTrue();
    }

    [Fact]
    public void CustomRestore_KeepsThePackingTargetChosenBeforeEnteringCustom()
    {
        var session = new ProtectionSession();
        session.UpdateSetting("controlFlow.enabled", true); // Custom, remembered with the default rid
        session.ApplyLevel(ObfuscationLevel.Minimal);
        session.UpdateSetting("packing.rid", "portable");
        session.Level.ShouldBe(ObfuscationLevel.Minimal); // the target does not flip the level

        session.ApplyLevel(ObfuscationLevel.Custom);

        Settings(session).Packing.Rid.ShouldBe("portable");
    }

    [Fact]
    public void Watermark_On_EmptyId_ProducesValidationError()
    {
        var session = new ProtectionSession();
        session.GetSnapshot().Errors.ShouldBeEmpty();

        session.UpdateSetting("watermark.enabled", true);

        session.GetSnapshot().Errors.ShouldContain(e => e.Field == "watermark.id");

        session.UpdateSetting("watermark.id", "customer-42");

        session.GetSnapshot().Errors.ShouldBeEmpty();
    }

    #endregion

    #region Patch, event and snapshot behaviour

    [Fact]
    public void Update_UnknownKey_ThrowsAndChangesNothing()
    {
        var session = new ProtectionSession();

        var ex = Should.Throw<ObfyApiException>(() =>
            session.Update(Patch(("controlFlow.enabled", true), ("controlFlow.nonsense", 1))));

        ex.Code.ShouldBe("settings.unknownField");
        Settings(session).ControlFlow.Enabled.ShouldBeFalse();
        session.Level.ShouldBe(ObfuscationLevel.Standard);
    }

    [Fact]
    public void Update_BadValue_ThrowsAndChangesNothing()
    {
        var session = new ProtectionSession();

        var ex = Should.Throw<ObfyApiException>(() =>
            session.Update(Patch(("controlFlow.enabled", true), ("controlFlow.mode", "Sideways"))));

        ex.Code.ShouldBe("settings.invalidValue");
        Settings(session).ControlFlow.Enabled.ShouldBeFalse();
    }

    [Fact]
    public void Update_WholeNumbersAreClampedToTheirRange()
    {
        var session = new ProtectionSession();

        session.UpdateSetting("controlFlow.intensity", 500);
        session.UpdateSetting("stringEncryption.minStringLength", 0);

        Settings(session).ControlFlow.Intensity.ShouldBe(100);
        Settings(session).StringEncryption.MinStringLength.ShouldBe(1);
    }

    [Fact]
    public void Changed_IsRaisedOncePerMutation_AndNotForANoOp()
    {
        var session = new ProtectionSession();
        var raised = 0;
        session.Changed += () => raised++;

        session.Update(Patch(("controlFlow.enabled", true), ("controlFlow.intensity", 70)));
        raised.ShouldBe(1);

        session.UpdateSetting("controlFlow.enabled", true); // already true
        raised.ShouldBe(1);

        session.ApplyLevel(ObfuscationLevel.Minimal);
        raised.ShouldBe(2);
        session.FromObfySettings(new ObfySettings());
        raised.ShouldBe(3);
    }

    [Fact]
    public void ProxyExternalCalls_TurnsTheProxyOn_AndTurningTheProxyOffClearsIt()
    {
        var session = new ProtectionSession();

        session.UpdateSetting("referenceProxy.proxyExternalCalls", true);
        Settings(session).Protection.ReferenceProxy.ShouldBeTrue();

        session.UpdateSetting("referenceProxy.enabled", false);
        Settings(session).Protection.ProxyExternalCalls.ShouldBeFalse();
    }

    [Fact]
    public void Snapshot_ProfileGatesIlProtections_ButKeepsStoredValues()
    {
        var session = new ProtectionSession();
        session.ApplyLevel(ObfuscationLevel.Aggressive);

        session.UpdateSetting("runtimeProfile", "NativeAot");

        var snapshot = session.GetSnapshot();
        snapshot.Level.ShouldBe("Aggressive");
        foreach (var id in new[] { "methodEncryption", "antiDump", "dependencyEmbedding", "virtualization" })
        {
            var technique = snapshot.Techniques.Single(t => t.Id == id);
            technique.Available.ShouldBeFalse(id);
            technique.UnavailableReason.ShouldBe("Not available for NativeAOT");
        }

        snapshot.Techniques.Single(t => t.Id == "methodEncryption").Enabled.ShouldBeTrue(); // stored value kept
        var antiDebug = snapshot.Techniques.Single(t => t.Id == "antiDebug");
        antiDebug.Available.ShouldBeTrue();
        antiDebug.UnavailableReason.ShouldBeNull();

        session.UpdateSetting("runtimeProfile", "Default");
        session.GetSnapshot().Techniques.Single(t => t.Id == "antiDump").Available.ShouldBeTrue();
    }

    [Fact]
    public void Snapshot_OptionsCarryEveryValueOfATechnique()
    {
        var session = new ProtectionSession();
        session.UpdateSetting("constantEncryption.integerThreshold", 9);

        var options = session.GetSnapshot().Techniques.Single(t => t.Id == "constantEncryption").Options;

        options["integerThreshold"].ShouldBe(9);
        options["algorithm"].ShouldBe("Xor");
        options.Keys.ShouldBe(
            new[] { "algorithm", "encryptIntegers", "encryptLongs", "encryptFloats", "encryptDoubles", "integerThreshold", "skipCommonFloats", "skipCommonDoubles" },
            ignoreOrder: true);
    }

    [Fact]
    public void Snapshot_DiffersFromLevel_MarksTechniquesChangedFromThePreset()
    {
        var session = new ProtectionSession();
        session.GetSnapshot().Techniques.ShouldAllBe(t => !t.DiffersFromLevel);

        session.UpdateSetting("controlFlow.enabled", true);

        var snapshot = session.GetSnapshot();
        snapshot.Level.ShouldBe("Custom");
        snapshot.Techniques.Single(t => t.Id == "controlFlow").DiffersFromLevel.ShouldBeTrue();
        snapshot.Techniques.Single(t => t.Id == "stringEncryption").DiffersFromLevel.ShouldBeFalse();

        session.ApplyLevel(ObfuscationLevel.Standard);
        session.GetSnapshot().Techniques.ShouldAllBe(t => !t.DiffersFromLevel);
    }

    [Fact]
    public void Snapshot_TracksConfigPathAndDirtyFlag()
    {
        var session = new ProtectionSession();
        var fresh = session.GetSnapshot();
        fresh.ConfigPath.ShouldBeNull();
        fresh.Dirty.ShouldBeFalse();

        session.FromObfySettings(new ObfySettings(), @"C:\cfg\obfy.json");
        session.GetSnapshot().ConfigPath.ShouldBe(@"C:\cfg\obfy.json");
        session.GetSnapshot().Dirty.ShouldBeFalse();

        session.UpdateSetting("exclusions.types", new[] { "X" });
        session.GetSnapshot().Dirty.ShouldBeTrue();

        session.MarkSaved(@"C:\cfg\other.json");
        session.GetSnapshot().ConfigPath.ShouldBe(@"C:\cfg\other.json");
        session.GetSnapshot().Dirty.ShouldBeFalse();
    }

    [Fact]
    public void Snapshot_SigningErrors_NameTheFieldAtFault()
    {
        var session = new ProtectionSession();

        session.UpdateSetting("signing.enabled", true);
        session.GetSnapshot().Errors.Select(e => e.Field).ShouldBe(new[] { "signing.keyFile" });

        session.UpdateSetting("signing.keyFile", @"C:\no\such\key.pfx");
        session.GetSnapshot().Errors.Select(e => e.Field)
            .ShouldBe(new[] { "signing.keyFile", "signing.passwordEnvironmentVariable" });

        session.UpdateSetting("signing.enabled", false);
        session.GetSnapshot().Errors.ShouldBeEmpty();
    }

    [Fact]
    public void Snapshot_SerializesWithCamelCaseNamesAndStringEnums()
    {
        var session = new ProtectionSession();
        session.UpdateSetting("runtimeProfile", "UnityIl2Cpp");

        var json = JsonUtils.Serialize(session.GetSnapshot());

        json.ShouldContain("\"level\":\"Standard\"");
        json.ShouldContain("\"runtimeProfile\":\"UnityIl2Cpp\"");
        json.ShouldContain("\"id\":\"stringEncryption\"");
        json.ShouldContain("\"unavailableReason\":\"Not available for Unity IL2CPP\"");
        json.ShouldContain("\"minStringLength\":3");
        json.ShouldContain("\"differsFromLevel\":false");
    }

    #endregion

    #region Events and revision

    private sealed class RecordingPublisher : Obfy.App.Services.IEventPublisher
    {
        public List<(string Name, object? Data)> Published { get; } = [];
        public void Publish(string eventName, object? data) => Published.Add((eventName, data));
    }

    [Fact]
    public void EveryChange_PublishesSettingsChangedWithTheNewSnapshot()
    {
        var events = new RecordingPublisher();
        var session = new ProtectionSession(events);

        session.UpdateSetting("controlFlow.enabled", true);
        session.UpdateSetting("controlFlow.enabled", true); // no-op
        session.ApplyLevel(ObfuscationLevel.Minimal);
        session.FromObfySettings(new ObfySettings(), @"C:\cfg.json");
        session.MarkSaved(@"C:\cfg.json");

        events.Published.Select(p => p.Name).ShouldAllBe(n => n == AppEvents.SettingsChanged);
        events.Published.Count.ShouldBe(4);
        var first = events.Published[0].Data.ShouldBeOfType<Obfy.App.Models.SettingsSnapshot>();
        first.Level.ShouldBe("Custom");
        first.Dirty.ShouldBeTrue();
    }

    [Fact]
    public void Revision_RisesWithEveryChange_AndNotForANoOp()
    {
        var session = new ProtectionSession();
        var r0 = session.GetSnapshot().Revision;

        session.UpdateSetting("controlFlow.enabled", true);
        var r1 = session.GetSnapshot().Revision;
        session.UpdateSetting("controlFlow.enabled", true);
        var r2 = session.GetSnapshot().Revision;
        session.ApplyLevel(ObfuscationLevel.Aggressive);
        var r3 = session.GetSnapshot().Revision;

        r1.ShouldBeGreaterThan(r0);
        r2.ShouldBe(r1);
        r3.ShouldBeGreaterThan(r2);
    }

    #endregion
}
