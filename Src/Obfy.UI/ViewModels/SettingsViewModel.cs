using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Obfy.Core.Models;

namespace Obfy.UI.ViewModels;

/// <summary>
/// ViewModel for the settings panel.
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    // Level selection
    [ObservableProperty]
    private ObfuscationLevel _level = ObfuscationLevel.Standard;

    // String Encryption
    [ObservableProperty]
    private bool _stringEncryptionEnabled = true;

    [ObservableProperty]
    private EncryptionAlgorithm _stringEncryptionAlgorithm = EncryptionAlgorithm.Aes256;

    [ObservableProperty]
    private int _minStringLength = 3;

    // Control Flow
    [ObservableProperty]
    private bool _controlFlowEnabled = true;

    [ObservableProperty]
    private ControlFlowMode _controlFlowMode = ControlFlowMode.Switch;

    [ObservableProperty]
    private int _controlFlowIntensity = 50;

    // Symbol Renaming
    [ObservableProperty]
    private bool _symbolRenamingEnabled = true;

    [ObservableProperty]
    private NamingMode _namingMode = NamingMode.Unreadable;

    [ObservableProperty]
    private bool _renameTypes = true;

    [ObservableProperty]
    private bool _renameMethods = true;

    [ObservableProperty]
    private bool _renameFields = true;

    [ObservableProperty]
    private bool _renameProperties = true;

    [ObservableProperty]
    private bool _renameParameters = true;

    [ObservableProperty]
    private bool _preservePublicApi = false;

    // Protection
    [ObservableProperty]
    private bool _antiDebugEnabled = false;

    [ObservableProperty]
    private bool _antiTamperEnabled = false;

    [ObservableProperty]
    private bool _tamperCheckEntryPoint = true;

    [ObservableProperty]
    private bool _tamperCheckModuleInit = true;

    [ObservableProperty]
    private bool _antiDumpEnabled = false;

    // Metadata
    [ObservableProperty]
    private bool _removeDebugInfo = true;

    [ObservableProperty]
    private bool _removeAttributes = true;

    [ObservableProperty]
    private bool _stripDocumentation = true;

    // Resource Encryption
    [ObservableProperty]
    private bool _resourceEncryptionEnabled = false;

    [ObservableProperty]
    private EncryptionAlgorithm _resourceAlgorithm = EncryptionAlgorithm.Aes256;

    // Constant Encryption
    [ObservableProperty]
    private bool _constantEncryptionEnabled = false;

    [ObservableProperty]
    private EncryptionAlgorithm _constantEncryptionAlgorithm = EncryptionAlgorithm.Xor;

    [ObservableProperty]
    private bool _encryptIntegers = true;

    [ObservableProperty]
    private bool _encryptLongs = true;

    [ObservableProperty]
    private bool _encryptFloats = true;

    [ObservableProperty]
    private bool _encryptDoubles = true;

    [ObservableProperty]
    private int _integerThreshold = 2;

    [ObservableProperty]
    private bool _skipCommonFloats = true;

    [ObservableProperty]
    private bool _skipCommonDoubles = true;

    // Exclusions
    public ObservableCollection<string> ExcludedNamespaces { get; } = new();
    public ObservableCollection<string> ExcludedTypes { get; } = new();
    public ObservableCollection<string> ExcludedMethods { get; } = new();

    // Available options for dropdowns
    public static ObfuscationLevel[] AvailableLevels { get; } = Enum.GetValues<ObfuscationLevel>();
    public static EncryptionAlgorithm[] AvailableAlgorithms { get; } = Enum.GetValues<EncryptionAlgorithm>();
    public static ControlFlowMode[] AvailableModes { get; } = Enum.GetValues<ControlFlowMode>();
    public static NamingMode[] AvailableNamingModes { get; } = Enum.GetValues<NamingMode>();

    partial void OnLevelChanged(ObfuscationLevel value)
    {
        if (value != ObfuscationLevel.Custom)
        {
            ApplyPreset(value);
        }
    }

    /// <summary>
    /// Applies a preset configuration based on the obfuscation level.
    /// </summary>
    public void ApplyPreset(ObfuscationLevel level)
    {
        switch (level)
        {
            case ObfuscationLevel.Minimal:
                StringEncryptionEnabled = false;
                ControlFlowEnabled = false;
                SymbolRenamingEnabled = true;
                AntiDebugEnabled = false;
                RemoveDebugInfo = true;
                ResourceEncryptionEnabled = false;
                ConstantEncryptionEnabled = false;
                break;

            case ObfuscationLevel.Standard:
                StringEncryptionEnabled = true;
                ControlFlowEnabled = false;
                SymbolRenamingEnabled = true;
                AntiDebugEnabled = false;
                RemoveDebugInfo = true;
                ResourceEncryptionEnabled = false;
                ConstantEncryptionEnabled = false;
                break;

            case ObfuscationLevel.Aggressive:
                StringEncryptionEnabled = true;
                ControlFlowEnabled = true;
                ControlFlowIntensity = 80;
                SymbolRenamingEnabled = true;
                AntiDebugEnabled = true;
                AntiTamperEnabled = true;
                RemoveDebugInfo = true;
                RemoveAttributes = true;
                ResourceEncryptionEnabled = true;
                ConstantEncryptionEnabled = true;
                ConstantEncryptionAlgorithm = EncryptionAlgorithm.Xor;
                break;
        }
    }

    /// <summary>
    /// Converts the ViewModel settings to an ObfySettings instance.
    /// </summary>
    public ObfySettings ToObfySettings()
    {
        return new ObfySettings
        {
            Level = Level,
            StringEncryption = new StringEncryptionSettings
            {
                Enabled = StringEncryptionEnabled,
                Algorithm = StringEncryptionAlgorithm,
                MinStringLength = MinStringLength
            },
            ControlFlow = new ControlFlowSettings
            {
                Enabled = ControlFlowEnabled,
                Mode = ControlFlowMode,
                Intensity = ControlFlowIntensity
            },
            SymbolRenaming = new SymbolRenamingSettings
            {
                Enabled = SymbolRenamingEnabled,
                Mode = NamingMode,
                RenameTypes = RenameTypes,
                RenameMethods = RenameMethods,
                RenameFields = RenameFields,
                RenameProperties = RenameProperties,
                RenameParameters = RenameParameters,
                PreservePublicApi = PreservePublicApi
            },
            Protection = new ProtectionSettings
            {
                AntiDebug = AntiDebugEnabled,
                AntiTamper = new AntiTamperSettings
                {
                    Enabled = AntiTamperEnabled,
                    CheckEntryPoint = TamperCheckEntryPoint,
                    CheckModuleInitializer = TamperCheckModuleInit
                },
                AntiDump = AntiDumpEnabled
            },
            Metadata = new MetadataSettings
            {
                RemoveDebugInfo = RemoveDebugInfo,
                RemoveAttributes = RemoveAttributes,
                StripDocumentation = StripDocumentation
            },
            ResourceEncryption = new ResourceEncryptionSettings
            {
                Enabled = ResourceEncryptionEnabled,
                Algorithm = ResourceAlgorithm
            },
            ConstantEncryption = new ConstantEncryptionSettings
            {
                Enabled = ConstantEncryptionEnabled,
                Algorithm = ConstantEncryptionAlgorithm,
                EncryptIntegers = EncryptIntegers,
                EncryptLongs = EncryptLongs,
                EncryptFloats = EncryptFloats,
                EncryptDoubles = EncryptDoubles,
                IntegerThreshold = IntegerThreshold,
                SkipCommonFloats = SkipCommonFloats,
                SkipCommonDoubles = SkipCommonDoubles
            },
            Exclusions = new ExclusionRules
            {
                Namespaces = ExcludedNamespaces.ToList(),
                Types = ExcludedTypes.ToList(),
                Methods = ExcludedMethods.ToList()
            }
        };
    }

    /// <summary>
    /// Loads settings from an ObfySettings instance.
    /// </summary>
    public void FromObfySettings(ObfySettings settings)
    {
        Level = settings.Level;

        // String Encryption
        StringEncryptionEnabled = settings.StringEncryption.Enabled;
        StringEncryptionAlgorithm = settings.StringEncryption.Algorithm;
        MinStringLength = settings.StringEncryption.MinStringLength;

        // Control Flow
        ControlFlowEnabled = settings.ControlFlow.Enabled;
        ControlFlowMode = settings.ControlFlow.Mode;
        ControlFlowIntensity = settings.ControlFlow.Intensity;

        // Symbol Renaming
        SymbolRenamingEnabled = settings.SymbolRenaming.Enabled;
        NamingMode = settings.SymbolRenaming.Mode;
        RenameTypes = settings.SymbolRenaming.RenameTypes;
        RenameMethods = settings.SymbolRenaming.RenameMethods;
        RenameFields = settings.SymbolRenaming.RenameFields;
        RenameProperties = settings.SymbolRenaming.RenameProperties;
        RenameParameters = settings.SymbolRenaming.RenameParameters;
        PreservePublicApi = settings.SymbolRenaming.PreservePublicApi;

        // Protection
        AntiDebugEnabled = settings.Protection.AntiDebug;
        AntiTamperEnabled = settings.Protection.AntiTamper.Enabled;
        TamperCheckEntryPoint = settings.Protection.AntiTamper.CheckEntryPoint;
        TamperCheckModuleInit = settings.Protection.AntiTamper.CheckModuleInitializer;
        AntiDumpEnabled = settings.Protection.AntiDump;

        // Metadata
        RemoveDebugInfo = settings.Metadata.RemoveDebugInfo;
        RemoveAttributes = settings.Metadata.RemoveAttributes;
        StripDocumentation = settings.Metadata.StripDocumentation;

        // Resource Encryption
        ResourceEncryptionEnabled = settings.ResourceEncryption.Enabled;
        ResourceAlgorithm = settings.ResourceEncryption.Algorithm;

        // Constant Encryption
        ConstantEncryptionEnabled = settings.ConstantEncryption.Enabled;
        ConstantEncryptionAlgorithm = settings.ConstantEncryption.Algorithm;
        EncryptIntegers = settings.ConstantEncryption.EncryptIntegers;
        EncryptLongs = settings.ConstantEncryption.EncryptLongs;
        EncryptFloats = settings.ConstantEncryption.EncryptFloats;
        EncryptDoubles = settings.ConstantEncryption.EncryptDoubles;
        IntegerThreshold = settings.ConstantEncryption.IntegerThreshold;
        SkipCommonFloats = settings.ConstantEncryption.SkipCommonFloats;
        SkipCommonDoubles = settings.ConstantEncryption.SkipCommonDoubles;

        // Exclusions
        ExcludedNamespaces.Clear();
        foreach (var ns in settings.Exclusions.Namespaces)
        {
            ExcludedNamespaces.Add(ns);
        }

        ExcludedTypes.Clear();
        foreach (var type in settings.Exclusions.Types)
        {
            ExcludedTypes.Add(type);
        }

        ExcludedMethods.Clear();
        foreach (var method in settings.Exclusions.Methods)
        {
            ExcludedMethods.Add(method);
        }
    }
}
