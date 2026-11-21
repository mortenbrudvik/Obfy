using System.Security.Cryptography;
using System.Text;
using System.Xml;
using Microsoft.Extensions.Logging;
using Obfy.App.Models;
using Obfy.App.Services;
using Obfy.Core.Models.Solution;
using Obfy.Core.Services.Solution;

namespace Obfy.App.Sessions;

/// <summary>
/// The list of files to protect, the output folder and the symbol-map option: lifted from the WPF
/// <c>FilesViewModel</c> without WPF or property-change plumbing. A singleton; the page reads an
/// <see cref="InputSnapshot"/> and calls methods by file id, and no rule lives in the page.
/// <para>
/// Every mutation runs under a lock, raises <see cref="Changed"/> and publishes <c>files.changed</c> after it, outside the
/// lock, once. The exceptions are <see cref="ApplyStartup"/>, <see cref="ApplyPreferences"/> and <see cref="InitializeAsync"/>:
/// they run before the shell may be ready, and the event publisher builds the web view on whichever thread first publishes,
/// so they change state and bump the revision but publish nothing. The page reads <c>files.get</c> when it loads.
/// </para>
/// <para>
/// Preferences are the last output folder and the symbol-map switch, nothing else. A <c>-o</c> folder from the command
/// line is for this run only and is never saved; typing a folder clears that flag.
/// </para>
/// </summary>
public sealed class InputSession
{
    private const string DefaultSymbolMapName = "symbolmap.json";

    private readonly object _gate = new();
    private readonly object _preferenceGate = new();
    private readonly List<Entry> _entries = [];
    private readonly ISettingsService _settings;
    private readonly ISolutionAnalyzer? _analyzer;
    private readonly IShellLauncher? _shell;
    private readonly IEventPublisher? _events;
    private readonly ILogger<InputSession>? _logger;
    private readonly TimeSpan _preferenceDelay;

    private string _outputFolder = string.Empty;
    private string? _outputWarning;
    private bool _writeSymbolMap;
    private string _symbolMapPath = string.Empty;
    private bool _sessionOutputOverride;
    private List<InputNotice> _notices = [];
    private long _revision;
    private int _preferenceVersion;
    private Task _preferenceSave = Task.CompletedTask;
    private CancellationTokenSource? _preferenceDebounce;

    /// <param name="settings">Where the preferences are kept.</param>
    /// <param name="analyzer">Expands a dropped solution or project; null refuses them with a notice.</param>
    /// <param name="shell">Opens folders for Reveal and OpenOutputFolder.</param>
    /// <param name="events">Where <c>files.changed</c> goes after every change; null for a session nobody listens to.</param>
    /// <param name="preferenceDelay">How long a preference change waits for the next one before it is saved. Default 300 ms; zero saves at once.</param>
    public InputSession(
        ISettingsService settings,
        ISolutionAnalyzer? analyzer = null,
        IShellLauncher? shell = null,
        IEventPublisher? events = null,
        ILogger<InputSession>? logger = null,
        TimeSpan? preferenceDelay = null)
    {
        _settings = settings;
        _analyzer = analyzer;
        _shell = shell;
        _events = events;
        _logger = logger;
        _preferenceDelay = preferenceDelay ?? TimeSpan.FromMilliseconds(300);
        ReadPreferencesLocked();
    }

    /// <summary>Raised once after any change but a quiet one, on the thread that made it, outside the session's lock.</summary>
    public event Action? Changed;

    private void RaiseChanged()
    {
        _events?.Publish(AppEvents.FilesChanged, GetSnapshot());
        Changed?.Invoke();
    }

    // ---- Preferences -------------------------------------------------------------------------------------------

    /// <summary>
    /// Reads the preferences from disk and applies them. Quiet: it runs at startup, before the page asks.
    /// </summary>
    public async Task InitializeAsync()
    {
        await _settings.LoadPreferencesAsync().ConfigureAwait(false);
        ApplyPreferences();
    }

    /// <summary>
    /// Copies the values the settings service holds onto the session. Call after the service has loaded them from disk
    /// (the constructor read what it held then). Quiet. Leaves a <c>-o</c> folder from the command line alone.
    /// </summary>
    public void ApplyPreferences()
    {
        lock (_gate)
        {
            ReadPreferencesLocked();
            _revision++;
        }
    }

