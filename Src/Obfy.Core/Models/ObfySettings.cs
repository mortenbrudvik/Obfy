using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Obfy.Core.Models;

/// <summary>
/// Root configuration for Obfy obfuscation settings.
/// </summary>
public class ObfySettings
{
    /// <summary>
    /// Global obfuscation level preset.
    /// </summary>
    public ObfuscationLevel Level { get; set; } = ObfuscationLevel.Standard;

    /// <summary>
    /// String encryption settings.
    /// </summary>
    public StringEncryptionSettings StringEncryption { get; init; } = new();

    /// <summary>
    /// Control flow obfuscation settings.
    /// </summary>
    public ControlFlowSettings ControlFlow { get; init; } = new();

    /// <summary>
    /// Symbol renaming settings.
    /// </summary>
    public SymbolRenamingSettings SymbolRenaming { get; init; } = new();

    /// <summary>
    /// Anti-debugging and anti-tampering settings.
    /// </summary>
    public ProtectionSettings Protection { get; init; } = new();

    /// <summary>
    /// Metadata removal settings.
    /// </summary>
    public MetadataSettings Metadata { get; init; } = new();

    /// <summary>
    /// Resource encryption settings.
    /// </summary>
    public ResourceEncryptionSettings ResourceEncryption { get; init; } = new();

    /// <summary>
    /// Constant (numeric) encryption settings.
    /// </summary>
    public ConstantEncryptionSettings ConstantEncryption { get; init; } = new();

    /// <summary>
    /// Assembly merging settings.
    /// </summary>
    public AssemblyMergeSettings AssemblyMerge { get; init; } = new();

    /// <summary>
    /// Embed referenced assemblies as resources and load them via AssemblyResolve.
    /// </summary>
    public DependencyEmbeddingSettings DependencyEmbedding { get; init; } = new();

    /// <summary>
    /// Embed a build/customer identifier in the output assembly.
    /// Requires <c>watermark.enabled</c> and a non-whitespace <c>watermark.id</c>.
    /// </summary>
    public WatermarkSettings Watermark { get; init; } = new();

    /// <summary>
    /// Exclusion rules (types, methods, namespaces to skip).
    /// </summary>
    public ExclusionRules Exclusions { get; init; } = new();

    /// <summary>
    /// Optional allow-list. Empty lists mean no allow-list. When any pattern is set, only matching
    /// namespaces, types, or methods are candidates for renaming, control flow, strings, and constants
    /// (exclusions still apply). Method-only lists still visit types that contain a matching method.
    /// </summary>
    public InclusionRules Inclusions { get; init; } = new();

    /// <summary>
    /// Whether post-build obfuscation is enabled (used by VS extension).
    /// </summary>
    public bool PostBuildEnabled { get; set; } = false;

    /// <summary>
    /// Target runtime. NativeAOT, Unity IL2CPP, and Blazor WASM disable method IL encryption,
    /// anti-dump, and dependency embedding (PE / kernel32 / AssemblyResolve). Anti-debug stays
    /// on but omits kernel32 P/Invoke. The pipeline emits report warnings when it turns features off.
    /// </summary>
    public RuntimeProfile RuntimeProfile { get; set; } = RuntimeProfile.Default;

    /// <summary>
    /// Strong-name re-signing after obfuscation.
    /// </summary>
    public SigningSettings Signing { get; init; } = new();

    /// <summary>
    /// Creates a settings instance for the specified level.
    /// </summary>
    public static ObfySettings ForLevel(ObfuscationLevel level)
    {
        var settings = new ObfySettings { Level = level };
        settings.ApplyLevel();
        return settings;
    }

    private static readonly JsonSerializerOptions _cloneJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Deep-copies this instance so callers can hand settings to the service without having
    /// <see cref="ApplyLevel"/> mutate their object.
    /// </summary>
    public ObfySettings Clone()
    {
        var json = JsonSerializer.Serialize(this, _cloneJsonOptions);
        return JsonSerializer.Deserialize<ObfySettings>(json, _cloneJsonOptions)
            ?? throw new InvalidOperationException("Failed to clone obfuscation settings.");
    }

