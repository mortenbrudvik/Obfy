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
    private readonly Random _random = new();

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
            NamingMode.Unreadable => GenerateUnreadable(),
            NamingMode.Sequential => GenerateSequential(),
            NamingMode.Random => GenerateRandom(),
            NamingMode.Hash => GenerateRandom(), // Fall back to random for no-input hash
            _ => GenerateRandom()
        };
    }

    /// <inheritdoc/>
    public string Generate(string originalName, NamingMode mode)
    {
        return mode switch
        {
            NamingMode.Unreadable => GenerateUnreadable(),
            NamingMode.Sequential => GenerateSequential(),
            NamingMode.Random => GenerateRandom(),
            NamingMode.Hash => GenerateHash(originalName),
            _ => GenerateRandom()
        };
    }

    /// <inheritdoc/>
    public void Reset()
    {
        _sequentialCounter = 0;
    }

    private string GenerateUnreadable()
    {
        var length = _random.Next(6, 12);
        var chars = new char[length];

        // First character must be a valid identifier start
        chars[0] = '_';

        for (var i = 1; i < length; i++)
        {
            chars[i] = UnreadableChars[_random.Next(UnreadableChars.Length)];
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
        var length = _random.Next(8, 16);
        var chars = new char[length];

        // First character must be a letter or underscore
        chars[0] = AlphanumericChars[_random.Next(52)]; // Only letters

        for (var i = 1; i < length; i++)
        {
            chars[i] = AlphanumericChars[_random.Next(AlphanumericChars.Length)];
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