    private void ReadPreferencesLocked()
    {
        _writeSymbolMap = _settings.GenerateSymbolMap;
        if (_sessionOutputOverride)
            return;

        var stored = _settings.LastOutputDirectory;
        if (!string.IsNullOrWhiteSpace(stored) && !Directory.Exists(stored))
        {
            _outputWarning = $"Saved output directory was not found: {stored}";
            _outputFolder = string.Empty;
            return;
        }

        _outputWarning = null;
        _outputFolder = stored ?? string.Empty;
    }

    /// <summary>
    /// Writes the current preferences through the settings service and waits for it, after any save already queued. A
    /// <c>-o</c> folder from the command line is not written.
    /// </summary>
    public async Task PersistPreferencesAsync()
    {
        // Cut a debounce short instead of waiting it out: the caller is usually shutting down.
        Task pending;
        lock (_preferenceGate)
        {
            _preferenceDebounce?.Cancel();
            pending = _preferenceSave;
        }
        await pending.ConfigureAwait(false);

        lock (_gate)
        {
            if (!_sessionOutputOverride)
                _settings.LastOutputDirectory = _outputFolder.Length == 0 ? null : _outputFolder;
            _settings.GenerateSymbolMap = _writeSymbolMap;
        }

        await SavePreferencesSafeAsync().ConfigureAwait(false);
    }

    private void QueuePreferenceSave()
    {
        lock (_preferenceGate)
        {
            _preferenceDebounce?.Cancel();
            var version = ++_preferenceVersion;
            if (_preferenceDelay <= TimeSpan.Zero)
            {
                _preferenceSave = SavePreferencesSafeAsync();
                return;
            }

            var debounce = _preferenceDebounce = new CancellationTokenSource();
            _preferenceSave = SavePreferencesDebouncedAsync(version, debounce.Token);
        }
    }

    private async Task SavePreferencesDebouncedAsync(int version, CancellationToken cancel)
    {
        try
        {
            await Task.Delay(_preferenceDelay, cancel).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer change, or flushed by PersistPreferencesAsync, which writes the current state itself.
            return;
        }

        if (version != Volatile.Read(ref _preferenceVersion))
            return;

        await SavePreferencesSafeAsync().ConfigureAwait(false);
    }

    private async Task SavePreferencesSafeAsync()
    {
        try
        {
            await _settings.SavePreferencesAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger?.LogWarning("Failed to save UI preferences ({ExceptionType})", ex.GetType().Name);
        }
    }

    // ---- Adding and removing files -----------------------------------------------------------------------------

    /// <summary>
    /// Adds the supported files among <paramref name="paths"/>: assemblies and sources as themselves, solutions and
    /// projects as the rows they expand to. Unsupported, missing and duplicate paths are skipped; a path that cannot be
    /// read becomes a notice, never an exception. The first <c>.json</c> is handed back as the configuration to load.
    /// </summary>
    public AddPathsResult AddPaths(IEnumerable<string> paths) => AddPathsCore(paths, publish: true);

    /// <summary>
    /// Applies process-start arguments: files and <c>-o</c> output folder. Quiet (see the class remarks). Returns what was
    /// found beyond the files, so the host can load a configuration among them.
    /// </summary>
    public AddPathsResult ApplyStartup(IReadOnlyList<string> args)
    {
        var parsed = StartupArguments.Parse(args);
        var result = AddPathsCore(parsed.Files, publish: false);

        if (!string.IsNullOrWhiteSpace(parsed.OutputDirectory))
        {
            try
            {
                var folder = NormalizeFolder(parsed.OutputDirectory);
                lock (_gate)
                {
                    _outputFolder = folder;
                    _outputWarning = null;
                    _sessionOutputOverride = true;
                    _revision++;
                }
            }
            catch (ObfyApiException ex)
            {
                AddNotice(new InputNotice(NoticeLevel.Error, "Invalid output folder", ex.Message), publish: false);
            }
        }

        return result;
    }

