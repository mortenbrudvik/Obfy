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

                newTrees.Add(CSharpSyntaxTree.Create(
                    (CSharpSyntaxNode)newRoot,
                    options: tree.Options as CSharpParseOptions,
                    path: tree.FilePath,
                    encoding: tree.Encoding));
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
        var eof = root.EndOfFileToken;

        if (members.OfType<FileScopedNamespaceDeclarationSyntax>().FirstOrDefault() is { } scoped)
        {
            var (leadingCloseTrivia, trailingCloseTrivia, remainingEofTrivia) =
                SplitFileScopedEofTrivia(root, scoped, eof.LeadingTrivia, newLine);

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
                SyntaxFactory.Token(SyntaxKind.CloseBraceToken)
                    .WithLeadingTrivia(leadingCloseTrivia)
                    .WithTrailingTrivia(trailingCloseTrivia),
                default);
            members = members.Replace(scoped, block);
            eof = eof.WithLeadingTrivia(remainingEofTrivia);
        }

        var newRoot = root
            .WithMembers(members.Add(member.WithLeadingTrivia(newLine, newLine)))
            .WithEndOfFileToken(eof);
        return CSharpSyntaxTree.Create(
            newRoot,
            options: tree.Options as CSharpParseOptions,
            path: tree.FilePath,
            encoding: tree.Encoding);
    }

    private static (SyntaxTriviaList LeadingClose, SyntaxTriviaList TrailingClose, SyntaxTriviaList RemainingEof)
        SplitFileScopedEofTrivia(
            CompilationUnitSyntax root,
            FileScopedNamespaceDeclarationSyntax scoped,
            SyntaxTriviaList eofTrivia,
            SyntaxTrivia newLine)
    {
        var outerDepth = 0;
        foreach (var trivia in root.DescendantTrivia(descendIntoTrivia: true))
        {
            if (trivia.SpanStart >= scoped.SemicolonToken.SpanStart)
                break;
            UpdateDirectiveDepth(trivia, ref outerDepth);
        }

        var innerDepth = 0;
        foreach (var trivia in scoped.DescendantTrivia(descendIntoTrivia: true))
        {
            if (trivia.SpanStart < scoped.SemicolonToken.Span.End)
                continue;
            UpdateDirectiveDepth(trivia, ref innerDepth, ref outerDepth);
        }

        if (innerDepth == 0 && !eofTrivia.Any(static t => t.IsDirective))
        {
            return (SyntaxFactory.TriviaList(newLine), SyntaxFactory.TriviaList(newLine), eofTrivia);
        }

        var beforeClose = new List<SyntaxTrivia>();
        var afterClose = new List<SyntaxTrivia>();
        var index = 0;

        // Directives opened inside the file-scoped namespace (e.g. `#if ...` before a type) must close
        // inside the converted block namespace before its closing `}`.
        while (index < eofTrivia.Count && innerDepth > 0)
        {
            var trivia = eofTrivia[index++];
            beforeClose.Add(trivia);
            UpdateDirectiveDepth(trivia, ref innerDepth);
        }

        // Remaining directives close an `#if` opened before the namespace (go after `}` so the appended
        // helper stays outside the conditional) or belong to a trailing block after the last member.
        var lastRemainingDirective = -1;
        for (var i = index; i < eofTrivia.Count; i++)
        {
            if (eofTrivia[i].IsDirective)
                lastRemainingDirective = i;
        }

        if (lastRemainingDirective >= index)
        {
            if (outerDepth > 0)
            {
                var localDepth = 0;
                while (index <= lastRemainingDirective && (outerDepth > 0 || localDepth > 0))
                {
                    var trivia = eofTrivia[index++];
                    afterClose.Add(trivia);
                    UpdateDirectiveDepth(trivia, ref localDepth, ref outerDepth);
                }
            }
            else
            {
                while (index <= lastRemainingDirective)
                    beforeClose.Add(eofTrivia[index++]);
            }
        }

        var remaining = new List<SyntaxTrivia>(eofTrivia.Count - index);
        for (var i = index; i < eofTrivia.Count; i++)
            remaining.Add(eofTrivia[i]);

        if (beforeClose.Count == 0 || !EndsWithNewLine(beforeClose[^1]))
            beforeClose.Add(newLine);

        if (afterClose.Count > 0)
        {
            if (!afterClose[0].IsKind(SyntaxKind.EndOfLineTrivia))
                afterClose.Insert(0, newLine);
            if (!EndsWithNewLine(afterClose[^1]))
                afterClose.Add(newLine);
        }
        else
        {
            afterClose.Add(newLine);
        }

        return (SyntaxFactory.TriviaList(beforeClose), SyntaxFactory.TriviaList(afterClose), SyntaxFactory.TriviaList(remaining));
    }

    private static bool IsDirectiveOpen(SyntaxTrivia trivia) =>
        trivia.IsKind(SyntaxKind.IfDirectiveTrivia) || trivia.IsKind(SyntaxKind.RegionDirectiveTrivia);

    private static bool IsDirectiveClose(SyntaxTrivia trivia) =>
        trivia.IsKind(SyntaxKind.EndIfDirectiveTrivia) || trivia.IsKind(SyntaxKind.EndRegionDirectiveTrivia);

    private static void UpdateDirectiveDepth(SyntaxTrivia trivia, ref int depth)
    {
        if (IsDirectiveOpen(trivia))
            depth++;
        else if (IsDirectiveClose(trivia) && depth > 0)
            depth--;
    }

    private static void UpdateDirectiveDepth(SyntaxTrivia trivia, ref int innerDepth, ref int outerDepth)
    {
        if (IsDirectiveOpen(trivia))
        {
            innerDepth++;
        }
        else if (IsDirectiveClose(trivia))
        {
            if (innerDepth > 0)
                innerDepth--;
            else if (outerDepth > 0)
                outerDepth--;
        }
    }

    private static bool EndsWithNewLine(SyntaxTrivia trivia)
    {
        if (trivia.IsKind(SyntaxKind.EndOfLineTrivia))
            return true;
        var text = trivia.ToFullString();
        return text.EndsWith('\n') || text.EndsWith('\r');
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
            var isVerbatim = visited.StringStartToken.IsKind(SyntaxKind.InterpolatedVerbatimStringStartToken);

            foreach (var content in visited.Contents)
            {
                if (content is InterpolatedStringTextSyntax text)
                {
                    // Decode from Token.Text rather than ValueText: ValueText keeps literal "{{"/"}}"
                    // doubled while unescaping "\u007b" -> "{", which would collapse "\u007b\u007b" into "{".
                    if (!TryDecodeInterpolatedText(text.TextToken.Text, isVerbatim, out var value) ||
                        string.IsNullOrEmpty(value) ||
                        value.Length < _settings.MinStringLength)
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

        private static bool TryDecodeInterpolatedText(string rawText, bool isVerbatim, out string decoded)
        {
            var sb = new System.Text.StringBuilder(rawText.Length);
            for (var i = 0; i < rawText.Length;)
            {
                var ch = rawText[i];
                if (ch is '{' or '}')
                {
                    if (i + 1 < rawText.Length && rawText[i + 1] == ch)
                    {
                        sb.Append(ch);
                        i += 2;
                        continue;
                    }

                    decoded = string.Empty;
                    return false;
                }

                if (isVerbatim)
                {
                    if (ch == '"' && i + 1 < rawText.Length && rawText[i + 1] == '"')
                    {
                        sb.Append('"');
                        i += 2;
                        continue;
                    }

                    sb.Append(ch);
                    i++;
                    continue;
                }

                if (ch != '\\')
                {
                    sb.Append(ch);
                    i++;
                    continue;
                }

                if (!TryReadEscapeSequence(rawText, ref i, sb))
                {
                    decoded = string.Empty;
                    return false;
                }
            }

            decoded = sb.ToString();
            return true;
        }

        private static bool TryReadEscapeSequence(string rawText, ref int index, System.Text.StringBuilder sb)
        {
            index++;
            if (index >= rawText.Length)
                return false;

            var esc = rawText[index++];
            switch (esc)
            {
                case '\'': sb.Append('\''); return true;
                case '"': sb.Append('"'); return true;
                case '\\': sb.Append('\\'); return true;
                case '0': sb.Append('\0'); return true;
                case 'a': sb.Append('\a'); return true;
                case 'b': sb.Append('\b'); return true;
                case 'e': sb.Append('\u001b'); return true;
                case 'f': sb.Append('\f'); return true;
                case 'n': sb.Append('\n'); return true;
                case 'r': sb.Append('\r'); return true;
                case 't': sb.Append('\t'); return true;
                case 'v': sb.Append('\v'); return true;
                case 'u':
                    if (!TryReadHexDigits(rawText, ref index, 4, 4, out var codeUnit))
                        return false;
                    sb.Append((char)codeUnit);
                    return true;
                case 'x':
                    if (!TryReadHexDigits(rawText, ref index, 1, 4, out var hexChar))
                        return false;
                    sb.Append((char)hexChar);
                    return true;
                case 'U':
                    if (!TryReadHexDigits(rawText, ref index, 8, 8, out var codePoint))
                        return false;
                    if (codePoint > 0x10FFFF || (codePoint >= 0xD800 && codePoint <= 0xDFFF))
                        return false;
                    sb.Append(char.ConvertFromUtf32((int)codePoint));
                    return true;
                default:
                    return false;
            }
        }

        private static bool TryReadHexDigits(string rawText, ref int index, int minDigits, int maxDigits, out uint value)
        {
            value = 0;
            var count = 0;
            while (count < maxDigits && index < rawText.Length && TryHexValue(rawText[index], out var digit))
            {
                value = (value << 4) | digit;
                index++;
                count++;
            }

            return count >= minDigits;
        }

        private static bool TryHexValue(char ch, out uint digit)
        {
            if (ch is >= '0' and <= '9')
            {
                digit = (uint)(ch - '0');
                return true;
            }
            if (ch is >= 'a' and <= 'f')
            {
                digit = (uint)(ch - 'a' + 10);
                return true;
            }
            if (ch is >= 'A' and <= 'F')
            {
                digit = (uint)(ch - 'A' + 10);
                return true;
            }

            digit = 0;
            return false;
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
