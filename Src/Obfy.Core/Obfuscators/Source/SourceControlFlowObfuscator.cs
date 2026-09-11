using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;
using Obfy.Core.Models;
using Obfy.Core.Pipeline;

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

            foreach (var tree in compilation.SyntaxTrees)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var root = await tree.GetRootAsync(cancellationToken);
                var rewriter = new ControlFlowRewriter(settings);
                var newRoot = rewriter.Visit(root);

                stats.MethodsControlFlowObfuscated += rewriter.MethodsObfuscated;

                newTrees.Add(newRoot.SyntaxTree);
            }

            context.Compilation = compilation
                .RemoveAllSyntaxTrees()
                .AddSyntaxTrees(newTrees);

            _logger.LogInformation("Obfuscated control flow in {Count} methods", stats.MethodsControlFlowObfuscated);

            return ObfuscationResult.Successful(stats);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Source control flow obfuscation failed");
            return ObfuscationResult.Failed($"Source control flow obfuscation failed: {ex.Message}", ex);
        }
    }

    private class ControlFlowRewriter : CSharpSyntaxRewriter
    {
        private readonly ControlFlowSettings _settings;
        private readonly Random _random = new();

        public int MethodsObfuscated { get; private set; }

        public ControlFlowRewriter(ControlFlowSettings settings)
        {
            _settings = settings;
        }

        public override SyntaxNode? VisitMethodDeclaration(MethodDeclarationSyntax node)
        {
            // Skip methods that are too simple
            if (node.Body == null || node.Body.Statements.Count < 3)
                return base.VisitMethodDeclaration(node);

            // Skip based on intensity
            if (_random.Next(100) > _settings.Intensity)
                return base.VisitMethodDeclaration(node);

            // Value-returning methods need a terminal statement after the switch dispatcher's loop,
            // or the compiler reports CS0161 ("not all code paths return a value").
            var needsReturnValue = MethodNeedsReturnValue(node);

            // Apply control flow obfuscation based on mode
            var newBody = _settings.Mode switch
            {
                ControlFlowMode.OpaquePredicate => InsertOpaquePredicates(node.Body),
                ControlFlowMode.Switch => ConvertToSwitchDispatcher(node.Body, needsReturnValue),
                ControlFlowMode.Combined => InsertOpaquePredicates(ConvertToSwitchDispatcher(node.Body, needsReturnValue)),
                _ => node.Body
            };

            if (newBody != node.Body)
            {
                MethodsObfuscated++;
                return node.WithBody(newBody);
            }

            return base.VisitMethodDeclaration(node);
        }

        private BlockSyntax InsertOpaquePredicates(BlockSyntax body)
        {
            var newStatements = new List<StatementSyntax>();
            var transformedAny = false;

            foreach (var statement in body.Statements)
            {
                // Randomly insert opaque predicates based on intensity
                if (CanWrapInPredicate(statement) && _random.Next(100) < _settings.Intensity)
                {
                    newStatements.Add(CreateOpaquePredicate(statement));
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
                        newStatements[i] = CreateOpaquePredicate(newStatements[i]);
                        transformedAny = true;
                        break;
                    }
                }
            }

            // Nothing eligible was wrapped — return the original body so we don't churn it needlessly.
            return transformedAny ? SyntaxFactory.Block(newStatements) : body;
        }

        /// <summary>
        /// Wrapping a statement in an <c>if</c> block moves it into a nested scope. Local declarations,
        /// local functions and labels are scope-sensitive: wrapping them would hide the declared name
        /// (or label) from the rest of the method and fail to compile. Such statements are left as-is.
        /// </summary>
        private static bool CanWrapInPredicate(StatementSyntax statement)
        {
            return statement is not (
                LocalDeclarationStatementSyntax or
                LocalFunctionStatementSyntax or
                LabeledStatementSyntax);
        }

        private static bool MethodNeedsReturnValue(MethodDeclarationSyntax method)
        {
            // void methods never need a return value.
            if (method.ReturnType is PredefinedTypeSyntax predefined &&
                predefined.Keyword.IsKind(SyntaxKind.VoidKeyword))
            {
                return false;
            }

            // async Task / async ValueTask (non-generic) and async void need no return value.
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

        private StatementSyntax CreateOpaquePredicate(StatementSyntax originalStatement)
        {
            // Create: if ((x * x) >= 0) { originalStatement } else { /* dead code */ }
            // This is always true for any real number

            var constant = _random.Next(1, 100);

            // (constant * constant) >= 0
            var condition = SyntaxFactory.BinaryExpression(
                SyntaxKind.GreaterThanOrEqualExpression,
                SyntaxFactory.BinaryExpression(
                    SyntaxKind.MultiplyExpression,
                    SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(constant)),
                    SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(constant))),
                SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(0)));

            // if (condition) { original } else { /* unreachable dead code */ }
            var ifStatement = SyntaxFactory.IfStatement(
                condition,
                originalStatement is BlockSyntax block ? block : SyntaxFactory.Block(originalStatement),
                SyntaxFactory.ElseClause(
                    SyntaxFactory.Block(
                        SyntaxFactory.ThrowStatement(
                            SyntaxFactory.ObjectCreationExpression(
                                SyntaxFactory.ParseTypeName("System.InvalidOperationException"))
                            .WithArgumentList(SyntaxFactory.ArgumentList())))));

            return ifStatement;
        }

        private static bool CanFlatten(BlockSyntax body)
        {
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

                if (statement.DescendantNodes().Any(n =>
                        n is BreakStatementSyntax
                            or ContinueStatementSyntax
                            or GotoStatementSyntax
                            or YieldStatementSyntax
                            or LocalDeclarationStatementSyntax))
                {
                    return false;
                }
            }

            return true;
        }

        private BlockSyntax ConvertToSwitchDispatcher(BlockSyntax body, bool needsReturnValue)
        {
            if (body.Statements.Count < 3)
                return body;

            if (!CanFlatten(body))
                return body;

            // Create state variable and switch-based dispatcher
            var statements = body.Statements.ToList();
            var stateVar = $"__state_{_random.Next(1000, 9999)}";

            // Assign random state numbers
            var stateMap = new Dictionary<int, int>();
            var usedStates = new HashSet<int>();
            for (var i = 0; i < statements.Count; i++)
            {
                int state;
                do
                {
                    state = _random.Next(100, 999);
                } while (!usedStates.Add(state));
                stateMap[i] = state;
            }

            // Build switch cases
            var switchSections = new List<SwitchSectionSyntax>();

            for (var i = 0; i < statements.Count; i++)
            {
                var nextState = i + 1 < statements.Count ? stateMap[i + 1] : -1;

                var caseStatements = new List<StatementSyntax> { statements[i] };

                if (nextState >= 0)
                {
                    // state = nextState;
                    caseStatements.Add(
                        SyntaxFactory.ExpressionStatement(
                            SyntaxFactory.AssignmentExpression(
                                SyntaxKind.SimpleAssignmentExpression,
                                SyntaxFactory.IdentifierName(stateVar),
                                SyntaxFactory.LiteralExpression(
                                    SyntaxKind.NumericLiteralExpression,
                                    SyntaxFactory.Literal(nextState)))));
                    caseStatements.Add(SyntaxFactory.BreakStatement());
                }
                else
                {
                    // state = -1; break;
                    caseStatements.Add(
                        SyntaxFactory.ExpressionStatement(
                            SyntaxFactory.AssignmentExpression(
                                SyntaxKind.SimpleAssignmentExpression,
                                SyntaxFactory.IdentifierName(stateVar),
                                SyntaxFactory.PrefixUnaryExpression(
                                    SyntaxKind.UnaryMinusExpression,
                                    SyntaxFactory.LiteralExpression(
                                        SyntaxKind.NumericLiteralExpression,
                                        SyntaxFactory.Literal(1))))));
                    caseStatements.Add(SyntaxFactory.BreakStatement());
                }

                var section = SyntaxFactory.SwitchSection(
                    SyntaxFactory.SingletonList<SwitchLabelSyntax>(
                        SyntaxFactory.CaseSwitchLabel(
                            SyntaxFactory.LiteralExpression(
                                SyntaxKind.NumericLiteralExpression,
                                SyntaxFactory.Literal(stateMap[i])))),
                    SyntaxFactory.List(caseStatements));

                switchSections.Add(section);
            }

            // Add default case
            switchSections.Add(
                SyntaxFactory.SwitchSection(
                    SyntaxFactory.SingletonList<SwitchLabelSyntax>(SyntaxFactory.DefaultSwitchLabel()),
                    SyntaxFactory.SingletonList<StatementSyntax>(SyntaxFactory.BreakStatement())));

            // Build the switch statement
            var switchStatement = SyntaxFactory.SwitchStatement(
                SyntaxFactory.IdentifierName(stateVar),
                SyntaxFactory.List(switchSections));

            // Build while loop: while (state >= 0) { switch... }
            var whileLoop = SyntaxFactory.WhileStatement(
                SyntaxFactory.BinaryExpression(
                    SyntaxKind.GreaterThanOrEqualExpression,
                    SyntaxFactory.IdentifierName(stateVar),
                    SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(0))),
                SyntaxFactory.Block(switchStatement));

            // Build new body: var state = initialState; while (state >= 0) { switch... }
            var newStatements = new List<StatementSyntax>
            {
                // var state = initialState;
                SyntaxFactory.LocalDeclarationStatement(
                    SyntaxFactory.VariableDeclaration(
                        SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.IntKeyword)))
                    .WithVariables(
                        SyntaxFactory.SingletonSeparatedList(
                            SyntaxFactory.VariableDeclarator(stateVar)
                            .WithInitializer(
                                SyntaxFactory.EqualsValueClause(
                                    SyntaxFactory.LiteralExpression(
                                        SyntaxKind.NumericLiteralExpression,
                                        SyntaxFactory.Literal(stateMap[0]))))))),
                whileLoop
            };

            // For a value-returning method the original return lives inside a switch case, so the
            // compiler cannot see that the loop always returns and reports CS0161. The last flattened
            // statement is always a return (CanFlatten enforces it), so this terminal is unreachable at
            // runtime and only satisfies the compiler's definite-return analysis.
            if (needsReturnValue)
            {
                newStatements.Add(
                    SyntaxFactory.ThrowStatement(
                        SyntaxFactory.ObjectCreationExpression(
                            SyntaxFactory.ParseTypeName("System.InvalidOperationException"))
                        .WithArgumentList(SyntaxFactory.ArgumentList())));
            }

            return SyntaxFactory.Block(newStatements);
        }
    }
}