    private AddPathsResult AddPathsCore(IEnumerable<string> paths, bool publish)
    {
        var incoming = new List<Entry>();
        var notices = new List<InputNotice>();
        var configs = new List<string>();

        foreach (var path in paths)
        {
            try
            {
                // A .json is a configuration, not an input (UI-058).
                if (IsConfigPath(path))
                {
                    configs.Add(Path.GetFullPath(path));
                    continue;
                }

                if (!IsSupportedInputPath(path))
                    continue;

                if (IsSolutionOrProjectExtension(Path.GetExtension(path)))
                    ExpandSolution(path, incoming, notices);
                else
                    incoming.Add(Entry.FromPath(path));
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException
                or UnauthorizedAccessException or XmlException or InvalidOperationException)
            {
                // The path is the user's: it stays out of the log. The notice goes to the page, on this machine.
                _logger?.LogWarning("Skipped adding a file ({ExceptionType})", ex.GetType().Name);
                notices.Add(new InputNotice(NoticeLevel.Error, "Could not open file", $"{Path.GetFileName(path)}: {ex.Message}"));
            }
        }

        // Only the first configuration loads; say so for the rest.
        if (configs.Count > 1)
        {
            var ignored = string.Join(", ", configs.Skip(1).Select(Path.GetFileName));
            notices.Add(new InputNotice(
                NoticeLevel.Info,
                "Several configurations dropped",
                $"Only {Path.GetFileName(configs[0])} was used. Ignored: {ignored}."));
        }

        var added = 0;
        bool changed;
        lock (_gate)
        {
            foreach (var entry in incoming)
            {
                if (_entries.Exists(e => SamePath(e.Path, entry.Path)))
                    continue;
                _entries.Add(entry);
                added++;
            }

            changed = added > 0 || notices.Count > 0 || _notices.Count > 0;
            _notices = notices;
            if (changed)
                _revision++;
        }

        if (changed && publish)
            RaiseChanged();
        return new AddPathsResult(configs.Count > 0 ? configs[0] : null, added);
    }

    private void ExpandSolution(string path, List<Entry> incoming, List<InputNotice> notices)
    {
        if (_analyzer is null)
        {
            _logger?.LogError("Solution analyzer is not configured");
            notices.Add(new InputNotice(NoticeLevel.Error, "Could not open solution", "Solution analyzer is not configured."));
            return;
        }

        try
        {
            var plan = _analyzer.Analyze(path);
            foreach (var entry in plan.Entries)
                incoming.Add(Entry.FromSessionEntry(entry));

            if (!plan.Entries.Any(static e => e.IsIncluded))
            {
                notices.Add(new InputNotice(
                    NoticeLevel.Warning, "Nothing to protect", "No built outputs found. Build Release and drop the solution again."));
            }
        }
        catch (Exception ex) when (ex is XmlException or InvalidOperationException or ArgumentException
            or IOException or UnauthorizedAccessException)
        {
            _logger?.LogWarning("Failed to expand a solution ({ExceptionType})", ex.GetType().Name);
            notices.Add(new InputNotice(NoticeLevel.Error, "Could not open solution", $"{Path.GetFileName(path)}: {ex.Message}"));
        }
    }

    /// <summary>Removes a row. A row that is already gone is not an error: the page may be a step behind.</summary>
    public void Remove(string id)
    {
        lock (_gate)
        {
            if (_entries.RemoveAll(e => e.Id == id) == 0)
                return;
            _notices = [];
            _revision++;
        }

        RaiseChanged();
    }

    public void Clear()
    {
        lock (_gate)
        {
            if (_entries.Count == 0 && _notices.Count == 0)
                return;
            _entries.Clear();
            _notices = [];
            _revision++;
        }

        RaiseChanged();
    }

    /// <summary>
    /// Includes or excludes a row. Excluding sets it Skipped; including sets it Pending and clears the reason and error.
    /// </summary>
    /// <exception cref="ObfyApiException">No row has that id.</exception>
    public void SetIncluded(string id, bool included)
    {
        lock (_gate)
        {
            var entry = Find(id);
            if (included == (entry.Status != FileStatus.Skipped))
                return;

            if (included)
            {
                entry.Status = FileStatus.Pending;
                entry.SkipReason = null;
                entry.Error = null;
            }
            else
            {
                // A row the analysis skipped keeps its own reason (UI-054); only a row that was never skipped is "Excluded".
                entry.Status = FileStatus.Skipped;
                entry.SkipReason = entry.OriginalSkipReason ?? "Excluded";
            }

            _notices = [];
            _revision++;
        }

        RaiseChanged();
    }

