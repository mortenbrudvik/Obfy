namespace Obfy.App.Services;

/// <summary>
/// Asks the file system whether a path is there. A seam so the session can be tested with a probe that stalls: on a dead
/// network share <see cref="File.Exists(string?)"/> and <see cref="Directory.Exists(string?)"/> can block for a long time.
/// </summary>
public interface IPathProbe
{
    /// <summary>Whether a file is at <paramref name="path"/>. False when it is not, cannot be asked, or does not answer in time.</summary>
    bool FileExists(string path);

    /// <summary>Whether a folder is at <paramref name="path"/>. False when it is not, cannot be asked, or does not answer in time.</summary>
    bool DirectoryExists(string path);

    /// <summary>
    /// Where the file or folder at <paramref name="path"/> leads when it is a symbolic link or a junction (the final target),
    /// or null when it is not a link, is not there, or cannot be read. The output-collision check asks this for every listed
    /// file and every folder above it, so it is the question that touches the disk when a run is checked. The default asks
    /// the real file system, without a time limit.
    /// </summary>
    FileSystemInfo? ResolveLinkTarget(string path) => FileLinks.Resolve(path);
}

/// <summary>The real file system's answer to <see cref="IPathProbe.ResolveLinkTarget"/>.</summary>
internal static class FileLinks
{
    public static FileSystemInfo? Resolve(string path)
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
}

/// <summary>
/// The real file system, with a limit on how long <see cref="FileExists"/> and <see cref="DirectoryExists"/> may take: a
/// path that does not answer (an unreachable share) is reported as not there instead of holding the caller. The question
/// keeps running on a pool thread until the system gives up. <see cref="IPathProbe.ResolveLinkTarget"/> is not limited: it
/// is the interface's default, which asks the real file system and waits for the answer.
/// </summary>
internal sealed class BoundedPathProbe(TimeSpan? timeout = null) : IPathProbe
{
    private readonly TimeSpan _timeout = timeout ?? TimeSpan.FromSeconds(2);

    public bool FileExists(string path) => Bounded(() => File.Exists(path));

    public bool DirectoryExists(string path) => Bounded(() => Directory.Exists(path));

    private bool Bounded(Func<bool> probe)
    {
        var task = Task.Run(probe);
        return task.Wait(_timeout) && task.Result;
    }
}
