namespace Obfy.Core.Models.Solution;

/// <summary>
/// Plan for a solution or project protection run (CLI or UI).
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
    /// Entries with an on-disk output (load may still fail).
    /// </summary>
    public IReadOnlyList<ProjectProtectionEntry> Included =>
        Entries.Where(static e => e.IsIncluded).ToArray();
}
