using System.Text.RegularExpressions;

namespace Obfy.Core.Utilities;

/// <summary>
/// Single source of wildcard matching for exclusion/inclusion patterns across all obfuscators.
/// Supports <c>*</c> (any run of characters) and <c>?</c> (a single character), case-insensitively,
/// so a pattern like <c>System.*</c> or <c>Foo?Bar</c> means the same thing everywhere.
/// </summary>
public static class WildcardMatcher
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Regex> Cache = new(StringComparer.Ordinal);

    public static bool IsMatch(string? value, string pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);

        if (string.IsNullOrEmpty(value))
            return false;

        var regex = Cache.GetOrAdd(pattern, static p =>
        {
            var text = "^" + Regex.Escape(p).Replace("\\*", ".*").Replace("\\?", ".") + "$";
            return new Regex(text, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
        });
        return regex.IsMatch(value);
    }
}
