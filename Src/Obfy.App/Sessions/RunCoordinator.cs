using System.Diagnostics;
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
/// command's <c>CanExecute</c>: a run is blocked while one is running, with nothing included, or while the protection
/// settings are invalid, and each says why in plain words (UI-052). The settings' reason is the first of
/// <see cref="ProtectionSession.ValidationErrors"/>; no rule is repeated here.
/// </para>
/// <para>
/// <b>Threads.</b> <see cref="Start"/> returns at once; the run loop is on the thread pool and publishes
/// <c>run.changed</c> from there, which is safe because a run starts only after the shell exists. Every change to this
/// class's state runs under a lock and bumps the revision; events go out after it, outside the lock, and the lock is never
/// held while another session is called. The loop never throws: cancel, a failure and an unexpected error each end the run
/// with a status.
/// </para>
/// </summary>
public sealed class RunCoordinator : IDisposable
{
    private const string SourceNotInClosedSet = "Source files are not part of a closed-set session";

    private readonly object _gate = new();
    private readonly IObfuscationService _obfuscation;
    private readonly IReportService _reports;
    private readonly ProtectionSession _protection;
    private readonly InputSession _input;
    private readonly LogBuffer _log;
    private readonly ResultsStore _results;
    private readonly IEventPublisher? _events;
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
    private bool _disposed;

    /// <param name="events">Where <c>run.changed</c> and <c>run.completed</c> go; null for a coordinator nobody listens to.</param>
    public RunCoordinator(
        IObfuscationService obfuscation,
        IReportService reports,
        ProtectionSession protection,
        InputSession input,
        LogBuffer log,
        ResultsStore results,
        IEventPublisher? events = null)
    {
        _obfuscation = obfuscation;
        _reports = reports;
        _protection = protection;
        _input = input;
        _log = log;
        _results = results;
        _events = events;

        // Whether a run can start, and how, depends on the settings and the files: follow both.
        _onDependencyChanged = OnDependencyChanged;
        _protection.Changed += _onDependencyChanged;
        _input.Changed += _onDependencyChanged;
    }

    /// <summary>Raised once after any change, on the thread that made it, outside the coordinator's lock.</summary>
    public event Action? Changed;

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

    public RunSnapshot GetSnapshot()
    {
        RunStatus status;
        string message;
        double progress;
        bool indeterminate;
        string? current;
        int completed, total, failed;
        long revision;
        RunPlan? active;
        lock (_gate)
        {
            status = _status;
            message = _message;
            progress = _progress;
            indeterminate = _indeterminate;
            current = _currentFile;
            completed = _completed;
            total = _total;
            failed = _failed;
            revision = _revision;
            active = _active;
        }

        // While a run goes, the plan it started with is the truth, and the other sessions are not asked.
        if (status == RunStatus.Running && active is not null)
        {
            return new RunSnapshot(
                revision, false, RunningReason, active.Mode, Explain(active.Mode), active.Output.Hint,
                progress, indeterminate, current, completed, total, failed, status, message);
        }

        var included = _input.IncludedInputs();
        var merge = _protection.ToObfySettings().AssemblyMerge.Enabled;
        var plan = Plan(included, merge);
        var blocked = BlockedReasonFor(included.Count);
        return new RunSnapshot(
            revision, blocked is null, blocked, plan.Mode, Explain(plan.Mode), plan.Output.Hint,
            progress, indeterminate, current, completed, total, failed, status, message);
    }

    private const string RunningReason = "A run is already in progress.";

    /// <summary>What stops a run from starting now, in plain words, or null. The settings' reason comes from the settings.</summary>
    private string? BlockedReasonFor(int includedCount)
    {
        if (includedCount == 0)
            return "Add at least one included file.";

        var errors = _protection.ValidationErrors();
        return errors.Count > 0 ? errors[0].Message : null;
    }

    // ---- The one mode rule -------------------------------------------------------------------------------------

    /// <summary>How a run treats the included files: the output plan the hint uses, and the mode that follows from it.</summary>
    private sealed record RunPlan(RunMode Mode, OutputPlan Output);

