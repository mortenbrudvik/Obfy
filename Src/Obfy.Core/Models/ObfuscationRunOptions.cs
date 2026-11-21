namespace Obfy.Core.Models;

/// <summary>
/// What the caller of a run will read from the result, so the service can tell whether the incremental cache can
/// stand in for the run. The service cannot see this on its own: whether a symbol map is written is decided by the
/// caller (the CLI's <c>--map</c> and <c>--report</c>, the desktop app's Results screen).
/// </summary>
public sealed record ObfuscationRunOptions
{
    /// <summary>The options of a caller that reads nothing a cache hit cannot supply.</summary>
    public static ObfuscationRunOptions Default { get; } = new();

    /// <summary>
    /// The caller reads <see cref="ObfuscationResult.SymbolMap"/>: it writes a map file, lists the symbols or
    /// summarises them in a report. The symbol map lists the original names next to the new ones, so the incremental
    /// cache never stores it. A cache hit is then taken only when the run that wrote the cached output renamed
    /// nothing (an empty map is what a hit supplies, and is right); otherwise the run is done again.
    /// </summary>
    public bool NeedsSymbolMap { get; init; }

    /// <summary>
    /// The caller builds a report from the result. A report lists what only a real run has: the symbol summary, the
    /// skipped items and the warning texts. The cache stores none of them (they name original types, members and
    /// resources), so a cache hit is taken only when the run that wrote the cached output renamed nothing, skipped
    /// nothing and raised no warning that a hit cannot repeat; otherwise the run is done again, so a report is never
    /// thinner than the run it describes. Implies <see cref="NeedsSymbolMap"/>.
    /// </summary>
    public bool NeedsReport { get; init; }
}
