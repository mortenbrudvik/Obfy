namespace Obfy.Core.Models.Solution;

/// <summary>
/// One project in a protection session, with skip state and settings hints.
/// </summary>
public sealed class ProjectProtectionEntry
{
    /// <summary>
    /// Absolute project path after analysis.
    /// </summary>
    public required string ProjectPath { get; init; }

    /// <summary>
    /// Display name. Solution names come from the <c>.sln</c> <c>Project(...)</c> line;
    /// multi-TFM included rows are <c>{name} ({tfm})</c>.
    /// </summary>
    public required string ProjectName { get; init; }

    /// <summary>
    /// Path to the built output assembly when available.
    /// </summary>
    public string? OutputPath { get; init; }

    /// <summary>
    /// Why this project is skipped, or <see cref="ProjectSkipReason.None"/> when included.
    /// </summary>
    public ProjectSkipReason SkipReason { get; init; }

    /// <summary>
    /// Optional human-readable skip explanation.
    /// </summary>
    public string? SkipMessage { get; init; }

    /// <summary>
    /// Inferred per-project settings hints.
    /// </summary>
    public ProjectSettingsHints Hints { get; init; } = new();

    /// <summary>
    /// Whether this entry has an on-disk output (load may still fail later).
    /// </summary>
    public bool IsIncluded => SkipReason == ProjectSkipReason.None && OutputPath is not null;

    /// <summary>
    /// Creates an included entry with a built output path.
    /// </summary>
    public static ProjectProtectionEntry Included(
        string projectPath,
        string projectName,
        string outputPath,
        ProjectSettingsHints? hints = null)
        => new()
        {
            ProjectPath = projectPath,
            ProjectName = projectName,
            OutputPath = outputPath,
            SkipReason = ProjectSkipReason.None,
            Hints = hints ?? new ProjectSettingsHints()
        };

    /// <summary>
    /// Creates a skipped entry. <paramref name="reason"/> must not be <see cref="ProjectSkipReason.None"/>.
    /// </summary>
    public static ProjectProtectionEntry Skipped(
        string projectPath,
        string projectName,
        ProjectSkipReason reason,
        string? message = null)
    {
        if (reason == ProjectSkipReason.None)
            throw new ArgumentException("Skipped entries require a skip reason.", nameof(reason));

        return new ProjectProtectionEntry
        {
            ProjectPath = projectPath,
            ProjectName = projectName,
            SkipReason = reason,
            SkipMessage = message
        };
    }
}
