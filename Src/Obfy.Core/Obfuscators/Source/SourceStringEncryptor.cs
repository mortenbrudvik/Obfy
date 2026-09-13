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

            foreach (var tree in compilation.SyntaxTrees)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var root = await tree.GetRootAsync(cancellationToken).ConfigureAwait(false);
                var model = compilation.GetSemanticModel(tree);
                var rewriter = new StringEncryptionRewriter(settings, key, encryptedStrings, model);
                var newRoot = rewriter.Visit(root);

                stats.StringsEncrypted += rewriter.EncryptedCount;

                newTrees.Add(newRoot.SyntaxTree);
            }

            // Add decryption helper class if any strings were encrypted
            if (stats.StringsEncrypted > 0)
            {
                var helperClass = GenerateDecryptionHelper(keyBase64, settings.Algorithm);
                newTrees.Add(CSharpSyntaxTree.ParseText(helperClass));
            }

            context.Compilation = compilation
                .RemoveAllSyntaxTrees()
                .AddSyntaxTrees(newTrees);

            _logger.LogInformation("Encrypted {Count} strings in source code", stats.StringsEncrypted);

            return ObfuscationResult.Successful(stats);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Source string encryption failed");
            return ObfuscationResult.Failed($"Source string encryption failed: {ex.Message}", ex);
        }
    }

    private string GenerateDecryptionHelper(string keyBase64, EncryptionAlgorithm algorithm)
    {
        var algorithmCode = algorithm == EncryptionAlgorithm.Aes256
            ? GenerateAesDecryptor()
            : GenerateXorDecryptor();

        return $$"""
            using System;
            using System.Text;
            {{(algorithm == EncryptionAlgorithm.Aes256 ? "using System.Security.Cryptography;" : "")}}

            namespace Obfy.Runtime
            {
                internal static class __ObfyStringDecryptor
                {
                    private static readonly byte[] _key = Convert.FromBase64String("{{keyBase64}}");

                    internal static string Decrypt(string encrypted)
                    {
                        if (string.IsNullOrEmpty(encrypted)) return encrypted;
                        var data = Convert.FromBase64String(encrypted);
                        {{algorithmCode}}
                    }
                }
            }
            """;
    }

    private static string GenerateAesDecryptor()
    {
        return """
                        using var aes = Aes.Create();
                        aes.Key = _key;
                        var iv = new byte[aes.BlockSize / 8];
                        Buffer.BlockCopy(data, 0, iv, 0, iv.Length);
                        aes.IV = iv;
                        var ciphertext = new byte[data.Length - iv.Length];
                        Buffer.BlockCopy(data, iv.Length, ciphertext, 0, ciphertext.Length);
                        using var decryptor = aes.CreateDecryptor();
                        var decrypted = decryptor.TransformFinalBlock(ciphertext, 0, ciphertext.Length);
                        return Encoding.UTF8.GetString(decrypted);
            """;
    }

    private static string GenerateXorDecryptor()
    {
        return """
                        var result = new byte[data.Length];
                        for (int i = 0; i < data.Length; i++)
                            result[i] = (byte)(data[i] ^ _key[i % _key.Length]);
                        return Encoding.UTF8.GetString(result);
            """;
    }

    private class StringEncryptionRewriter : CSharpSyntaxRewriter
    {
        private readonly StringEncryptionSettings _settings;
        private readonly byte[] _key;
        private readonly List<(string Original, string Encrypted)> _encryptedStrings;
        private readonly SemanticModel _model;

        public int EncryptedCount { get; private set; }

        public StringEncryptionRewriter(
            StringEncryptionSettings settings,
            byte[] key,
            List<(string Original, string Encrypted)> encryptedStrings,
            SemanticModel model)
        {
            _settings = settings;
            _key = key;
            _encryptedStrings = encryptedStrings;
            _model = model;
        }

        public override SyntaxNode? VisitLiteralExpression(LiteralExpressionSyntax node)
        {
            if (!node.IsKind(SyntaxKind.StringLiteralExpression))
                return base.VisitLiteralExpression(node);

            if (node.Ancestors().Any(a => a is AttributeArgumentSyntax or AttributeSyntax))
                return base.VisitLiteralExpression(node);

            var enclosing = _model.GetEnclosingSymbol(node.SpanStart);
            if (enclosing != null && ObfuscationAttributeRules.IsExcluded(enclosing, ObfuscationFeature.Strings))
                return base.VisitLiteralExpression(node);

            var value = node.Token.ValueText;

            if (!_settings.EncryptConstantStrings)
                return base.VisitLiteralExpression(node);

            if (string.IsNullOrEmpty(value) || value.Length < _settings.MinStringLength)
                return base.VisitLiteralExpression(node);

            // Encrypt the string
            var encrypted = EncryptionHelper.EncryptToBase64(value, _key, _settings.Algorithm);
            _encryptedStrings.Add((value, encrypted));
            EncryptedCount++;

            return CreateDecryptCall(encrypted).WithTriviaFrom(node);
        }

        public override SyntaxNode? VisitInterpolatedStringExpression(InterpolatedStringExpressionSyntax node)
        {
            if (!_settings.EncryptConstantStrings)
                return base.VisitInterpolatedStringExpression(node);

            if (node.Contents.OfType<InterpolationSyntax>().Any(i => i.AlignmentClause != null || i.FormatClause != null))
                return base.VisitInterpolatedStringExpression(node);

            ExpressionSyntax? combined = null;
            var encryptedAny = false;

            foreach (var content in node.Contents)
            {
                ExpressionSyntax piece;
                if (content is InterpolatedStringTextSyntax text)
                {
                    var value = text.TextToken.ValueText;
                    if (string.IsNullOrEmpty(value) || value.Length < _settings.MinStringLength)
                    {
                        piece = SyntaxFactory.LiteralExpression(
                            SyntaxKind.StringLiteralExpression,
                            SyntaxFactory.Literal(value));
                    }
                    else
                    {
                        var encrypted = EncryptionHelper.EncryptToBase64(value, _key, _settings.Algorithm);
                        _encryptedStrings.Add((value, encrypted));
                        EncryptedCount++;
                        encryptedAny = true;
                        piece = CreateDecryptCall(encrypted);
                    }
                }
                else if (content is InterpolationSyntax interpolation)
                {
                    piece = SyntaxFactory.InvocationExpression(
                        SyntaxFactory.MemberAccessExpression(
                            SyntaxKind.SimpleMemberAccessExpression,
                            interpolation.Expression,
                            SyntaxFactory.IdentifierName("ToString")))
                        .WithArgumentList(SyntaxFactory.ArgumentList());
                }
                else
                {
                    return base.VisitInterpolatedStringExpression(node);
                }

                combined = combined == null
                    ? piece
                    : SyntaxFactory.BinaryExpression(SyntaxKind.AddExpression, combined, piece);
            }

            if (!encryptedAny || combined == null)
                return base.VisitInterpolatedStringExpression(node);

            return combined.WithTriviaFrom(node);
        }

        private static InvocationExpressionSyntax CreateDecryptCall(string encrypted)
        {
            return SyntaxFactory.InvocationExpression(
                SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    SyntaxFactory.MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        SyntaxFactory.MemberAccessExpression(
                            SyntaxKind.SimpleMemberAccessExpression,
                            SyntaxFactory.IdentifierName("Obfy"),
                            SyntaxFactory.IdentifierName("Runtime")),
                        SyntaxFactory.IdentifierName("__ObfyStringDecryptor")),
                    SyntaxFactory.IdentifierName("Decrypt")),
                SyntaxFactory.ArgumentList(
                    SyntaxFactory.SingletonSeparatedList(
                        SyntaxFactory.Argument(
                            SyntaxFactory.LiteralExpression(
                                SyntaxKind.StringLiteralExpression,
                                SyntaxFactory.Literal(encrypted))))));
        }
    }
}
