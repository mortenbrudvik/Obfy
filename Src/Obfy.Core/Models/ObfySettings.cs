using System.ComponentModel.DataAnnotations;

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
    public StringEncryptionSettings StringEncryption { get; set; } = new();

    /// <summary>
    /// Control flow obfuscation settings.
    /// </summary>
    public ControlFlowSettings ControlFlow { get; set; } = new();

    /// <summary>
    /// Symbol renaming settings.
    /// </summary>
    public SymbolRenamingSettings SymbolRenaming { get; set; } = new();

    /// <summary>
    /// Anti-debugging and anti-tampering settings.
    /// </summary>
    public ProtectionSettings Protection { get; set; } = new();

    /// <summary>
    /// Metadata removal settings.
    /// </summary>
    public MetadataSettings Metadata { get; set; } = new();

    /// <summary>
    /// Resource encryption settings.
    /// </summary>
    public ResourceEncryptionSettings ResourceEncryption { get; set; } = new();

    /// <summary>
    /// Constant (numeric) encryption settings.
    /// </summary>
    public ConstantEncryptionSettings ConstantEncryption { get; set; } = new();

    /// <summary>
    /// Assembly merging settings.
    /// </summary>
    public AssemblyMergeSettings AssemblyMerge { get; set; } = new();

    /// <summary>
    /// Exclusion rules (types, methods, namespaces to skip).
    /// </summary>
    public ExclusionRules Exclusions { get; set; } = new();

    /// <summary>
    /// Whether post-build obfuscation is enabled (used by VS extension).
    /// </summary>
    public bool PostBuildEnabled { get; set; } = false;

    /// <summary>
    /// Creates a settings instance for the specified level.
    /// </summary>
    public static ObfySettings ForLevel(ObfuscationLevel level)
    {
        var settings = new ObfySettings { Level = level };
        settings.ApplyLevel();
        return settings;
    }

    /// <summary>
    /// Applies the current level preset to all individual settings.
    /// </summary>
    public void ApplyLevel()
    {
        switch (Level)
        {
            // Each branch sets every field it depends on to an explicit value (never relying on
            // defaults or on prior state), so re-applying a level over an existing instance is
            // deterministic and cannot leak values from a previously applied level.
            case ObfuscationLevel.Minimal:
                StringEncryption.Enabled = false;
                ControlFlow.Enabled = false;
                ControlFlow.Intensity = 50;
                SymbolRenaming.Enabled = true;
                Protection.AntiDebug = false;
                Protection.AntiTamper.Enabled = false;
                Protection.AntiDecompiler.Enabled = false;
                Protection.AntiDump = false;
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
                Protection.AntiDump = false;
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
        ValidateObject(Exclusions);

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
    Xor,

    /// <summary>
    /// AES-256 encryption.
    /// </summary>
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
    Switch,

    /// <summary>
    /// Insert opaque predicates (always-true/false conditions).
    /// </summary>
    OpaquePredicate,

    /// <summary>
    /// Combine switch flattening with opaque predicates.
    /// </summary>
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
    /// Whether to preserve public API names (types and members with public visibility).
    /// </summary>
    public bool PreservePublicApi { get; set; } = false;
}

/// <summary>
/// Naming modes for symbol renaming.
/// </summary>
public enum NamingMode
{
    /// <summary>
    /// Generate unreadable names using non-printable or confusing characters.
    /// </summary>
    Unreadable,

    /// <summary>
    /// Generate sequential names (a, b, c, aa, ab, etc.).
    /// </summary>
    Sequential,

    /// <summary>
    /// Generate hash-based names from original names.
    /// </summary>
    Hash,

    /// <summary>
    /// Generate random alphanumeric names.
    /// </summary>
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
    /// Whether to inject anti-dump protection. Reserved; not implemented.
    /// </summary>
    public bool AntiDump { get; set; } = false;
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
        "DataMemberAttribute"
    };
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
    /// </summary>
    public List<string> ExcludePatterns { get; set; } = new() { "*.resources" };
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
