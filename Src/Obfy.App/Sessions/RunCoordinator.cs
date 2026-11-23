using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Obfy.App.Models;
using Obfy.App.Services;
using Obfy.Core.Models;
using Obfy.Core.Models.Solution;
using Obfy.Core.Services;
using Obfy.Core.Services.Reporting;
using LogLevel = Obfy.App.Models.LogLevel;

namespace Obfy.App.Sessions;

/// <summary>
/// Runs the obfuscation: lifted from the run half of the WPF <c>MainViewModel</c> without WPF, commands or a dispatcher.
/// A singleton that drives the four sessions: it reads the settings (<see cref="ProtectionSession"/>) and the files
/// (<see cref="InputSession"/>), writes the file statuses back to the file list, writes the log (<see cref="LogBuffer"/>)
/// and hands the outcome to <see cref="ResultsStore"/>.
/// <para>
/// <b>One rule for the mode.</b> How a run treats the included files is decided by <see cref="InputSession.PlanOutput"/>,
/// the same call that produces the output hint the page shows, so the hint and the run cannot disagree (UI-053): merge
/// when the merge option is on and every included file is an assembly; a closed set for two or more included assemblies or
/// any that came from a solution; otherwise each file alone, with two or more C# files compiled as one.
/// </para>
/// <para>
/// <b>Explicit blockers.</b> <see cref="RunSnapshot.CanStart"/> and <see cref="RunSnapshot.BlockedReason"/> are state, not a
/// command's <c>CanExecute</c>: a run is blocked while one is running, with nothing included, when an output or the symbol
/// map would replace a listed file, or while the protection settings are invalid, and each says why in plain words
/// (UI-052). The settings' reason is the first of <see cref="ProtectionSession.ValidationErrors"/>; no rule is repeated
/// here.
/// </para>
/// <para>
/// <b>Threads.</b> <see cref="Start"/> returns at once; the run loop is on the thread pool and publishes
/// <c>run.changed</c> from there, which is safe because a run starts only after the shell exists. Every change to this
/// class's state runs under a lock and bumps the revision; events go out after it, outside the lock. Nothing that touches
/// the disk runs under that lock (<see cref="IsRunning"/>, <see cref="EnsureNotRunning"/> and <see cref="WhileIdle(Action)"/>
/// wait on it, and the window's close handler calls the first): <see cref="Start"/> reads and checks the settings and the
/// files first and takes the lock only to confirm they are unchanged and to become Running. Under it the other sessions are
/// asked a revision only, read without their locks. The loop never throws: cancel, a failure and an unexpected error each
/// end the run with a status.
/// </para>
/// <para>
/// <b>Push order.</b> Snapshots are built outside <c>_publishGate</c> (so disk probes in <see cref="InputSession.CaptureRun"/>
/// never block other publishers) and pushed under <c>_publishGate</c> only when their revision is at least
/// <c>_lastPublishedRevision</c>, so pushes leave in revision order: a late <c>run.changed</c> from one thread can never
/// follow the final one from the run thread with older values. Pushing does not block (the web view queues it), so the lock is short.
/// </para>
/// </summary>
public sealed class RunCoordinator : IDisposable
{
    private const string SourceNotInClosedSet = "Source files are not part of a closed-set session";
    private const string NotProcessed = "The closed set returned no result for this assembly.";

    /// <summary>
    /// The Results screen lists the symbols of every run, and every run builds a report that the export can write to
    /// disk. The incremental cache never stores the symbol map, the skipped items or the warning texts (they name the
    /// original types, members and resources): a run whose cached output renamed symbols, skipped items or raised
    /// warnings is done again, so neither the list nor the exported report is thinner than the run it describes.
    /// </summary>
    private static readonly ObfuscationRunOptions ResultsAndReportNeedTheFullRun =
        new() { NeedsSymbolMap = true, NeedsReport = true };

    private readonly object _gate = new();
    private readonly object _publishGate = new();
    private readonly IObfuscationService _obfuscation;
    private readonly IReportService _reports;
    private readonly ProtectionSession _protection;
    private readonly InputSession _input;
    private readonly LogBuffer _log;
    private readonly ResultsStore _results;
    private readonly IEventPublisher? _events;
    private readonly ILogger<RunCoordinator>? _logger;
    private readonly Action _onDependencyChanged;

    private long _revision;
    private RunStatus _status = RunStatus.Idle;
    private string _message = "Ready";
    private double _progress;
    private bool _indeterminate;
    private string? _currentFile;
    private int _completed;
    private int _total;
    private int _failed;
    private RunPlan? _active;
    private CancellationTokenSource? _cts;
    private Task _runTask = Task.CompletedTask;
    private long _lastPublishedRevision = -1;
    private int _whileIdleDepth;
    private bool _deferredDependencyChange;
    private bool _disposed;
    private bool _closing;

    /// <param name="events">Where <c>run.changed</c> and <c>run.completed</c> go; null for a coordinator nobody listens to.</param>
    /// <param name="logger">The file log. A failure is logged by its exception type only, never its message: that can hold a path the user chose.</param>
    public RunCoordinator(
        IObfuscationService obfuscation,
        IReportService reports,
        ProtectionSession protection,
        InputSession input,
        LogBuffer log,
        ResultsStore results,
        IEventPublisher? events = null,
        ILogger<RunCoordinator>? logger = null)
    {
        _obfuscation = obfuscation;
        _reports = reports;
        _protection = protection;
        _input = input;
        _log = log;
        _results = results;
        _events = events;
        _logger = logger;

        // Whether a run can start, and how, depends on the settings and the files: follow both.
        _onDependencyChanged = OnDependencyChanged;
        _protection.Changed += _onDependencyChanged;
        _input.Changed += _onDependencyChanged;
    }

