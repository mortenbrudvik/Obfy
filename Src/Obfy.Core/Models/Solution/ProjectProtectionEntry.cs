namespace Obfy.Core.Models.Solution;

/// <summary>
/// One project in a protection session, with skip state and settings hints.
/// </summary>
public sealed class ProjectProtectionEntry
{
    /// <summary>
    /// Absolute or solution-relative path to the project file.
    /// </summary>
    public required string ProjectPath { get; init; }

    /// <summary>
    /// Project name (typically the file name without extension).
    /// </summary>
    public required string ProjectName { get; init; }

    /// <summary>
    /// Path to the built output assembly when available.
    /// </summary>
    public string? OutputPath { get; init; }

    /// <summary>
    /// Why this project is skipped, or <see cref="SkipReason.None"/> when included.
    /// </summary>
    public SkipReason SkipReason { get; init; }

    /// <summary>
    /// Optional human-readable skip explanation.
    /// </summary>
    public string? SkipMessage { get; init; }

    /// <summary>
    /// Inferred per-project settings hints.
    /// </summary>
    public ProjectSettingsHints Hints { get; init; } = new();

    /// <summary>
    /// Whether this entry should be obfuscated.
    /// </summary>
    public bool IsIncluded => SkipReason == SkipReason.None && OutputPath is not null;
}