    /// <summary>
    /// Applies the current level preset to all individual settings.
    /// </summary>
    public void ApplyLevel()
    {
        switch (Level)
        {
            // Each non-Custom branch assigns this fixed set of flags (enabled bits, intensity,
            // constant-encryption algorithm, metadata/debug) so *those* values do not leak from a
            // previously applied level. Other nested settings (control-flow mode, string/resource
            // algorithms, naming mode, PreservePublicApi, PreserveXaml, junk counts, include/exclude
            // patterns, RuntimeProfile, Signing, Watermark, DependencyEmbedding, AddDecoyAttributes)
            // keep their prior or default values. ProxyExternalCalls is cleared when ReferenceProxy is turned off.
            case ObfuscationLevel.Minimal:
                StringEncryption.Enabled = false;
                ControlFlow.Enabled = false;
                ControlFlow.Intensity = 50;
                SymbolRenaming.Enabled = true;
                Protection.AntiDebug = false;
                Protection.AntiTamper.Enabled = false;
                Protection.AntiDecompiler.Enabled = false;
                Protection.AntiDump = false;
                Protection.ReferenceProxy = false;
                Protection.ProxyExternalCalls = false;
                Protection.MethodEncryption = false;
                Metadata.RemoveDebugInfo = true;
                Metadata.RemoveAttributes = false;
                ResourceEncryption.Enabled = false;
                ConstantEncryption.Enabled = false;
                ConstantEncryption.Algorithm = EncryptionAlgorithm.Xor;
                break;

            case ObfuscationLevel.Standard:
                StringEncryption.Enabled = true;
                ControlFlow.Enabled = false;
                ControlFlow.Intensity = 50;
                SymbolRenaming.Enabled = true;
                Protection.AntiDebug = false;
                Protection.AntiTamper.Enabled = false;
                Protection.AntiDecompiler.Enabled = false;
                Protection.AntiDump = false;
                Protection.ReferenceProxy = false;
                Protection.ProxyExternalCalls = false;
                Protection.MethodEncryption = false;
                Metadata.RemoveDebugInfo = true;
                Metadata.RemoveAttributes = true;
                ResourceEncryption.Enabled = false;
                ConstantEncryption.Enabled = false;
                ConstantEncryption.Algorithm = EncryptionAlgorithm.Xor;
                break;

            case ObfuscationLevel.Aggressive:
                StringEncryption.Enabled = true;
                ControlFlow.Enabled = true;
                ControlFlow.Intensity = 80;
                SymbolRenaming.Enabled = true;
                Protection.AntiDebug = true;
                Protection.AntiTamper.Enabled = true;
                Protection.AntiDecompiler.Enabled = true;
                Protection.AntiDump = true;
                Protection.ReferenceProxy = true;
                Protection.MethodEncryption = true;
                Metadata.RemoveDebugInfo = true;
                Metadata.RemoveAttributes = true;
                ResourceEncryption.Enabled = true;
                ConstantEncryption.Enabled = true;
                ConstantEncryption.Algorithm = EncryptionAlgorithm.Xor;
                break;

            case ObfuscationLevel.Custom:
                // Use individual settings as-is
                break;
        }
    }

    /// <summary>
    /// Validates the range-constrained settings, throwing <see cref="ValidationException"/> if any
    /// value is out of its declared range. DataAnnotations attributes are not enforced automatically,
    /// so this must be called explicitly before the settings are used.
    /// </summary>
    public void Validate()
    {
        ValidateObject(this);
        ValidateObject(StringEncryption);
        ValidateObject(ControlFlow);
        ValidateObject(SymbolRenaming);
        ValidateObject(Protection);
        ValidateObject(Protection.AntiTamper);
        ValidateObject(Protection.AntiDecompiler);
        ValidateObject(Metadata);
        ValidateObject(ResourceEncryption);
        ValidateObject(ConstantEncryption);
        ValidateObject(AssemblyMerge);
        ValidateObject(DependencyEmbedding);
        ValidateObject(Exclusions);
        ValidateObject(Inclusions);
        ValidateObject(Signing);
        ValidateObject(Watermark);

        Inclusions.Namespaces ??= new();
        Inclusions.Types ??= new();
        Inclusions.Methods ??= new();
        DependencyEmbedding.IncludePatterns ??= new();
        DependencyEmbedding.ExcludePatterns ??= new();

        if (Signing.Enabled)
        {
            var keyFile = Signing.KeyFile;
            if (string.IsNullOrWhiteSpace(keyFile))
            {
                throw new ValidationException("Signing is enabled but no key file was specified.");
            }

            if ((keyFile.EndsWith(".pfx", StringComparison.OrdinalIgnoreCase) ||
                 keyFile.EndsWith(".p12", StringComparison.OrdinalIgnoreCase)) &&
                string.IsNullOrWhiteSpace(Signing.PasswordEnvironmentVariable))
            {
                throw new ValidationException(
                    "PFX signing requires Signing.PasswordEnvironmentVariable to name an environment variable that holds the password.");
            }
        }

        if (Watermark.Enabled && string.IsNullOrWhiteSpace(Watermark.Id))
        {
            throw new ValidationException("Watermark is enabled but no id was specified.");
        }

        static void ValidateObject(object instance) =>
            Validator.ValidateObject(instance, new ValidationContext(instance), validateAllProperties: true);
    }
}

