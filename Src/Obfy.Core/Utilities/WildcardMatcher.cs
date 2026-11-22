using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace Obfy.Core.Utilities;

/// <summary>
/// Single source of wildcard matching for exclusion/inclusion patterns across all obfuscators.
/// Supports <c>*</c> (any run of characters) and <c>?</c> (a single character), case-insensitively,
/// so a pattern like <c>System.*</c> or <c>Foo?Bar</c> means the same thing everywhere.
/// </summary>
public static class WildcardMatcher
{
    private const int MaxCacheEntries = 4096;
    private static readonly ConcurrentDictionary<string, Regex> Cache = new(StringComparer.Ordinal);

    public static bool IsMatch(string? value, string pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);

        if (string.IsNullOrEmpty(value))
            return false;

        if (!Cache.TryGetValue(pattern, out var regex))
        {
            regex = CompilePattern(pattern);
            if (Cache.Count >= MaxCacheEntries)
                Cache.Clear();
            Cache.TryAdd(pattern, regex);
        }

        return regex.IsMatch(value);
    }

    private static Regex CompilePattern(string pattern)
    {
        var text = "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
        return new Regex(text, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    }
}
