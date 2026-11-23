using System.Diagnostics;
using System.Windows;
using Microsoft.Extensions.Logging;
using Obfy.App.Sessions;

namespace Obfy.UI.Services;

/// <summary>
/// The one way out of the shell: closing the window, the failure panel's Quit and its Restart all pass through here.
/// A run executes on pool (background) threads, so ending the process under it cuts the file it is writing short. When a
/// run is going the user is asked first, the run is cancelled, and the shutdown waits (for a bounded time) until it has
/// stopped. <see cref="Application.Shutdown()"/> ignores a window's cancelled Closing, so Quit and Restart cannot rely on
/// the window to ask.
/// </summary>
public sealed class ShutdownCoordinator
{
    /// <summary>How long a shutdown waits for a cancelled run to stop; a run that never does must not keep Obfy open.</summary>
    public static readonly TimeSpan RunStopTimeout = TimeSpan.FromSeconds(10);

    /// <summary>How long a restart waits for the preferences to be written before it starts the new process anyway.</summary>
    public static readonly TimeSpan PreferencesTimeout = TimeSpan.FromSeconds(3);

    private readonly Func<bool> _isRunning;
    private readonly Action _cancelRun;
    private readonly Func<Task> _runCompletion;
    private readonly Func<bool> _confirmCancelRun;
    private readonly Func<bool> _tryRefuseNewRuns;
    private readonly Action _refuseNewRuns;
    private readonly Action _allowNewRuns;
    private readonly Func<Task> _flushPreferences;
    private readonly Func<bool> _startNewInstance;
    private readonly Action _shutdown;
    private readonly TimeSpan _runStopTimeout;
    private readonly TimeSpan _preferencesTimeout;
    private readonly ILogger _logger;

    // UI thread only.
    private bool _preparing;
    private bool _restarting;

    public ShutdownCoordinator(RunCoordinator run, InputSession input, ILogger<ShutdownCoordinator> logger)
        : this(
            () => run.IsRunning, run.Cancel, () => run.Completion, ConfirmWithUser,
            run.TryRefuseNewRuns, run.RefuseNewRuns, run.AllowNewRuns,
            input.PersistPreferencesAsync, () => StartNewInstance(logger), () => Application.Current.Shutdown(),
            RunStopTimeout, PreferencesTimeout, logger)
    {
    }

    internal ShutdownCoordinator(
        Func<bool> isRunning, Action cancelRun, Func<Task> runCompletion, Func<bool> confirmCancelRun,
        Func<bool> tryRefuseNewRuns, Action refuseNewRuns, Action allowNewRuns,
        Func<Task> flushPreferences, Func<bool> startNewInstance, Action shutdown,
        TimeSpan runStopTimeout, TimeSpan preferencesTimeout, ILogger logger)
    {
        _isRunning = isRunning;
        _cancelRun = cancelRun;
        _runCompletion = runCompletion;
        _confirmCancelRun = confirmCancelRun;
        _tryRefuseNewRuns = tryRefuseNewRuns;
        _refuseNewRuns = refuseNewRuns;
        _allowNewRuns = allowNewRuns;
        _flushPreferences = flushPreferences;
        _startNewInstance = startNewInstance;
        _shutdown = shutdown;
        _runStopTimeout = runStopTimeout;
        _preferencesTimeout = preferencesTimeout;
        _logger = logger;
    }

    /// <summary>
    /// Approves the shutdown at once when no run is going, and from that moment refuses new runs (one atomic step, see
    /// <see cref="RunCoordinator.TryRefuseNewRuns"/>): a run whose files are still being checked would otherwise become Running
    /// while the process exits. False, with nothing changed, when a run is going and the user has to be asked. Call on the UI thread.
    /// </summary>
    public bool TryApproveWithoutAsking()
    {
        if (_restarting || _preparing)
            return false;

        if (Approved)
            return true;

        if (!_tryRefuseNewRuns())
            return false;

        Approved = true;
        return true;
    }

    /// <summary>
    /// The shell is staying after all: a close was approved (new runs refused) but will not happen, because closing the
    /// window or shutting down failed. Takes the approval back and lets runs start again; the next close asks again. Call
    /// on the UI thread.
    /// </summary>
    public void AbandonClose()
    {
        Approved = false;
        _allowNewRuns();
    }