/// <summary>
/// Settings for string encryption obfuscation.
/// </summary>
public class StringEncryptionSettings
{
    /// <summary>
    /// Whether string encryption is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// The encryption algorithm to use.
    /// </summary>
    public EncryptionAlgorithm Algorithm { get; set; } = EncryptionAlgorithm.Aes256;

    /// <summary>
    /// Whether to encrypt constant strings in code.
    /// </summary>
    public bool EncryptConstantStrings { get; set; } = true;

    /// <summary>
    /// Whether to encrypt embedded resource strings.
    /// </summary>
    public bool EncryptResourceStrings { get; set; } = true;

    /// <summary>
    /// Minimum string length to encrypt (shorter strings are ignored).
    /// </summary>
    [Range(1, 100)]
    public int MinStringLength { get; set; } = 3;
}

/// <summary>
/// Encryption algorithms for string encryption.
/// </summary>
public enum EncryptionAlgorithm
{
    /// <summary>
    /// XOR encryption with key rotation.
    /// </summary>
    [System.ComponentModel.Description("XOR")]
    Xor,

    /// <summary>
    /// AES-256 encryption.
    /// </summary>
    [System.ComponentModel.Description("AES-256")]
    Aes256
}

/// <summary>
/// Settings for control flow obfuscation.
/// </summary>
public class ControlFlowSettings
{
    /// <summary>
    /// Whether control flow obfuscation is enabled.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// The control flow obfuscation mode.
    /// </summary>
    public ControlFlowMode Mode { get; set; } = ControlFlowMode.Switch;

    /// <summary>
    /// Obfuscation intensity (0-100). Higher values create more complex control flow.
    /// </summary>
    [Range(0, 100)]
    public int Intensity { get; set; } = 50;
}

/// <summary>
/// Control flow obfuscation modes.
/// </summary>
public enum ControlFlowMode
{
    /// <summary>
    /// Convert linear code to switch-based state machine.
    /// </summary>
    [System.ComponentModel.Description("Switch flattening")]
    Switch,

    /// <summary>
    /// Insert opaque predicates (always-true/false conditions).
    /// </summary>
    [System.ComponentModel.Description("Opaque predicates")]
    OpaquePredicate,

    /// <summary>
    /// Combine switch flattening with opaque predicates.
    /// </summary>
    [System.ComponentModel.Description("Combined")]
    Combined
}

/// <summary>
/// Settings for symbol renaming obfuscation.
/// </summary>
public class SymbolRenamingSettings
{
    /// <summary>
    /// Whether symbol renaming is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// The naming mode for renamed symbols.
    /// </summary>
    public NamingMode Mode { get; set; } = NamingMode.Unreadable;

    /// <summary>
    /// Whether to rename types (classes, structs, enums).
    /// </summary>
    public bool RenameTypes { get; set; } = true;

    /// <summary>
    /// Whether to rename methods.
    /// </summary>
    public bool RenameMethods { get; set; } = true;

    /// <summary>
    /// Whether to rename properties.
    /// </summary>
    public bool RenameProperties { get; set; } = true;

    /// <summary>
    /// Whether to rename fields.
    /// </summary>
    public bool RenameFields { get; set; } = true;

    /// <summary>
    /// Whether to rename parameters.
    /// </summary>
    public bool RenameParameters { get; set; } = true;

