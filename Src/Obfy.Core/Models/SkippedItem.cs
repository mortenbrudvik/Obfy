namespace Obfy.Core.Models;

/// <summary>
/// Represents an item that was skipped during obfuscation.
/// </summary>
public sealed class SkippedItem
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

    /// <summary>
    /// A method skipped because it uses an unsupported construct (e.g. exception handlers).
    /// </summary>
    public static SkippedItem UnsupportedMethod(string name, string details)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new()
        {
            Reason = SkipReason.UnsupportedConstruct,
            ItemType = SkippedItemType.Method,
            ItemName = name,
            Details = details
        };
    }

    /// <summary>
    /// An embedded resource skipped because it matched an exclude pattern.
    /// </summary>
    public static SkippedItem ResourceExcluded(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new()
        {
            Reason = SkipReason.ResourceExcluded,
            ItemType = SkippedItemType.Resource,
            ItemName = name
        };
    }

    /// <summary>
    /// A method skipped by method IL encryption because it is generic.
    /// </summary>
    public static SkippedItem GenericMethodSkipped(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new()
        {
            Reason = SkipReason.GenericMethod,
            ItemType = SkippedItemType.Method,
            ItemName = name,
            Details = "Generic methods and methods on generic types are not IL-encrypted."
        };
    }
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
    ResourceExcluded,

    /// <summary>
    /// Generic method or method on a generic type (method IL encryption skips shared generic IL).
    /// </summary>
    GenericMethod
}
