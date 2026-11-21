using Microsoft.Extensions.Logging;

namespace Obfy.UI.Services;

/// <summary>
/// How the shell logs an exception it cannot handle. The log file keeps Debug and above and must hold nothing the user typed
/// or picked, and an exception's message is exactly that kind of thing (a file that was not found, a folder that cannot be
/// written). What a crash report needs is where it happened and what kind of failure it was: the exception types and the
/// stack of the innermost one.
/// </summary>
internal static class CrashLog
{
    private const int MaxDepth = 10;

    /// <summary>Logs <paramref name="message"/> and <see cref="Describe"/> of the exception, as an error.</summary>
    public static void Error(ILogger logger, Exception ex, string message) =>
        logger.LogError("{Message}: {Exception}", message, Describe(ex));

    /// <summary>The exception types from the outside in ("A &gt; B"), then the stack trace of the innermost; no message.</summary>
    public static string Describe(Exception ex)
    {
        var types = new List<string>();
        var innermost = ex;
        for (Exception? current = ex; current is not null && types.Count < MaxDepth; current = current.InnerException)
        {
            types.Add(current.GetType().Name);
            innermost = current;
        }

        return string.Join(" > ", types) + Environment.NewLine + innermost.StackTrace;
    }
}
