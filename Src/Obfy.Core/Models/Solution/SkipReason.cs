namespace Obfy.Core.Models.Solution;

/// <summary>
/// Why a project was excluded from a protection session.
/// </summary>
public enum SkipReason
{
    /// <summary>
    /// Project is included (not skipped).
    /// </summary>
    None,

    /// <summary>
    /// Project name indicates a test project.
    /// </summary>
    SkipTest,

    /// <summary>
    /// Project or output is missing.
    /// </summary>
    SkipMissing,

    /// <summary>
    /// Project file path could not be resolved.
    /// </summary>
    SkipMissingProject,

    /// <summary>
    /// Project failed to load.
    /// </summary>
    SkipLoadFailed,

    /// <summary>
    /// Project type or target is unsupported.
    /// </summary>
    SkipUnsupported
}