    /// <summary>Raised once after any change, on the thread that made it, outside the coordinator's lock.</summary>
    public event Action? Changed;

    /// <summary>What the host answers when a change to the files or the configuration is asked for while a run goes.</summary>
    public const string InputBlockedMessage = "The files and the configuration cannot change while a run is in progress.";

    /// <summary>Whether a run is going now.</summary>
    public bool IsRunning
    {
        get
        {
            lock (_gate)
                return _status == RunStatus.Running;
        }
    }

    /// <summary>
    /// The rule that input is refused during a run, kept here in the host and not in the page: a native drop and the picker
    /// shortcut never pass through the page's disabled controls.
    /// </summary>
    /// <exception cref="ObfyApiException"><c>run.blocked</c>: a run is going.</exception>
    public void EnsureNotRunning()
    {
        lock (_gate)
            EnsureNotRunningLocked();
    }

    private void EnsureNotRunningLocked()
    {
        if (_status == RunStatus.Running)
            throw new ObfyApiException("run.blocked", InputBlockedMessage);
    }

    /// <summary>
    /// Runs a quick in-memory mutation atomically with the check that no run is in progress, under the run gate, so
    /// <see cref="Start"/> cannot interleave between checking and mutating. Dependency-change publishing is deferred
    /// until the gate is released so disk I/O never runs under the lock.
    /// </summary>
    /// <exception cref="ObfyApiException"><c>run.blocked</c>: a run is going.</exception>
    public void WhileIdle(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        WhileIdle<object?>(() =>
        {
            action();
            return null;
        });
    }

    /// <inheritdoc cref="WhileIdle(Action)"/>
    public T WhileIdle<T>(Func<T> func)
    {
        ArgumentNullException.ThrowIfNull(func);
        var publish = false;
        try
        {
            lock (_gate)
            {
                EnsureNotRunningLocked();
                _whileIdleDepth++;
                try
                {
                    return func();
                }
                finally
                {
                    _whileIdleDepth--;
                    publish = _whileIdleDepth == 0 && _deferredDependencyChange;
                    if (publish)
                        _deferredDependencyChange = false;
                }
            }
        }
        finally
        {
            if (publish)
                PublishChanged();
        }
    }

    /// <summary>
    /// For the shutdown path: closes the door on new runs, but only if none is going. Atomic with the check, under the lock
    /// <see cref="Start"/> takes to become Running: either a run is Running before this (it answers false, nothing
    /// changes, and the shell asks the user), or it can no longer begin (<see cref="Start"/> is refused with
    /// <c>run.blocked</c>). Without this a <see cref="Start"/> that is still checking its files, where
    /// <see cref="IsRunning"/> is false, would become a run while the process exits.
    /// </summary>
    /// <returns>True when new runs are now refused; false when a run goes.</returns>
    public bool TryRefuseNewRuns()
    {
        lock (_gate)
        {
            if (_status == RunStatus.Running)
                return false;
            SetClosing(true);
            return true;
        }
    }

    /// <summary>Closes the door on new runs whether or not one is going: the user has agreed to cancel it and close.</summary>
    public void RefuseNewRuns()
    {
        lock (_gate)
            SetClosing(true);
    }

    /// <summary>Opens the door again, for a shutdown that was given up (a restart that could not start the new process).</summary>
    public void AllowNewRuns()
    {
        lock (_gate)
            SetClosing(false);
    }

    /// <summary>
    /// Caller holds the gate. A change bumps the revision, so a snapshot read afterwards (<see cref="GetSnapshot"/> says why
    /// a closing shell cannot start) is not mistaken for the one before. Nothing is pushed: this is called from the
    /// shell's close handler on the UI thread, and a push would take a snapshot, which checks the files on the disk.
    /// </summary>
    private void SetClosing(bool closing)
    {
        if (_closing == closing)
            return;
        _closing = closing;
        _revision++;
    }

    /// <summary>Finishes when the run now going ends (or already has). Never faults.</summary>
    public Task Completion
    {
        get
        {
            lock (_gate)
                return _runTask;
        }
    }

    // ---- Snapshot ----------------------------------------------------------------------------------------------

    private readonly record struct SnapshotState(
        bool Closing,
        RunStatus Status,
        string Message,
        double Progress,
        bool Indeterminate,
        string? Current,
        int Completed,
        int Total,
        int Failed,
        long Revision,
        RunPlan? Active);

    private SnapshotState ReadSnapshotStateLocked() => new(
        _closing, _status, _message, _progress, _indeterminate, _currentFile,
        _completed, _total, _failed, _revision, _active);

    private RunSnapshot BuildSnapshot(in SnapshotState state)
    {
        // While a run goes, the plan it started with is the truth, and the other sessions are not asked.
        if (state.Status == RunStatus.Running && state.Active is not null)
        {
            return new RunSnapshot(
                state.Revision, false, RunningReason, state.Active.Mode, Explain(state.Active.Mode),
                state.Active.Output.Hint, state.Progress, state.Indeterminate, state.Current,
                state.Completed, state.Total, state.Failed, state.Status, state.Message);
        }

        var capture = _input.CaptureRun(_protection.ToObfySettings().AssemblyMerge.Enabled);
        var plan = Plan(capture);

        // Start refuses a closing shell before anything else, so the run bar says so first: one rule.
        var blocked = state.Closing ? ClosingReason : BlockedReasonFor(capture.Included.Count, capture.Collision);
        return new RunSnapshot(
            state.Revision, blocked is null, blocked, plan.Mode, Explain(plan.Mode), plan.Output.Hint,
            state.Progress, state.Indeterminate, state.Current, state.Completed, state.Total, state.Failed,
            state.Status, state.Message);
    }

