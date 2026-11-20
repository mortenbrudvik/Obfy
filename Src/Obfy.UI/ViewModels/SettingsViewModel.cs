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

    private string _packingRid = "win-x64";

    [ObservableProperty]
    private bool _virtualizationEnabled = false;

    [ObservableProperty]
    private bool _incrementalEnabled = false;

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

    /// <summary>
    /// Settings last loaded from disk. <see cref="ToObfySettings"/> copies this and
    /// overlays the panel fields, so values the panel does not edit survive Obfuscate and Save.
    /// </summary>
    private ObfySettings? _loadedSettings;

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
                    ControlFlowMode = ControlFlowMode.Switch;
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
                    VirtualizationEnabled = false;
                    IncrementalEnabled = false;
                    PackingEnabled = false;
                    break;

                case ObfuscationLevel.Standard:
                    StringEncryptionEnabled = true;
                    ControlFlowEnabled = false;
                    ControlFlowMode = ControlFlowMode.Switch;
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
                    VirtualizationEnabled = false;
                    IncrementalEnabled = false;
                    PackingEnabled = false;
                    break;

                case ObfuscationLevel.Aggressive:
                    StringEncryptionEnabled = true;
                    ControlFlowEnabled = true;
                    ControlFlowMode = ControlFlowMode.Switch;
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
                    VirtualizationEnabled = false;
                    IncrementalEnabled = false;
                    PackingEnabled = false;
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
    /// Starts from the loaded configuration when one exists, then writes the panel fields
    /// onto that copy. Fields the panel does not edit stay as loaded.
    /// </summary>
    public ObfySettings ToObfySettings()
    {
        var settings = _loadedSettings?.Clone() ?? new ObfySettings();
        settings.Level = Level;

        settings.StringEncryption.Enabled = StringEncryptionEnabled;
        settings.StringEncryption.Algorithm = StringEncryptionAlgorithm;
        settings.StringEncryption.MinStringLength = MinStringLength;

        settings.ControlFlow.Enabled = ControlFlowEnabled;
        settings.ControlFlow.Mode = ControlFlowMode;
        settings.ControlFlow.Intensity = ControlFlowIntensity;

        settings.SymbolRenaming.Enabled = SymbolRenamingEnabled;
        settings.SymbolRenaming.Mode = NamingMode;
        settings.SymbolRenaming.RenameTypes = RenameTypes;
        settings.SymbolRenaming.RenameMethods = RenameMethods;
        settings.SymbolRenaming.RenameFields = RenameFields;
        settings.SymbolRenaming.RenameProperties = RenameProperties;
        settings.SymbolRenaming.RenameParameters = RenameParameters;
        settings.SymbolRenaming.RenameEvents = RenameEvents;
        settings.SymbolRenaming.RenameNamespaces = RenameNamespaces;
        settings.SymbolRenaming.PreservePublicApi = PreservePublicApi;
        settings.SymbolRenaming.PreserveXaml = PreserveXaml;

        settings.Protection.AntiDebug = AntiDebugEnabled;
        settings.Protection.AntiTamper.Enabled = AntiTamperEnabled;
        settings.Protection.AntiTamper.CheckEntryPoint = TamperCheckEntryPoint;
        settings.Protection.AntiTamper.CheckModuleInitializer = TamperCheckModuleInit;
        settings.Protection.AntiDecompiler.Enabled = AntiDecompilerEnabled;
        settings.Protection.AntiDecompiler.InjectJunkTypes = InjectJunkTypes;
        settings.Protection.AntiDecompiler.AddSuppressIldasmAttribute = AddSuppressIldasmAttribute;
        settings.Protection.AntiDecompiler.AddDecoyAttributes = AddDecoyAttributes;
        settings.Protection.AntiDecompiler.JunkTypeCount = JunkTypeCount;
        settings.Protection.AntiDecompiler.JunkMethodsPerType = JunkMethodsPerType;
        settings.Protection.AntiDump = AntiDumpEnabled;
        settings.Protection.ReferenceProxy = ReferenceProxyEnabled;
        settings.Protection.ProxyExternalCalls = ProxyExternalCalls;
        settings.Protection.MethodEncryption = MethodEncryptionEnabled;

        settings.Metadata.RemoveDebugInfo = RemoveDebugInfo;
        settings.Metadata.RemoveAttributes = RemoveAttributes;
        settings.Metadata.StripDocumentation = StripDocumentation;

        settings.ResourceEncryption.Enabled = ResourceEncryptionEnabled;
        settings.ResourceEncryption.Algorithm = ResourceAlgorithm;

        settings.AssemblyMerge.Enabled = AssemblyMergeEnabled;
        settings.AssemblyMerge.Internalize = InternalizeMergedTypes;

        settings.DependencyEmbedding.Enabled = DependencyEmbeddingEnabled;
        if (_dependencyEmbeddingIncludes is not null)
            settings.DependencyEmbedding.IncludePatterns = _dependencyEmbeddingIncludes.ToList();
        if (_dependencyEmbeddingExcludes is not null)
            settings.DependencyEmbedding.ExcludePatterns = _dependencyEmbeddingExcludes.ToList();

        settings.ConstantEncryption.Enabled = ConstantEncryptionEnabled;
        settings.ConstantEncryption.Algorithm = ConstantEncryptionAlgorithm;
        settings.ConstantEncryption.EncryptIntegers = EncryptIntegers;
        settings.ConstantEncryption.EncryptLongs = EncryptLongs;
        settings.ConstantEncryption.EncryptFloats = EncryptFloats;
        settings.ConstantEncryption.EncryptDoubles = EncryptDoubles;
        settings.ConstantEncryption.IntegerThreshold = IntegerThreshold;
        settings.ConstantEncryption.SkipCommonFloats = SkipCommonFloats;
        settings.ConstantEncryption.SkipCommonDoubles = SkipCommonDoubles;

        settings.Exclusions.Namespaces = ExcludedNamespaces.ToList();
        settings.Exclusions.Types = ExcludedTypes.ToList();
        settings.Exclusions.Methods = ExcludedMethods.ToList();

        settings.Inclusions.Namespaces = IncludedNamespaces.ToList();
        settings.Inclusions.Types = IncludedTypes.ToList();
        settings.Inclusions.Methods = IncludedMethods.ToList();

        settings.Watermark.Enabled = WatermarkEnabled;
        settings.Watermark.Id = WatermarkId ?? string.Empty;

        settings.RuntimeProfile = RuntimeProfile;
        settings.Signing.Enabled = SigningEnabled;
        settings.Signing.KeyFile = string.IsNullOrWhiteSpace(SigningKeyFile) ? null : SigningKeyFile;
        settings.Signing.PasswordEnvironmentVariable = string.IsNullOrWhiteSpace(SigningPasswordEnvironmentVariable)
            ? null
            : SigningPasswordEnvironmentVariable;
        settings.Packing.Enabled = PackingEnabled;
        settings.Packing.Rid = _packingRid;
        settings.Virtualization.Enabled = VirtualizationEnabled;
        settings.Incremental.Enabled = IncrementalEnabled;
        return settings;
    }

    /// <summary>
    /// Loads settings from an ObfySettings instance.
    /// </summary>
    public void FromObfySettings(ObfySettings settings)
    {
        _applyingPreset++;
        try
        {
        _loadedSettings = settings.Clone();
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
        _packingRid = string.IsNullOrWhiteSpace(settings.Packing.Rid) ? "win-x64" : settings.Packing.Rid;
        VirtualizationEnabled = settings.Virtualization.Enabled;
        IncrementalEnabled = settings.Incremental.Enabled;
        }
        finally
        {
            _applyingPreset--;
        }
    }
}
