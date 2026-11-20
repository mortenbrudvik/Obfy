namespace Obfy.Core.Models;

/// <summary>
/// Parses symbol-map keys written by the assembly and source renamers.
/// Assembly keys look like <c>Method:Ns.Type.Name(System.Void ())</c>.
/// Members are parented by the longest <c>Type:</c> full name that prefixes them.
/// </summary>
public static class SymbolMapKey
{
    public const string TypeKind = "Type";
    public const string MethodKind = "Method";
    public const string FieldKind = "Field";
    public const string PropertyKind = "Property";
    public const string EventKind = "Event";
    public const string NamespaceKind = "Namespace";
    public const string ParameterKind = "Parameter";

    public readonly record struct Entry(
        string Key,
        string Kind,
        string? TypeFullName,
        string DisplayName);

    public static string KindOf(string key)
    {
        if (string.IsNullOrEmpty(key))
            return string.Empty;

        var colon = key.IndexOf(':');
        return colon <= 0 ? string.Empty : key[..colon];
    }

    public static IReadOnlyList<Entry> ParseAll(IEnumerable<string> keys)
    {
        var list = keys.Where(static key => !string.IsNullOrEmpty(key)).ToList();
        var typeNames = list
            .Where(static key => KindOf(key) == TypeKind)
            .Select(static key => key[(TypeKind.Length + 1)..])
            .Where(static name => name.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .OrderByDescending(static name => name.Length)
            .ToList();

        return list.Select(key => Parse(key, typeNames)).ToList();
    }

    public static Entry Parse(string key, IReadOnlyList<string> typeNamesLongestFirst)
    {
        var colon = key.IndexOf(':');
        var kind = colon <= 0 ? string.Empty : key[..colon];
        var body = colon < 0 || colon == key.Length - 1 ? (colon < 0 ? key : string.Empty) : key[(colon + 1)..];

        if (kind is TypeKind or NamespaceKind or ParameterKind or "")
        {
            var rootKind = kind.Length == 0 ? TypeKind : kind;
            return new Entry(key, rootKind, null, body);
        }

        if (kind is MethodKind or FieldKind or PropertyKind or EventKind)
        {
            foreach (var typeName in typeNamesLongestFirst)
            {
                var prefix = typeName + ".";
                if (body.StartsWith(prefix, StringComparison.Ordinal))
                    return new Entry(key, kind, typeName, body[prefix.Length..]);
            }

            var (fallbackType, member) = SplitFallback(kind, body);
            return new Entry(key, kind, fallbackType, member);
        }

        return new Entry(key, kind, null, body);
    }

    private static (string? typeName, string member) SplitFallback(string kind, string body)
    {
        var head = body;
        var signature = string.Empty;
        if (kind == MethodKind)
        {
            var paren = body.IndexOf('(');
            if (paren >= 0)
            {
                head = body[..paren];
                signature = body[paren..];
            }
        }

        var dot = head.LastIndexOf('.');
        if (dot <= 0 || dot >= head.Length - 1)
            return (null, body);

        return (head[..dot], head[(dot + 1)..] + signature);
    }
}
