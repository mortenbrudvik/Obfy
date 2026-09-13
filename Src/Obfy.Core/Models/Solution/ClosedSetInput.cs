namespace Obfy.Core.Models.Solution;

/// <summary>
/// One assembly in a closed-set protection run.
/// </summary>
public sealed class ClosedSetInput
{
    /// <summary>
    /// Path to the built assembly to load.
    /// </summary>
    public required string AssemblyPath { get; init; }

    /// <summary>
    /// Per-project hints overlaid onto the session's base settings.
    /// </summary>
    public required ProjectSettingsHints Hints { get; init; }

    /// <summary>
    /// Creates an input after rejecting a null or whitespace path.
    /// </summary>
    public static ClosedSetInput From(string assemblyPath, ProjectSettingsHints? hints = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyPath);
        return new ClosedSetInput
        {
            AssemblyPath = assemblyPath,
            Hints = hints ?? new ProjectSettingsHints()
        };
    }

    /// <summary>
    /// Creates an input from an included session entry.
    /// </summary>
    public static ClosedSetInput FromIncluded(ProjectProtectionEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (!entry.IsIncluded)
            throw new ArgumentException("Entry is not included in the closed set.", nameof(entry));

        return From(entry.OutputPath!, entry.Hints);
    }
}