    /// <summary>Records what a run did to a row: its status, where it wrote, what failed, or why it skipped.</summary>
    /// <exception cref="ObfyApiException">No row has that id.</exception>
    public void SetFileResult(string id, FileStatus status, string? outputPath = null, string? error = null, string? skipReason = null)
    {
        lock (_gate)
        {
            var entry = Find(id);
            entry.Status = status;
            entry.Error = error;
            if (outputPath is not null)
                entry.OutputPath = outputPath;
            if (status == FileStatus.Skipped)
                entry.SkipReason = skipReason ?? entry.SkipReason ?? "Skipped";
            _revision++;
        }

        RaiseChanged();
    }

    /// <summary>Sets every row that is not skipped back to Pending and clears its error; skipped rows stay skipped.</summary>
    public void ResetStatus()
    {
        var changed = false;
        lock (_gate)
        {
            foreach (var entry in _entries)
            {
                if (entry.Status == FileStatus.Skipped || (entry.Status == FileStatus.Pending && entry.Error is null))
                    continue;
                entry.Status = FileStatus.Pending;
                entry.Error = null;
                changed = true;
            }

            if (changed)
                _revision++;
        }

        if (changed)
            RaiseChanged();
    }

    // ---- Output folder and symbol map --------------------------------------------------------------------------

    /// <summary>
    /// Sets the output folder from text the user typed or picked: trimmed and made absolute; empty clears it. The folder
    /// need not exist yet (a run creates it). The choice is saved as the next start's folder.
    /// </summary>
    /// <exception cref="ObfyApiException">The text is not a usable folder path, or names a file.</exception>
    public void SetOutputFolder(string? folder)
    {
        var value = NormalizeFolder(folder);
        lock (_gate)
        {
            if (value == _outputFolder && !_sessionOutputOverride && _outputWarning is null)
                return;

            _outputFolder = value;
            _outputWarning = null;
            _sessionOutputOverride = false;
            _settings.LastOutputDirectory = value.Length == 0 ? null : value;
            _notices = [];
            _revision++;
        }

        QueuePreferenceSave();
        RaiseChanged();
    }

    public void SetWriteSymbolMap(bool enabled)
    {
        lock (_gate)
        {
            if (enabled == _writeSymbolMap)
                return;

            _writeSymbolMap = enabled;
            _settings.GenerateSymbolMap = enabled;
            _notices = [];
            _revision++;
        }

        QueuePreferenceSave();
        RaiseChanged();
    }

