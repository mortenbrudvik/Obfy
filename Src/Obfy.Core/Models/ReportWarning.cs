namespace Obfy.Core.Models;

/// <summary>
/// Represents a warning generated during obfuscation.
/// </summary>
public class ReportWarning
{
    /// <summary>
    /// Warning severity level.
    /// </summary>
    public WarningSeverity Severity { get; init; }

    /// <summary>
    /// Warning category.
    /// </summary>
    public WarningCategory Category { get; init; }

    /// <summary>
    /// Warning message.
    /// </summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>
    /// Related item (symbol name, setting name, etc.)
    /// </summary>
    public string? RelatedItem { get; init; }

    /// <summary>
    /// Additional details or suggestion.
    /// </summary>
    public string? Details { get; init; }
}

/// <summary>
/// Warning severity levels.
/// </summary>
public enum WarningSeverity
{
    /// <summary>
    /// Informational notice.
    /// </summary>
    Info,

    /// <summary>
    /// Warning that may need attention.
    /// </summary>
    Warning,

    /// <summary>
    /// Important issue that should be addressed.
    /// </summary>
    Important
}

/// <summary>
/// Warning category types.
/// </summary>
public enum WarningCategory
{
    /// <summary>
    /// Item was skipped during obfuscation.
    /// </summary>
    SkippedItem,

    /// <summary>
    /// Setting was enabled but had no effect.
    /// </summary>
    UnusedSetting,

    /// <summary>
    /// Public API may have been changed.
    /// </summary>
    PublicApiChange,

    /// <summary>
    /// Potential issue detected.
    /// </summary>
    PotentialIssue,

    /// <summary>
    /// An enabled protection could not take effect (or is not implemented).
    /// </summary>
    ProtectionIneffective
}
