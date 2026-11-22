using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;
using Obfy.Core.Models;
using Obfy.Core.Pipeline;
using Obfy.Core.Utilities;

namespace Obfy.Core.Obfuscators.Source;

/// <summary>
/// Obfuscates control flow in C# source code using Roslyn.
/// </summary>
public class SourceControlFlowObfuscator : IObfuscator
{
    private readonly ILogger<SourceControlFlowObfuscator> _logger;

    public SourceControlFlowObfuscator(ILogger<SourceControlFlowObfuscator> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public string Name => "SourceControlFlow";

    /// <inheritdoc/>
    public int Priority => (int)ObfuscationPhase.ControlFlow;

    /// <inheritdoc/>
    public bool SupportsTargetType(TargetType targetType) => targetType == TargetType.SourceCode;

    /// <inheritdoc/>
    public bool IsEnabled(ObfySettings settings) => settings.ControlFlow.Enabled;

    /// <inheritdoc/>
    public async Task<ObfuscationResult> ObfuscateAsync(PipelineContext context, CancellationToken cancellationToken = default)
    {
        var compilation = context.RequireCompilation();
        var settings = context.Settings.ControlFlow;
        var stats = new ObfuscationStatistics();

        _logger.LogDebug("Starting source control flow obfuscation with mode {Mode}", settings.Mode);

        try
        {
            var newTrees = new List<SyntaxTree>();
            var scope = new SourceScope(context.Settings);
            scope.WarnIfInclusionsMatchedNothing(compilation, context.Warnings);

            foreach (var tree in compilation.SyntaxTrees)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var root = await tree.GetRootAsync(cancellationToken).ConfigureAwait(false);
                var model = compilation.GetSemanticModel(tree);
                var rewriter = new ControlFlowRewriter(settings, model, scope);
                var newRoot = rewriter.Visit(root);

                stats.MethodsControlFlowObfuscated += rewriter.MethodsObfuscated;

                newTrees.Add(newRoot.SyntaxTree.WithFilePath(tree.FilePath));
            }

            var newCompilation = compilation
                .RemoveAllSyntaxTrees()
                .AddSyntaxTrees(newTrees);
            context.Compilation = newCompilation;

            if (stats.MethodsControlFlowObfuscated > 0)
            {
                SourceOutputCheck.WarnOnNewErrors(
                    "Source control flow", compilation, newCompilation, context.Warnings, cancellationToken);
            }

            _logger.LogInformation("Obfuscated control flow in {Count} methods", stats.MethodsControlFlowObfuscated);

            return ObfuscationResult.Successful(stats);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError("Source control flow obfuscation failed ({ExceptionType})", ex.GetType().Name);
            return ObfuscationResult.Failed($"Source control flow obfuscation failed: {ex.Message}", ex);
        }
    }

    private class ControlFlowRewriter : CSharpSyntaxRewriter
    {
        private readonly ControlFlowSettings _settings;
        private readonly SemanticModel _model;
        private readonly SourceScope _scope;
        private readonly Random _random = new();
        private int _tickCounter;

        public int MethodsObfuscated { get; private set; }

        public ControlFlowRewriter(ControlFlowSettings settings, SemanticModel model, SourceScope scope)
        {
            _settings = settings;
            _model = model;
            _scope = scope;
        }

