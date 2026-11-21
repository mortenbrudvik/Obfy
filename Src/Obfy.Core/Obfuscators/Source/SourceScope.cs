using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Obfy.Core.Models;
using Obfy.Core.Utilities;

namespace Obfy.Core.Obfuscators.Source;

/// <summary>
/// Applies the user's scope rules (<c>Exclusions</c> and <c>Inclusions</c>) to Roslyn symbols, the way
/// the assembly obfuscators apply them to dnlib types and methods: a type excluded by namespace, name
/// or attribute protects everything inside it, and an allow-list limits every technique to the matching
/// types and methods.
/// </summary>
internal sealed class SourceScope
{
    public const string InclusionsMatchedNothing =
        "Inclusions matched 0 types/methods; nothing was obfuscated. Check namespaces/types/methods patterns.";

    private readonly ExclusionRules _exclusions;
    private readonly InclusionRules _inclusions;
    private readonly Dictionary<ISymbol, bool> _allowed = new(SymbolEqualityComparer.Default);

    public SourceScope(ObfySettings settings)
    {
        _exclusions = settings.Exclusions;
        _inclusions = settings.Inclusions;
    }

    public bool HasInclusions => _inclusions.HasAny;

    /// <summary>
    /// A member (or the code inside it) may be transformed: not excluded by namespace, type, method or
    /// attribute rules, and inside the allow-list when there is one.
    /// </summary>
    public bool Allows(ISymbol? symbol)
    {
        if (symbol == null)
            return !HasInclusions;

        // Asked once per string literal, so the answer is remembered per enclosing symbol.
        if (_allowed.TryGetValue(symbol, out var cached))
            return cached;

        var allowed = !IsExcluded(symbol) && !IsMethodNameExcluded(symbol) && IsIncluded(symbol, methodNameCounts: true);
        _allowed[symbol] = allowed;
        return allowed;
    }

    /// <summary>
    /// The symbol, or a type that contains it, is excluded by an excluded namespace, type name or
    /// attribute (member attributes count too).
    /// </summary>
    public bool IsExcluded(ISymbol symbol)
    {
        for (var current = Normalize(symbol); current != null && current is not INamespaceSymbol; current = current.ContainingSymbol)
        {
            if (HasExcludedAttribute(current))
                return true;

            if (current is INamedTypeSymbol type)
            {
                if (_exclusions.Types.Any(p => WildcardMatcher.IsMatch(type.Name, p)))
                    return true;
                if (_exclusions.Namespaces.Any(p => WildcardMatcher.IsMatch(NamespaceOf(type), p)))
                    return true;
            }
        }

        return false;
    }

    /// <summary>The nearest named method (not a lambda or accessor) matches an excluded method pattern.</summary>
    public bool IsMethodNameExcluded(ISymbol symbol)
    {
        if (_exclusions.Methods.Count == 0)
            return false;

        var method = NearestMethod(symbol);
        return method != null && _exclusions.Methods.Any(p => WildcardMatcher.IsMatch(method.Name, p));
    }

    /// <summary>
    /// Inside the allow-list: the symbol's type (or a type around it) matches a namespace or type
    /// pattern, or, when <paramref name="methodNameCounts"/> is set, its nearest method matches a
    /// method pattern. Always true without an allow-list.
    /// </summary>
    public bool IsIncluded(ISymbol symbol, bool methodNameCounts)
    {
        if (!HasInclusions)
            return true;

        var type = symbol as INamedTypeSymbol ?? symbol.ContainingType;
        if (type != null && TypeMatchesInclusions(type))
            return true;

        if (methodNameCounts && symbol is not INamedTypeSymbol && _inclusions.Methods.Count > 0)
        {
            var method = NearestMethod(symbol);
            if (method != null && _inclusions.Methods.Any(p => WildcardMatcher.IsMatch(method.Name, p)))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Adds a warning when an allow-list is set but no type in the compilation can match it, so the run
    /// does not silently do nothing.
    /// </summary>
    public void WarnIfInclusionsMatchedNothing(Compilation compilation, ICollection<string> warnings)
    {
        if (!HasInclusions || warnings.Contains(InclusionsMatchedNothing))
            return;

        foreach (var tree in compilation.SyntaxTrees)
        {
            var model = compilation.GetSemanticModel(tree);
            foreach (var declaration in tree.GetRoot().DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
            {
                if (model.GetDeclaredSymbol(declaration) is not INamedTypeSymbol type)
                    continue;

                if (TypeMatchesInclusions(type))
                    return;

                if (_inclusions.Methods.Count > 0 &&
                    type.GetMembers().OfType<IMethodSymbol>()
                        .Any(m => _inclusions.Methods.Any(p => WildcardMatcher.IsMatch(m.Name, p))))
                    return;
            }
        }

        warnings.Add(InclusionsMatchedNothing);
    }

    private bool TypeMatchesInclusions(INamedTypeSymbol type)
    {
        for (var current = type; current != null; current = current.ContainingType)
        {
            if (_inclusions.Types.Any(p => WildcardMatcher.IsMatch(current.Name, p)))
                return true;
            if (_inclusions.Namespaces.Any(p => WildcardMatcher.IsMatch(NamespaceOf(current), p)))
                return true;
        }

        return false;
    }

    private bool HasExcludedAttribute(ISymbol symbol)
    {
        if (_exclusions.Attributes.Count == 0)
            return false;

        foreach (var attribute in symbol.GetAttributes())
        {
            var fullName = attribute.AttributeClass?.ToDisplayString();
            if (!string.IsNullOrEmpty(fullName) &&
                _exclusions.Attributes.Any(p => ObfuscatorHelpers.MatchesAttribute(fullName, p)))
                return true;
        }

        return false;
    }

    private static string NamespaceOf(INamedTypeSymbol type) =>
        type.ContainingNamespace is { IsGlobalNamespace: false } ns ? ns.ToDisplayString() : string.Empty;

    /// <summary>An accessor stands for its property or event, so the member's own attributes apply.</summary>
    private static ISymbol? Normalize(ISymbol symbol) =>
        symbol is IMethodSymbol { AssociatedSymbol: { } associated } ? associated : symbol;

    private static IMethodSymbol? NearestMethod(ISymbol symbol)
    {
        for (var current = symbol; current != null; current = current.ContainingSymbol)
        {
            if (current is not IMethodSymbol method)
            {
                if (current is INamedTypeSymbol or INamespaceSymbol)
                    return null;
                continue;
            }

            if (method.AssociatedSymbol != null)
                return null;

            if (method.MethodKind is MethodKind.AnonymousFunction or MethodKind.LocalFunction)
                continue;

            return method;
        }

        return null;
    }
}
