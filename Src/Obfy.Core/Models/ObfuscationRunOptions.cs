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
}