    /// <summary>
    /// Sets where the symbol map is written; empty goes back to the default (<c>symbolmap.json</c> in the output folder).
    /// A relative path resolves against the output folder. Only while the map is on.
    /// </summary>
    /// <exception cref="ObfyApiException">The map is off, or the text is not a path.</exception>
    public void SetSymbolMapPath(string? path)
    {
        var value = (path ?? string.Empty).Trim();
        lock (_gate)
        {
            if (!_writeSymbolMap)
                throw new ObfyApiException("output.symbolMapOff", "Turn on the symbol map before choosing where it goes.");
            if (value.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
                throw new ObfyApiException("output.invalidPath", "The symbol map path contains characters a path cannot hold.");
            if (!IsUsablePath(value))
                throw new ObfyApiException("output.invalidPath", "That is not a usable symbol map path.");
            if (value == _symbolMapPath)
                return;

            _symbolMapPath = value;
            _notices = [];
            _revision++;
        }

        RaiseChanged();
    }

    private static string NormalizeFolder(string? text)
    {
        var trimmed = (text ?? string.Empty).Trim();
        if (trimmed.Length == 0)
            return string.Empty;

        string full;
        try
        {
            if (trimmed.Contains('\0'))
                throw new ArgumentException("A path cannot hold a null character.");
            full = Path.GetFullPath(trimmed);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException or IOException)
        {
            throw new ObfyApiException("output.invalidPath", $"That is not a usable folder path: {ex.Message}");
        }

        if (File.Exists(full))
            throw new ObfyApiException("output.isFile", "That path is a file, not a folder.");

        var root = Path.GetPathRoot(full);
        return full == root ? full : Path.TrimEndingDirectorySeparator(full);
    }

    // ---- What a run needs --------------------------------------------------------------------------------------

    /// <summary>
    /// Where a run writes, by the one rule the hint on the page also uses. With <paramref name="merge"/> and two or more
    /// included assemblies the result is merged; two or more included assemblies, or any from a solution, are a closed set;
    /// otherwise each file goes next to its input. Nothing is created.
    /// </summary>
    public OutputPlan PlanOutput(bool merge)
    {
        lock (_gate)
            return PlanLocked(merge);
    }

    /// <summary>
    /// What a run reads from the list, in one read under one lock: the included rows, the skipped ones, the plan made from
    /// exactly those rows, and the symbol-map location. Use this, not <see cref="IncludedInputs"/> and
    /// <see cref="PlanOutput"/> one after the other, which a concurrent edit could split.
    /// </summary>
    public RunInput CaptureRun(bool merge)
    {
        RunInput captured;
        OutputPlan? defaultPlan;
        lock (_gate)
        {
            var included = IncludedLocked()
                .Select(e => new InputFile(e.Id, e.Path, e.Kind, e.Origin == FileOrigin.Solution, e.Hints))
                .ToList();
            var skipped = _entries.Where(e => e.Status == FileStatus.Skipped).Select(ToRow).ToList();
            captured = new RunInput(
                included,
                skipped,
                PlanLocked(merge),
                _outputFolder.Length > 0,
                _writeSymbolMap ? ResolveSymbolMapLocked() : null);

            // What clearing the folder would write, so the message can say whether that way out is open.
            defaultPlan = _outputFolder.Length > 0 ? PlanLocked(merge, folder: null) : null;
        }

        // Outside the lock: telling two paths apart may ask the file system about links.
        var listed = captured.Included.Select(static f => f.Path).Concat(captured.Skipped.Select(static r => r.Path));
        return captured with { Collision = OutputCollisions.Find(listed, captured.Plan, defaultPlan) };
    }

    /// <summary>The symbol map's file: the typed path (a relative one against the output folder), or the default name there.</summary>
    public string ResolveSymbolMapPath()
    {
        lock (_gate)
            return ResolveSymbolMapLocked();
    }

    /// <summary>The included rows a run protects: assemblies and sources, not projects without output.</summary>
    public IReadOnlyList<InputFile> IncludedInputs()
    {
        lock (_gate)
            return IncludedLocked()
                .Select(e => new InputFile(e.Id, e.Path, e.Kind, e.Origin == FileOrigin.Solution, e.Hints))
                .ToList();
    }

    /// <summary>
    /// The rows a run protects: included assemblies and sources, in list order. The one source of "which files" for the
    /// output name, the symbol-map location, the mode hint and the run (UI-053), so excluding a row changes all of them.
    /// </summary>
    private List<Entry> IncludedLocked() =>
        _entries.Where(e => e.Status != FileStatus.Skipped && e.Kind != FileKind.Project).ToList();

    private OutputPlan PlanLocked(bool merge) => PlanLocked(merge, _outputFolder.Length == 0 ? null : _outputFolder);

    /// <summary>
    /// The plan for the included rows with <paramref name="folder"/> as the output folder (null: the defaults). The one
    /// place that decides where each file is written: the hint, the run (<see cref="OutputPlan.OutputPathFor"/>) and the
    /// check that no output replaces a listed file (<see cref="OutputCollisions"/>) all read it.
    /// </summary>
    private OutputPlan PlanLocked(bool merge, string? folder)
    {
        var inputs = IncludedLocked();
        if (inputs.Count == 0)
            return new OutputPlan(OutputMode.None, null, null, string.Empty, []);

        var assemblies = inputs.Where(e => e.Kind == FileKind.Assembly).ToList();

        if (merge && inputs.Count >= 2 && assemblies.Count == inputs.Count)
        {
            var primary = assemblies[0];
            var directory = folder ?? DirectoryOf(primary);
            var path = Path.Combine(directory, ObfuscatedName(primary));
            return new OutputPlan(
                OutputMode.Merge, directory, path, $"Merges {inputs.Count} assemblies into {path}.", [new PlannedOutput(primary.Path, path)]);
        }

        if (assemblies.Count >= 2 || inputs.Any(e => e.Origin == FileOrigin.Solution))
        {
            var directory = folder ?? Path.Combine(DirectoryOf(assemblies.FirstOrDefault() ?? inputs[0]), "obfy-out");
            return new OutputPlan(
                OutputMode.ClosedSet, directory, null, $"Writes the protected assemblies to {directory}.", ClosedSetOutputs(inputs, assemblies, directory));
        }

        return new OutputPlan(OutputMode.Each, folder, null, EachHint(inputs, folder), EachOutputs(inputs, folder));
    }

    /// <summary>
    /// What a run of separate files writes, as <c>RunCoordinator.EachAsync</c> does it: with no folder a file goes next to
    /// its input as <c>name.obfuscated.ext</c> and two or more C# files are compiled together into <c>obfy-src</c> next to
    /// the first of them; with a folder every file goes there under its own name.
    /// </summary>
    private static string EachHint(List<Entry> inputs, string? folder)
    {
        if (inputs.Count == 1)
        {
            return folder is null
                ? $"Writes {ObfuscatedName(inputs[0])} next to the input."
                : $"Writes {inputs[0].Name} to {folder}.";
        }

        if (folder is not null)
            return $"Writes each file to {folder}.";

        var sources = inputs.Where(e => e.Kind == FileKind.Source).ToList();
        if (sources.Count < 2)
            return "Writes each file next to its input as name.obfuscated.ext.";

        var sourceFolder = DefaultSourceFolder(sources[0]);
        return sources.Count == inputs.Count
            ? $"Compiles the C# files together and writes them to {sourceFolder}."
            : $"Compiles the C# files together and writes them to {sourceFolder}; the other file goes next to its input as name.obfuscated.ext.";
    }

    /// <summary>
    /// The files <c>RunCoordinator.EachAsync</c> writes: two or more C# files are one compilation, laid out by the engine
    /// under the chosen folder or <c>obfy-src</c> beside the first; every other file is written alone, under its own name in
    /// the chosen folder or as <c>name.obfuscated.ext</c> beside its input.
    /// </summary>
    private static List<PlannedOutput> EachOutputs(List<Entry> inputs, string? folder)
    {
        var outputs = new List<PlannedOutput>(inputs.Count);
        var sources = inputs.Where(e => e.Kind == FileKind.Source).ToList();
        var separate = inputs;

        if (sources.Count >= 2)
        {
            var sourceFolder = folder ?? DefaultSourceFolder(sources[0]);
            var written = Obfy.Core.Services.SourceProcessor.PlanMultiFileOutputPaths([.. sources.Select(static s => s.Path)], sourceFolder);
            for (var i = 0; i < sources.Count; i++)
                outputs.Add(new PlannedOutput(sources[i].Path, written[i]));
            separate = inputs.Where(e => e.Kind != FileKind.Source).ToList();
        }

        foreach (var entry in separate)
        {
            var path = folder is null
                ? Path.Combine(DirectoryOf(entry), ObfuscatedName(entry))
                : Path.Combine(folder, entry.Name);
            outputs.Add(new PlannedOutput(entry.Path, path));
        }

        return outputs;
    }

    /// <summary>
    /// The files a closed set writes under <paramref name="directory"/>: the engine's own names for the assemblies (a shared
    /// name goes into a folder of its own), nothing for the C# files, which a closed set skips. With no assembly at all the
    /// run falls back to separate files in that folder, and so does this.
    /// </summary>
    private static List<PlannedOutput> ClosedSetOutputs(List<Entry> inputs, List<Entry> assemblies, string directory)
    {
        if (assemblies.Count == 0)
            return EachOutputs(inputs, directory);

        var relative = ClosedSetProcessor.PlanRelativeOutputPaths([.. assemblies.Select(static a => a.Path)]);
        return [.. assemblies.Select((a, i) => new PlannedOutput(a.Path, Path.Combine(directory, relative[i])))];
    }

    private static string DefaultSourceFolder(Entry first) => Path.Combine(DirectoryOf(first), "obfy-src");

    /// <summary>
    /// Runs on every snapshot, so it must never throw on what the user typed: a typed path that cannot be made absolute
    /// against the output folder (set-time checks cannot know the folder it will be combined with) falls back to the
    /// default name in that folder.
    /// </summary>
    private string ResolveSymbolMapLocked()
    {
        var baseDirectory = _outputFolder.Length > 0
            ? _outputFolder
            : IncludedLocked() is [var first, ..] ? DirectoryOf(first) : ".";
        var name = _symbolMapPath.Length > 0 ? _symbolMapPath : DefaultSymbolMapName;
        try
        {
            return Path.GetFullPath(Path.Combine(baseDirectory, name));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException or IOException)
        {
            try
            {
                return Path.GetFullPath(Path.Combine(baseDirectory, DefaultSymbolMapName));
            }
            catch (Exception inner) when (inner is ArgumentException or NotSupportedException or PathTooLongException or IOException)
            {
                return DefaultSymbolMapName;
            }
        }
    }

    private static bool IsUsablePath(string value)
    {
        try
        {
            Path.GetFullPath(value);
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException or IOException)
        {
            return false;
        }
    }

    private static string DirectoryOf(Entry entry) => Path.GetDirectoryName(entry.Path) ?? ".";

    private static string ObfuscatedName(Entry entry) =>
        $"{Path.GetFileNameWithoutExtension(entry.Name)}.obfuscated{Path.GetExtension(entry.Name)}";

    // ---- Reveal and open ---------------------------------------------------------------------------------------

    /// <summary>Shows a row's output in the file browser, selected. A row without output, or whose output is gone, is a notice.</summary>
    /// <exception cref="ObfyApiException">No row has that id.</exception>
    public void Reveal(string id)
    {
        string name;
        string? path;
        lock (_gate)
        {
            var entry = Find(id);
            name = entry.Name;
            path = entry.OutputPath;
            _notices = [];
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            AddNotice(new InputNotice(NoticeLevel.Warning, "No output yet", $"{name} has no output path."), publish: true);
            return;
        }

        if (File.Exists(path))
        {
            Launch(shell => shell.RevealFile(path));
            return;
        }

        if (Directory.Exists(path))
        {
            Launch(shell => shell.OpenFolder(path));
            return;
        }

        AddNotice(new InputNotice(NoticeLevel.Warning, "Output not found", path), publish: true);
    }

    /// <summary>
    /// Opens the folder the output went to: that of the last row with output that is still there, else the chosen output
    /// folder. Neither is a notice.
    /// </summary>
    public void OpenOutputFolder()
    {
        string? directory = null;
        lock (_gate)
        {
            var outputs = _entries.Select(e => e.OutputPath).Where(p => !string.IsNullOrEmpty(p)).Reverse();
            foreach (var path in outputs)
            {
                if (File.Exists(path))
                    directory = Path.GetDirectoryName(path);
                else if (Directory.Exists(path))
                    directory = path;
                if (directory is not null)
                    break;
            }

            if (directory is null && _outputFolder.Length > 0 && Directory.Exists(_outputFolder))
                directory = _outputFolder;
            _notices = [];
        }

        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
        {
            AddNotice(
                new InputNotice(NoticeLevel.Warning, "Output folder not found", "There is no output folder to open yet."),
                publish: true);
            return;
        }

        Launch(shell => shell.OpenFolder(directory));
    }

    private void Launch(Action<IShellLauncher> open)
    {
        try
        {
            if (_shell is null)
                throw new IOException("No file browser is available.");
            open(_shell);
        }
        catch (IOException ex)
        {
            AddNotice(new InputNotice(NoticeLevel.Error, "Could not open output", ex.Message), publish: true);
            return;
        }

        // The notices the action cleared are gone; tell the page.
        lock (_gate)
            _revision++;
        RaiseChanged();
    }

    /// <summary>Adds a message to what the page shows for the last change.</summary>
    public void Notify(NoticeLevel level, string title, string message) =>
        AddNotice(new InputNotice(level, title, message), publish: true);

    private void AddNotice(InputNotice notice, bool publish)
    {
        lock (_gate)
        {
            _notices = [.. _notices, notice];
            _revision++;
        }

        if (publish)
            RaiseChanged();
    }

    // ---- Snapshot ----------------------------------------------------------------------------------------------

    public InputSnapshot GetSnapshot()
    {
        lock (_gate)
        {
            var plan = PlanLocked(merge: false);
            var mergePlan = PlanLocked(merge: true);
            return new InputSnapshot(
                _revision,
                _entries.Select(ToRow).ToList(),
                IncludedLocked().Count > 0,
                _outputFolder,
                plan.Hint,
                mergePlan.Hint,
                _outputWarning,
                _writeSymbolMap,
                _symbolMapPath,
                _writeSymbolMap ? ResolveSymbolMapLocked() : null,
                _notices.ToList());
        }
    }

    private static FileRow ToRow(Entry e) => new(
        e.Id, e.Name, e.Path, e.Origin, e.Kind, e.Size, FormatSize(e.Size),
        e.Status != FileStatus.Skipped, e.Status, e.SkipReason, e.OutputPath, e.Error);

    private static string FormatSize(long size) => size switch
    {
        < 1024 => $"{size} B",
        < 1024 * 1024 => $"{size / 1024.0:0.#} KB",
        _ => $"{size / (1024.0 * 1024.0):0.#} MB"
    };

    private Entry Find(string id) =>
        _entries.Find(e => e.Id == id) ?? throw new ObfyApiException("files.notFound", "That file is not in the list.");

    // ---- Paths -------------------------------------------------------------------------------------------------

    /// <summary>Whether any dropped path is something the list or the settings would take.</summary>
    public static bool CanAcceptDrop(IEnumerable<string>? paths) =>
        paths != null && paths.Any(path => IsSupportedInputPath(path) || IsConfigPath(path));

    /// <summary>An existing <c>.json</c> file: a configuration to load.</summary>
    public static bool IsConfigPath(string path)
    {
        try
        {
            return !string.IsNullOrWhiteSpace(path)
                && File.Exists(path)
                && Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException)
        {
            return false;
        }
    }

    /// <summary>An existing assembly, C# source, solution or project file.</summary>
    public static bool IsSupportedInputPath(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return false;
            var ext = Path.GetExtension(path);
            return ext.Equals(".dll", StringComparison.OrdinalIgnoreCase)
                || ext.Equals(".exe", StringComparison.OrdinalIgnoreCase)
                || ext.Equals(".cs", StringComparison.OrdinalIgnoreCase)
                || IsSolutionOrProjectExtension(ext);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException)
        {
            return false;
        }
    }