    private RunPlan Plan(IReadOnlyList<InputFile> included, bool merge)
    {
        var output = _input.PlanOutput(merge);
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
    /// <exception cref="ObfyApiException"><c>run.blocked</c>: a run is going, nothing is included, or the settings are invalid; the message says which.</exception>
    public Task Start()
    {
        var included = _input.IncludedInputs();
        var settings = _protection.ToObfySettings();
        var blocked = BlockedReasonFor(included.Count);
        if (blocked is not null)
            throw new ObfyApiException("run.blocked", blocked);

        var snapshot = _input.GetSnapshot();
        var plan = Plan(included, settings.AssemblyMerge.Enabled);
        var skipped = snapshot.Files.Where(static f => f.Status == FileStatus.Skipped).ToList();
        var context = new RunContext(
            included, skipped, settings, plan, snapshot.OutputFolder.Length > 0,
            snapshot.WriteSymbolMap ? _input.ResolveSymbolMapPath() : null);

        var cts = new CancellationTokenSource();
        lock (_gate)
        {
            if (_status == RunStatus.Running || _disposed)
            {
                cts.Dispose();
                throw new ObfyApiException("run.blocked", RunningReason);
            }

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
        }

        _results.BeginRun();
        _input.ResetStatus();

        _log.StartRunSeparator();
        _log.Add(LogLevel.Info, "Starting obfuscation...");
        _log.Add(LogLevel.Info, $"Level: {settings.Level}");
        _log.Add(LogLevel.Info, $"Files: {included.Count}");
        foreach (var file in skipped)
            LogSkip(file);

        PublishChanged();

        var task = Task.Run(() => RunAsync(context, cts.Token));
        lock (_gate)
            _runTask = task;
        return task;
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
                    .Where(static p => p is not null && File.Exists(p) && Path.GetExtension(p) is ".dll" or ".exe")
                    .Cast<string>());
            }

            if (run.SymbolMapPath is not null && outcome.Symbols.Count > 0)
                await WriteSymbolMapAsync(outcome.Symbols, run.SymbolMapPath).ConfigureAwait(false);

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
                message = "Obfuscation complete";
                _log.Add(
                    LogLevel.Success,
                    $"Obfuscation completed: {outcome.Statistics.TotalTransformations} total transformations in {stopwatch.Elapsed:mm\\:ss\\.fff}");
            }
        }
        catch (OperationCanceledException)
        {
            _log.Add(LogLevel.Warning, "Obfuscation cancelled by user");
            status = RunStatus.Cancelled;
            message = "Cancelled";
            ResetProcessingFiles(error: null);
            _results.Clear();
        }
        catch (Exception ex)
        {
            // Whatever the engine, the report service or a file system call throws, the run ends with a status.
            _log.Add(LogLevel.Error, $"Obfuscation failed: {ex.Message}");
            status = RunStatus.Faulted;
            message = ex.Message;
            ResetProcessingFiles(error: ex.Message);
            _results.Clear();
        }

        Finish(status, message);
    }

    private void Finish(RunStatus status, string message)
    {
        CancellationTokenSource? cts;
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
        }

        cts?.Dispose();

        var snapshot = GetSnapshot();
        _events?.Publish(AppEvents.RunChanged, snapshot);
        _events?.Publish(AppEvents.RunCompleted, snapshot);
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
        _events?.Publish(AppEvents.RunChanged, GetSnapshot());
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

            var outputPath = outputDir is not null ? Path.Combine(outputDir, name) : null;
            var result = await _obfuscation.ObfuscateAsync(file.Path, outputPath, run.Settings, cancellationToken).ConfigureAwait(false);

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
            files.Select(static f => f.Path).ToList(), outputPath, run.Settings, cancellationToken).ConfigureAwait(false);

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

        foreach (var file in assemblies)
        {
            var name = Path.GetFileName(file.Path);
            var module = FindModuleResult(file, name, byInput, result.ModuleResults);
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

            if (FindLoadFailure(file, name, result.LoadFailures) is { } loadFailure)
            {
                SetFile(file.Id, FileStatus.Error, error: $"Failed to load {name}: {loadFailure.Message}");
                CountFailed(1);
                continue;
            }

            // The set gave this assembly neither a result nor a failure: it was not processed.
            SetFile(file.Id, FileStatus.Pending);
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
        InputFile file, string name, Dictionary<string, ObfuscationResult> byInput, IReadOnlyList<ObfuscationResult> modules)
    {
        if (byInput.TryGetValue(file.Path, out var module))
            return module;

        return modules.FirstOrDefault(m =>
            (!string.IsNullOrEmpty(m.InputPath)
                && string.Equals(Path.GetFileName(m.InputPath), name, StringComparison.OrdinalIgnoreCase))
            || (!string.IsNullOrEmpty(m.OutputPath)
                && string.Equals(Path.GetFileName(m.OutputPath), name, StringComparison.OrdinalIgnoreCase)));
    }

    private static ClosedSetLoadFailure? FindLoadFailure(
        InputFile file, string name, IReadOnlyList<ClosedSetLoadFailure> loadFailures)
    {
        foreach (var failure in loadFailures)
        {
            if (string.Equals(failure.Path, file.Path, StringComparison.OrdinalIgnoreCase))
                return failure;
            if (string.Equals(Path.GetFileName(failure.Path), name, StringComparison.OrdinalIgnoreCase))
                return failure;
        }

        return null;
    }

    // ---- Helpers -----------------------------------------------------------------------------------------------

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

    private async Task WriteSymbolMapAsync(Dictionary<string, string> symbols, string mapPath)
    {
        try
        {
            await _obfuscation.WriteSymbolMapAsync(symbols, mapPath).ConfigureAwait(false);
            _log.Add(LogLevel.Info, $"Symbol map written to {mapPath}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Add(LogLevel.Warning, $"Failed to write symbol map: {ex.Message}");
        }
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
