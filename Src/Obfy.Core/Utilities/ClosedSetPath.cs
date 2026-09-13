namespace Obfy.Core.Utilities;

/// <summary>
/// Path helpers for closed-set TFM layout.
/// </summary>
internal static class ClosedSetPath
{
    /// <summary>
    /// Returns the last <c>netX.Y</c> or <c>netX.Y-platform</c> segment in <paramref name="path"/>, if any.
    /// </summary>
    public static string? FindTfmSegment(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return null;

        var segments = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        for (var i = segments.Length - 2; i >= 0; i--)
        {
            if (LooksLikeTfm(segments[i]))
                return segments[i];
        }

        return null;
    }

    // netX.Y or netX.Y-platform (net8.0, net10.0-windows). Excludes net48 / netstandard2.0.
    private static bool LooksLikeTfm(string segment)
    {
        if (segment.Length < 5 || !segment.StartsWith("net", StringComparison.OrdinalIgnoreCase))
            return false;

        var i = 3;
        if (!char.IsDigit(segment[i]))
            return false;
        while (i < segment.Length && char.IsDigit(segment[i]))
            i++;
        if (i >= segment.Length || segment[i] != '.')
            return false;
        i++;
        return i < segment.Length && char.IsDigit(segment[i]);
    }
}
