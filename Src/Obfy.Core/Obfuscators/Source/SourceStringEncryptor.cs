using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;
using Obfy.Core.Models;
using Obfy.Core.Pipeline;
using Obfy.Core.Utilities;

namespace Obfy.Core.Obfuscators.Source;

/// <summary>
/// Encrypts string literals in C# source code using Roslyn.
/// </summary>
public class SourceStringEncryptor : IObfuscator
{
    private readonly ILogger<SourceStringEncryptor> _logger;

    public SourceStringEncryptor(ILogger<SourceStringEncryptor> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public string Name => "SourceStringEncryption";

    /// <inheritdoc/>
    public int Priority => (int)ObfuscationPhase.StringEncryption;

    /// <inheritdoc/>
    public bool SupportsTargetType(TargetType targetType) => targetType == TargetType.SourceCode;

    /// <inheritdoc/>
    public bool IsEnabled(ObfySettings settings) => settings.StringEncryption.Enabled;

    /// <inheritdoc/>
    public async Task<ObfuscationResult> ObfuscateAsync(PipelineContext context, CancellationToken cancellationToken = default)
    {
        var compilation = context.RequireCompilation();
        var settings = context.Settings.StringEncryption;
        var stats = new ObfuscationStatistics();

        _logger.LogDebug("Starting source string encryption");

        try
        {
            // Generate encryption key
            var key = EncryptionHelper.GenerateKey(settings.Algorithm);
            var keyBase64 = Convert.ToBase64String(key);

            var newTrees = new List<SyntaxTree>();
            var encryptedStrings = new List<(string Original, string Encrypted)>();

            // A second run over already obfuscated source finds the first run's helper. The new helper
            // gets a free name so the two never collide (CS0101) and each keeps its own key.
            var helperName = ChooseHelperName(compilation);
            var scope = new SourceScope(context.Settings);
            scope.WarnIfInclusionsMatchedNothing(compilation, context.Warnings);

            foreach (var tree in compilation.SyntaxTrees)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var root = await tree.GetRootAsync(cancellationToken).ConfigureAwait(false);
                var model = compilation.GetSemanticModel(tree);
                var rewriter = new StringEncryptionRewriter(settings, key, encryptedStrings, model, helperName, scope);
                var newRoot = rewriter.Visit(root);

                stats.StringsEncrypted += rewriter.EncryptedCount;

                newTrees.Add(newRoot.SyntaxTree.WithFilePath(tree.FilePath));
            }

            // Add decryption helper class if any strings were encrypted
            if (stats.StringsEncrypted > 0)
            {
                var helperClass = GenerateDecryptionHelper(helperName, keyBase64, settings.Algorithm);
                var helperRoot = SyntaxFactory.ParseCompilationUnit(helperClass);
                if (newTrees.Count == 1)
                {
                    // One input file stays one output file: the helper goes into the same file.
                    newTrees[0] = AppendToTree(newTrees[0], helperRoot.Members[0]);
                }
                else
                {
                    // A fixed per-name path, so a re-run overwrites the helper instead of adding another.
                    newTrees.Add(CSharpSyntaxTree.Create(helperRoot, path: helperName + ".cs"));
                }
            }

            var newCompilation = compilation
                .RemoveAllSyntaxTrees()
                .AddSyntaxTrees(newTrees);
            context.Compilation = newCompilation;

            if (stats.StringsEncrypted > 0)
            {
                SourceOutputCheck.WarnOnNewErrors(
                    "Source string encryption", compilation, newCompilation, context.Warnings, cancellationToken);
            }

            _logger.LogInformation("Encrypted {Count} strings in source code", stats.StringsEncrypted);

            return ObfuscationResult.Successful(stats);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError("Source string encryption failed ({ExceptionType})", ex.GetType().Name);
            return ObfuscationResult.Failed($"Source string encryption failed: {ex.Message}", ex);
        }
    }

    private const string HelperBaseName = "__ObfyStringDecryptor";

