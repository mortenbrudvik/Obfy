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
}

/// <summary>
/// The real file system, with a limit on how long one question may take: a path that does not answer (an unreachable
/// share) is reported as not there instead of holding the caller. The question keeps running on a pool thread until the
/// system gives up.
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
