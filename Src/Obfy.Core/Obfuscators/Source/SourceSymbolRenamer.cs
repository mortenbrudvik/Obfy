using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;
using Obfy.Core.Models;
using Obfy.Core.Pipeline;
using Obfy.Core.Utilities;

namespace Obfy.Core.Obfuscators.Source;

/// <summary>
/// Renames symbols in C# source code using Roslyn.
/// </summary>
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
        var stats = new ObfuscationStatistics();

        _logger.LogDebug("Starting source symbol renaming with mode {Mode}", settings.Mode);

        try
        {
            _nameGenerator.Reset();

            // First pass: collect all symbols to rename
            var symbolMap = new Dictionary<string, string>();

            foreach (var tree in compilation.SyntaxTrees)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var root = await tree.GetRootAsync(cancellationToken);
                var semanticModel = compilation.GetSemanticModel(tree);

                CollectSymbols(root, semanticModel, settings, symbolMap, context.Settings.Exclusions);
            }

            // Second pass: apply renames
            var newTrees = new List<SyntaxTree>();

            foreach (var tree in compilation.SyntaxTrees)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var root = await tree.GetRootAsync(cancellationToken);
                var rewriter = new SymbolRenamingRewriter(symbolMap, settings);
                var newRoot = rewriter.Visit(root);

                stats.TypesRenamed += rewriter.TypesRenamed;
                stats.MethodsRenamed += rewriter.MethodsRenamed;
                stats.FieldsRenamed += rewriter.FieldsRenamed;
                stats.PropertiesRenamed += rewriter.PropertiesRenamed;
                stats.ParametersRenamed += rewriter.ParametersRenamed;

                newTrees.Add(newRoot.SyntaxTree);
            }

            // Store symbol map in context
            foreach (var (key, value) in symbolMap)
            {
                context.SymbolMap[key] = value;
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

    private void CollectSymbols(
        SyntaxNode root,
        SemanticModel semanticModel,
        SymbolRenamingSettings settings,
        Dictionary<string, string> symbolMap,
        ExclusionRules exclusions)
    {
        foreach (var node in root.DescendantNodes())
        {
            switch (node)
            {
                case ClassDeclarationSyntax classDecl when settings.RenameTypes:
                    if (ShouldRename(classDecl.Identifier.Text, classDecl.Modifiers, settings, exclusions))
                    {
                        var key = $"Type:{classDecl.Identifier.Text}";
                        if (!symbolMap.ContainsKey(key))
                        {
                            symbolMap[key] = _nameGenerator.Generate(classDecl.Identifier.Text, settings.Mode);
                        }
                    }
                    break;

                case StructDeclarationSyntax structDecl when settings.RenameTypes:
                    if (ShouldRename(structDecl.Identifier.Text, structDecl.Modifiers, settings, exclusions))
                    {
                        var key = $"Type:{structDecl.Identifier.Text}";
                        if (!symbolMap.ContainsKey(key))
                        {
                            symbolMap[key] = _nameGenerator.Generate(structDecl.Identifier.Text, settings.Mode);
                        }
                    }
                    break;

                case MethodDeclarationSyntax methodDecl when settings.RenameMethods:
                    if (ShouldRenameMethod(methodDecl, settings, exclusions))
                    {
                        var key = $"Method:{methodDecl.Identifier.Text}";
                        if (!symbolMap.ContainsKey(key))
                        {
                            symbolMap[key] = _nameGenerator.Generate(methodDecl.Identifier.Text, settings.Mode);
                        }
                    }
                    break;

                case FieldDeclarationSyntax fieldDecl when settings.RenameFields:
                    foreach (var variable in fieldDecl.Declaration.Variables)
                    {
                        if (ShouldRename(variable.Identifier.Text, fieldDecl.Modifiers, settings, exclusions))
                        {
                            var key = $"Field:{variable.Identifier.Text}";
                            if (!symbolMap.ContainsKey(key))
                            {
                                symbolMap[key] = _nameGenerator.Generate(variable.Identifier.Text, settings.Mode);
                            }
                        }
                    }
                    break;

                case PropertyDeclarationSyntax propDecl when settings.RenameProperties:
                    if (ShouldRename(propDecl.Identifier.Text, propDecl.Modifiers, settings, exclusions))
                    {
                        var key = $"Property:{propDecl.Identifier.Text}";
                        if (!symbolMap.ContainsKey(key))
                        {
                            symbolMap[key] = _nameGenerator.Generate(propDecl.Identifier.Text, settings.Mode);
                        }
                    }
                    break;

                case ParameterSyntax param when settings.RenameParameters:
                    var paramKey = $"Parameter:{param.Identifier.Text}";
                    if (!symbolMap.ContainsKey(paramKey))
                    {
                        symbolMap[paramKey] = _nameGenerator.Generate(param.Identifier.Text, settings.Mode);
                    }
                    break;

                case VariableDeclaratorSyntax varDecl:
                    // Local variables
                    if (varDecl.Parent?.Parent is LocalDeclarationStatementSyntax)
                    {
                        var key = $"Local:{varDecl.Identifier.Text}";
                        if (!symbolMap.ContainsKey(key))
                        {
                            symbolMap[key] = _nameGenerator.Generate(varDecl.Identifier.Text, settings.Mode);
                        }
                    }
                    break;
            }
        }
    }

    private bool ShouldRename(string name, SyntaxTokenList modifiers, SymbolRenamingSettings settings, ExclusionRules exclusions)
    {
        // Check exclusions
        if (exclusions.Types.Any(t => MatchesPattern(name, t)))
            return false;

        // Preserve public API if configured
        if (settings.PreservePublicApi && modifiers.Any(m => m.IsKind(SyntaxKind.PublicKeyword)))
            return false;

        // Don't rename special names
        if (name.StartsWith("_") && name.Contains("Obfy"))
            return false;

        return true;
    }

    private bool ShouldRenameMethod(MethodDeclarationSyntax method, SymbolRenamingSettings settings, ExclusionRules exclusions)
    {
        var name = method.Identifier.Text;

        // Don't rename Main
        if (name == "Main")
            return false;

        // Check exclusions
        if (exclusions.Methods.Any(m => MatchesPattern(name, m)))
            return false;

        // Preserve public API if configured
        if (settings.PreservePublicApi && method.Modifiers.Any(m => m.IsKind(SyntaxKind.PublicKeyword)))
            return false;

        // Don't rename override methods
        if (method.Modifiers.Any(m => m.IsKind(SyntaxKind.OverrideKeyword)))
            return false;

        return true;
    }

    private static bool MatchesPattern(string value, string pattern)
    {
        if (pattern.EndsWith("*"))
        {
            return value.StartsWith(pattern[..^1], StringComparison.OrdinalIgnoreCase);
        }
        return string.Equals(value, pattern, StringComparison.OrdinalIgnoreCase);
    }

    private class SymbolRenamingRewriter : CSharpSyntaxRewriter
    {
        private readonly Dictionary<string, string> _symbolMap;
        private readonly SymbolRenamingSettings _settings;

        public int TypesRenamed { get; private set; }
        public int MethodsRenamed { get; private set; }
        public int FieldsRenamed { get; private set; }
        public int PropertiesRenamed { get; private set; }
        public int ParametersRenamed { get; private set; }

        public SymbolRenamingRewriter(Dictionary<string, string> symbolMap, SymbolRenamingSettings settings)
        {
            _symbolMap = symbolMap;
            _settings = settings;
        }

        public override SyntaxNode? VisitClassDeclaration(ClassDeclarationSyntax node)
        {
            var key = $"Type:{node.Identifier.Text}";
            if (_symbolMap.TryGetValue(key, out var newName))
            {
                TypesRenamed++;
                node = node.WithIdentifier(SyntaxFactory.Identifier(newName).WithTriviaFrom(node.Identifier));
            }
            return base.VisitClassDeclaration(node);
        }

        public override SyntaxNode? VisitStructDeclaration(StructDeclarationSyntax node)
        {
            var key = $"Type:{node.Identifier.Text}";
            if (_symbolMap.TryGetValue(key, out var newName))
            {
                TypesRenamed++;
                node = node.WithIdentifier(SyntaxFactory.Identifier(newName).WithTriviaFrom(node.Identifier));
            }
            return base.VisitStructDeclaration(node);
        }

        public override SyntaxNode? VisitMethodDeclaration(MethodDeclarationSyntax node)
        {
            var key = $"Method:{node.Identifier.Text}";
            if (_symbolMap.TryGetValue(key, out var newName))
            {
                MethodsRenamed++;
                node = node.WithIdentifier(SyntaxFactory.Identifier(newName).WithTriviaFrom(node.Identifier));
            }
            return base.VisitMethodDeclaration(node);
        }

        public override SyntaxNode? VisitPropertyDeclaration(PropertyDeclarationSyntax node)
        {
            var key = $"Property:{node.Identifier.Text}";
            if (_symbolMap.TryGetValue(key, out var newName))
            {
                PropertiesRenamed++;
                node = node.WithIdentifier(SyntaxFactory.Identifier(newName).WithTriviaFrom(node.Identifier));
            }
            return base.VisitPropertyDeclaration(node);
        }

        public override SyntaxNode? VisitVariableDeclarator(VariableDeclaratorSyntax node)
        {
            // Check if it's a field
            var fieldKey = $"Field:{node.Identifier.Text}";
            if (_symbolMap.TryGetValue(fieldKey, out var fieldName))
            {
                FieldsRenamed++;
                return node.WithIdentifier(SyntaxFactory.Identifier(fieldName).WithTriviaFrom(node.Identifier));
            }

            // Check if it's a local
            var localKey = $"Local:{node.Identifier.Text}";
            if (_symbolMap.TryGetValue(localKey, out var localName))
            {
                return node.WithIdentifier(SyntaxFactory.Identifier(localName).WithTriviaFrom(node.Identifier));
            }

            return base.VisitVariableDeclarator(node);
        }

        public override SyntaxNode? VisitParameter(ParameterSyntax node)
        {
            var key = $"Parameter:{node.Identifier.Text}";
            if (_symbolMap.TryGetValue(key, out var newName))
            {
                ParametersRenamed++;
                node = node.WithIdentifier(SyntaxFactory.Identifier(newName).WithTriviaFrom(node.Identifier));
            }
            return base.VisitParameter(node);
        }

        public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node)
        {
            // Try to rename references to renamed symbols
            foreach (var prefix in new[] { "Type:", "Method:", "Field:", "Property:", "Parameter:", "Local:" })
            {
                var key = $"{prefix}{node.Identifier.Text}";
                if (_symbolMap.TryGetValue(key, out var newName))
                {
                    return SyntaxFactory.IdentifierName(newName).WithTriviaFrom(node);
                }
            }
            return base.VisitIdentifierName(node);
        }
    }
}
