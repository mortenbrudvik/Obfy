namespace Obfy.Core.Models;

/// <summary>
/// Represents an item that was skipped during obfuscation.
/// </summary>
public class SkippedItem
{
    /// <summary>
    /// Reason the item was skipped.
    /// </summary>
    public SkipReason Reason { get; init; }

    /// <summary>
    /// Type of the item (String, Constant, Type, Method, etc.)
    /// </summary>
    public string ItemType { get; init; } = string.Empty;

    /// <summary>
    /// Name or identifier of the item.
    /// </summary>
    public string ItemName { get; init; } = string.Empty;

    /// <summary>
    /// Additional details about why the item was skipped.
    /// </summary>
    public string? Details { get; init; }
}

/// <summary>
/// Reasons why an item may be skipped during obfuscation.
/// </summary>
public enum SkipReason
{
    /// <summary>
    /// String was shorter than minimum length.
    /// </summary>
    StringTooShort,

    /// <summary>
    /// Constant value was below threshold.
    /// </summary>
    ConstantBelowThreshold,

    /// <summary>
    /// Common float/double value (0, 1, -1).
    /// </summary>
    CommonFloatValue,

    /// <summary>
    /// Excluded by user-defined rule.
    /// </summary>
    ExcludedByRule,

    /// <summary>
    /// Preserved due to public API setting.
    /// </summary>
    PreservedPublicApi,

    /// <summary>
    /// Compiler-generated code.
    /// </summary>
    CompilerGenerated,

    /// <summary>
    /// Unsupported construct.
    /// </summary>
    UnsupportedConstruct,

    /// <summary>
    /// Resource excluded by pattern.
    /// </summary>
    ResourceExcluded
}
