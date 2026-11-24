using System.Buffers;
using System.Security;
using System.Text.RegularExpressions;
using System.Xml;

namespace Obfy.Core.Services.Solution;

/// <summary>
/// Extracts CLR type names referenced by XAML source files (<c>x:Class</c>, <c>x:Subclass</c>,
/// <c>clr-namespace:</c> elements, <c>{x:Type ...}</c>, <c>{x:Static ...}</c>, <c>TargetType</c>, <c>DataType</c>).
/// </summary>
public static class XamlTypeCollector
{
    private const string Xaml2006Namespace = "http://schemas.microsoft.com/winfx/2006/xaml";
    private const string Xaml2009Namespace = "http://schemas.microsoft.com/winfx/2009/xaml";
    private const string ClrNamespacePrefix = "clr-namespace:";
    internal const int MaxDirectoryDepth = 32;

    private static readonly EnumerationOptions TopDirectoryNoReparseOptions = new()
    {
        RecurseSubdirectories = false,
        AttributesToSkip = FileAttributes.ReparsePoint
    };

    private static readonly HashSet<string> SkippedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin",
        "obj",
        ".git",
        "node_modules",
        ".vs",
        "packages"
    };

    private static readonly XmlReaderSettings XmlSettings = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        IgnoreComments = true
    };

    private static readonly Regex MarkupExtensionRegex = new(
        @"\{\s*([A-Za-z_][A-Za-z0-9_.-]*)\s*:\s*(Type(?:Extension)?|Static(?:Extension)?)\b([^}{]*)\}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    private static readonly SearchValues<char> ExtensionArgumentDelimiters = SearchValues.Create(", \t\r\n");

    /// <summary>
    /// Recursively scans <paramref name="projectDirectory"/> (up to <see cref="MaxDirectoryDepth"/> levels,
    /// skipping reparse points and build/tool folders such as <c>bin</c>, <c>obj</c>, <c>.git</c>,
    /// <c>node_modules</c>, <c>.vs</c>, and <c>packages</c>) for <c>*.xaml</c> files and returns
    /// deduplicated, ordinally sorted CLR type names. Malformed or unreadable XAML files are ignored without throwing.
    /// </summary>
    public static IReadOnlyList<string> CollectFromProjectDirectory(string projectDirectory)
    {
        ArgumentNullException.ThrowIfNull(projectDirectory);

        if (string.IsNullOrWhiteSpace(projectDirectory) || !Directory.Exists(projectDirectory))
            return [];

        var types = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<(string Directory, int Depth)>();
        pending.Push((projectDirectory, 0));

        while (pending.Count > 0)
        {
            var (dir, depth) = pending.Pop();

            try
            {
                foreach (var file in Directory.EnumerateFiles(dir, "*", TopDirectoryNoReparseOptions))
                {
                    if (!file.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
                        continue;

                    try
                    {
                        using var stream = File.OpenRead(file);
                        using var reader = XmlReader.Create(stream, XmlSettings);
                        var fileTypes = new HashSet<string>(StringComparer.Ordinal);
                        CollectFromReader(reader, fileTypes);
                        types.UnionWith(fileTypes);
                    }
                    catch (Exception ex) when (ex is XmlException or IOException or UnauthorizedAccessException or SecurityException)
                    {
                        // Ignore malformed or unreadable XAML files gracefully.
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
            {
                continue;
            }

            if (depth >= MaxDirectoryDepth)
                continue;

            try
            {
                foreach (var subDir in Directory.EnumerateDirectories(dir, "*", TopDirectoryNoReparseOptions))
                {
                    var name = Path.GetFileName(subDir);
                    if (!string.IsNullOrEmpty(name) && !SkippedDirectories.Contains(name))
                        pending.Push((subDir, depth + 1));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
            {
                // Ignore unreadable directories gracefully.
            }
        }

        return ToSortedList(types);
    }

    /// <summary>
    /// Extracts deduplicated, ordinally sorted CLR type names from <paramref name="xamlContent"/>.
    /// Malformed XML or prohibited DTDs are ignored and yield an empty list without throwing.
    /// </summary>
    public static IReadOnlyList<string> CollectFromXaml(string xamlContent)
    {
        ArgumentNullException.ThrowIfNull(xamlContent);

        if (string.IsNullOrWhiteSpace(xamlContent))
            return [];

        var types = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            using var stringReader = new StringReader(xamlContent);
            using var reader = XmlReader.Create(stringReader, XmlSettings);
            CollectFromReader(reader, types);
        }
        catch (XmlException)
        {
            return [];
        }

        return ToSortedList(types);
    }

    private static void CollectFromReader(XmlReader reader, ISet<string> types)
    {
        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element)
                continue;

            var elementNs = reader.NamespaceURI;
            var elementLocalName = reader.LocalName;

            if (TryExtractClrNamespace(elementNs, out var elementClrNs))
            {
                var dotIndex = elementLocalName.IndexOf('.');
                var typeName = dotIndex >= 0 ? elementLocalName[..dotIndex] : elementLocalName;
                AddResolvedType(elementClrNs, typeName, types);
            }

            List<(string NamespaceUri, string LocalName, string Value)>? attributes = null;
            if (reader.HasAttributes)
            {
                attributes = new List<(string, string, string)>();
                while (reader.MoveToNextAttribute())
                    attributes.Add((reader.NamespaceURI, reader.LocalName, reader.Value));
                reader.MoveToElement();
            }

            if (attributes is null)
                continue;

            foreach (var (attrNs, attrLocalName, attrValue) in attributes)
            {
                if (IsXamlNamespace(attrNs) && attrLocalName is "Class" or "Subclass")
                {
                    AddDirectTypeName(attrValue, types);
                    continue;
                }

                if (TryExtractClrNamespace(attrNs, out var attrClrNs))
                {
                    var dotIndex = attrLocalName.IndexOf('.');
                    if (dotIndex > 0)
                        AddResolvedType(attrClrNs, attrLocalName[..dotIndex], types);
                }

                if (string.IsNullOrWhiteSpace(attrValue))
                    continue;

                var trimmed = attrValue.Trim();
                if (!trimmed.StartsWith("{}", StringComparison.Ordinal))
                    ProcessMarkupExtensions(trimmed, reader.LookupNamespace, types);

                if ((string.IsNullOrEmpty(attrNs) || IsXamlNamespace(attrNs)) &&
                    attrLocalName is "TargetType" or "DataType" &&
                    !trimmed.StartsWith('{'))
                {
                    TryAddTypeToken(trimmed, reader.LookupNamespace, types);
                }

                if (IsXamlNamespace(elementNs) && string.IsNullOrEmpty(attrNs))
                {
                    if (elementLocalName is "Type" or "TypeExtension" && attrLocalName == "TypeName")
                        TryAddTypeToken(trimmed, reader.LookupNamespace, types);
                    else if (elementLocalName is "Static" or "StaticExtension" && attrLocalName == "Member")
                        TryAddStaticToken(trimmed, reader.LookupNamespace, types);
                }
            }
        }
    }

    private static void ProcessMarkupExtensions(
        string value,
        Func<string, string?> lookupNamespace,
        ISet<string> types)
    {
        try
        {
            foreach (Match match in MarkupExtensionRegex.Matches(value))
            {
                var extPrefix = match.Groups[1].Value;
                var extNs = lookupNamespace(extPrefix);
                if (!IsXamlNamespace(extNs) && !(extNs is null && extPrefix == "x"))
                    continue;

                var extKind = match.Groups[2].Value;
                var args = match.Groups[3].Value;

                if (extKind.StartsWith("Type", StringComparison.Ordinal))
                    TryAddTypeToken(ExtractExtensionArgument(args, "TypeName"), lookupNamespace, types);
                else
                    TryAddStaticToken(ExtractExtensionArgument(args, "Member"), lookupNamespace, types);
            }
        }
        catch (RegexMatchTimeoutException)
        {
            // Ignore pathological markup extension values without throwing.
        }
    }

    private static string ExtractExtensionArgument(string args, string namedParameter)
    {
        var span = args.AsSpan().Trim();
        var eq = span.IndexOf('=');
        if (eq > 0 && span[..eq].Trim().Equals(namedParameter, StringComparison.Ordinal))
            span = span[(eq + 1)..].TrimStart();

        if (span.Length >= 2 && span[0] is '\'' or '"')
        {
            var quote = span[0];
            var close = span[1..].IndexOf(quote);
            if (close >= 0)
            {
                var afterClose = 1 + close + 1;
                if (afterClose == span.Length || ExtensionArgumentDelimiters.Contains(span[afterClose]))
                    return span.Slice(1, close).Trim().ToString();
            }
        }

        var end = span.IndexOfAny(ExtensionArgumentDelimiters);
        return (end >= 0 ? span[..end] : span).ToString();
    }

    private static void TryAddTypeToken(
        string token,
        Func<string, string?> lookupNamespace,
        ISet<string> types)
    {
        if (!TrySplitPrefix(token, out var prefix, out var typeName))
            return;

        if (!TryExtractClrNamespace(lookupNamespace(prefix), out var clrNamespace))
            return;

        AddResolvedType(clrNamespace, typeName, types);
    }

    private static void TryAddStaticToken(
        string token,
        Func<string, string?> lookupNamespace,
        ISet<string> types)
    {
        if (!TrySplitPrefix(token, out var prefix, out var memberRef))
            return;

        var dotIndex = memberRef.IndexOf('.');
        if (dotIndex <= 0)
            return;

        if (!TryExtractClrNamespace(lookupNamespace(prefix), out var clrNamespace))
            return;

        AddResolvedType(clrNamespace, memberRef[..dotIndex], types);
    }

    private static bool TrySplitPrefix(string token, out string prefix, out string localPart)
    {
        prefix = string.Empty;
        localPart = string.Empty;

        if (string.IsNullOrWhiteSpace(token))
            return false;

        var trimmed = token.Trim();
        var colon = trimmed.IndexOf(':');
        if (colon < 0)
        {
            localPart = trimmed;
            return !ContainsWhitespace(localPart);
        }

        if (colon == 0 || colon == trimmed.Length - 1)
            return false;

        prefix = trimmed[..colon];
        localPart = trimmed[(colon + 1)..];
        return !ContainsWhitespace(prefix) && !ContainsWhitespace(localPart);
    }

    private static bool TryExtractClrNamespace(string? xmlNamespace, out string clrNamespace)
    {
        clrNamespace = string.Empty;
        if (string.IsNullOrEmpty(xmlNamespace) ||
            !xmlNamespace.StartsWith(ClrNamespacePrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var rest = xmlNamespace.AsSpan(ClrNamespacePrefix.Length);
        var semi = rest.IndexOf(';');
        if (semi >= 0)
            rest = rest[..semi];

        clrNamespace = rest.Trim().ToString();
        return true;
    }

    private static void AddDirectTypeName(string rawTypeName, ISet<string> types)
    {
        if (string.IsNullOrWhiteSpace(rawTypeName))
            return;

        var normalized = rawTypeName.Trim().Replace('+', '.');
        if (!ContainsWhitespace(normalized))
            types.Add(normalized);
    }

    private static void AddResolvedType(string clrNamespace, string typeName, ISet<string> types)
    {
        if (string.IsNullOrWhiteSpace(typeName))
            return;

        var normalizedType = typeName.Trim().Replace('+', '.');
        if (ContainsWhitespace(normalizedType))
            return;

        types.Add(string.IsNullOrEmpty(clrNamespace)
            ? normalizedType
            : $"{clrNamespace}.{normalizedType}");
    }

    private static bool IsXamlNamespace(string? xmlNamespace) =>
        string.Equals(xmlNamespace, Xaml2006Namespace, StringComparison.Ordinal) ||
        string.Equals(xmlNamespace, Xaml2009Namespace, StringComparison.Ordinal);

    private static bool ContainsWhitespace(string value)
    {
        foreach (var ch in value)
        {
            if (char.IsWhiteSpace(ch))
                return true;
        }

        return false;
    }

    private static IReadOnlyList<string> ToSortedList(HashSet<string> types)
    {
        if (types.Count == 0)
            return [];

        var list = new List<string>(types);
        list.Sort(StringComparer.Ordinal);
        return list;
    }
}
