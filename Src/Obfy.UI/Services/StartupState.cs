using Microsoft.Extensions.Logging;

namespace Obfy.UI.Services;

/// <summary>
/// The two things the process applies before the shell exists: the last session's preferences, then the files, <c>-o</c>
/// folder and configuration it was started with. They are separate steps with separate failures: a preferences file that
/// cannot be read must not cost the command line (it is what the user just asked for), and neither failure may stop Obfy
/// from starting. Each says so to the user as a notice, and to the log by exception type only (a message names a file).
/// </summary>
internal static class StartupState
{
    /// <summary>How <see cref="StartAsync"/> ended.</summary>
    internal enum StartResult
    {
        Started,
        RuntimeMissing,
        Failed,
    }

    /// <summary>
    /// Runs the start-up, the WebView2 probe included, so that nothing it throws can leave the process without a window:
    /// the probe throws DllNotFoundException or COMException when the loader is broken, and an exception that escapes the
    /// async-void OnStartup is marked handled by the dispatcher handler, which leaves an invisible process that never exits.
    /// </summary>
    /// <param name="runtimeInstalled">Whether the WebView2 Runtime is there.</param>
    /// <param name="startHost">Builds and starts the host.</param>
    /// <param name="onFailure">Told the exception of a start that failed (the caller shows it and ends the process).</param>
    public static async Task<StartResult> StartAsync(Func<bool> runtimeInstalled, Func<Task> startHost, Action<Exception> onFailure)
    {
        try
        {
            if (!runtimeInstalled())
                return StartResult.RuntimeMissing;

            await startHost();
            return StartResult.Started;
        }
        catch (Exception ex)
        {
            onFailure(ex);
            return StartResult.Failed;
        }
    }

    internal const string PreferencesNoticeTitle = "Saved settings not loaded";
    internal const string ArgumentsNoticeTitle = "Start-up files not loaded";

    /// <param name="loadPreferences">Reads the saved preferences.</param>
    /// <param name="applyArguments">Applies the command line.</param>
    /// <param name="notify">Puts a notice (title, message) in front of the user. It must not throw; if it does, that is logged too.</param>
    /// <param name="logger">Where failures are logged.</param>
    public static async Task ApplyAsync(Func<Task> loadPreferences, Func<Task> applyArguments, Action<string, string> notify, ILogger logger)
    {
        await RunAsync(
            loadPreferences, notify, logger,
            "Could not load the saved preferences",
            PreferencesNoticeTitle, "Obfy could not read its saved preferences, so it started with the defaults.");

        await RunAsync(
            applyArguments, notify, logger,
            "Could not apply the start-up arguments",
            ArgumentsNoticeTitle, "Obfy could not apply the files, output folder or configuration it was started with. Add them again.");
    }

    private static async Task RunAsync(
        Func<Task> step, Action<string, string> notify, ILogger logger, string logMessage, string title, string message)
    {
        try
        {
            await step();
        }
        catch (Exception ex)
        {
            logger.LogError("{Message} ({ExceptionType})", logMessage, ex.GetType().Name);
            try
            {
                notify(title, message);
            }
            catch (Exception noticeEx)
            {
                logger.LogError("Could not tell the user ({ExceptionType})", noticeEx.GetType().Name);
            }
        }
    }
}
