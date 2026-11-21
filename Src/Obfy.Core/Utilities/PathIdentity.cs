namespace Obfy.Core.Utilities;

/// <summary>
/// Whether two paths name the same file. Full paths, so a <c>..</c> segment, a different case on Windows or the
/// <c>\\?\</c> prefix do not hide it, and a symbolic link or a junction, in the file's name or in any folder above it,
/// is followed (<see cref="FileSystemInfo.ResolveLinkTarget"/>), the way the desktop app's output-collision check does.
/// Not covered: 8.3 short names, hard links, and a drive letter that aliases another path (<c>subst</c>, a mapped
/// drive); none of them can be told apart without native calls.
/// The incremental cache uses this to spot a run whose output replaces its input. It is not the guard that keeps a
/// stale cache from vouching for an unprotected file: the fingerprint of the output is (<see cref="FileFingerprint"/>).
/// Never throws; whatever the file system refuses is left as the text said.
/// </summary>
internal static class PathIdentity
{
    private const int MaxLinkDepth = 16;

    /// <summary>True when <paramref name="a"/> and <paramref name="b"/> lead to the same file.</summary>
    public static bool SameFile(string a, string b)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return string.Equals(Resolve(a), Resolve(b), comparison);
    }

    /// <summary>One spelling of the file a path names: absolute, no trailing separator, links followed.</summary>
    internal static string Resolve(string path)
    {
        var depth = 0;
        var full = Normalize(path);
        var folder = Path.GetDirectoryName(full);
        return folder is null
            ? full
            : ResolveEntry(Path.Combine(ResolveFolder(folder, ref depth), Path.GetFileName(full)), ref depth);
    }

    private static string ResolveFolder(string folder, ref int depth)
    {
        var parent = Path.GetDirectoryName(folder);
        return parent is null
            ? folder
            : ResolveEntry(Path.Combine(ResolveFolder(parent, ref depth), Path.GetFileName(folder)), ref depth);
    }

    /// <summary>An entry whose parent is already resolved: if it is a link, the place it leads to, with that place's own folders resolved.</summary>
    private static string ResolveEntry(string path, ref int depth)
    {
        if (depth >= MaxLinkDepth || ResolveLink(path) is not { } target)
            return path;

        var targetPath = Normalize(target.FullName);
        var targetFolder = Path.GetDirectoryName(targetPath);
        if (targetFolder is null)
            return targetPath;

        depth++;
        var resolved = Path.Combine(ResolveFolder(targetFolder, ref depth), Path.GetFileName(targetPath));
        depth--;
        return resolved;
    }

    private static FileSystemInfo? ResolveLink(string path)
    {
        try
        {
            // One attribute read tells a plain file or folder (nearly always) from a link; a missing entry reads as -1.
            var attributes = new FileInfo(path).Attributes;
            if ((int)attributes == -1 || !attributes.HasFlag(FileAttributes.ReparsePoint))
                return null;

            FileSystemInfo info = attributes.HasFlag(FileAttributes.Directory) ? new DirectoryInfo(path) : new FileInfo(path);
            return info.ResolveLinkTarget(returnFinalTarget: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private static string Normalize(string path)
    {
        string full;
        try
        {
            full = Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException or IOException)
        {
            full = path;
        }

        // \\?\C:\x and C:\x name the same file.
        if (full.StartsWith(@"\\?\", StringComparison.Ordinal) && full.Length > 6 && full[5] == ':')
            full = full[4..];

        return Path.TrimEndingDirectorySeparator(full);
    }
}