    private static bool IsSolutionOrProjectExtension(string ext) =>
        ext.Equals(".sln", StringComparison.OrdinalIgnoreCase)
        || ext.Equals(".slnx", StringComparison.OrdinalIgnoreCase)
        || ext.Equals(".csproj", StringComparison.OrdinalIgnoreCase)
        || ext.Equals(".vbproj", StringComparison.OrdinalIgnoreCase)
        || ext.Equals(".fsproj", StringComparison.OrdinalIgnoreCase);

    private static bool SamePath(string a, string b) => a.Equals(b, StringComparison.OrdinalIgnoreCase);

    /// <summary>One row's state. Private: callers see <see cref="FileRow"/> and <see cref="InputFile"/>.</summary>
    private sealed class Entry
    {
        public required string Id { get; init; }
        public required string Path { get; init; }
        public required string Name { get; init; }
        public long Size { get; init; }
        public FileOrigin Origin { get; init; }
        public FileKind Kind { get; init; }
        public FileStatus Status { get; set; } = FileStatus.Pending;
        public string? SkipReason { get; set; }

        /// <summary>The reason the analysis gave when it skipped the row; kept when the row is included and excluded again.</summary>
        public string? OriginalSkipReason { get; init; }

        public string? OutputPath { get; set; }
        public string? Error { get; set; }
        public ProjectSettingsHints? Hints { get; init; }