    public RunSnapshot GetSnapshot()
    {
        SnapshotState state;
        lock (_gate)
            state = ReadSnapshotStateLocked();

        return BuildSnapshot(state);
    }

    private const string RunningReason = "A run is already in progress.";
    private const string NoFilesReason = "Add at least one included file.";

    /// <summary>
    /// What stops a run from starting now, in plain words, or null. The settings' reason comes from the settings; the
    /// collision's comes from <see cref="OutputCollisions"/>: a run never writes over a file the user listed.
    /// </summary>
    private string? BlockedReasonFor(int includedCount, OutputCollision? collision)
    {
        if (includedCount == 0)
            return NoFilesReason;

        if (collision is not null)
            return collision.Message;

        var errors = _protection.ValidationErrors();
        return errors.Count > 0 ? errors[0].Message : null;
    }

    /// <summary>
    /// <see cref="BlockedReasonFor(int, OutputCollision?)"/> with the settings' reason already worked out. Validating the
    /// settings probes the signing key file, and the collision comes from a check that asks the file system about every
    /// listed path, so <see cref="Start"/> does both before it takes the gate and passes the results here: nothing that
    /// touches the disk may hold the lock that IsRunning and EnsureNotRunning wait on.
    /// </summary>
    private static string? BlockedReasonFor(int includedCount, OutputCollision? collision, string? settingsError)
    {
        if (includedCount == 0)
            return NoFilesReason;

        return collision is not null ? collision.Message : settingsError;
    }

    // ---- The one mode rule -------------------------------------------------------------------------------------

    /// <summary>How a run treats the included files: the output plan the hint uses, and the mode that follows from it.</summary>
    private sealed record RunPlan(RunMode Mode, OutputPlan Output);

    private static RunPlan Plan(RunInput input)
    {
        var output = input.Plan;
        var included = input.Included;
        var mode = output.Mode switch
        {
            OutputMode.None => RunMode.None,
            OutputMode.Merge => RunMode.Merge,
            OutputMode.ClosedSet => RunMode.ClosedSet,
            _ => included.Count(static f => f.Kind == FileKind.Source) >= 2 ? RunMode.Sources : RunMode.Single
        };
        return new RunPlan(mode, output);
    }

    private static string Explain(RunMode mode) => mode switch
    {
        RunMode.Merge => "Merges the assemblies into one, then protects it.",
        RunMode.ClosedSet => "Protects the assemblies together as one set, so names they share stay consistent.",
        RunMode.Sources => "Compiles the C# files together as one unit, then protects it.",
        RunMode.Single => "Protects each file on its own.",
        _ => "Add a file to protect."
    };

    // ---- Starting and cancelling -------------------------------------------------------------------------------

    /// <summary>
    /// Starts a run on the thread pool and returns its task at once; the task never faults. The page does not wait for
    /// it: it follows <c>run.changed</c> and <c>run.completed</c>. Tests await it.
    /// </summary>
    /// <exception cref="ObfyApiException"><c>run.blocked</c>: a run is going, the shell is closing, nothing is included, an output or the symbol map would replace a listed file, or the settings are invalid; the message says which.</exception>
    public Task Start()
    {
        // A second Start while one goes is refused before anything is read: there is nothing to check a list for.
        ThrowIfCannotBegin();

        CancellationTokenSource cts;
        ObfySettings settings;
        RunInput input;
        IReadOnlyList<InputFile> included;
        RunContext context;
        TaskCompletionSource<bool> startGate;
        Task task;
        for (var attempt = 1; ; attempt++)
        {
            // Everything that touches the disk happens here, outside the gate: the settings' validation probes the signing
            // key file, and the collision check asks the file system about every listed path, which on an unreachable share
            // can take a long time. The revisions say what the reads below belong to.
            var (candidateSettings, settingsRevision) = _protection.CaptureSettings();
            var settingsError = _protection.ValidationErrors() is { Count: > 0 } errors ? errors[0].Message : null;
            var candidate = _input.CaptureRun(candidateSettings.AssemblyMerge.Enabled);

            lock (_gate)
            {
                ThrowIfCannotBegin();

                // The reads are only worth anything if nothing changed since: the run protects exactly the settings and
                // files that are there when it becomes Running, and the collision verdict is about exactly those files.
                // Any change since means the reads are of an older list (a stale "nothing collides" would let a run replace
                // a file listed meanwhile, a stale "collides" would refuse a list that is fine now), so read again. Only
                // revisions are asked here, read without the sessions' locks: no disk, and no wait on a lock that is held
                // across a disk question. The user rarely edits in this window; if they keep doing so, give up.
                if (_protection.Revision != settingsRevision || _input.Revision != candidate.Revision)
                {
                    if (attempt >= MaxStartAttempts)
                        throw new ObfyApiException("run.blocked", ChangedWhileStartingReason);
                    continue;
                }

                settings = candidateSettings;
                input = candidate;
                included = input.Included;
                var blocked = BlockedReasonFor(included.Count, input.Collision, settingsError);
                if (blocked is not null)
                    throw new ObfyApiException("run.blocked", blocked);

                var plan = Plan(input);
                context = new RunContext(included, input.Skipped, settings, plan, input.OutputFolderChosen, input.SymbolMapPath);

                cts = new CancellationTokenSource();
                startGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                _cts = cts;
                _active = plan;
                _status = RunStatus.Running;
                _message = "Starting...";
                _progress = 0;
                _indeterminate = plan.Mode is RunMode.Merge or RunMode.ClosedSet || included.Count <= 1;
                _currentFile = null;
                _completed = 0;
                _total = included.Count;
                _failed = 0;
                _revision++;
                task = Task.Run(async () =>
                {
                    if (!await startGate.Task.ConfigureAwait(false))
                        return;
                    await RunAsync(context, cts.Token).ConfigureAwait(false);
                });
                _runTask = task;
                break;
            }
        }

        // From here the coordinator is Running with a live _runTask awaiting startGate. Whatever fails before
        // startGate opens (an event publisher, the log, the results store) must not leave it that way: AbandonStart
        // resets the state and completes startGate(false) so neither CanStart nor Completion hangs.
        try
        {
            _results.BeginRun();
            _input.ResetStatus();

            _log.StartRunSeparator();
            _log.Add(LogLevel.Info, "Starting obfuscation...");
            _log.Add(LogLevel.Info, $"Level: {settings.Level}");
            _log.Add(LogLevel.Info, $"Files: {included.Count}");
            foreach (var file in input.Skipped)
                LogSkip(file);

            PublishChanged();

            startGate.TrySetResult(true);
            return task;
        }
        catch (Exception ex)
        {
            AbandonStart(cts, startGate, ex);
            throw;
        }
    }