    /// <summary>
    /// Whether to rename events (and their add_/remove_ accessors).
    /// </summary>
    public bool RenameEvents { get; set; } = true;

    /// <summary>
    /// Whether to rename namespaces. Public namespaces are kept when PreservePublicApi is set.
    /// </summary>
    public bool RenameNamespaces { get; set; } = true;

    /// <summary>
    /// Whether to preserve public API names (types and members with public visibility).
    /// </summary>
    public bool PreservePublicApi { get; set; } = false;

    /// <summary>
    /// Preserve public instance properties on types that look XAML-bindable: name ends with
    /// ViewModel/View, implements INotifyPropertyChanged, declares DependencyProperty fields,
    /// or has a resolvable base whose name contains DependencyObject. Framework WPF bases often
    /// fail to resolve, so prefer the *ViewModel suffix. Off by default; Desktop wizard / Settings
    /// panel can turn it on.
    /// </summary>
    public bool PreserveXaml { get; set; } = false;
}

/// <summary>
/// Naming modes for symbol renaming.
/// </summary>
public enum NamingMode
{
    /// <summary>
    /// Generate unreadable names using non-printable or confusing characters.
    /// </summary>
    [System.ComponentModel.Description("Unreadable")]
    Unreadable,

    /// <summary>
    /// Generate sequential names (a, b, c, aa, ab, etc.).
    /// </summary>
    [System.ComponentModel.Description("Sequential")]
    Sequential,

    /// <summary>
    /// Generate hash-based names from original names.
    /// </summary>
    [System.ComponentModel.Description("Hash")]
    Hash,

    /// <summary>
    /// Generate random alphanumeric names.
    /// </summary>
    [System.ComponentModel.Description("Random")]
    Random
}

/// <summary>
/// Settings for anti-debugging and anti-tampering protection.
/// </summary>
public class ProtectionSettings
{
    /// <summary>
    /// Whether to inject anti-debugging checks.
    /// </summary>
    public bool AntiDebug { get; set; } = false;

    /// <summary>
    /// Anti-tamper detection settings.
    /// </summary>
    public AntiTamperSettings AntiTamper { get; set; } = new();

    /// <summary>
    /// Anti-decompiler protection settings.
    /// </summary>
    public AntiDecompilerSettings AntiDecompiler { get; set; } = new();

    /// <summary>
    /// Whether to inject anti-dump protection (in-memory PE header wipe and
    /// x86/x64 <c>dbghelp!MiniDumpWriteDump</c> patch on Windows). Gated off for
    /// NativeAOT / Unity IL2CPP / Blazor WASM.
    /// </summary>
    public bool AntiDump { get; set; } = false;

    /// <summary>
    /// Whether to hide method call targets behind proxy methods.
    /// </summary>
    public bool ReferenceProxy { get; set; } = false;

    /// <summary>
    /// When reference proxy is on, also proxy selected out-of-module calls (BCL and third-party).
    /// Ignored unless <see cref="ReferenceProxy"/> is true. Off by default; in-module calls are still proxied.
    /// Skips compiler/interop/pointer/value-type/generic/vararg/ctor signatures.
    /// </summary>
    public bool ProxyExternalCalls { get; set; } = false;

    /// <summary>
    /// Whether to XOR-encrypt method IL in the PE and decrypt it at module load (Windows).
    /// </summary>
    public bool MethodEncryption { get; set; } = false;
}

/// <summary>
/// Settings for anti-tamper detection.
/// </summary>
public class AntiTamperSettings
{
    /// <summary>
    /// Whether anti-tamper detection is enabled.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Whether to check integrity at the entry point method.
    /// </summary>
    public bool CheckEntryPoint { get; set; } = true;

    /// <summary>
    /// Whether to check integrity at the module initializer (.cctor).
    /// </summary>
    public bool CheckModuleInitializer { get; set; } = true;
}

/// <summary>
/// Settings for anti-decompiler protection.
/// </summary>
public class AntiDecompilerSettings
{
    /// <summary>
    /// Whether anti-decompiler protection is enabled.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Whether to inject junk types and methods to clutter analysis.
    /// </summary>
    public bool InjectJunkTypes { get; set; } = true;

