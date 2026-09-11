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
    /// Kind of the item that was skipped.
    /// </summary>
    public SkippedItemType ItemType { get; init; }

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
/// The kind of item that can be skipped during obfuscation.
/// </summary>
public enum SkippedItemType
{
    /// <summary>A string literal.</summary>
    String,

    /// <summary>A numeric constant.</summary>
    Constant,

    /// <summary>A type (class, struct, enum, ...).</summary>
    Type,

    /// <summary>A method.</summary>
    Method,

    /// <summary>A field.</summary>
    Field,

    /// <summary>A property.</summary>
    Property,

    /// <summary>A parameter.</summary>
    Parameter,

    /// <summary>An embedded resource.</summary>
    Resource
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
