namespace Obfy.Core.Models.Solution;

/// <summary>
/// Why a project was excluded from a protection session.
/// </summary>
public enum ProjectSkipReason
{
    /// <summary>
    /// Project has an on-disk output and is included (load may still fail later).
    /// </summary>
    None,

    /// <summary>
    /// Test project (<c>IsTestProject</c>, test SDK/package, or test-like project name).
    /// </summary>
    Test,

    /// <summary>
    /// Built output was not found under conventional <c>bin/Release</c> or <c>bin/Debug</c> layouts.
    /// </summary>
    MissingOutput,

    /// <summary>
    /// Project file path could not be resolved.
    /// </summary>
    MissingProject,

    /// <summary>
    /// Project file could not be read (invalid XML or I/O).
    /// </summary>
    LoadFailed,

    /// <summary>
    /// Project type or target is unsupported.
    /// </summary>
    Unsupported
}
