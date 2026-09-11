using System.Security.Cryptography;
using System.Text;
using Obfy.Core.Models;

namespace Obfy.Core.Utilities;

/// <summary>
/// Generates obfuscated names for symbols.
/// </summary>
public interface INameGenerator
{
    /// <summary>
    /// Generates a new obfuscated name that is unique within this generator instance and is never
    /// a C# reserved or contextual keyword.
    /// </summary>
    /// <param name="mode">The naming mode to use.</param>
    /// <returns>An obfuscated name.</returns>
    string Generate(NamingMode mode);

    /// <summary>
    /// Generates an obfuscated name based on the original name.
    /// </summary>
    /// <param name="originalName">The original name to obfuscate.</param>
    /// <param name="mode">The naming mode to use.</param>
    /// <returns>An obfuscated name.</returns>
    string Generate(string originalName, NamingMode mode);

    /// <summary>
    /// Resets the generator state (for sequential naming).
    /// </summary>
    void Reset();
}

/// <summary>
/// Default implementation of name generation.
/// </summary>
public class NameGenerator : INameGenerator
{
    private int _sequentialCounter;
    private byte[] _hashSalt = RandomNumberGenerator.GetBytes(16);
    private static Random Rng => Random.Shared;

    // Guards the mutable state (_sequentialCounter, _used) so the generator is safe to share across
    // threads even though the pipeline currently drives it sequentially.
    private readonly object _lock = new();

    // Every name handed out during a run is recorded here so no two symbols can be given the same
    // name. Two identical names in one scope (methods/fields in a type, types in a namespace) would
    // produce invalid metadata, and the random/hash modes can otherwise collide.
    private readonly HashSet<string> _used = new(StringComparer.Ordinal);

    // Characters that look similar or are hard to read
    private static readonly char[] UnreadableChars = new[]
    {
        '\u200B', // Zero-width space
        '\u200C', // Zero-width non-joiner
        '\u200D', // Zero-width joiner
        '\u2060', // Word joiner
        '\uFEFF', // Zero-width no-break space
        'l', '1', 'I', 'O', '0',
        '\u0131', // Dotless i
        '\u0399', // Greek capital iota
        '\u03B9', // Greek small iota
    };

    // Standard alphanumeric for random mode
    private static readonly char[] AlphanumericChars =
        "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789".ToCharArray();

    // C# reserved keywords. Sequential (base-26) names can land on one of these (e.g. "do", "if",
    // "int"); using such a name for a renamed source symbol would not compile, so they are rejected.
    private static readonly HashSet<string> CSharpKeywords = new(StringComparer.Ordinal)
    {
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked",
        "class", "const", "continue", "decimal", "default", "delegate", "do", "double", "else",
        "enum", "event", "explicit", "extern", "false", "finally", "fixed", "float", "for",
        "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock",
        "long", "namespace", "new", "null", "object", "operator", "out", "override", "params",
        "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed", "short",
        "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true",
        "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "virtual",
        "void", "volatile", "while",
        // Contextual keywords that sequential base-26 names can still emit (e.g. "var", "file").
        "var", "record", "file", "required", "async", "await", "yield", "dynamic", "nint", "nuint",
        "nameof", "when", "where", "and", "or", "not", "with", "init", "managed", "unmanaged",
        "alias", "args", "from", "let", "select", "group", "into", "orderby", "join", "equals",
        "by", "on", "ascending", "descending"
    };

    /// <inheritdoc/>
    public string Generate(NamingMode mode)
    {
        return mode switch
        {
            NamingMode.Unreadable => EnsureUnique(GenerateUnreadable),
            NamingMode.Sequential => EnsureUnique(GenerateSequential),
            NamingMode.Random => EnsureUnique(GenerateRandom),
            NamingMode.Hash => EnsureUnique(GenerateRandom), // Fall back to random for no-input hash
            _ => EnsureUnique(GenerateRandom)
        };
    }

    /// <inheritdoc/>
    public string Generate(string originalName, NamingMode mode)
    {
        return mode switch
        {
            NamingMode.Unreadable => EnsureUnique(GenerateUnreadable),
            NamingMode.Sequential => EnsureUnique(GenerateSequential),
            NamingMode.Random => EnsureUnique(GenerateRandom),
            NamingMode.Hash => EnsureUnique(() => GenerateHash(originalName, _hashSalt)),
            _ => EnsureUnique(GenerateRandom)
        };
    }

    /// <inheritdoc/>
    public void Reset()
    {
        lock (_lock)
        {
            _sequentialCounter = 0;
            _hashSalt = RandomNumberGenerator.GetBytes(16);
            _used.Clear();
        }
    }

    /// <summary>
    /// Returns a name from <paramref name="generator"/> that has not been handed out in this run.
    /// Up to 16 attempts for any mode, then a base-26 suffix until the name is unused and not a
    /// keyword, so termination is guaranteed even if the generator's space is exhausted.
    /// </summary>
    private string EnsureUnique(Func<string> generator)
    {
        lock (_lock)
        {
            for (var attempt = 0; attempt < 16; attempt++)
            {
                var candidate = generator();
                if (IsAcceptable(candidate))
                    return candidate;
            }

            var baseName = generator();
            var counter = 0;
            string suffixed;
            do
            {
                suffixed = baseName + ToBase26(counter++);
            } while (!IsAcceptable(suffixed));
            return suffixed;
        }
    }

    // A name is acceptable if it is not a C# keyword and has not already been handed out.
    private bool IsAcceptable(string candidate) => !CSharpKeywords.Contains(candidate) && _used.Add(candidate);

    private string GenerateUnreadable()
    {
        var length = Rng.Next(6, 12);
        var chars = new char[length];

        // First character must be a valid identifier start
        chars[0] = '_';

        for (var i = 1; i < length; i++)
        {
            chars[i] = UnreadableChars[Rng.Next(UnreadableChars.Length)];
        }

        return new string(chars);
    }

    private string GenerateSequential()
    {
        var counter = _sequentialCounter++;
        return ToBase26(counter);
    }

    private string GenerateRandom()
    {
        var length = Rng.Next(8, 16);
        var chars = new char[length];

        // First character must be a letter or underscore
        chars[0] = AlphanumericChars[Rng.Next(52)]; // Only letters

        for (var i = 1; i < length; i++)
        {
            chars[i] = AlphanumericChars[Rng.Next(AlphanumericChars.Length)];
        }

        return new string(chars);
    }

    private static string GenerateHash(string originalName, byte[] salt)
    {
        var nameBytes = Encoding.UTF8.GetBytes(originalName);
        var data = new byte[salt.Length + nameBytes.Length];
        Buffer.BlockCopy(salt, 0, data, 0, salt.Length);
        Buffer.BlockCopy(nameBytes, 0, data, salt.Length, nameBytes.Length);
        var hash = SHA256.HashData(data);
        var sb = new StringBuilder("_");

        for (var i = 0; i < 8; i++)
        {
            sb.Append(hash[i].ToString("x2"));
        }

        return sb.ToString();
    }

    private static string ToBase26(int number)
    {
        var result = new StringBuilder();

        do
        {
            result.Insert(0, (char)('a' + (number % 26)));
            number /= 26;
        } while (number > 0);

        return result.ToString();
    }
}