    /// <summary>
    /// How often <see cref="Start"/> reads the files and settings again when they change while it is checking them. A
    /// change in that window is rare (it takes an edit that passed its own run check just before, or one made while the
    /// check waits on a slow disk), so a few tries are plenty; after that the run is refused rather than started on a list
    /// that was not checked.
    /// </summary>
    private const int MaxStartAttempts = 3;

    private const string ChangedWhileStartingReason = "The files or settings changed while the run was starting. Try again.";
    private const string ClosingReason = "Obfy is closing.";

    /// <summary>
    /// Refuses a start while a run goes, the coordinator is disposed or the shell is closing. Takes the gate itself (it is
    /// re-entrant), so <see cref="Start"/> may call it inside its own hold.
    /// </summary>
    /// <exception cref="ObfyApiException"><c>run.blocked</c>.</exception>
    private void ThrowIfCannotBegin()
    {
        lock (_gate)
        {
            if (_status == RunStatus.Running || _disposed)
                throw new ObfyApiException("run.blocked", RunningReason);
            if (_closing)
                throw new ObfyApiException("run.blocked", ClosingReason);
        }
    }

    /// <summary>Puts a run that never began back to a state the page can recover from: Faulted, token source disposed.</summary>
    private void AbandonStart(CancellationTokenSource cts, TaskCompletionSource<bool> startGate, Exception ex)
    {
        lock (_gate)
        {
            _cts = null;
            _active = null;
            _status = RunStatus.Faulted;
            _message = ex.Message;
            _currentFile = null;
            _indeterminate = false;
            _revision++;
        }

        startGate.TrySetResult(false);
        cts.Dispose();
        _logger?.LogError("A run could not start ({ExceptionType})", ex.GetType().Name);

        // Best effort, each step on its own: whatever failed may fail again, and the state is already recovered.
        Attempt("results", _results.Clear);
        Attempt("log", () => _log.Add(LogLevel.Error, $"Obfuscation could not start: {ex.Message}"));
        Attempt("publish", PublishChanged);
    }

    /// <summary>Asks the running run to stop. Nothing happens when none is running.</summary>
    public void Cancel()
    {
        CancellationTokenSource? cts;
        lock (_gate)
        {
            if (_status != RunStatus.Running || _cts is null)
                return;

            cts = _cts;
            _message = "Cancelling...";
            _revision++;
        }

        try
        {
            cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The run ended between the check and the cancel; there is nothing left to stop.
        }

        PublishChanged();
    }

    private void OnDependencyChanged()
    {
        lock (_gate)
        {
            // A run's own writes to the file list come through here; the run reports its progress itself.
            if (_status == RunStatus.Running || _disposed)
                return;
            _revision++;
            if (_whileIdleDepth > 0)
            {
                _deferredDependencyChange = true;
                return;
            }
        }

        PublishChanged();
    }

    // ---- The run -----------------------------------------------------------------------------------------------

    private sealed record RunContext(
        IReadOnlyList<InputFile> Included,
        IReadOnlyList<FileRow> Skipped,
        ObfySettings Settings,
        RunPlan Plan,
        bool OutputFolderChosen,
        string? SymbolMapPath);

    /// <summary>What a run collects as its files finish.</summary>
    private sealed class RunOutcome
    {
        public Dictionary<string, string> Symbols { get; } = [];
        public ObfuscationStatistics Statistics { get; } = new();
        public List<ObfuscationResult> Successful { get; } = [];
    }

