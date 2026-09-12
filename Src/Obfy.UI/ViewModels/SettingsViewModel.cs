using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
    private bool _controlFlowEnabled = false;

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
    private bool _renameEvents = true;

    [ObservableProperty]
    private bool _renameNamespaces = true;

    [ObservableProperty]
    private bool _preservePublicApi = false;

    [ObservableProperty]
    private bool _preserveXaml = false;

    [ObservableProperty]
    private RuntimeProfile _runtimeProfile = RuntimeProfile.Default;

    [ObservableProperty]
    private bool _signingEnabled = false;

    [ObservableProperty]
    private bool _packingEnabled = false;

    [ObservableProperty]
    private string _signingKeyFile = string.Empty;

    [ObservableProperty]
    private string _signingPasswordEnvironmentVariable = string.Empty;

    [ObservableProperty]
    private bool _referenceProxyEnabled = false;

    [ObservableProperty]
    private bool _proxyExternalCalls = false;

    [ObservableProperty]
    private bool _methodEncryptionEnabled = false;

    [ObservableProperty]
    private bool _dependencyEmbeddingEnabled = false;

    private List<string>? _dependencyEmbeddingIncludes;
    private List<string>? _dependencyEmbeddingExcludes;

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

    // Anti-Decompiler
    [ObservableProperty]
    private bool _antiDecompilerEnabled = false;

    [ObservableProperty]
    private bool _injectJunkTypes = true;

    [ObservableProperty]
    private bool _addSuppressIldasmAttribute = true;

    [ObservableProperty]
    private int _junkTypeCount = 5;

    [ObservableProperty]
    private int _junkMethodsPerType = 3;

    [ObservableProperty]
    private bool _addDecoyAttributes = true;

    [ObservableProperty]
    private bool _watermarkEnabled;

    [ObservableProperty]
    private string _watermarkId = string.Empty;

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

    // Assembly Merge
    [ObservableProperty]
    private bool _assemblyMergeEnabled = false;

    [ObservableProperty]
    private bool _internalizeMergedTypes = true;

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

    public ObservableCollection<string> IncludedNamespaces { get; } = new();
    public ObservableCollection<string> IncludedTypes { get; } = new();
    public ObservableCollection<string> IncludedMethods { get; } = new();

    [ObservableProperty]
    private string _newExcludedNamespace = string.Empty;

    [ObservableProperty]
    private string _newExcludedType = string.Empty;

    [ObservableProperty]
    private string _newExcludedMethod = string.Empty;

    [RelayCommand]
    private void AddExcludedNamespace() => AddUnique(ExcludedNamespaces, NewExcludedNamespace, v => NewExcludedNamespace = v);

    [RelayCommand]
    private void AddExcludedType() => AddUnique(ExcludedTypes, NewExcludedType, v => NewExcludedType = v);

    [RelayCommand]
    private void AddExcludedMethod() => AddUnique(ExcludedMethods, NewExcludedMethod, v => NewExcludedMethod = v);

    [RelayCommand]
    private void RemoveExcludedNamespace(string? value)
    {
        if (!string.IsNullOrEmpty(value))
            ExcludedNamespaces.Remove(value);
    }

    [RelayCommand]
    private void RemoveExcludedType(string? value)
    {
        if (!string.IsNullOrEmpty(value))
            ExcludedTypes.Remove(value);
    }

    [RelayCommand]
    private void RemoveExcludedMethod(string? value)
    {
        if (!string.IsNullOrEmpty(value))
            ExcludedMethods.Remove(value);
    }

    private static void AddUnique(ObservableCollection<string> items, string raw, Action<string> clear)
    {
        var value = raw.Trim();
        if (value.Length == 0 || items.Contains(value))
            return;
        items.Add(value);
        clear(string.Empty);
    }

    // Available options for dropdowns
    public static ObfuscationLevel[] AvailableLevels { get; } = Enum.GetValues<ObfuscationLevel>();
    public static EncryptionAlgorithm[] AvailableAlgorithms { get; } = Enum.GetValues<EncryptionAlgorithm>();
    public static ControlFlowMode[] AvailableModes { get; } = Enum.GetValues<ControlFlowMode>();
    public static NamingMode[] AvailableNamingModes { get; } = Enum.GetValues<NamingMode>();
    public static RuntimeProfile[] AvailableRuntimeProfiles { get; } = Enum.GetValues<RuntimeProfile>();

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
    private int _applyingPreset;

    public void ApplyPreset(ObfuscationLevel level)
    {
        _applyingPreset++;
        try
        {
            switch (level)
            {
                case ObfuscationLevel.Minimal:
                    StringEncryptionEnabled = false;
                    ControlFlowEnabled = false;
                    ControlFlowIntensity = 50;
                    SymbolRenamingEnabled = true;
                    AntiDebugEnabled = false;
                    AntiTamperEnabled = false;
                    AntiDecompilerEnabled = false;
                    AntiDumpEnabled = false;
                    ReferenceProxyEnabled = false;
                    ProxyExternalCalls = false;
                    MethodEncryptionEnabled = false;
                    RemoveDebugInfo = true;
                    RemoveAttributes = false;
                    ResourceEncryptionEnabled = false;
                    ConstantEncryptionEnabled = false;
                    ConstantEncryptionAlgorithm = EncryptionAlgorithm.Xor;
                    break;

                case ObfuscationLevel.Standard:
                    StringEncryptionEnabled = true;
                    ControlFlowEnabled = false;
                    ControlFlowIntensity = 50;
                    SymbolRenamingEnabled = true;
                    AntiDebugEnabled = false;
                    AntiTamperEnabled = false;
                    AntiDecompilerEnabled = false;
                    AntiDumpEnabled = false;
                    ReferenceProxyEnabled = false;
                    ProxyExternalCalls = false;
                    MethodEncryptionEnabled = false;
                    RemoveDebugInfo = true;
                    RemoveAttributes = true;
                    ResourceEncryptionEnabled = false;
                    ConstantEncryptionEnabled = false;
                    ConstantEncryptionAlgorithm = EncryptionAlgorithm.Xor;
                    break;

                case ObfuscationLevel.Aggressive:
                    StringEncryptionEnabled = true;
                    ControlFlowEnabled = true;
                    ControlFlowIntensity = 80;
                    SymbolRenamingEnabled = true;
                    AntiDebugEnabled = true;
                    AntiTamperEnabled = true;
                    AntiDecompilerEnabled = true;
                    AntiDumpEnabled = true;
                    ReferenceProxyEnabled = true;
                    MethodEncryptionEnabled = true;
                    RemoveDebugInfo = true;
                    RemoveAttributes = true;
                    ResourceEncryptionEnabled = true;
                    ConstantEncryptionEnabled = true;
                    ConstantEncryptionAlgorithm = EncryptionAlgorithm.Xor;
                    break;
            }
        }
        finally
        {
            _applyingPreset--;
        }
    }

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (_applyingPreset > 0)
            return;
        if (e.PropertyName is null or nameof(Level))
            return;
        if (Level != ObfuscationLevel.Custom)
            Level = ObfuscationLevel.Custom;
    }

    partial void OnProxyExternalCallsChanged(bool value)
    {
        if (_applyingPreset > 0)
            return;
        if (value)
            ReferenceProxyEnabled = true;
    }

    partial void OnReferenceProxyEnabledChanged(bool value)
    {
        if (_applyingPreset > 0)
            return;
        if (!value)
            ProxyExternalCalls = false;
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
                RenameEvents = RenameEvents,
                RenameNamespaces = RenameNamespaces,
                PreservePublicApi = PreservePublicApi,
                PreserveXaml = PreserveXaml
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
                AntiDecompiler = new AntiDecompilerSettings
                {
                    Enabled = AntiDecompilerEnabled,
                    InjectJunkTypes = InjectJunkTypes,
                    AddSuppressIldasmAttribute = AddSuppressIldasmAttribute,
                    AddDecoyAttributes = AddDecoyAttributes,
                    JunkTypeCount = JunkTypeCount,
                    JunkMethodsPerType = JunkMethodsPerType
                },
                AntiDump = AntiDumpEnabled,
                ReferenceProxy = ReferenceProxyEnabled,
                ProxyExternalCalls = ProxyExternalCalls,
                MethodEncryption = MethodEncryptionEnabled
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
            AssemblyMerge = new AssemblyMergeSettings
            {
                Enabled = AssemblyMergeEnabled,
                Internalize = InternalizeMergedTypes
            },
            DependencyEmbedding = new DependencyEmbeddingSettings
            {
                Enabled = DependencyEmbeddingEnabled,
                IncludePatterns = _dependencyEmbeddingIncludes ?? new List<string> { "*.dll" },
                ExcludePatterns = _dependencyEmbeddingExcludes ?? new List<string> { "*.resources.dll" }
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
                Methods = ExcludedMethods.ToList(),
                Attributes = new List<string>(new ExclusionRules().Attributes)
            },
            Inclusions = new InclusionRules
            {
                Namespaces = IncludedNamespaces.ToList(),
                Types = IncludedTypes.ToList(),
                Methods = IncludedMethods.ToList()
            },
            Watermark = new WatermarkSettings
            {
                Enabled = WatermarkEnabled,
                Id = WatermarkId ?? string.Empty
            },
            RuntimeProfile = this.RuntimeProfile,
            Signing = new SigningSettings
            {
                Enabled = SigningEnabled,
                KeyFile = string.IsNullOrWhiteSpace(SigningKeyFile) ? null : SigningKeyFile,
                PasswordEnvironmentVariable = string.IsNullOrWhiteSpace(SigningPasswordEnvironmentVariable)
                    ? null
                    : SigningPasswordEnvironmentVariable
            },
            Packing = new PackingSettings { Enabled = PackingEnabled }
        };
    }

    /// <summary>
    /// Loads settings from an ObfySettings instance.
    /// </summary>
    public void FromObfySettings(ObfySettings settings)
    {
        _applyingPreset++;
        try
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
        RenameEvents = settings.SymbolRenaming.RenameEvents;
        RenameNamespaces = settings.SymbolRenaming.RenameNamespaces;
        PreservePublicApi = settings.SymbolRenaming.PreservePublicApi;
        PreserveXaml = settings.SymbolRenaming.PreserveXaml;

        // Protection
        AntiDebugEnabled = settings.Protection.AntiDebug;
        AntiTamperEnabled = settings.Protection.AntiTamper.Enabled;
        TamperCheckEntryPoint = settings.Protection.AntiTamper.CheckEntryPoint;
        TamperCheckModuleInit = settings.Protection.AntiTamper.CheckModuleInitializer;
        AntiDecompilerEnabled = settings.Protection.AntiDecompiler.Enabled;
        InjectJunkTypes = settings.Protection.AntiDecompiler.InjectJunkTypes;
        AddSuppressIldasmAttribute = settings.Protection.AntiDecompiler.AddSuppressIldasmAttribute;
        AddDecoyAttributes = settings.Protection.AntiDecompiler.AddDecoyAttributes;
        JunkTypeCount = settings.Protection.AntiDecompiler.JunkTypeCount;
        JunkMethodsPerType = settings.Protection.AntiDecompiler.JunkMethodsPerType;
        AntiDumpEnabled = settings.Protection.AntiDump;
        MethodEncryptionEnabled = settings.Protection.MethodEncryption;
        ReferenceProxyEnabled = settings.Protection.ReferenceProxy;
        ProxyExternalCalls = settings.Protection.ProxyExternalCalls;

        // Metadata
        RemoveDebugInfo = settings.Metadata.RemoveDebugInfo;
        RemoveAttributes = settings.Metadata.RemoveAttributes;
        StripDocumentation = settings.Metadata.StripDocumentation;

        // Resource Encryption
        ResourceEncryptionEnabled = settings.ResourceEncryption.Enabled;
        ResourceAlgorithm = settings.ResourceEncryption.Algorithm;

        // Assembly Merge
        AssemblyMergeEnabled = settings.AssemblyMerge.Enabled;
        InternalizeMergedTypes = settings.AssemblyMerge.Internalize;
        DependencyEmbeddingEnabled = settings.DependencyEmbedding.Enabled;
        _dependencyEmbeddingIncludes = settings.DependencyEmbedding.IncludePatterns?.ToList();
        _dependencyEmbeddingExcludes = settings.DependencyEmbedding.ExcludePatterns?.ToList();

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

        IncludedNamespaces.Clear();
        foreach (var ns in settings.Inclusions.Namespaces)
            IncludedNamespaces.Add(ns);
        IncludedTypes.Clear();
        foreach (var type in settings.Inclusions.Types)
            IncludedTypes.Add(type);
        IncludedMethods.Clear();
        foreach (var method in settings.Inclusions.Methods)
            IncludedMethods.Add(method);

        WatermarkEnabled = settings.Watermark.Enabled;
        WatermarkId = settings.Watermark.Id ?? string.Empty;

        RuntimeProfile = settings.RuntimeProfile;
        SigningEnabled = settings.Signing.Enabled;
        SigningKeyFile = settings.Signing.KeyFile ?? string.Empty;
        SigningPasswordEnvironmentVariable = settings.Signing.PasswordEnvironmentVariable ?? string.Empty;
        PackingEnabled = settings.Packing.Enabled;
        }
        finally
        {
            _applyingPreset--;
        }
    }
}
