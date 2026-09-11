using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;
using Obfy.Core.Models;
using Obfy.Core.Pipeline;
using Obfy.Core.Utilities;

namespace Obfy.Core.Obfuscators.Source;

/// <summary>
/// Renames symbols in C# source code using Roslyn's semantic model.
/// </summary>
/// <remarks>
/// Renaming is driven by resolved symbols, not identifier text: only symbols declared in the source
/// are renamed, and each identifier is rewritten based on the symbol it binds to. This keeps distinct
/// symbols that happen to share a name (locals in different methods, a field and an unrelated method
/// parameter, or a framework member with the same name) independent, and never touches identifiers
/// that resolve to types or members outside the source being obfuscated.
/// </remarks>
public class SourceSymbolRenamer : IObfuscator
{
    private readonly INameGenerator _nameGenerator;
    private readonly ILogger<SourceSymbolRenamer> _logger;

    public SourceSymbolRenamer(INameGenerator nameGenerator, ILogger<SourceSymbolRenamer> logger)
    {
        _nameGenerator = nameGenerator;
        _logger = logger;
    }

    /// <inheritdoc/>
    public string Name => "SourceSymbolRenaming";

    /// <inheritdoc/>
    public int Priority => 50;

    /// <inheritdoc/>
    public bool SupportsTargetType(TargetType targetType) => targetType == TargetType.SourceCode;

    /// <inheritdoc/>
    public bool IsEnabled(ObfySettings settings) => settings.SymbolRenaming.Enabled;

    /// <inheritdoc/>
    public async Task<ObfuscationResult> ObfuscateAsync(PipelineContext context, CancellationToken cancellationToken = default)
    {
        var compilation = context.Compilation!;
        var settings = context.Settings.SymbolRenaming;
        var exclusions = context.Settings.Exclusions;
        var stats = new ObfuscationStatistics();

        _logger.LogDebug("Starting source symbol renaming with mode {Mode}", settings.Mode);

        try
        {
            _nameGenerator.Reset();

            // Pass 1: choose a new name for every renamable source-declared symbol, keyed by symbol
            // identity so the decision is independent of how the name is spelled at each use site.
            var renames = new Dictionary<ISymbol, string>(SymbolEqualityComparer.Default);

            foreach (var tree in compilation.SyntaxTrees)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var model = compilation.GetSemanticModel(tree);
                var root = await tree.GetRootAsync(cancellationToken);

                foreach (var node in root.DescendantNodes())
                {
                    if (!TryGetDeclarationIdentifier(node, model, out _, out var symbol) || symbol == null)
                        continue;

                    var definition = symbol.OriginalDefinition;
                    if (renames.ContainsKey(definition))
                        continue;

                    if (!ShouldRenameDeclaration(node, definition, settings, exclusions))
                        continue;

                    renames[definition] = _nameGenerator.Generate(definition.Name, settings.Mode);
                }
            }

            if (renames.Count == 0)
            {
                _logger.LogInformation("No symbols eligible for renaming");
                return ObfuscationResult.Successful(stats);
            }

            // Pass 2: per tree, map each identifier token that binds to a renamed symbol to its new
            // name, then rewrite exactly those tokens.
            var newTrees = new List<SyntaxTree>();

            foreach (var tree in compilation.SyntaxTrees)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var model = compilation.GetSemanticModel(tree);
                var root = await tree.GetRootAsync(cancellationToken);

                var tokenRenames = new Dictionary<SyntaxToken, string>();

                foreach (var node in root.DescendantNodes())
                {
                    // Declaration identifiers
                    if (TryGetDeclarationIdentifier(node, model, out var token, out var declared) &&
                        declared != null &&
                        renames.TryGetValue(declared.OriginalDefinition, out var declName))
                    {
                        tokenRenames[token] = declName;
                        CountDeclaration(declared, stats);
                        continue;
                    }

                    // Constructor / destructor names spell the containing type, so they must follow
                    // the type's rename even though their own symbol is not the type.
                    switch (node)
                    {
                        case ConstructorDeclarationSyntax ctor:
                            MapContainingTypeName(ctor.Identifier, model.GetDeclaredSymbol(ctor), renames, tokenRenames);
                            break;
                        case DestructorDeclarationSyntax dtor:
                            MapContainingTypeName(dtor.Identifier, model.GetDeclaredSymbol(dtor), renames, tokenRenames);
                            break;
                    }
                }

                // References: any simple name (identifier or generic name) that binds to a renamed symbol.
                foreach (var name in root.DescendantNodes().OfType<SimpleNameSyntax>())
                {
                    var info = model.GetSymbolInfo(name, cancellationToken);
                    var symbol = (info.Symbol ?? info.CandidateSymbols.FirstOrDefault())?.OriginalDefinition;
                    if (symbol != null && renames.TryGetValue(symbol, out var refName))
                    {
                        tokenRenames[name.Identifier] = refName;
                    }
                }

                if (tokenRenames.Count == 0)
                {
                    newTrees.Add(tree);
                    continue;
                }

                var rewriter = new TokenRenamingRewriter(tokenRenames);
                var newRoot = rewriter.Visit(root);
                newTrees.Add(newRoot.SyntaxTree);
            }

