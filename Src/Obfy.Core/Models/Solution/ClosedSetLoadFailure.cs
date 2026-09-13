namespace Obfy.Core.Models.Solution;

/// <summary>
/// An included assembly that could not be loaded and was omitted from the closed set.
/// </summary>
public sealed class ClosedSetLoadFailure
{
    /// <summary>
    /// Absolute path that failed to load.
    /// </summary>
    public required string Path { get; init; }

    /// <summary>
    /// Exception message from the load attempt.
    /// </summary>
    public required string Message { get; init; }
}
