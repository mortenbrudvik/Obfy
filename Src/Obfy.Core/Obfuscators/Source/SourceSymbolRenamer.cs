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
    public int Priority => (int)ObfuscationPhase.SymbolRenaming;

    /// <inheritdoc/>
    public bool SupportsTargetType(TargetType targetType) => targetType == TargetType.SourceCode;

    /// <inheritdoc/>
    public bool IsEnabled(ObfySettings settings) => settings.SymbolRenaming.Enabled;

    /// <inheritdoc/>
    public async Task<ObfuscationResult> ObfuscateAsync(PipelineContext context, CancellationToken cancellationToken = default)
    {
        var compilation = context.RequireCompilation();
        var settings = context.Settings.SymbolRenaming;
        var scope = new SourceScope(context.Settings);
        var stats = new ObfuscationStatistics();

        _logger.LogDebug("Starting source symbol renaming with mode {Mode}", settings.Mode);

        try
        {
            _nameGenerator.Reset();
            scope.WarnIfInclusionsMatchedNothing(compilation, context.Warnings);

            // Pass 1: choose a new name for every renamable source-declared symbol, keyed by symbol
            // identity so the decision is independent of how the name is spelled at each use site.
            var renames = new Dictionary<ISymbol, string>(SymbolEqualityComparer.Default);

            // A generated name must not equal an identifier the code already uses: a kept parameter "a"
            // would capture a renamed method "a" at its call sites (CS0149), and so on.
            var reservedNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var tree in compilation.SyntaxTrees)
            {
                var treeRoot = await tree.GetRootAsync(cancellationToken).ConfigureAwait(false);
                foreach (var token in treeRoot.DescendantTokens())
                {
                    if (token.IsKind(SyntaxKind.IdentifierToken))
                        reservedNames.Add(token.ValueText);
                }
            }

            foreach (var tree in compilation.SyntaxTrees)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var model = compilation.GetSemanticModel(tree);
                var root = await tree.GetRootAsync(cancellationToken).ConfigureAwait(false);

                foreach (var node in root.DescendantNodes())
                {
                    if (!TryGetDeclarationIdentifier(node, model, out _, out var symbol) || symbol == null)
                        continue;

                    var definition = Canonical(symbol);
                    if (renames.ContainsKey(definition))
                        continue;

                    if (!ShouldRenameDeclaration(node, definition, settings, scope))
                        continue;
                    if (ObfuscationAttributeRules.IsExcluded(definition, ObfuscationFeature.Renaming))
                        continue;
                    if (IsExcludedOnPartialImplementation(definition))
                        continue;

                    // A record positional parameter also declares a synthesized property of the same
                    // name. Check the backing property against all renaming rules before accepting the pair,
                    // or a positional property implementing an interface / excluded / XAML-bindable gets broken.
                    IPropertySymbol? recordProperty = null;
                    if (definition is IParameterSymbol param &&
                        node.Parent?.Parent is RecordDeclarationSyntax &&
                        param.ContainingType is { } recordType)
                    {
                        recordProperty = recordType.GetMembers(param.Name).OfType<IPropertySymbol>().FirstOrDefault();
                        if (recordProperty != null && !ShouldRenameRecordProperty(recordProperty, settings, scope))
                            continue;
                    }

                    string newName;
                    do
                    {
                        newName = _nameGenerator.Generate(definition.Name, settings.Mode);
                    } while (reservedNames.Contains(newName));
                    renames[definition] = newName;

                    if (recordProperty != null)
                        renames[recordProperty.OriginalDefinition] = newName;
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
                var root = await tree.GetRootAsync(cancellationToken).ConfigureAwait(false);

                var tokenRenames = new Dictionary<SyntaxToken, string>();

                foreach (var node in root.DescendantNodes())
                {
                    // Declaration identifiers
                    if (TryGetDeclarationIdentifier(node, model, out var token, out var declared) &&
                        declared != null &&
                        renames.TryGetValue(Canonical(declared), out var declName))
                    {
                        tokenRenames[token] = declName;
                        // A partial method/property is declared twice (definition and implementation) but is one member.
                        if (!IsPartialImplementationPart(declared))
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
                    var symbol = info.Symbol ?? info.CandidateSymbols.FirstOrDefault();
                    if (symbol is IMethodSymbol { ReducedFrom: { } reduced })
                        symbol = reduced;
                    if (symbol == null)
                        continue;

                    symbol = Canonical(symbol);

                    if (renames.TryGetValue(symbol, out var refName))
                    {
                        tokenRenames[name.Identifier] = refName;
                        continue;
                    }

                    // Attribute names bind to the constructor, not the type. Constructors are not
                    // entered into `renames` (only ordinary methods are), so look up the type.
                    if (symbol is IMethodSymbol { MethodKind: MethodKind.Constructor } ctor)
                    {
                        var type = ctor.ContainingType.OriginalDefinition;
                        if (renames.TryGetValue(type, out var typeName))
                            tokenRenames[name.Identifier] = typeName;
                    }
                }

                if (tokenRenames.Count == 0)
                {
                    newTrees.Add(tree);
                    continue;
                }

                var rewriter = new TokenRenamingRewriter(tokenRenames);
                var newRoot = rewriter.Visit(root);
                newTrees.Add(newRoot.SyntaxTree.WithFilePath(tree.FilePath));
            }

            // Keys are symbol identities. Locals and parameters print as just their name, so they carry
            // their owner, and a key that is still taken (two "i" in one method) gets a number.
            var usedKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (symbol, newName) in renames)
            {
                var key = $"{symbol.Kind}:{SymbolMapDisplay(symbol)}";
                var unique = key;
                for (var n = 2; !usedKeys.Add(unique); n++)
                    unique = $"{key}#{n}";
                context.SymbolMap[unique] = newName;
            }

            var newCompilation = compilation
                .RemoveAllSyntaxTrees()
                .AddSyntaxTrees(newTrees);
            context.Compilation = newCompilation;
            SourceOutputCheck.WarnOnNewErrors(
                "Source symbol renaming", compilation, newCompilation, context.Warnings, cancellationToken);

            _logger.LogInformation(
                "Renamed {Types} types, {Methods} methods, {Fields} fields, {Properties} properties, {Parameters} parameters",
                stats.TypesRenamed, stats.MethodsRenamed, stats.FieldsRenamed,
                stats.PropertiesRenamed, stats.ParametersRenamed);

            return ObfuscationResult.Successful(stats);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError("Source symbol renaming failed ({ExceptionType})", ex.GetType().Name);
            return ObfuscationResult.Failed($"Source symbol renaming failed: {ex.Message}", ex);
        }
    }

    private static readonly HashSet<string> CompilerPatternMembers = new(StringComparer.Ordinal)
    {
        "GetEnumerator",
        "GetAsyncEnumerator",
        "MoveNext",
        "MoveNextAsync",
        "Current",
        "GetAwaiter",
        "IsCompleted",
        "GetResult",
        "OnCompleted",
        "UnsafeOnCompleted",
        "Deconstruct",
        "Add",
        "Dispose",
        "DisposeAsync",
        "Slice",
        "PrintMembers",
        "Select",
        "SelectMany",
        "Where",
        "OrderBy",
        "OrderByDescending",
        "ThenBy",
        "ThenByDescending",
        "GroupBy",
        "Join",
        "GroupJoin"
    };

    /// <summary>
    /// The symbol that stands for a declaration everywhere: the generic definition and the defining part
    /// of a partial method/property (its implementation and parameters are declared separately but share the name).
    /// </summary>
    private static ISymbol Canonical(ISymbol symbol)
    {
        symbol = symbol.OriginalDefinition;
        return symbol switch
        {
            IMethodSymbol { PartialDefinitionPart: { } definition } => definition.OriginalDefinition,
            IPropertySymbol { PartialDefinitionPart: { } definition } => definition.OriginalDefinition,
            IParameterSymbol { ContainingSymbol: IMethodSymbol { AssociatedSymbol: IPropertySymbol prop } } param
                when param.Ordinal >= 0 && param.Ordinal < prop.OriginalDefinition.Parameters.Length
                => Canonical(prop.OriginalDefinition.Parameters[param.Ordinal]),
            IParameterSymbol { ContainingSymbol: IMethodSymbol { PartialDefinitionPart: { } methodDef } } param
                when param.Ordinal >= 0 && param.Ordinal < methodDef.OriginalDefinition.Parameters.Length
                => methodDef.OriginalDefinition.Parameters[param.Ordinal],
            IParameterSymbol { ContainingSymbol: IPropertySymbol { PartialDefinitionPart: { } propDef } } param
                when param.Ordinal >= 0 && param.Ordinal < propDef.OriginalDefinition.Parameters.Length
                => propDef.OriginalDefinition.Parameters[param.Ordinal],
            _ => symbol
        };
    }

    private static bool IsPartialImplementationPart(ISymbol symbol) => symbol switch
    {
        IMethodSymbol { PartialDefinitionPart: not null } => true,
        IPropertySymbol { PartialDefinitionPart: not null } => true,
        IParameterSymbol { ContainingSymbol: IMethodSymbol { PartialDefinitionPart: not null } } => true,
        IParameterSymbol { ContainingSymbol: IPropertySymbol { PartialDefinitionPart: not null } } => true,
        _ => false
    };

    private static bool IsExcludedOnPartialImplementation(ISymbol symbol) => symbol switch
    {
        IMethodSymbol { PartialImplementationPart: { } impl } => ObfuscationAttributeRules.IsExcluded(impl, ObfuscationFeature.Renaming),
        IPropertySymbol { PartialImplementationPart: { } impl } => ObfuscationAttributeRules.IsExcluded(impl, ObfuscationFeature.Renaming),
        IParameterSymbol { ContainingSymbol: IMethodSymbol { PartialImplementationPart: { } impl } } param
            when param.Ordinal >= 0 && param.Ordinal < impl.Parameters.Length
            => ObfuscationAttributeRules.IsExcluded(impl.Parameters[param.Ordinal], ObfuscationFeature.Renaming),
        IParameterSymbol { ContainingSymbol: IPropertySymbol { PartialImplementationPart: { } impl } } param
            when param.Ordinal >= 0 && param.Ordinal < impl.Parameters.Length
            => ObfuscationAttributeRules.IsExcluded(impl.Parameters[param.Ordinal], ObfuscationFeature.Renaming),
        _ => false
    };

    private static string SymbolMapDisplay(ISymbol symbol) =>
        symbol is ILocalSymbol or IParameterSymbol
            ? $"{symbol.ContainingSymbol?.ToDisplayString()}.{symbol.Name}"
            : symbol.ToDisplayString();

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
            case EventDeclarationSyntax ev: token = ev.Identifier; break;
            case ParameterSyntax pa: token = pa.Identifier; break;
            case VariableDeclaratorSyntax v: token = v.Identifier; break;
            default: return false;
        }

        symbol = model.GetDeclaredSymbol(node);
        return true;
    }

    private static bool ShouldRenameDeclaration(SyntaxNode node, ISymbol symbol, SymbolRenamingSettings settings, SourceScope scope)
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
            case EventDeclarationSyntax:
                if (!settings.RenameEvents) return false;
                break;
            case ParameterSyntax:
                if (!settings.RenameParameters) return false;
                break;
            case VariableDeclaratorSyntax v:
                if (v.Parent?.Parent is FieldDeclarationSyntax)
                {
                    if (!settings.RenameFields) return false;
                }
                else if (v.Parent?.Parent is EventFieldDeclarationSyntax)
                {
                    if (!settings.RenameEvents) return false;
                }
                else if (v.Parent?.Parent is not LocalDeclarationStatementSyntax)
                {
                    return false;
                }
                break;
            default:
                return false;
        }

        return IsRenamable(symbol, settings, scope);
    }

    private static bool ShouldRenameRecordProperty(
        IPropertySymbol property, SymbolRenamingSettings settings, SourceScope scope)
    {
        if (!settings.RenameProperties)
            return false;

        if (!IsRenamable(property, settings, scope, allowImplicitlyDeclared: true))
            return false;

        return !ObfuscationAttributeRules.IsExcluded(property, ObfuscationFeature.Renaming);
    }

    private static bool IsRenamable(
        ISymbol symbol, SymbolRenamingSettings settings, SourceScope scope, bool allowImplicitlyDeclared = false)
    {
        // Never rename anything not declared in the source (framework/metadata symbols).
        if (!symbol.Locations.Any(l => l.IsInSource))
            return false;

        // Never rename implicitly declared members (auto-property backing fields, record members, ...),
        // unless checking a record positional parameter's synthesized backing property.
        if (!allowImplicitlyDeclared && symbol.IsImplicitlyDeclared)
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

        // C# duck-typed compiler pattern members (foreach, await, deconstruction, LINQ query syntax, etc.)
        // are looked up by exact name rather than interface/override dispatch.
        if (symbol is IMethodSymbol or IPropertySymbol && CompilerPatternMembers.Contains(symbol.Name))
            return false;

        // Renaming an override, virtual/abstract member, or interface implementation would break the
        // contract with the base type or interface. Events follow the same rule (an event that
        // implements INotifyPropertyChanged.PropertyChanged must keep its name).
        if (symbol is IMethodSymbol or IPropertySymbol or IEventSymbol && IsContractKeptMember(symbol))
            return false;

        // Keep parameter names on methods/indexers that override or implement a base/interface/virtual contract.
        if (symbol is IParameterSymbol parameter && HasContractKeptContainingMember(parameter))
            return false;

        // Preserve the externally visible API surface if requested.
        if (settings.PreservePublicApi && IsExternallyVisible(symbol))
            return false;

        // Exclusions: an excluded namespace, type name or attribute protects the type and everything in
        // it; excluded method names protect those methods.
        if (scope.IsExcluded(symbol))
            return false;
        if (symbol.Kind == SymbolKind.Method && scope.IsMethodNameExcluded(symbol))
            return false;

        // Allow-list: only matching types (and their members) and matching methods are candidates.
        if (!scope.IsIncluded(symbol, methodNameCounts: symbol is IMethodSymbol or IParameterSymbol or ILocalSymbol))
            return false;

        // Public instance properties of XAML-bindable types are looked up by name from markup.
        if (settings.PreserveXaml &&
            symbol is IPropertySymbol { DeclaredAccessibility: Accessibility.Public, IsStatic: false } &&
            symbol.ContainingType is { } owner &&
            LooksLikeXamlBindable(owner))
        {
            return false;
        }

        return true;
    }

    private static bool HasContractKeptContainingMember(IParameterSymbol parameter) => parameter.ContainingSymbol switch
    {
        IMethodSymbol method => IsContractKeptMember(method),
        IPropertySymbol property => IsContractKeptMember(property),
        _ => false
    };

    private static bool IsContractKeptMember(ISymbol symbol)
    {
        if (symbol.IsOverride || symbol.IsVirtual || symbol.IsAbstract)
            return true;
        if (symbol.ContainingType?.TypeKind == TypeKind.Interface)
            return true;
        if (symbol is IMethodSymbol { ExplicitInterfaceImplementations.Length: > 0 } or
            IPropertySymbol { ExplicitInterfaceImplementations.Length: > 0 } or
            IEventSymbol { ExplicitInterfaceImplementations.Length: > 0 })
        {
            return true;
        }

        return ImplementsInterfaceMember(symbol);
    }

    /// <summary>
    /// Heuristic for XAML-bindable types, mirroring the assembly renamer: a name ending in ViewModel or
    /// View, INotifyPropertyChanged, a DependencyProperty field, or a DependencyObject base. WPF bases
    /// usually do not resolve here, so the name checks carry most of the weight.
    /// </summary>
    private static bool LooksLikeXamlBindable(INamedTypeSymbol type)
    {
        if (type.Name.EndsWith("ViewModel", StringComparison.OrdinalIgnoreCase) ||
            type.Name.EndsWith("View", StringComparison.OrdinalIgnoreCase))
            return true;

        if (type.AllInterfaces.Any(i => i.Name.Contains("INotifyPropertyChanged", StringComparison.Ordinal)))
            return true;

        for (var current = type; current != null; current = current.BaseType)
        {
            if (current.Name.Contains("DependencyObject", StringComparison.Ordinal))
                return true;
            if (current.GetMembers().OfType<IFieldSymbol>().Any(f => f.Type.Name == "DependencyProperty"))
                return true;
        }

        return false;
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
            case SymbolKind.Event:
                stats.EventsRenamed++;
                break;
        }
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

        /// <summary>
        /// <c>new { p.Name }</c> names its member after the property. When the property is renamed, the
        /// member would be renamed with it and break every <c>x.Name</c> consumer (and change what a
        /// serializer writes), so the original name is spelled out: <c>new { Name = p.A }</c>.
        /// </summary>
        public override SyntaxNode? VisitAnonymousObjectMemberDeclarator(AnonymousObjectMemberDeclaratorSyntax node)
        {
            if (node.NameEquals == null &&
                InferredNameToken(node.Expression) is { } token &&
                _tokenRenames.ContainsKey(token))
            {
                var expression = (ExpressionSyntax)Visit(node.Expression)!;
                var nameEquals = SyntaxFactory.NameEquals(
                    SyntaxFactory.IdentifierName(SyntaxFactory.Identifier(token.ValueText))
                        .WithLeadingTrivia(expression.GetLeadingTrivia()));
                return node.WithNameEquals(nameEquals).WithExpression(expression.WithoutLeadingTrivia());
            }

            return base.VisitAnonymousObjectMemberDeclarator(node);
        }

        /// <summary>
        /// Same for tuples: <c>(p.Name, p.Age)</c> infers element names. Elements whose inferred name
        /// would not apply anyway (a duplicate, or a reserved name such as Item1) are left alone.
        /// </summary>
        public override SyntaxNode? VisitTupleExpression(TupleExpressionSyntax node)
        {
            var inferred = node.Arguments
                .Select(a => a.NameColon == null ? InferredNameToken(a.Expression) : null)
                .ToList();

            var explicitNames = node.Arguments
                .Where(a => a.NameColon != null)
                .Select(a => a.NameColon!.Name.Identifier.ValueText)
                .ToHashSet(StringComparer.Ordinal);

            var isDeconstructionTarget = node.Parent is AssignmentExpressionSyntax assignment && assignment.Left == node;
            var arguments = new List<ArgumentSyntax>(node.Arguments.Count);
            var changed = false;

            for (var i = 0; i < node.Arguments.Count; i++)
            {
                var argument = node.Arguments[i];
                if (!isDeconstructionTarget &&
                    inferred[i] is { } nameToken &&
                    _tokenRenames.ContainsKey(nameToken) &&
                    CanNameTupleElement(nameToken.ValueText, i, inferred, explicitNames))
                {
                    var expression = (ExpressionSyntax)Visit(argument.Expression)!;
                    var nameColon = SyntaxFactory.NameColon(
                        SyntaxFactory.IdentifierName(SyntaxFactory.Identifier(nameToken.ValueText))
                            .WithLeadingTrivia(expression.GetLeadingTrivia()),
                        SyntaxFactory.Token(SyntaxKind.ColonToken).WithTrailingTrivia(SyntaxFactory.Space));
                    arguments.Add(argument.WithNameColon(nameColon).WithExpression(expression.WithoutLeadingTrivia()));
                    changed = true;
                }
                else
                {
                    arguments.Add((ArgumentSyntax)Visit(argument)!);
                }
            }

            return changed
                ? node.WithArguments(SyntaxFactory.SeparatedList(arguments, node.Arguments.GetSeparators()))
                : base.VisitTupleExpression(node);
        }

        private static bool CanNameTupleElement(
            string name, int index, List<SyntaxToken?> inferred, HashSet<string> explicitNames)
        {
            if (explicitNames.Contains(name))
                return false;

            // Two elements inferring the same name get no names at all.
            for (var i = 0; i < inferred.Count; i++)
            {
                if (i != index && inferred[i] is { } other && other.ValueText == name)
                    return false;
            }

            // Item1, Rest and the object members cannot be used as inferred element names.
            var reservedItem = name.StartsWith("Item", StringComparison.Ordinal) &&
                               name.Length > 4 && name.Skip(4).All(char.IsDigit);
            return !(reservedItem || name is "Rest" or "ToString" or "GetHashCode" or "Equals" or "GetType");
        }

        /// <summary>The identifier C# infers a member or element name from: x, a.x or a?.x.</summary>
        private static SyntaxToken? InferredNameToken(ExpressionSyntax expression) => expression switch
        {
            IdentifierNameSyntax name => name.Identifier,
            MemberAccessExpressionSyntax { Name: IdentifierNameSyntax name } => name.Identifier,
            MemberBindingExpressionSyntax { Name: IdentifierNameSyntax name } => name.Identifier,
            ConditionalAccessExpressionSyntax access => InferredNameToken(access.WhenNotNull),
            _ => null
        };
    }
}