    /// <summary>
    /// First helper name that no type or file in the compilation uses yet: the plain name on a first
    /// run, then a numbered one when the input already carries a helper.
    /// </summary>
    private static string ChooseHelperName(CSharpCompilation compilation)
    {
        var taken = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tree in compilation.SyntaxTrees)
        {
            foreach (var declaration in tree.GetRoot().DescendantNodes().OfType<TypeDeclarationSyntax>())
            {
                if (declaration.Identifier.ValueText.StartsWith(HelperBaseName, StringComparison.Ordinal))
                    taken.Add(declaration.Identifier.ValueText);
            }

            if (!string.IsNullOrEmpty(tree.FilePath))
                taken.Add(Path.GetFileNameWithoutExtension(tree.FilePath));
        }

        var name = HelperBaseName;
        for (var n = 2; taken.Contains(name); n++)
            name = HelperBaseName + n;

        return name;
    }

    /// <summary>
    /// Appends a namespace to the end of a file. A file-scoped namespace cannot share a file with a
    /// second namespace, so it becomes the equivalent block-scoped one first.
    /// </summary>
    private static SyntaxTree AppendToTree(SyntaxTree tree, MemberDeclarationSyntax member)
    {
        var root = (CompilationUnitSyntax)tree.GetRoot();
        var newLine = SyntaxFactory.EndOfLine("\n");
        var members = root.Members;

        if (members.OfType<FileScopedNamespaceDeclarationSyntax>().FirstOrDefault() is { } scoped)
        {
            var block = SyntaxFactory.NamespaceDeclaration(
                scoped.AttributeLists,
                scoped.Modifiers,
                scoped.NamespaceKeyword,
                scoped.Name,
                SyntaxFactory.Token(SyntaxKind.OpenBraceToken)
                    .WithLeadingTrivia(newLine)
                    .WithTrailingTrivia(scoped.SemicolonToken.TrailingTrivia),
                scoped.Externs,
                scoped.Usings,
                scoped.Members,
                SyntaxFactory.Token(SyntaxKind.CloseBraceToken).WithLeadingTrivia(newLine).WithTrailingTrivia(newLine),
                default);
            members = members.Replace(scoped, block);
        }

        var newRoot = root.WithMembers(members.Add(member.WithLeadingTrivia(newLine, newLine)));
        return CSharpSyntaxTree.Create(newRoot, path: tree.FilePath, encoding: tree.Encoding);
    }

    /// <summary>
    /// The helper is a self-contained namespace: every framework name is global-qualified and it has no
    /// usings, so it can sit in its own file or at the end of a user's file without clashing with user
    /// types (a type called Convert or Buffer, a namespace called System).
    /// </summary>
    private static string GenerateDecryptionHelper(string helperName, string keyBase64, EncryptionAlgorithm algorithm)
    {
        var algorithmCode = algorithm == EncryptionAlgorithm.Aes256
            ? GenerateAesDecryptor()
            : GenerateXorDecryptor();

        return $$"""
            namespace Obfy.Runtime
            {
                internal static class {{helperName}}
                {
                    private static readonly byte[] _key = global::System.Convert.FromBase64String("{{keyBase64}}");

                    internal static string Decrypt(string encrypted)
                    {
                        if (string.IsNullOrEmpty(encrypted)) return encrypted;
                        var data = global::System.Convert.FromBase64String(encrypted);
                        {{algorithmCode}}
                    }
                }
            }
            """;
    }

    private static string GenerateAesDecryptor()
    {
        return """
                        using var aes = global::System.Security.Cryptography.Aes.Create();
                        aes.Key = _key;
                        var iv = new byte[aes.BlockSize / 8];
                        global::System.Buffer.BlockCopy(data, 0, iv, 0, iv.Length);
                        aes.IV = iv;
                        var ciphertext = new byte[data.Length - iv.Length];
                        global::System.Buffer.BlockCopy(data, iv.Length, ciphertext, 0, ciphertext.Length);
                        using var decryptor = aes.CreateDecryptor();
                        var decrypted = decryptor.TransformFinalBlock(ciphertext, 0, ciphertext.Length);
                        var chars = new char[decrypted.Length / 2];
                        for (int i = 0; i < chars.Length; i++)
                            chars[i] = (char)(decrypted[i * 2] | (decrypted[(i * 2) + 1] << 8));
                        return new string(chars);
            """;
    }

    private static string GenerateXorDecryptor()
    {
        return """
                        var result = new byte[data.Length];
                        for (int i = 0; i < data.Length; i++)
                            result[i] = (byte)(data[i] ^ _key[i % _key.Length]);
                        var chars = new char[result.Length / 2];
                        for (int i = 0; i < chars.Length; i++)
                            chars[i] = (char)(result[i * 2] | (result[(i * 2) + 1] << 8));
                        return new string(chars);
            """;
    }

    private class StringEncryptionRewriter : CSharpSyntaxRewriter
    {
        private readonly StringEncryptionSettings _settings;
        private readonly byte[] _key;
        private readonly List<(string Original, string Encrypted)> _encryptedStrings;
        private readonly SemanticModel _model;
        private readonly ExpressionSyntax _decryptTarget;
        private readonly SourceScope _scope;

        public int EncryptedCount { get; private set; }

        public StringEncryptionRewriter(
            StringEncryptionSettings settings,
            byte[] key,
            List<(string Original, string Encrypted)> encryptedStrings,
            SemanticModel model,
            string helperName,
            SourceScope scope)
        {
            _settings = settings;
            _key = key;
            _encryptedStrings = encryptedStrings;
            _model = model;
            // global:: so a user namespace or type called Obfy cannot capture the name.
            _decryptTarget = SyntaxFactory.ParseExpression($"global::Obfy.Runtime.{helperName}.Decrypt");
            _scope = scope;
        }

        /// <summary>
        /// A decryptor helper from an earlier run (input that was already obfuscated) must stay as it is:
        /// encrypting its own key literal would make the helper decrypt itself.
        /// </summary>
        private static bool IsInsideDecryptorHelper(SyntaxNode node) =>
            node.Ancestors().OfType<TypeDeclarationSyntax>()
                .Any(t => t.Identifier.ValueText.StartsWith(HelperBaseName, StringComparison.Ordinal));

        public override SyntaxNode? VisitLiteralExpression(LiteralExpressionSyntax node)
        {
            if (!node.IsKind(SyntaxKind.StringLiteralExpression))
                return base.VisitLiteralExpression(node);

            if (node.Ancestors().Any(a => a is AttributeArgumentSyntax or AttributeSyntax))
                return base.VisitLiteralExpression(node);

            if (IsInsideDecryptorHelper(node))
                return base.VisitLiteralExpression(node);

            var enclosing = _model.GetEnclosingSymbol(node.SpanStart);
            if (enclosing != null && ObfuscationAttributeRules.IsExcluded(enclosing, ObfuscationFeature.Strings))
                return base.VisitLiteralExpression(node);

            if (!_scope.Allows(enclosing))
                return base.VisitLiteralExpression(node);

            var value = node.Token.ValueText;

            if (!_settings.EncryptConstantStrings)
                return base.VisitLiteralExpression(node);

            if (string.IsNullOrEmpty(value) || value.Length < _settings.MinStringLength)
                return base.VisitLiteralExpression(node);

            if (MustRemainConstant(node))
                return base.VisitLiteralExpression(node);

            // Encrypt the string
            var encrypted = EncryptionHelper.EncryptToBase64(value, _key, _settings.Algorithm);
            _encryptedStrings.Add((value, encrypted));
            EncryptedCount++;

            // Inside an interpolation hole a ':' starts the format clause, so the '::' of "global::" would
            // end the hole early. Parentheses keep the whole call in the hole.
            ExpressionSyntax replacement = CreateDecryptCall(encrypted);
            if (node.Ancestors().OfType<InterpolationSyntax>().Any())
                replacement = SyntaxFactory.ParenthesizedExpression(replacement);

            return replacement.WithTriviaFrom(node);
        }

        public override SyntaxNode? VisitInterpolatedStringExpression(InterpolatedStringExpressionSyntax node)
        {
            // Visit the holes first: string literals nested in them (and nested interpolated strings)
            // are rewritten on their own, whether or not this string's own text is encrypted.
            var visited = (InterpolatedStringExpressionSyntax)base.VisitInterpolatedStringExpression(node)!;

            if (!_settings.EncryptConstantStrings)
                return visited;

            if (node.Ancestors().Any(a => a is AttributeArgumentSyntax or AttributeSyntax))
                return visited;

            if (IsInsideDecryptorHelper(node))
                return visited;

            var enclosing = _model.GetEnclosingSymbol(node.SpanStart);
            if (enclosing != null && ObfuscationAttributeRules.IsExcluded(enclosing, ObfuscationFeature.Strings))
                return visited;

            if (!_scope.Allows(enclosing))
                return visited;

            if (MustRemainConstant(node))
                return visited;

            // Raw interpolated strings can use "$$" (holes are "{{x}}") and have their own brace and
            // indentation rules, so a text part cannot be swapped for a hole safely. Leave them as written.
            if (node.StringStartToken.Kind() is SyntaxKind.InterpolatedSingleLineRawStringStartToken
                or SyntaxKind.InterpolatedMultiLineRawStringStartToken)
                return visited;

            // Only a plain string target. FormattableString / IFormattable consumers (for example
            // parameterized SQL builders) read the format string, and interpolated string handlers
            // receive the literal parts, so changing the parts would change what they see.
            var convertedType = _model.GetTypeInfo(node).ConvertedType;
            if (convertedType == null || convertedType.SpecialType is not (SpecialType.System_String or SpecialType.System_Object))
                return visited;

            // The text parts become holes ({Decrypt("...")}) and every original hole is kept exactly
            // as written, so its format specifier, alignment and ToString behavior are untouched.
            // Joining the parts with "+" would instead add adjacent numeric/char/TimeSpan holes.
            var contents = new List<InterpolatedStringContentSyntax>();
            var encryptedAny = false;

            foreach (var content in visited.Contents)
            {
                if (content is InterpolatedStringTextSyntax text)
                {
                    // ValueText keeps the "{{" and "}}" escapes of the source; the value is the unescaped text.
                    var value = text.TextToken.ValueText.Replace("{{", "{", StringComparison.Ordinal)
                        .Replace("}}", "}", StringComparison.Ordinal);
                    if (string.IsNullOrEmpty(value) || value.Length < _settings.MinStringLength)
                    {
                        contents.Add(text);
                        continue;
                    }

                    var encrypted = EncryptionHelper.EncryptToBase64(value, _key, _settings.Algorithm);
                    _encryptedStrings.Add((value, encrypted));
                    EncryptedCount++;
                    encryptedAny = true;
                    // Parenthesized: a bare "global::" inside a hole would be cut at the first ':'.
                    contents.Add(SyntaxFactory.Interpolation(
                        SyntaxFactory.ParenthesizedExpression(CreateDecryptCall(encrypted))));
                }
                else
                {
                    contents.Add(content);
                }
            }

            return encryptedAny
                ? visited.WithContents(SyntaxFactory.List(contents))
                : visited;
        }

        private static bool MustRemainConstant(SyntaxNode node)
        {
            foreach (var ancestor in node.Ancestors())
            {
                switch (ancestor)
                {
                    case CaseSwitchLabelSyntax:
                    case ConstantPatternSyntax:
                        return true;
                    case EqualsValueClauseSyntax equals when IsConstInitializer(equals) || equals.Parent is ParameterSyntax:
                        return true;
                }
            }

            return false;
        }

        private static bool IsConstInitializer(EqualsValueClauseSyntax equals)
        {
            if (equals.Parent is not VariableDeclaratorSyntax { Parent: VariableDeclarationSyntax declaration })
                return false;

            return declaration.Parent switch
            {
                LocalDeclarationStatementSyntax local => local.IsConst,
                FieldDeclarationSyntax field => field.Modifiers.Any(SyntaxKind.ConstKeyword),
                _ => false
            };
        }

        private InvocationExpressionSyntax CreateDecryptCall(string encrypted)
        {
            return SyntaxFactory.InvocationExpression(
                _decryptTarget,
                SyntaxFactory.ArgumentList(
                    SyntaxFactory.SingletonSeparatedList(
                        SyntaxFactory.Argument(
                            SyntaxFactory.LiteralExpression(
                                SyntaxKind.StringLiteralExpression,
                                SyntaxFactory.Literal(encrypted))))));
        }
    }
}