    private async Task RunAsync(RunContext run, CancellationToken cancellationToken)
    {
        var outcome = new RunOutcome();
        var stopwatch = Stopwatch.StartNew();
        RunStatus status;
        string message;

        try
        {
            switch (run.Plan.Mode)
            {
                case RunMode.Merge:
                    await MergeAsync(run, outcome, cancellationToken).ConfigureAwait(false);
                    break;
                case RunMode.ClosedSet:
                    await ClosedSetAsync(run, outcome, cancellationToken).ConfigureAwait(false);
                    break;
                default:
                    await EachAsync(run.Included, run, outcome, cancellationToken).ConfigureAwait(false);
                    break;
            }

            stopwatch.Stop();

            await _results.LoadResultsAsync(outcome.Symbols, outcome.Statistics, stopwatch.Elapsed).ConfigureAwait(false);
            if (outcome.Successful.Count > 0)
            {
                var combined = CombineResults(outcome, stopwatch.Elapsed);
                _results.SetReport(_reports.BuildReport(combined, run.Settings));
                _results.SetPreviewPaths(outcome.Successful
                    .Select(static r => r.OutputPath)
                    .Where(static p => p is not null && File.Exists(p) && IsAssemblyFile(p))
                    .Cast<string>());
            }

            // The map is the only way to read a stack trace from the protected build, so a run that was asked for one and did
            // not write it says so. The outputs are written by now: it is a warning, never a failed run.
            string? mapWarning = null;
            if (run.SymbolMapPath is not null)
            {
                if (outcome.Symbols.Count > 0)
                    mapWarning = await WriteSymbolMapAsync(outcome.Symbols, run.SymbolMapPath).ConfigureAwait(false);
                else if (outcome.Successful.Count > 0)
                    mapWarning = NoSymbolsWarning;
            }

            int failed;
            lock (_gate)
                failed = _failed;
            status = RunStatus.Done;
            if (failed > 0)
            {
                message = "Completed with errors";
                _log.Add(LogLevel.Error, $"Obfuscation finished with errors: {failed} file(s) failed.");
            }
            else
            {
                message = mapWarning is null ? "Obfuscation complete" : "Obfuscation complete, but the symbol map was not written";
                _log.Add(
                    LogLevel.Success,
                    $"Obfuscation completed: {outcome.Statistics.TotalTransformations} total transformations in {stopwatch.Elapsed:mm\\:ss\\.fff}");
            }

            if (mapWarning is not null)
                Attempt("symbol map warning", () => WarnAboutSymbolMap(mapWarning));
        }
        catch (OperationCanceledException)
        {
            status = RunStatus.Cancelled;
            message = "Cancelled";
            CleanUpAfterStop(() => _log.Add(LogLevel.Warning, "Obfuscation cancelled by user"), error: null);
        }
        catch (Exception ex)
        {
            // Whatever the engine, the report service or a file system call throws, the run ends with a status.
            status = RunStatus.Faulted;
            message = ex.Message;
            _logger?.LogError("A run failed unexpectedly ({ExceptionType})", ex.GetType().Name);
            CleanUpAfterStop(() => _log.Add(LogLevel.Error, $"Obfuscation failed: {ex.Message}"), error: ex.Message);
        }

        Finish(status, message);
    }

    /// <summary>
    /// The log line, the file rows and the results after a run that did not finish. Best effort and never throwing: what
    /// it calls may fail, and <see cref="Finish"/> must still run, or the coordinator would stay Running for good.
    /// </summary>
    private void CleanUpAfterStop(Action log, string? error)
    {
        // Each step on its own: one failing must not skip the others.
        Attempt("log", log);
        Attempt("file rows", () => ResetProcessingFiles(error));
        Attempt("results", _results.Clear);
    }