    /// <summary>
    /// Whether to add the SuppressIldasm attribute to block ILDasm.
    /// </summary>
    public bool AddSuppressIldasmAttribute { get; set; } = true;

    /// <summary>
    /// Inject internal <c>ConfusedByAttribute</c> / <c>DotfuscatorAttribute</c> types and assembly
    /// attributes. Names are pinned against renaming so name-based detectors can see them.
    /// This does not block de4dot.
    /// </summary>
    public bool AddDecoyAttributes { get; set; } = true;

    /// <summary>
    /// Number of junk types to inject.
    /// </summary>
    [Range(1, 50)]
    public int JunkTypeCount { get; set; } = 5;

    /// <summary>
    /// Number of junk methods per type.
    /// </summary>
    [Range(1, 20)]
    public int JunkMethodsPerType { get; set; } = 3;
}

/// <summary>
/// Settings for metadata removal.
/// </summary>
public class MetadataSettings
{
    /// <summary>
    /// Whether to remove debug information (PDB references).
    /// </summary>
    public bool RemoveDebugInfo { get; set; } = true;

    /// <summary>
    /// Whether to remove custom attributes.
    /// </summary>
    public bool RemoveAttributes { get; set; } = true;

    /// <summary>
    /// Whether to strip XML documentation.
    /// </summary>
    public bool StripDocumentation { get; set; } = true;
}

/// <summary>
/// Rules for excluding types, methods, or namespaces from obfuscation.
/// </summary>
public class ExclusionRules
{
    /// <summary>
    /// Namespace patterns to exclude (supports wildcards).
    /// </summary>
    public List<string> Namespaces { get; set; } = new();

    /// <summary>
    /// Type name patterns to exclude (supports wildcards).
    /// </summary>
    public List<string> Types { get; set; } = new();

    /// <summary>
    /// Method name patterns to exclude (supports wildcards).
    /// </summary>
    public List<string> Methods { get; set; } = new();

    /// <summary>
    /// Attribute types that mark members for exclusion.
    /// </summary>
    public List<string> Attributes { get; set; } = new()
    {
        "SerializableAttribute",
        "DataContractAttribute",
        "DataMemberAttribute",
        "JsonPropertyNameAttribute",
        "JsonPropertyAttribute",
        "XmlElementAttribute",
        "XmlAttributeAttribute"
    };
}

/// <summary>
/// Optional allow-list. Empty lists mean no allow-list (everything is a candidate).
/// Patterns use the same wildcards as exclusions and match short names. Dimensions are OR'd:
/// a type matches if its namespace, type name, or (for methods) method name hits a pattern.
/// </summary>
public class InclusionRules
{
    public List<string> Namespaces { get; set; } = new();

    public List<string> Types { get; set; } = new();

    public List<string> Methods { get; set; } = new();

    [JsonIgnore]
    public bool HasAny =>
        (Namespaces?.Count ?? 0) > 0 || (Types?.Count ?? 0) > 0 || (Methods?.Count ?? 0) > 0;
}

/// <summary>
/// Runtime the obfuscated assembly will run on. NativeAOT, Unity IL2CPP, and Blazor WASM
/// disable method encryption, anti-dump, and AssemblyResolve embedding.
/// </summary>
public enum RuntimeProfile
{
    Default,
    NativeAot,
    UnityIl2Cpp,
    BlazorWasm
}

/// <summary>
/// Re-sign the output assembly with a strong-name key.
/// </summary>
public class SigningSettings
{
    public bool Enabled { get; set; }

    /// <summary>Path to an .snk, .pfx, or .p12 file.</summary>
    public string? KeyFile { get; set; }

    /// <summary>
    /// Name of the environment variable that holds the PFX password. Required for .pfx/.p12;
    /// unused for .snk. Empty PFX passwords are not supported.
    /// </summary>
    public string? PasswordEnvironmentVariable { get; set; }
}

/// <summary>
/// Embed sibling <c>{name}.dll</c> files (matching assembly refs next to the input) as resources
/// loaded via AppDomain.AssemblyResolve. Disabled on NativeAot / UnityIl2Cpp / BlazorWasm.
/// </summary>
public class DependencyEmbeddingSettings
{
    public bool Enabled { get; set; }

    public List<string> IncludePatterns { get; set; } = new() { "*.dll" };