            foreach (var (symbol, newName) in renames)
            {
                context.SymbolMap[$"{symbol.Kind}:{symbol.ToDisplayString()}"] = newName;
            }

            context.Compilation = compilation
                .RemoveAllSyntaxTrees()
                .AddSyntaxTrees(newTrees);

            _logger.LogInformation(
                "Renamed {Types} types, {Methods} methods, {Fields} fields, {Properties} properties, {Parameters} parameters",
                stats.TypesRenamed, stats.MethodsRenamed, stats.FieldsRenamed,
                stats.PropertiesRenamed, stats.ParametersRenamed);

            return ObfuscationResult.Successful(stats);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Source symbol renaming failed");
            return ObfuscationResult.Failed($"Source symbol renaming failed: {ex.Message}", ex);
        }
    }

    private static void MapContainingTypeName(
        SyntaxToken identifier,
        ISymbol? memberSymbol,
        Dictionary<ISymbol, string> renames,
        Dictionary<SyntaxToken, string> tokenRenames)
    {
        var type = memberSymbol?.ContainingType?.OriginalDefinition;
        if (type != null && renames.TryGetValue(type, out var newName))
        {
            tokenRenames[identifier] = newName;
        }
    }

    private static bool TryGetDeclarationIdentifier(SyntaxNode node, SemanticModel model, out SyntaxToken token, out ISymbol? symbol)
    {
        token = default;
        symbol = null;

        switch (node)
        {
            case ClassDeclarationSyntax c: token = c.Identifier; break;
            case StructDeclarationSyntax s: token = s.Identifier; break;
            case InterfaceDeclarationSyntax i: token = i.Identifier; break;
            case EnumDeclarationSyntax e: token = e.Identifier; break;
            case RecordDeclarationSyntax r: token = r.Identifier; break;
            case MethodDeclarationSyntax m: token = m.Identifier; break;
            case PropertyDeclarationSyntax p: token = p.Identifier; break;
            case ParameterSyntax pa: token = pa.Identifier; break;
            case VariableDeclaratorSyntax v: token = v.Identifier; break;
            default: return false;
        }

        symbol = model.GetDeclaredSymbol(node);
        return true;
    }

    private static bool ShouldRenameDeclaration(SyntaxNode node, ISymbol symbol, SymbolRenamingSettings settings, ExclusionRules exclusions)
    {
        switch (node)
        {
            case BaseTypeDeclarationSyntax:
                if (!settings.RenameTypes) return false;
                break;
            case MethodDeclarationSyntax method:
                if (!settings.RenameMethods) return false;
                if (method.Identifier.Text == "Main") return false;
                break;
            case PropertyDeclarationSyntax:
                if (!settings.RenameProperties) return false;
                break;
            case ParameterSyntax:
                if (!settings.RenameParameters) return false;
                break;
            case VariableDeclaratorSyntax v:
                if (v.Parent?.Parent is FieldDeclarationSyntax)
                {
                    if (!settings.RenameFields) return false;
                }
                else if (v.Parent?.Parent is not LocalDeclarationStatementSyntax)
                {
                    // Not a field or a local (e.g. an event or fixed buffer) — leave it alone.
                    return false;
                }
                break;
            default:
                return false;
        }

        return IsRenamable(symbol, settings, exclusions);
    }

    private static bool IsRenamable(ISymbol symbol, SymbolRenamingSettings settings, ExclusionRules exclusions)
    {
        // Never rename anything not declared in the source (framework/metadata symbols).
        if (!symbol.Locations.Any(l => l.IsInSource))
            return false;

        // Never rename implicitly declared members (auto-property backing fields, record members, ...).
        if (symbol.IsImplicitlyDeclared)
            return false;

        // Leave Obfy's own injected runtime helpers and their members untouched.
        if (symbol.Name.Contains("Obfy", StringComparison.Ordinal) ||
            symbol.ContainingType?.Name.Contains("Obfy", StringComparison.Ordinal) == true)
        {
            return false;
        }

        // Only ordinary methods are renamable directly; constructors follow their type, and operators
        // and accessors must keep their compiler-mandated names.
        if (symbol is IMethodSymbol { MethodKind: not MethodKind.Ordinary })
            return false;

        // Renaming an override, virtual/abstract member, or interface implementation would break the
        // contract with the base type or interface.
        if (symbol is IMethodSymbol or IPropertySymbol)
        {
            if (symbol.IsOverride || symbol.IsVirtual || symbol.IsAbstract)
                return false;
            if (ImplementsInterfaceMember(symbol))
                return false;
        }

        // Preserve the externally visible API surface if requested.
        if (settings.PreservePublicApi && IsExternallyVisible(symbol))
            return false;

        // Honor configured exclusion patterns.
        if (symbol.Kind == SymbolKind.NamedType && exclusions.Types.Any(t => MatchesPattern(symbol.Name, t)))
            return false;
        if (symbol.Kind == SymbolKind.Method && exclusions.Methods.Any(m => MatchesPattern(symbol.Name, m)))
            return false;

        return true;
    }

    private static bool ImplementsInterfaceMember(ISymbol symbol)
    {
        var type = symbol.ContainingType;
        if (type == null)
            return false;

        foreach (var iface in type.AllInterfaces)
        {
            foreach (var member in iface.GetMembers())
            {
                var implementation = type.FindImplementationForInterfaceMember(member);
                if (implementation != null && SymbolEqualityComparer.Default.Equals(implementation, symbol))
                    return true;
            }
        }

        return false;
    }

    private static bool IsExternallyVisible(ISymbol symbol)
    {
        // A parameter's visibility follows its containing method.
        var current = symbol is IParameterSymbol ? symbol.ContainingSymbol : symbol;

        while (current != null && current.Kind != SymbolKind.Namespace)
        {
            switch (current.DeclaredAccessibility)
            {
                case Accessibility.Public:
                case Accessibility.Protected:
                case Accessibility.ProtectedOrInternal:
                case Accessibility.NotApplicable:
                    break; // visible (or not access-controlled) at this level; keep checking containers
                default:
                    return false; // private/internal somewhere in the chain -> not externally visible
            }

            current = current.ContainingSymbol;
        }

        return true;
    }

    private static void CountDeclaration(ISymbol symbol, ObfuscationStatistics stats)
    {
        switch (symbol.Kind)
        {
            case SymbolKind.NamedType:
                stats.TypesRenamed++;
                break;
            case SymbolKind.Method:
                stats.MethodsRenamed++;
                break;
            case SymbolKind.Property:
                stats.PropertiesRenamed++;
                break;
            case SymbolKind.Field:
                stats.FieldsRenamed++;
                break;
            case SymbolKind.Parameter:
                stats.ParametersRenamed++;
                break;
        }
    }

    private static bool MatchesPattern(string value, string pattern)
    {
        if (pattern.EndsWith("*", StringComparison.Ordinal))
        {
            return value.StartsWith(pattern[..^1], StringComparison.OrdinalIgnoreCase);
        }
        return string.Equals(value, pattern, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Rewrites a fixed set of identifier tokens (identified by position within one tree) to new
    /// names. Only the tokens chosen via the semantic model are changed; every other token is left
    /// exactly as it was.
    /// </summary>
    private sealed class TokenRenamingRewriter : CSharpSyntaxRewriter
    {
        private readonly Dictionary<SyntaxToken, string> _tokenRenames;

        public TokenRenamingRewriter(Dictionary<SyntaxToken, string> tokenRenames)
        {
            _tokenRenames = tokenRenames;
        }

        public override SyntaxToken VisitToken(SyntaxToken token)
        {
            if (_tokenRenames.TryGetValue(token, out var newName))
            {
                return SyntaxFactory.Identifier(newName).WithTriviaFrom(token);
            }
            return base.VisitToken(token);
        }
    }
}