    /// <summary>Runs one best-effort step. A failure is logged by the step's name and the exception's type, never its message (it can hold a path).</summary>
    private void Attempt(string step, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning("A step after a run stopped failed ({Step}, {ExceptionType})", step, ex.GetType().Name);
        }
    }

    private void Finish(RunStatus status, string message)
    {
        CancellationTokenSource? cts;
        SnapshotState terminalState;
        lock (_gate)
        {
            cts = _cts;
            _cts = null;
            _status = status;
            _message = message;
            _currentFile = null;
            if (status == RunStatus.Done)
                _progress = 100;
            _indeterminate = false;
            _revision++;
            terminalState = ReadSnapshotStateLocked();
        }

        cts?.Dispose();

        // Build the snapshot outside _gate and _publishGate: CaptureRun probes the file system and must not hold
        // either lock. Using terminalState captured under _gate ensures RunCompleted never reflects a later Start().
        var snapshot = BuildSnapshot(terminalState);

        lock (_publishGate)
        {
            if (snapshot.Revision >= _lastPublishedRevision)
            {
                _lastPublishedRevision = snapshot.Revision;
                _events?.Publish(AppEvents.RunChanged, snapshot);
            }

            _events?.Publish(AppEvents.RunCompleted, snapshot);
        }

        Changed?.Invoke();
    }

    private void Report(string message, string? currentFile = null, int? completed = null)
    {
        lock (_gate)
        {
            _message = message;
            _currentFile = currentFile;
            if (completed is { } done)
            {
                _completed = done;
                _progress = _total == 0 ? 0 : done * 100.0 / _total;
            }

            _revision++;
        }

        PublishChanged();
    }

    /// <summary>
    /// Records what the run did to a row. The user may remove a row while the run goes; a row that is gone has nothing to
    /// record, and that is not a reason to stop the run.
    /// </summary>
    private void SetFile(string id, FileStatus status, string? outputPath = null, string? error = null, string? skipReason = null)
    {
        try
        {
            _input.SetFileResult(id, status, outputPath, error, skipReason);
        }
        catch (ObfyApiException ex) when (ex.Code == "files.notFound")
        {
            // Removed from the list during the run.
        }
    }

    private void CountFailed(int count)
    {
        lock (_gate)
            _failed += count;
    }

    private void PublishChanged()
    {
        // Build the snapshot outside _publishGate so slow disk I/O in CaptureRun never stalls pushes on other
        // threads, while _lastPublishedRevision inside _publishGate guarantees pushes leave in revision order.
        var snapshot = GetSnapshot();
        lock (_publishGate)
        {
            if (snapshot.Revision < _lastPublishedRevision)
                return;
            _lastPublishedRevision = snapshot.Revision;
            _events?.Publish(AppEvents.RunChanged, snapshot);
        }

        Changed?.Invoke();
    }

    // ---- Each file, or the C# files as one compilation ---------------------------------------------------------

    private async Task EachAsync(
        IReadOnlyList<InputFile> files, RunContext run, RunOutcome outcome, CancellationToken cancellationToken)
    {
        var outputDir = run.Plan.Output.Directory;
        var sourceFiles = files.Where(static f => f.Kind == FileKind.Source).ToList();
        var separate = sourceFiles.Count >= 2 ? files.Where(static f => f.Kind != FileKind.Source).ToList() : files.ToList();
        var completed = 0;

        if (sourceFiles.Count >= 2)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var file in sourceFiles)
                SetFile(file.Id, FileStatus.Processing);

            Report($"Processing {sourceFiles.Count} source files...", currentFile: null, completed);
            _log.Add(LogLevel.Info, $"Processing {sourceFiles.Count} source files as one compilation...");

            var combined = await _obfuscation.ObfuscateSourcesAsync(
                sourceFiles.Select(static f => f.Path).ToList(), outputDir, run.Settings, cancellationToken).ConfigureAwait(false);

            ApplyCombinedSourceResult(sourceFiles, combined, outcome);
            completed = sourceFiles.Count;
            Report($"Processed {sourceFiles.Count} source files", currentFile: null, completed);
        }

        foreach (var file in separate)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var name = Path.GetFileName(file.Path);
            SetFile(file.Id, FileStatus.Processing);
            Report($"Processing {name}...", name, completed);
            _log.Add(LogLevel.Info, $"Processing {name}...");

            var outputPath = outputDir is not null ? run.Plan.Output.OutputPathFor(file.Path) : null;
            var result = await _obfuscation.ObfuscateAsync(
                file.Path, outputPath, run.Settings, ResultsAndReportNeedTheFullRun, cancellationToken).ConfigureAwait(false);

            ApplyResult(file, name, result, outcome);
            completed++;
            Report($"Processed {name}", currentFile: null, completed);
        }
    }

    private void ApplyCombinedSourceResult(List<InputFile> files, ObfuscationResult result, RunOutcome outcome)
    {
        if (!result.Success)
        {
            foreach (var file in files)
                SetFile(file.Id, FileStatus.Error, error: result.ErrorMessage);

            CountFailed(files.Count);
            _log.Add(LogLevel.Error, $"Failed source files: {result.ErrorMessage}");
            return;
        }

        Collect(result, outcome);
        foreach (var file in files)
            SetFile(file.Id, FileStatus.Success, outputPath: result.OutputPath);

        _log.Add(LogLevel.Success, $"Completed {files.Count} source files: {result.Statistics?.TotalTransformations ?? 0} transformations");
        foreach (var warning in result.Warnings)
            _log.Add(LogLevel.Warning, warning);
    }

    private void ApplyResult(InputFile file, string name, ObfuscationResult result, RunOutcome outcome)
    {
        if (!result.Success)
        {
            SetFile(file.Id, FileStatus.Error, error: result.ErrorMessage);
            CountFailed(1);
            _log.Add(LogLevel.Error, $"Failed {name}: {result.ErrorMessage}");
            return;
        }

        Collect(result, outcome);
        SetFile(file.Id, FileStatus.Success, outputPath: result.OutputPath);

        _log.Add(LogLevel.Success, $"Completed {name}: {result.Statistics?.TotalTransformations ?? 0} transformations");
        if (result.SkippedItems.Count > 0)
            _log.Add(LogLevel.Warning, $"{result.SkippedItems.Count} item(s) in {name} were skipped and left unobfuscated.");
        foreach (var warning in result.Warnings)
            _log.Add(LogLevel.Warning, warning);
    }

    private static void Collect(ObfuscationResult result, RunOutcome outcome)
    {
        outcome.Successful.Add(result);
        if (result.Statistics != null)
            outcome.Statistics.Merge(result.Statistics);
        foreach (var (key, value) in result.SymbolMap)
            outcome.Symbols[key] = value;
    }

    // ---- Merge -------------------------------------------------------------------------------------------------

    private async Task MergeAsync(RunContext run, RunOutcome outcome, CancellationToken cancellationToken)
    {
        var files = run.Included;
        foreach (var file in files)
            SetFile(file.Id, FileStatus.Processing);

        Report("Merging and obfuscating assemblies...");
        _log.Add(LogLevel.Info, $"Merging {files.Count} assemblies...");

        var outputPath = run.Plan.Output.MergePath
            ?? throw new InvalidOperationException("A merge run has no merge output path.");
        var result = await _obfuscation.MergeAndObfuscateAsync(
            files.Select(static f => f.Path).ToList(), outputPath, run.Settings, ResultsAndReportNeedTheFullRun, cancellationToken)
            .ConfigureAwait(false);

        Report("Merged", currentFile: null, completed: files.Count);

        if (result.Success)
        {
            Collect(result, outcome);
            foreach (var file in files)
                SetFile(file.Id, FileStatus.Success, outputPath: result.OutputPath);

            _log.Add(LogLevel.Success, $"Merged and obfuscated {files.Count} assemblies: {result.Statistics?.TotalTransformations ?? 0} transformations");
            if (result.SkippedItems.Count > 0)
                _log.Add(LogLevel.Warning, $"{result.SkippedItems.Count} item(s) were skipped and left unobfuscated.");
            foreach (var warning in result.Warnings)
                _log.Add(LogLevel.Warning, warning);
        }
        else
        {
            foreach (var file in files)
                SetFile(file.Id, FileStatus.Error, error: result.ErrorMessage);

            CountFailed(files.Count);
            _log.Add(LogLevel.Error, $"Merge failed: {result.ErrorMessage}");
        }
    }

    // ---- Closed set --------------------------------------------------------------------------------------------

    private async Task ClosedSetAsync(RunContext run, RunOutcome outcome, CancellationToken cancellationToken)
    {
        var included = run.Included;
        var assemblies = included.Where(static a => a.IsAssembly).ToList();
        if (assemblies.Count == 0)
        {
            await EachAsync(included, run, outcome, cancellationToken).ConfigureAwait(false);
            return;
        }

        foreach (var leftover in included.Where(static f => !f.IsAssembly))
        {
            SetFile(leftover.Id, FileStatus.Skipped, skipReason: SourceNotInClosedSet);
            _log.Add(LogLevel.Warning, $"Skipping {Path.GetFileName(leftover.Path)}: {SourceNotInClosedSet}");
        }

        foreach (var file in assemblies)
            SetFile(file.Id, FileStatus.Processing);

        Report("Protecting closed set...");
        _log.Add(LogLevel.Info, $"Protecting {assemblies.Count} assemblies as a closed set...");

        var outputDir = run.Plan.Output.Directory
            ?? throw new InvalidOperationException("A closed-set run has no output folder.");
        if (!run.OutputFolderChosen)
            _log.Add(LogLevel.Info, $"Output directory: {outputDir}");

        var inputs = assemblies.Select(static a => new ClosedSetInput
        {
            AssemblyPath = a.Path,
            Hints = a.Hints ?? new ProjectSettingsHints()
        }).ToList();

        var result = await _obfuscation.ObfuscateClosedSetAsync(
            inputs, outputDir, run.Settings, run.Settings.SymbolRenaming.PreservePublicApi, cancellationToken).ConfigureAwait(false);

        Report("Closed set protected", currentFile: null, completed: assemblies.Count);
        ApplyClosedSetResult(assemblies, result, outcome);
    }

    private void ApplyClosedSetResult(List<InputFile> assemblies, ClosedSetResult result, RunOutcome outcome)
    {
        if (!result.Success)
        {
            var message = result.ErrorMessage ?? "Closed-set obfuscation failed.";
            foreach (var file in assemblies)
                SetFile(file.Id, FileStatus.Error, error: message);

            CountFailed(assemblies.Count);
            _log.Add(LogLevel.Error, message);
            foreach (var failure in result.LoadFailures)
                _log.Add(LogLevel.Warning, $"Failed to load {failure.Path}: {failure.Message}");
            foreach (var module in result.ModuleResults.Where(static m => !m.Success))
                _log.Add(LogLevel.Error, $"Failed {Path.GetFileName(module.InputPath)}: {module.ErrorMessage}");
            return;
        }

        var byInput = result.ModuleResults
            .Where(static m => !string.IsNullOrEmpty(m.InputPath))
            .GroupBy(static m => m.InputPath!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static g => g.Key, static g => g.Last(), StringComparer.OrdinalIgnoreCase);

        var uniqueNames = assemblies
            .GroupBy(static a => Path.GetFileName(a.Path), StringComparer.OrdinalIgnoreCase)
            .Where(static g => g.Count() == 1)
            .Select(static g => g.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var file in assemblies)
        {
            var name = Path.GetFileName(file.Path);
            var allowNameFallback = uniqueNames.Contains(name);
            var module = FindModuleResult(file, name, allowNameFallback, byInput, result.ModuleResults);
            if (module != null)
            {
                if (module.Success)
                {
                    SetFile(file.Id, FileStatus.Success, outputPath: module.OutputPath);
                }
                else
                {
                    SetFile(file.Id, FileStatus.Error, error: module.ErrorMessage);
                    CountFailed(1);
                }

                continue;
            }

            if (FindLoadFailure(file, name, allowNameFallback, result.LoadFailures) is { } loadFailure)
            {
                SetFile(file.Id, FileStatus.Error, error: $"Failed to load {name}: {loadFailure.Message}");
                CountFailed(1);
                continue;
            }

            // The set gave this assembly neither a result nor a failure: it was not protected, and that is a failure.
            SetFile(file.Id, FileStatus.Error, error: NotProcessed);
            CountFailed(1);
            _log.Add(LogLevel.Error, $"{name} was not protected: {NotProcessed}");
        }

        foreach (var module in result.ModuleResults.Where(static m => m.Success))
        {
            outcome.Successful.Add(module);
            if (module.Statistics != null)
                outcome.Statistics.Merge(module.Statistics);

            if (module.SkippedItems.Count > 0)
                _log.Add(LogLevel.Warning, $"{module.SkippedItems.Count} item(s) in {Path.GetFileName(module.InputPath)} were skipped and left unobfuscated.");
            foreach (var warning in module.Warnings)
                _log.Add(LogLevel.Warning, warning);
        }

        foreach (var (key, value) in result.SymbolMap)
            outcome.Symbols[key] = value;
        foreach (var module in result.ModuleResults)
        {
            foreach (var (key, value) in module.SymbolMap)
                outcome.Symbols.TryAdd(key, value);
        }

        _log.Add(LogLevel.Success, $"Closed-set obfuscation completed: {outcome.Statistics.TotalTransformations} transformations");
        foreach (var failure in result.LoadFailures)
            _log.Add(LogLevel.Warning, $"Failed to load {failure.Path}: {failure.Message}");
    }

    private static ObfuscationResult? FindModuleResult(
        InputFile file, string name, bool allowNameFallback, Dictionary<string, ObfuscationResult> byInput, IReadOnlyList<ObfuscationResult> modules)
    {
        if (byInput.TryGetValue(file.Path, out var module))
            return module;

        if (!allowNameFallback)
            return null;

        return modules.FirstOrDefault(m =>
            (!string.IsNullOrEmpty(m.InputPath)
                && string.Equals(Path.GetFileName(m.InputPath), name, StringComparison.OrdinalIgnoreCase))
            || (!string.IsNullOrEmpty(m.OutputPath)
                && string.Equals(Path.GetFileName(m.OutputPath), name, StringComparison.OrdinalIgnoreCase)));
    }

    private static ClosedSetLoadFailure? FindLoadFailure(
        InputFile file, string name, bool allowNameFallback, IReadOnlyList<ClosedSetLoadFailure> loadFailures)
    {
        foreach (var failure in loadFailures)
        {
            if (string.Equals(failure.Path, file.Path, StringComparison.OrdinalIgnoreCase))
                return failure;
        }

        if (!allowNameFallback)
            return null;

        foreach (var failure in loadFailures)
        {
            if (string.Equals(Path.GetFileName(failure.Path), name, StringComparison.OrdinalIgnoreCase))
                return failure;
        }

        return null;
    }

    // ---- Helpers -----------------------------------------------------------------------------------------------

    private static bool IsAssemblyFile(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".dll", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".exe", StringComparison.OrdinalIgnoreCase);
    }

    private void LogSkip(FileRow row)
    {
        var name = Path.GetFileNameWithoutExtension(row.Name);
        _log.Add(
            LogLevel.Info,
            string.IsNullOrWhiteSpace(row.SkipReason) ? $"Skipping {name}" : $"Skipping {name}: {row.SkipReason}");
    }

    /// <summary>A cancelled run puts the rows it was working on back to Pending; a failed one marks them with the error.</summary>
    private void ResetProcessingFiles(string? error)
    {
        foreach (var row in _input.GetSnapshot().Files.Where(static f => f.Status == FileStatus.Processing))
        {
            if (error is null)
                SetFile(row.Id, FileStatus.Pending);
            else
                SetFile(row.Id, FileStatus.Error, error: error);
        }
    }

    private static ObfuscationResult CombineResults(RunOutcome outcome, TimeSpan elapsed)
    {
        var successful = outcome.Successful;
        return ObfuscationResult.Successful(
            outcome.Statistics,
            inputPath: string.Join(", ", successful.Select(static r => r.InputPath).Where(static p => !string.IsNullOrEmpty(p))),
            outputPath: successful.Last().OutputPath,
            elapsedTime: elapsed,
            skippedItems: successful.SelectMany(static r => r.SkippedItems).ToList(),
            symbolMap: outcome.Symbols,
            warnings: successful.SelectMany(static r => r.Warnings).ToList(),
            packedLauncherPath: successful.LastOrDefault(static r => r.PackedLauncherPath is not null)?.PackedLauncherPath);
    }

    private const string NoSymbolsWarning =
        "No symbol map was written: this run renamed no symbols (renaming is off, or the output was reused from the incremental cache).";

    /// <summary>Writes the symbol map; returns what to tell the user when it could not be written, or null.</summary>
    private async Task<string?> WriteSymbolMapAsync(Dictionary<string, string> symbols, string mapPath)
    {
        try
        {
            await _obfuscation.WriteSymbolMapAsync(symbols, mapPath).ConfigureAwait(false);
            _log.Add(LogLevel.Info, $"Symbol map written to {mapPath}");
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Not only IO errors: a path the OS rejects or a serialization error must not mark a run whose outputs are
            // already written as failed. The message goes to the page, which is on this machine; the file log gets the type.
            _logger?.LogWarning("The symbol map could not be written ({ExceptionType})", ex.GetType().Name);
            return $"The symbol map could not be written ({ex.Message}). Without it, stack traces from the protected build cannot be translated back to your names.";
        }
    }

    private void WarnAboutSymbolMap(string warning)
    {
        _log.Add(LogLevel.Warning, warning);
        _input.Notify(NoticeLevel.Warning, "Symbol map not written", warning);
    }

    public void Dispose()
    {
        CancellationTokenSource? cts;
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            cts = _cts;
        }

        _protection.Changed -= _onDependencyChanged;
        _input.Changed -= _onDependencyChanged;

        try
        {
            cts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The run already ended and disposed it.
        }
    }
}
