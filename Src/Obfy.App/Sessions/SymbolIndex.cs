using Obfy.App.Models;
using Obfy.Core.Models;

namespace Obfy.App.Sessions;

/// <summary>
/// The parsed symbol map as a tree, built once per load (off the calling thread) and never changed afterwards, so any
/// number of queries can read it without a lock. Members hang under the longest matching <c>Type:</c> entry, as
/// <see cref="SymbolMapKey.ParseAll"/> decides.
/// </summary>
internal sealed class SymbolIndex
{
    public static readonly SymbolIndex Empty = new([], new Dictionary<string, Node>(), 0);

    private readonly List<Node> _roots;
    private readonly Dictionary<string, Node> _byId;

    private SymbolIndex(List<Node> roots, Dictionary<string, Node> byId, int totalSymbols)
    {
        _roots = roots;
        _byId = byId;
        TotalSymbols = totalSymbols;
    }

    /// <summary>Entries in the whole map.</summary>
    public int TotalSymbols { get; }

    public static SymbolIndex Build(IReadOnlyDictionary<string, string> symbolMap)
    {
        var roots = new List<Node>();
        var byId = new Dictionary<string, Node>(StringComparer.Ordinal);
        var typeNodes = new Dictionary<string, Node>(StringComparer.Ordinal);
        var nextId = 0;

        Node NewNode(string original, string? obfuscated, SymbolKind kind, bool hasEntry)
        {
            var node = new Node($"s{nextId++}", original, obfuscated, kind, hasEntry);
            byId[node.Id] = node;
            return node;
        }

        foreach (var entry in SymbolMapKey.ParseAll(symbolMap.Keys))
        {
            var obfuscated = symbolMap[entry.Key];

            if (entry.TypeFullName is null)
            {
                if (entry.Kind == SymbolMapKey.TypeKind && typeNodes.TryGetValue(entry.DisplayName, out var existing))
                {
                    // The type was first seen through one of its members: it is a real entry after all.
                    existing.Obfuscated = obfuscated;
                    existing.HasEntry = true;
                    continue;
                }

                var node = NewNode(entry.DisplayName, obfuscated, MapKind(entry.Kind), hasEntry: true);
                if (entry.Kind == SymbolMapKey.TypeKind)
                    typeNodes[entry.DisplayName] = node;

                roots.Add(node);
                continue;
            }

            if (!typeNodes.TryGetValue(entry.TypeFullName, out var parent))
            {
                // No Type: entry: the type was not renamed, only its members. It is a container, not a rename.
                parent = NewNode(entry.TypeFullName, null, SymbolKind.Type, hasEntry: false);
                typeNodes[entry.TypeFullName] = parent;
                roots.Add(parent);
            }

            parent.Children.Add(NewNode(entry.DisplayName, obfuscated, MapKind(entry.Kind), hasEntry: true));
        }

        return new SymbolIndex(roots, byId, symbolMap.Count);
    }

    /// <summary>The nodes of one level that show under <paramref name="query"/> (null for no search), in map order. Null when <paramref name="parentId"/> names no node.</summary>
    public List<Node>? Level(string? parentId, string? query)
    {
        if (parentId is null)
        {
            return query is null
                ? _roots
                : _roots.Where(node => RootVisible(node, query)).ToList();
        }

        if (!_byId.TryGetValue(parentId, out var parent))
            return null;

        return query is null
            ? parent.Children
            : parent.Children.Where(child => ChildVisible(parent, child, query)).ToList();
    }

    /// <summary>How many children of <paramref name="node"/> show under <paramref name="query"/>.</summary>
    public static int VisibleChildCount(Node node, string? query)
    {
        if (query is null)
            return node.Children.Count;

        return node.Children.Count(child => ChildVisible(node, child, query));
    }

    // A type shows when it or one of its members matches; any other root shows when it matches itself.
    private static bool RootVisible(Node node, string query) =>
        node.Kind == SymbolKind.Type
            ? SelfMatches(node, query) || node.Children.Any(child => SelfMatches(child, query))
            : SelfMatches(node, query);

    // A member shows when its type matches (by entry or by name), or it matches itself.
    private static bool ChildVisible(Node parent, Node child, string query) =>
        SelfMatches(parent, query)
        || SelfMatches(child, query);

    // Matches display names only (the original and the new name), never the raw map key: a key such as "Type:Foo" would
    // make a search for "Type" match everything. A container has no new name; its original, the full type name, is what
    // finds it.
    private static bool SelfMatches(Node node, string query) =>
        node.Original.Contains(query, StringComparison.OrdinalIgnoreCase)
        || (node.Obfuscated?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false);

    private static SymbolKind MapKind(string kind) => kind switch
    {
        SymbolMapKey.NamespaceKind => SymbolKind.Namespace,
        SymbolMapKey.MethodKind => SymbolKind.Method,
        SymbolMapKey.FieldKind => SymbolKind.Field,
        SymbolMapKey.PropertyKind => SymbolKind.Property,
        SymbolMapKey.EventKind => SymbolKind.Event,
        SymbolMapKey.ParameterKind => SymbolKind.Parameter,
        _ => SymbolKind.Type
    };

    internal sealed class Node(string id, string original, string? obfuscated, SymbolKind kind, bool hasEntry)
    {
        public string Id { get; } = id;

        public string Original { get; } = original;

        public string? Obfuscated { get; set; } = obfuscated;

        public SymbolKind Kind { get; } = kind;

        /// <summary>False for a type that exists only because members named it.</summary>
        public bool HasEntry { get; set; } = hasEntry;

        public List<Node> Children { get; } = [];

        /// <summary>A type with no entry of its own, or mapped to its own name, is a grouping, not a rename.</summary>
        public bool IsContainer =>
            Kind == SymbolKind.Type
            && (!HasEntry || string.Equals(Obfuscated, Original, StringComparison.Ordinal));
    }
}