        public override SyntaxNode? VisitMethodDeclaration(MethodDeclarationSyntax node)
        {
            if (node.Body == null || node.Body.Statements.Count < 3)
                return base.VisitMethodDeclaration(node);

            var symbol = _model.GetDeclaredSymbol(node);
            if (symbol != null && ObfuscationAttributeRules.IsExcluded(symbol, ObfuscationFeature.ControlFlow))
                return base.VisitMethodDeclaration(node);

            if (!_scope.Allows(symbol))
                return base.VisitMethodDeclaration(node);

            // Skip based on intensity: Next(100) is 0..99, so the method is kept with probability
            // Intensity/100 (0 never transforms, 100 always does).
            if (_random.Next(100) >= _settings.Intensity)
                return base.VisitMethodDeclaration(node);

            // Value-returning methods need a terminal statement after the switch dispatcher's loop,
            // or the compiler reports CS0161 ("not all code paths return a value").
            var needsReturnValue = MethodNeedsReturnValue(node);
            var usedNames = CollectIdentifierNames(node);

            // Apply control flow obfuscation based on mode
            var newBody = _settings.Mode switch
            {
                ControlFlowMode.OpaquePredicate => InsertOpaquePredicates(node.Body, usedNames),
                ControlFlowMode.Switch => ConvertToSwitchDispatcher(node, needsReturnValue, usedNames),
                ControlFlowMode.Combined => InsertOpaquePredicates(ConvertToSwitchDispatcher(node, needsReturnValue, usedNames), usedNames),
                _ => node.Body
            };

            if (newBody != node.Body)
            {
                MethodsObfuscated++;
                return node.WithBody(newBody);
            }

            return base.VisitMethodDeclaration(node);
        }

        private static HashSet<string> CollectIdentifierNames(MethodDeclarationSyntax method)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var token in method.DescendantTokens())
            {
                if (token.IsKind(SyntaxKind.IdentifierToken))
                    names.Add(token.ValueText);
            }

