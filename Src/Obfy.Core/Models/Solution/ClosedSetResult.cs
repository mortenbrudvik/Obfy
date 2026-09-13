namespace Obfy.Core.Models.Solution;

/// <summary>
/// Outcome of a closed-set protection run.
/// </summary>
public sealed class ClosedSetResult
{
    /// <summary>
    /// Whether every remaining module was obfuscated and committed.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Failure reason when <see cref="Success"/> is false.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Per-module pipeline results for assemblies that loaded.
    /// </summary>
    public IReadOnlyList<ObfuscationResult> ModuleResults { get; init; } = [];

    /// <summary>
    /// Assembly paths that could not be loaded (omitted from the set).
    /// </summary>
    public IReadOnlyList<string> LoadFailures { get; init; } = [];

    /// <summary>
    /// Combined original-to-obfuscated symbol map for the session.
    /// </summary>
    public Dictionary<string, string> SymbolMap { get; init; } = new();
}