    public List<string> ExcludePatterns { get; set; } = new() { "*.resources.dll" };
}

/// <summary>
/// Embed a customer or build identifier as an assembly custom-attribute constructor argument
/// and a public <c>Id</c> field. The type name <c>WatermarkAttribute</c> is pinned against
/// renaming; recover the id from the CA blob or that field, not by encrypting secrets.
/// </summary>
public class WatermarkSettings
{
    public bool Enabled { get; set; }

    /// <summary>Plaintext identifier written into the custom assembly attribute. Required when <see cref="Enabled"/> is true.</summary>
    public string Id { get; set; } = "";
}

/// <summary>
/// Settings for resource encryption.
/// </summary>
public class ResourceEncryptionSettings
{
    /// <summary>
    /// Whether resource encryption is enabled.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// The encryption algorithm to use.
    /// </summary>
    public EncryptionAlgorithm Algorithm { get; set; } = EncryptionAlgorithm.Aes256;

    /// <summary>
    /// Patterns for resources to include (supports wildcards: *, ?).
    /// Default includes all resources.
    /// </summary>
    public List<string> IncludePatterns { get; set; } = new() { "*" };

    /// <summary>
    /// Patterns for resources to exclude (supports wildcards: *, ?).
    /// Excluded patterns take precedence over include patterns.
    /// *.resources is excluded by default so ResourceManager satellite files keep working.
    /// Obfy.Embedded.* is excluded so packed dependency DLLs stay plaintext for Assembly.Load;
    /// resource encryption also hard-skips that prefix even if this list is overwritten.
    /// </summary>
    public List<string> ExcludePatterns { get; set; } = new() { "*.resources", "Obfy.Embedded.*" };
}

/// <summary>
/// Settings for constant (numeric) encryption obfuscation.
/// </summary>
public class ConstantEncryptionSettings
{
    /// <summary>
    /// Whether constant encryption is enabled.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// The encryption algorithm to use.
    /// </summary>
    public EncryptionAlgorithm Algorithm { get; set; } = EncryptionAlgorithm.Xor;

    /// <summary>
    /// Whether to encrypt integer (int) constants.
    /// </summary>
    public bool EncryptIntegers { get; set; } = true;

    /// <summary>
    /// Whether to encrypt long constants.
    /// </summary>
    public bool EncryptLongs { get; set; } = true;

    /// <summary>
    /// Whether to encrypt float constants.
    /// </summary>
    public bool EncryptFloats { get; set; } = true;

    /// <summary>
    /// Whether to encrypt double constants.
    /// </summary>
    public bool EncryptDoubles { get; set; } = true;

    /// <summary>
    /// Minimum absolute value for integer encryption.
    /// Values with |value| less than this threshold are skipped.
    /// Default: 2 (skips -1, 0, 1)
    /// </summary>
    [Range(0, 1000)]
    public int IntegerThreshold { get; set; } = 2;

    /// <summary>
    /// Minimum absolute value for long encryption.
    /// </summary>
    [Range(0, 1000)]
    public long LongThreshold { get; set; } = 2;

    /// <summary>
    /// Skip common float values (0.0f, 1.0f, -1.0f).
    /// </summary>
    public bool SkipCommonFloats { get; set; } = true;

    /// <summary>
    /// Skip common double values (0.0, 1.0, -1.0).
    /// </summary>
    public bool SkipCommonDoubles { get; set; } = true;
}

/// <summary>
/// Settings for assembly merging.
/// </summary>
public class AssemblyMergeSettings
{
    /// <summary>
    /// Whether assembly merging is enabled.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Make merged types internal (recommended for obfuscation).
    /// When true, public types from secondary assemblies become internal.
    /// </summary>
    public bool Internalize { get; set; } = true;

    /// <summary>
    /// Preserve debug information in merged assembly.
    /// </summary>
    public bool PreserveDebugInfo { get; set; } = false;

    /// <summary>
    /// Additional directories to search for resolving dependencies.
    /// </summary>
    public List<string> SearchDirectories { get; set; } = new();

    /// <summary>
    /// Assembly patterns to exclude from merging (keep as references).
    /// Supports wildcards: System.*, Microsoft.*
    /// </summary>
    public List<string> ExcludePatterns { get; set; } = new();
}