            return names;
        }

        private string NextTickVariableName(HashSet<string> usedNames)
        {
            string name;
            do
            {
                name = $"__obfyTick_{++_tickCounter}";
            } while (!usedNames.Add(name));

            return name;
        }

        private BlockSyntax InsertOpaquePredicates(BlockSyntax body, HashSet<string> usedNames)
        {
            var newStatements = new List<StatementSyntax>();
            var transformedAny = false;

            foreach (var statement in body.Statements)
            {
                // Randomly insert opaque predicates based on intensity
                if (CanWrapInPredicate(statement) && _random.Next(100) < _settings.Intensity)
                {
                    newStatements.Add(CreateOpaquePredicate(statement, usedNames));
                    transformedAny = true;
                }
                else
                {
                    newStatements.Add(statement);
                }
            }

            // Ensure at least one wrappable statement is transformed if intensity > 0
            if (!transformedAny && _settings.Intensity > 0)
            {
                for (var i = 0; i < newStatements.Count; i++)
                {
                    if (CanWrapInPredicate(newStatements[i]))
                    {
                        newStatements[i] = CreateOpaquePredicate(newStatements[i], usedNames);
                        transformedAny = true;
                        break;
                    }
                }
            }

            // Nothing eligible was wrapped — return the original body so we don't churn it needlessly.
            return transformedAny ? body.WithStatements(SyntaxFactory.List(newStatements)) : body;
        }

        /// <summary>
        /// Wrapping a statement in an <c>if</c> block moves it into a nested scope. Local declarations,
        /// local functions and labels are scope-sensitive: wrapping them would hide the declared name
        /// (or label) from the rest of the method and fail to compile. Such statements are left as-is.
        /// </summary>
        private static bool CanWrapInPredicate(StatementSyntax statement)
        {
            if (statement is LocalDeclarationStatementSyntax or LocalFunctionStatementSyntax or LabeledStatementSyntax)
                return false;

            // `out var`, pattern variables (`is string s`, `is not string s`), local functions and nested
            // labels are scoped to the enclosing block; a nested predicate block would hide them.
            return !statement.DescendantNodes().Any(n =>
                n is DeclarationExpressionSyntax
                    or SingleVariableDesignationSyntax
                    or LocalFunctionStatementSyntax
                    or LabeledStatementSyntax);
        }

        private static bool MethodNeedsReturnValue(MethodDeclarationSyntax method)
        {
            // void methods never need a return value.
            if (method.ReturnType is PredefinedTypeSyntax predefined &&
                predefined.Keyword.IsKind(SyntaxKind.VoidKeyword))
            {
                return false;
            }

            // async Task / async ValueTask (non-generic) need no return value. async void is handled
            // by the void-keyword check above.
            if (method.Modifiers.Any(m => m.IsKind(SyntaxKind.AsyncKeyword)))
            {
                var returnType = method.ReturnType.ToString();
                if (returnType is "Task" or "ValueTask" ||
                    returnType.EndsWith(".Task", StringComparison.Ordinal) ||
                    returnType.EndsWith(".ValueTask", StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        // n * (n + 1) is always even, even when the product wraps (2^32 is even). TickCount is not a
        // compile-time constant, so the C# compiler and decompilers cannot fold the branch.
        // It is read ONCE into a local: two reads can straddle a tick, making n * (n + 1) odd and the
        // else branch throw at run time. The product is unchecked because it overflows int for any
        // machine up more than ~46 seconds, which a project built with checked arithmetic would turn
        // into an OverflowException. The outer block keeps the local out of the surrounding scope.
        // The shell is parsed from text (not built with SyntaxFactory) so that it carries real
        // whitespace: factory nodes print as "thrownew" and "int__state" when written to a file.
        private const string OriginalMarker = "__obfyOriginal";

        private static string BuildOpaquePredicateShell(string tickVar) => $$"""
            {
                int {{tickVar}} = global::System.Environment.TickCount;
                if (unchecked({{tickVar}} * ({{tickVar}} + 1)) % 2 == 0)
                {
                    __obfyOriginal;
                }
                else
                {
                    throw new global::System.InvalidOperationException();
                }
            }
            """;

        private StatementSyntax CreateOpaquePredicate(StatementSyntax originalStatement, HashSet<string> usedNames)
        {
            var tickVar = NextTickVariableName(usedNames);
            var shell = (BlockSyntax)SyntaxFactory.ParseStatement(BuildOpaquePredicateShell(tickVar));
            var marker = shell.DescendantNodes().OfType<ExpressionStatementSyntax>()
                .Single(e => e.Expression is IdentifierNameSyntax { Identifier.ValueText: OriginalMarker });

            // The leading and trailing trivia of the original statement (comments, #if/#endif pairs,
            // line breaks) stays around the wrapper, where it was.
            var leading = originalStatement.GetLeadingTrivia();
            var trailing = originalStatement.GetTrailingTrivia();
            var inner = originalStatement
                .WithLeadingTrivia(SyntaxFactory.Whitespace(" "))
                .WithTrailingTrivia(SyntaxFactory.Whitespace(" "));

            var wrapped = originalStatement is BlockSyntax
                ? shell.ReplaceNode(marker.Parent!, inner)
                : shell.ReplaceNode(marker, inner);

            return wrapped.WithLeadingTrivia(leading).WithTrailingTrivia(trailing);
        }

        private static bool CanFlatten(BlockSyntax body)
        {
            // #if/#endif pairs would end up in different switch sections.
            if (body.DescendantTrivia().Any(t => t.IsDirective))
                return false;

            for (var i = 0; i < body.Statements.Count; i++)
            {
                var statement = body.Statements[i];
                if (statement is ReturnStatementSyntax)
                {
                    if (i != body.Statements.Count - 1)
                        return false;
                    continue;
                }

                if (statement is LocalDeclarationStatementSyntax
                    or LocalFunctionStatementSyntax
                    or BreakStatementSyntax
                    or ContinueStatementSyntax
                    or GotoStatementSyntax
                    or LabeledStatementSyntax
                    or SwitchStatementSyntax
                    or WhileStatementSyntax
                    or ForStatementSyntax
                    or ForEachStatementSyntax
                    or DoStatementSyntax
                    or TryStatementSyntax
                    or UsingStatementSyntax
                    or LockStatementSyntax
                    or CheckedStatementSyntax
                    or UnsafeStatementSyntax
                    or YieldStatementSyntax)
                {
                    return false;
                }

                // Declaration expressions (`out var x`), pattern variables, nested local functions and labels
                // introduce names whose scope is the enclosing block. Flattening puts each statement in its
                // own switch section, so those names would not be visible to later statements.
                if (statement.DescendantNodes().Any(n =>
                        n is BreakStatementSyntax
                            or ContinueStatementSyntax
                            or GotoStatementSyntax
                            or YieldStatementSyntax
                            or LocalDeclarationStatementSyntax
                            or LocalFunctionStatementSyntax
                            or DeclarationExpressionSyntax
                            or SingleVariableDesignationSyntax
                            or LabeledStatementSyntax))
                {
                    return false;
                }
            }

            return true;
        }

        private BlockSyntax ConvertToSwitchDispatcher(MethodDeclarationSyntax method, bool needsReturnValue, HashSet<string> usedNames)
        {
            var body = method.Body!;
            if (body.Statements.Count < 3)
                return body;

            // An out parameter must be definitely assigned before the method returns. Once each
            // statement sits in its own switch section the compiler can no longer prove that (CS0177).
            if (method.ParameterList.Parameters.Any(p => p.Modifiers.Any(SyntaxKind.OutKeyword)))
                return body;

            if (!CanFlatten(body))
                return body;

            // Create state variable and switch-based dispatcher
            var statements = body.Statements.ToList();
            string stateVar;
            do
            {
                stateVar = $"__state_{_random.Next(1000, 9999)}";
            } while (!usedNames.Add(stateVar));

            // Assign random, distinct state numbers. The range grows with the statement count so a long
            // method (a large InitializeComponent) can never run out of unused states and spin forever;
            // it stays far above the count, so the draw below finishes after a few retries at most.
            var stateRange = Math.Max(900, statements.Count * 4);
            var states = new int[statements.Count];
            var usedStates = new HashSet<int>();
            for (var i = 0; i < statements.Count; i++)
            {
                int state;
                do
                {
                    state = _random.Next(100, 100 + stateRange);
                } while (!usedStates.Add(state));
                states[i] = state;
            }

            // The dispatcher is parsed from text with a placeholder statement per original statement, so
            // it carries real whitespace; each placeholder is then swapped for the original statement.
            var text = new System.Text.StringBuilder();
            text.AppendLine("{");
            text.AppendLine($"    int {stateVar} = {states[0]};");
            text.AppendLine($"    while ({stateVar} >= 0)");
            text.AppendLine("    {");
            text.AppendLine($"        switch ({stateVar})");
            text.AppendLine("        {");
            for (var i = 0; i < statements.Count; i++)
            {
                var nextState = i + 1 < statements.Count ? states[i + 1] : -1;
                text.AppendLine($"            case {states[i]}:");
                text.AppendLine($"                {StatementMarkerPrefix}{i};");
                if (statements[i] is not (ReturnStatementSyntax or ThrowStatementSyntax))
                {
                    text.AppendLine($"                {stateVar} = {nextState};");
                    text.AppendLine("                break;");
                }
            }

            text.AppendLine("            default:");
            text.AppendLine("                break;");
            text.AppendLine("        }");
            text.AppendLine("    }");

            // For a value-returning method the original return lives inside a switch case, so the
            // compiler cannot see that the loop always returns and reports CS0161. CanFlatten forbids
            // early return; a final throw (or other allowed statement) may still flatten. The original
            // body already returns or throws, so this terminal is unreachable and only satisfies
            // definite-assignment.
            if (needsReturnValue)
                text.AppendLine("    throw new global::System.InvalidOperationException();");

            text.AppendLine("}");

            var shell = (BlockSyntax)SyntaxFactory.ParseStatement(text.ToString());
            var markers = shell.DescendantNodes().OfType<ExpressionStatementSyntax>()
                .Where(e => e.Expression is IdentifierNameSyntax id &&
                            id.Identifier.ValueText.StartsWith(StatementMarkerPrefix, StringComparison.Ordinal))
                .ToList();

            var dispatcher = shell.ReplaceNodes(markers, (original, _) =>
            {
                var name = ((IdentifierNameSyntax)original.Expression).Identifier.ValueText;
                return statements[int.Parse(name[StatementMarkerPrefix.Length..])];
            });

            // Keep the layout around the original braces (and any comment before the closing one).
            return dispatcher
                .WithOpenBraceToken(dispatcher.OpenBraceToken.WithTriviaFrom(body.OpenBraceToken))
                .WithCloseBraceToken(dispatcher.CloseBraceToken.WithTriviaFrom(body.CloseBraceToken));
        }

        private const string StatementMarkerPrefix = "__obfyStatement";
    }
}
