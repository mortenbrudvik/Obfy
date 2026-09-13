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
}
