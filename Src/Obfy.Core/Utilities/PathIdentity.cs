namespace Obfy.Core.Utilities;

/// <summary>
/// Whether two paths name the same file. Full paths, so a <c>..</c> segment, a different case on Windows or the
/// <c>\\?\</c> and <c>\\?\UNC\</c> prefixes do not hide it, and a symbolic link or a junction, in the file's name or in
/// any folder above it, is followed (<see cref="FileSystemInfo.ResolveLinkTarget"/>), the way the desktop app's
/// output-collision check does. <see cref="Path.GetFullPath(string)"/> expands the 8.3 short name of an existing file on
/// Windows, so that spelling is covered too. Not covered: hard links, and a drive or share that aliases another path
/// (<c>subst</c>, a mapped drive, an admin share such as <c>\\localhost\c$</c>); none of them can be told apart without
/// native calls.
/// The incremental cache uses this to spot a run whose output replaces its input. It is not the guard that keeps a
/// stale cache from vouching for an unprotected file: the fingerprint of the output is (<see cref="FileFingerprint"/>).
/// Never throws, and never recurses with the length of a path: whatever the file system refuses is left as the text
/// said, and a path of more than <see cref="MaxComponents"/> components is compared as text.
/// </summary>
internal static class PathIdentity
{
    private const int MaxLinkDepth = 16;

    /// <summary>
    /// More components than any real path has (the OS refuses paths of about 32,000 characters, and a deep tree is a few
    /// dozen folders). Walking one costs a file-system read per component, so a longer path is not walked.
    /// </summary>
    internal const int MaxComponents = 1024;

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
        return ResolveFull(Normalize(path), ref depth);
    }

    /// <summary>
    /// Walks a full path from its root, one component at a time, and replaces each prefix that is a link by the place it
    /// leads to. A loop, not recursion, so the length of the path cannot overflow the stack.
    /// </summary>
    private static string ResolveFull(string full, ref int depth)
    {
        var root = Path.GetPathRoot(full);
        if (string.IsNullOrEmpty(root))
            return full;

        var components = full[root.Length..].Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
        if (components.Length > MaxComponents)
            return full;

        var current = root;
        foreach (var component in components)
            current = ResolveEntry(Path.Combine(current, component), ref depth);

        return current;
    }

    /// <summary>
    /// An entry whose parent is already resolved: if it is a link, the place it leads to, with that place's own folders
    /// resolved. Recursion here follows links, which <see cref="MaxLinkDepth"/> bounds, never the length of a path.
    /// </summary>
    private static string ResolveEntry(string path, ref int depth)
    {
        if (depth >= MaxLinkDepth || ResolveLink(path) is not { } target)
            return path;

        depth++;
        var resolved = ResolveFull(Normalize(target.FullName), ref depth);
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

    internal static string Normalize(string path)
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

        // \\?\UNC\server\share\x and \\server\share\x name the same file, and so do \\?\C:\x and C:\x.
        if (full.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
            full = @"\\" + full[8..];
        else if (full.StartsWith(@"\\?\", StringComparison.Ordinal) && full.Length > 6 && full[5] == ':')
            full = full[4..];

        return Path.TrimEndingDirectorySeparator(full);
    }
}
