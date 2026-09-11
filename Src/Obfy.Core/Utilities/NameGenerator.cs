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
    /// Generates a new obfuscated name.
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
            NamingMode.Hash => EnsureUnique(() => GenerateHash(originalName)),
            _ => EnsureUnique(GenerateRandom)
        };
    }

    /// <inheritdoc/>
    public void Reset()
    {
        lock (_lock)
        {
            _sequentialCounter = 0;
            _used.Clear();
        }
    }

    /// <summary>
    /// Returns a name from <paramref name="generator"/> that has not been handed out in this run.
    /// Retries the generator for the randomized modes, then falls back to a deterministic suffix so
    /// termination is guaranteed even if the generator's space is exhausted.
    /// </summary>
    private string EnsureUnique(Func<string> generator)
    {
        lock (_lock)
        {
            for (var attempt = 0; attempt < 16; attempt++)
            {
                var candidate = generator();
                if (_used.Add(candidate))
                    return candidate;
            }

            var baseName = generator();
            var counter = 0;
            string suffixed;
            do
            {
                suffixed = baseName + ToBase26(counter++);
            } while (!_used.Add(suffixed));
            return suffixed;
        }
    }

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

    private static string GenerateHash(string originalName)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(originalName));
        var sb = new StringBuilder("_");

        // Take first 8 bytes of hash
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
