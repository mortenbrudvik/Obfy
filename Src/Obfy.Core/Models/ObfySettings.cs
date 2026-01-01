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
    /// Exclusion rules (types, methods, namespaces to skip).
    /// </summary>
    public ExclusionRules Exclusions { get; set; } = new();

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
            case ObfuscationLevel.Minimal:
                StringEncryption.Enabled = false;
                ControlFlow.Enabled = false;
                SymbolRenaming.Enabled = true;
                Protection.AntiDebug = false;
                Metadata.RemoveDebugInfo = true;
                ConstantEncryption.Enabled = false;
                break;

            case ObfuscationLevel.Standard:
                StringEncryption.Enabled = true;
                ControlFlow.Enabled = false;
                SymbolRenaming.Enabled = true;
                Protection.AntiDebug = false;
                Metadata.RemoveDebugInfo = true;
                ConstantEncryption.Enabled = false;
                break;

            case ObfuscationLevel.Aggressive:
                StringEncryption.Enabled = true;
                ControlFlow.Enabled = true;
                ControlFlow.Intensity = 80;
                SymbolRenaming.Enabled = true;
                Protection.AntiDebug = true;
                Protection.AntiTamper = true;
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
    public bool Enabled { get; set; } = true;

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
    public bool AntiDebug { get; set; } = true;

    /// <summary>
    /// Whether to inject anti-tampering checks.
    /// </summary>
    public bool AntiTamper { get; set; } = false;

    /// <summary>
    /// Whether to inject anti-dump protection.
    /// </summary>
    public bool AntiDump { get; set; } = false;
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
    /// </summary>
    public List<string> ExcludePatterns { get; set; } = new();
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
