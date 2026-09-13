using Obfy.Core.Models;

namespace Obfy.Core.Models.Solution;

/// <summary>
/// Outcome of a closed-set protection run.
/// </summary>
public sealed class ClosedSetResult
{
    /// <summary>
    /// Whether every remaining loaded module was obfuscated and committed.
    /// Pipeline/save/commit is all-or-nothing; modules that fail to load may be omitted
    /// while the remaining set still succeeds.
    /// </summary>
    public bool Success { get; private init; }

    /// <summary>
    /// Failure reason when <see cref="Success"/> is false.
    /// </summary>
    public string? ErrorMessage { get; private init; }

    /// <summary>
    /// Per-module pipeline results for assemblies that loaded.
    /// </summary>
    public IReadOnlyList<ObfuscationResult> ModuleResults { get; private init; } = [];

    /// <summary>
    /// Assemblies that could not be loaded (omitted from the set).
    /// </summary>
    public IReadOnlyList<ClosedSetLoadFailure> LoadFailures { get; private init; } = [];

    /// <summary>
    /// Combined original-to-obfuscated symbol map for the session.
    /// </summary>
    public Dictionary<string, string> SymbolMap { get; private init; } = new();

    private ClosedSetResult()
    {
    }

    /// <summary>
    /// Remaining modules were obfuscated and committed. <see cref="LoadFailures"/> may still be non-empty.
    /// </summary>
    public static ClosedSetResult Succeeded(
        IReadOnlyList<ObfuscationResult> moduleResults,
        IReadOnlyList<ClosedSetLoadFailure>? loadFailures = null,
        Dictionary<string, string>? symbolMap = null)
        => new()
        {
            Success = true,
            ModuleResults = moduleResults,
            LoadFailures = loadFailures ?? [],
            SymbolMap = symbolMap ?? new Dictionary<string, string>()
        };

    /// <summary>
    /// No output was committed.
    /// </summary>
    public static ClosedSetResult Failed(
        string errorMessage,
        IReadOnlyList<ClosedSetLoadFailure>? loadFailures = null,
        IReadOnlyList<ObfuscationResult>? moduleResults = null,
        Dictionary<string, string>? symbolMap = null)
        => new()
        {
            Success = false,
            ErrorMessage = errorMessage,
            ModuleResults = moduleResults ?? [],
            LoadFailures = loadFailures ?? [],
            SymbolMap = symbolMap ?? new Dictionary<string, string>()
        };
}