    /// <summary>The shutdown has been agreed (and any run stopped): the window may close without asking again.</summary>
    public bool Approved { get; private set; }

    /// <summary>
    /// Settles whether the shell may go away now. With no run: yes. With a run: asks, cancels it and waits for it to stop,
    /// then yes; a "no" answer, or a request that arrives while another is still waiting, is no. Once the answer is yes no
    /// new run can start, so the process does not exit under one. A "no" leaves everything as it was. Call on the UI thread.
    /// </summary>
    public async Task<bool> PrepareToCloseAsync()
    {
        if (TryApproveWithoutAsking())
            return true;

        // A second click on the close button while the first waits for the user or for the run: not another question.
        // While RestartAsync is flushing preferences, do not let a concurrent Quit or close preempt the restart.
        if (_preparing || _restarting)
            return false;

        _preparing = true;
        var doorClosed = false;
        try
        {
            if (!_confirmCancelRun())
                return false;

            // The user has said yes: no run may start behind the cancel, for example one whose files were still being
            // checked when the question was asked.
            _refuseNewRuns();
            doorClosed = true;

            // The run may have ended while the question was open.
            if (_isRunning())
            {
                _logger.LogInformation("Cancelling the run before the shell goes away");
                _cancelRun();
                await WaitAsync(_runCompletion, _runStopTimeout, "The run did not stop in time; closing anyway");
            }

            Approved = true;
            return true;
        }
        finally
        {
            _preparing = false;

            // Said yes, then something failed before the close was approved (the cancel's own event handler threw, say): the
            // shell is staying, so runs must be able to start.
            if (doorClosed && !Approved)
                _allowNewRuns();
        }
    }

    /// <summary>The failure panel's Quit.</summary>
    public async Task QuitAsync()
    {
        if (await PrepareToCloseAsync())
            ShutDown();
    }

    /// <summary>Ends the process through the shutdown action. If that throws the shell is staying, so the approval is taken back.</summary>
    private void ShutDown()
    {
        try
        {
            _shutdown();
        }
        catch (Exception)
        {
            AbandonClose();
            throw;
        }
    }

    /// <summary>
    /// The failure panel's Restart: the old process writes the preferences first, because the new one reads them as it
    /// starts. If no new process can be started this one stays open.
    /// </summary>
    public async Task RestartAsync()
    {
        if (_preparing || _restarting)
            return;

        if (!await PrepareToCloseAsync())
            return;

        _restarting = true;
        try
        {
            await WaitAsync(_flushPreferences, _preferencesTimeout, "The preferences were not written in time; restarting anyway");

            if (!_startNewInstance())
            {
                // Not leaving: a run started later must be asked about again, and must be able to start.
                AbandonClose();
                return;
            }

            _restarting = false;
            ShutDown();
        }
        catch (Exception) when (Approved)
        {
            // Starting the new process threw: the shell is staying too. (A throwing shutdown has already taken the approval back.)
            AbandonClose();
            throw;
        }
        finally
        {
            _restarting = false;
        }
    }

    /// <summary>Waits for the task <paramref name="start"/> returns for at most <paramref name="timeout"/>. Never throws; says why in the log (the exception type only).</summary>
    private async Task WaitAsync(Func<Task> start, TimeSpan timeout, string timeoutMessage)
    {
        try
        {
            await start().WaitAsync(timeout).ConfigureAwait(true);
        }
        catch (TimeoutException)
        {
            _logger.LogWarning("{Message}", timeoutMessage);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Waiting for the shell to wind down failed ({ExceptionType})", ex.GetType().Name);
        }
    }

    private static bool ConfirmWithUser()
    {
        const string text = "An obfuscation run is still in progress. Cancel it and close Obfy?";
        var owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive && w.IsLoaded)
            ?? Application.Current?.MainWindow;
        var answer = owner is { IsLoaded: true }
            ? MessageBox.Show(owner, text, "Obfy", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No)
            : MessageBox.Show(text, "Obfy", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        return answer == MessageBoxResult.Yes;
    }

    private static bool StartNewInstance(ILogger logger)
    {
        try
        {
            if (Environment.ProcessPath is not { } path)
                return false;
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning("Could not restart ({ExceptionType})", ex.GetType().Name);
            return false;
        }
    }
}
