namespace Obfy.App.Sessions;

/// <summary>A file the run would write that is also one of the listed inputs, and the sentence that says so.</summary>
/// <param name="InputPath">The listed file that would be replaced.</param>
/// <param name="OutputPath">The planned output that lands on it.</param>
/// <param name="Message">What the run bar and the <c>run.blocked</c> error say: the problem, then the way out.</param>
public sealed record OutputCollision(string InputPath, string OutputPath, string Message);

/// <summary>
/// The data-loss guard: a run must never write over a file the user listed, because the original is gone afterwards (a closed
/// set even deletes the backup it made). It reads <see cref="OutputPlan.Outputs"/>, the paths the run itself writes by, so
/// the check and the run cannot disagree, and compares them with the listed files.
/// <para>
/// Paths are compared as full paths, ignoring case, so a trailing separator, a <c>..</c> segment or a different spelling of
/// the folder does not hide a collision. A symbolic link or a junction, in the file's name or in any folder above it, is
/// followed (<see cref="FileSystemInfo.ResolveLinkTarget"/>). Not covered: 8.3 short names, hard links, and a drive letter
/// that aliases another path (<c>subst</c>, a mapped drive); none of them can be told apart without native calls.
/// </para>
/// </summary>
public static class OutputCollisions
{
    private const int MaxLinkDepth = 16;

    /// <summary>The first planned output that would replace a listed file, or null when none would. Never throws.</summary>
    /// <param name="listedPaths">Every file the user listed, protected or not: the run replaces none of them.</param>
    /// <param name="plan">What the run would write.</param>
    /// <param name="defaultPlan">What it would write with no output folder; null when no folder is chosen. Tells the message whether clearing the folder is a way out.</param>
    public static OutputCollision? Find(IEnumerable<string> listedPaths, OutputPlan plan, OutputPlan? defaultPlan)
    {
        if (plan.Outputs.Count == 0)
            return null;

        var paths = new PathResolver();
        var listed = listedPaths.Select(p => (Path: p, Key: paths.ResolveFile(p))).ToList();

        var hit = FindHit(listed, plan, paths);
        if (hit is null)
            return null;

        return new OutputCollision(hit.Value.InputPath, hit.Value.OutputPath, Describe(hit.Value, plan, listed, defaultPlan, paths));
    }

    private static (string InputPath, string OutputPath)? FindHit(
        List<(string Path, string Key)> listed, OutputPlan plan, PathResolver paths)
    {
        foreach (var output in plan.Outputs)
        {
            var key = paths.ResolveFile(output.OutputPath);
            foreach (var (path, listedKey) in listed)
            {
                if (key.Equals(listedKey, StringComparison.OrdinalIgnoreCase))
                    return (path, output.OutputPath);
            }
        }

        return null;
    }

    private static string Describe(
        (string InputPath, string OutputPath) hit,
        OutputPlan plan,
        List<(string Path, string Key)> listed,
        OutputPlan? defaultPlan,
        PathResolver paths)
    {
        var name = Path.GetFileName(hit.InputPath);
        if (defaultPlan is null)
            return $"The run would write {name} over your input of the same name. Pick an output folder to write elsewhere.";

        // A closed set puts a shared name into a folder of its own, so the output folder is not always the folder of the file.
        var sameFolder = plan.Directory is not null
            && paths.ResolveFolderOf(plan.Directory).Equals(paths.ResolveDirectoryOf(hit.InputPath), StringComparison.OrdinalIgnoreCase);
        var problem = sameFolder
            ? $"The output folder is the folder of {name}; the run would replace your original."
            : $"The output folder holds {name}, one of your inputs; the run would replace your original.";

        // Clearing the folder is a way out only when the default names do not land on a listed file too.
        if (FindHit(listed, defaultPlan, paths) is not null)
            return $"{problem} Pick another folder.";

        var written = DefaultOutputFor(defaultPlan, hit.InputPath);
        if (written is null)
            return $"{problem} Pick another folder, or clear it to use the default location.";

        var writtenName = Path.GetFileName(written);
        var beside = paths.ResolveDirectoryOf(hit.InputPath).Equals(paths.ResolveDirectoryOf(written), StringComparison.OrdinalIgnoreCase);
        return beside
            ? $"{problem} Pick another folder, or clear it to write {writtenName} beside it."
            : $"{problem} Pick another folder, or clear it to write {writtenName} to {Path.GetDirectoryName(written)}.";
    }

    private static string? DefaultOutputFor(OutputPlan defaultPlan, string inputPath) =>
        defaultPlan.Mode == OutputMode.Merge
            ? defaultPlan.MergePath
            : defaultPlan.Outputs.FirstOrDefault(o => string.Equals(o.InputPath, inputPath, StringComparison.OrdinalIgnoreCase))?.OutputPath;

    /// <summary>
    /// Turns a path into one spelling of the file it names: absolute, no trailing separator, links followed. Folders are
    /// cached for the one call, so a list of files in a few folders costs a few file-system reads, not one per file.
    /// Whatever the file system refuses is left as the text said; nothing here throws.
    /// </summary>
    private sealed class PathResolver
    {
        private readonly Dictionary<string, string> _folders = new(StringComparer.OrdinalIgnoreCase);
        private int _depth;

        public string ResolveFile(string path)
        {
            var full = Normalize(path);
            var folder = Path.GetDirectoryName(full);
            return folder is null
                ? full
                : ResolveEntry(Path.Combine(ResolveFolder(folder), Path.GetFileName(full)));
        }

        /// <summary>A folder's own path, resolved: what <see cref="ResolveDirectoryOf"/> gives for a file inside it.</summary>
        public string ResolveFolderOf(string folder) => ResolveFolder(Normalize(folder));

        public string ResolveDirectoryOf(string path)
        {
            var folder = Path.GetDirectoryName(Normalize(path));
            return folder is null ? string.Empty : ResolveFolder(folder);
        }

        private string ResolveFolder(string folder)
        {
            if (_folders.TryGetValue(folder, out var cached))
                return cached;

            var parent = Path.GetDirectoryName(folder);
            var resolved = parent is null
                ? folder
                : ResolveEntry(Path.Combine(ResolveFolder(parent), Path.GetFileName(folder)));
            _folders[folder] = resolved;
            return resolved;
        }

        /// <summary>An entry whose parent is already resolved: if it is a link, the place it leads to, with that place's own folders resolved.</summary>
        private string ResolveEntry(string path)
        {
            if (_depth >= MaxLinkDepth || TryResolveLink(path) is not { } target)
                return path;

            var targetPath = Normalize(target.FullName);
            var targetFolder = Path.GetDirectoryName(targetPath);
            if (targetFolder is null)
                return targetPath;

            _depth++;
            try
            {
                return Path.Combine(ResolveFolder(targetFolder), Path.GetFileName(targetPath));
            }
            finally
            {
                _depth--;
            }
        }

        private static FileSystemInfo? TryResolveLink(string path)
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
}