        public static Entry FromPath(string path) => Create(path, FileOrigin.Added, FileStatus.Pending, null, null);

        public static Entry FromSessionEntry(ProjectProtectionEntry entry) => Create(
            entry.OutputPath ?? entry.ProjectPath,
            FileOrigin.Solution,
            entry.IsIncluded ? FileStatus.Pending : FileStatus.Skipped,
            entry.IsIncluded ? null : entry.SkipMessage ?? entry.SkipReason.ToString(),
            entry.Hints);

        private static Entry Create(string path, FileOrigin origin, FileStatus status, string? skipReason, ProjectSettingsHints? hints)
        {
            var full = System.IO.Path.GetFullPath(path);
            var info = new FileInfo(full);
            var ext = info.Extension;
            return new Entry
            {
                Id = IdOf(full),
                Path = full,
                Name = info.Name,
                Size = info.Exists ? info.Length : 0,
                Origin = origin,
                Kind = ext.Equals(".dll", StringComparison.OrdinalIgnoreCase) || ext.Equals(".exe", StringComparison.OrdinalIgnoreCase)
                    ? FileKind.Assembly
                    : ext.Equals(".cs", StringComparison.OrdinalIgnoreCase) ? FileKind.Source : FileKind.Project,
                Status = status,
                SkipReason = skipReason,
                OriginalSkipReason = skipReason,
                Hints = hints
            };
        }

        /// <summary>Twelve hex digits of the path's hash: the same path always has the same id.</summary>
        private static string IdOf(string fullPath) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fullPath.ToUpperInvariant())), 0, 6).ToLowerInvariant();
    }
}
