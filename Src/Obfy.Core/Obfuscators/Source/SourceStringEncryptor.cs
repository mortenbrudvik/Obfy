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

                var root = await tree.GetRootAsync(cancellationToken);
                var rewriter = new StringEncryptionRewriter(settings, key, encryptedStrings);
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
        catch (Exception ex)
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
                        var encrypted = new byte[data.Length - iv.Length];
                        Buffer.BlockCopy(data, iv.Length, encrypted, 0, encrypted.Length);
                        using var decryptor = aes.CreateDecryptor();
                        var decrypted = decryptor.TransformFinalBlock(encrypted, 0, encrypted.Length);
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

        public int EncryptedCount { get; private set; }

        public StringEncryptionRewriter(
            StringEncryptionSettings settings,
            byte[] key,
            List<(string Original, string Encrypted)> encryptedStrings)
        {
            _settings = settings;
            _key = key;
            _encryptedStrings = encryptedStrings;
        }

        public override SyntaxNode? VisitLiteralExpression(LiteralExpressionSyntax node)
        {
            if (!node.IsKind(SyntaxKind.StringLiteralExpression))
                return base.VisitLiteralExpression(node);

            if (node.Ancestors().Any(a => a is AttributeArgumentSyntax or AttributeSyntax))
                return base.VisitLiteralExpression(node);

            var value = node.Token.ValueText;

            if (string.IsNullOrEmpty(value) || value.Length < _settings.MinStringLength)
                return base.VisitLiteralExpression(node);

            // Encrypt the string
            var encrypted = EncryptionHelper.EncryptToBase64(value, _key, _settings.Algorithm);
            _encryptedStrings.Add((value, encrypted));
            EncryptedCount++;

            // Replace with: Obfy.Runtime.__ObfyStringDecryptor.Decrypt("encrypted")
            var decryptCall = SyntaxFactory.InvocationExpression(
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

            return decryptCall.WithTriviaFrom(node);
        }

        public override SyntaxNode? VisitInterpolatedStringExpression(InterpolatedStringExpressionSyntax node)
        {
            // For interpolated strings, we could potentially encrypt the constant parts
            // For now, skip them as they're more complex
            return base.VisitInterpolatedStringExpression(node);
        }
    }
}
