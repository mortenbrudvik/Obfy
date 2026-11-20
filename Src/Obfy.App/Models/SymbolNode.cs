namespace Obfy.App.Models;

/// <summary>What a symbol-map entry names. Serialized by name.</summary>
public enum SymbolKind
{
    Namespace,
    Type,
    Method,
    Field,
    Property,
    Parameter,
    Event
}

/// <summary>
/// One row of the symbol-map tree. Children are not inside it: the page asks for them with the node's <see cref="Id"/>
/// as <c>parentId</c>.
/// </summary>
/// <param name="Id">Stable until the results are reloaded; the same node keeps its id across searches.</param>
/// <param name="OriginalName">The name in the input.</param>
/// <param name="ObfuscatedName">The new name. Null for a container, which was not renamed: the page shows the name once, not <c>X → X</c>.</param>
/// <param name="Kind">What the entry names.</param>
/// <param name="IsContainer">True for a type that only groups renamed members (it has no entry of its own, or maps to its own name). It is not a rename.</param>
/// <param name="ChildCount">How many children the node has under the current search.</param>
/// <param name="Expanded">True when a search is active and the node has matching children, so the page opens it.</param>
public sealed record SymbolNode(
    string Id,
    string OriginalName,
    string? ObfuscatedName,
    SymbolKind Kind,
    bool IsContainer,
    int ChildCount,
    bool Expanded);

/// <summary>A window of nodes at one level of the tree, answering <c>results.symbols</c>.</summary>
/// <param name="Revision">The results revision the page was served from; a page of an older revision is stale.</param>
/// <param name="ParentId">The level asked for; null for the roots.</param>
/// <param name="Total">How many nodes the level has under the search, not just in this window.</param>
/// <param name="Offset">Index of the first node of <paramref name="Items"/> within the level.</param>
/// <param name="TotalSymbols">Entries in the whole map, whatever the search.</param>
/// <param name="HasNoSymbols">The map is empty.</param>
/// <param name="HasNoSearchMatches">A search is active and no root matches it.</param>
public sealed record SymbolsPage(
    long Revision,
    string? ParentId,
    int Total,
    int Offset,
    IReadOnlyList<SymbolNode> Items,
    int TotalSymbols,
    bool HasNoSymbols,
    bool HasNoSearchMatches);
