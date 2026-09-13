namespace Obfy.Core.Models.Solution;

/// <summary>
/// Closed set of projects considered for a solution-drop protection run.
/// </summary>
public sealed class ProtectionSession
{
    /// <summary>
    /// Path to the solution or project that seeded this session.
    /// </summary>
    public required string SourcePath { get; init; }

    /// <summary>
    /// All discovered project entries (included and skipped).
    /// </summary>
    public required IReadOnlyList<ProjectProtectionEntry> Entries { get; init; }

    /// <summary>
    /// Entries that will be obfuscated.
    /// </summary>
    public IEnumerable<ProjectProtectionEntry> Included => Entries.Where(static e => e.IsIncluded);
}
