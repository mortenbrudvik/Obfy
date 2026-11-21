using Obfy.App.Models;
using Obfy.App.Services;
using Obfy.Core.Models;

namespace Obfy.App.Sessions;

/// <summary>
/// What a run produced: the statistics, the symbol map as a tree, the report and the decompiled preview. Lifted from the
/// WPF <c>ResultsViewModel</c> without WPF, <c>ObservableCollection</c> or <c>ICollectionView</c>. A singleton; the page
/// reads a <see cref="ResultsSnapshot"/>, asks for the tree one page at a time and follows <c>results.changed</c>.
/// <para>
/// Every change runs under a lock, bumps <see cref="ResultsSnapshot.Revision"/>, raises <see cref="Changed"/> and publishes
/// <c>results.changed</c> after it, outside the lock, once. The snapshot a push carries is taken inside a second lock that
/// orders the pushes, so a push never reaches the page after a newer one.
/// </para>
/// <para>
/// The two heavy jobs never run on the calling thread. <see cref="LoadResultsAsync"/> parses the symbol keys once, in
/// <see cref="Task.Run(Action)"/>; <see cref="Query"/> then only filters and slices that parsed model, and
/// <see cref="QueryAsync"/> does it off-thread too. <see cref="LoadPreviewAsync"/> decompiles in <see cref="Task.Run(Action)"/>.
/// An answer that arrives after a <see cref="Clear"/>, <see cref="BeginRun"/> or a newer request is dropped, not stored.
/// </para>
/// </summary>
public sealed class ResultsStore
{
    private const int DefaultPageSize = 200;
    private const int MaxPageSize = 1000;

    private readonly object _gate = new();
    private readonly object _publishGate = new();
    private readonly IEventPublisher? _events;
    private readonly IAssemblyDecompiler _decompiler;
    private readonly Action? _onParse;

    private long _revision;
    private ResultsState _state = ResultsState.NotRun;
    private int _generation;
    private int _previewVersion;

    private Dictionary<string, string> _symbolMap = [];
    private SymbolIndex _index = SymbolIndex.Empty;
    private IReadOnlyList<StatisticGroup> _groups = [];
    private long _elapsedMs;
    private ObfuscationReport? _report;
    private List<string> _previewPaths = [];
    private string? _selectedPreviewPath;
    private string _previewText = string.Empty;
    private string? _previewError;

    /// <param name="events">Where <c>results.changed</c> goes after every change; null for a store nobody listens to.</param>
    /// <param name="decompiler">What decompiles an assembly; null uses <see cref="AssemblyPreviewDecompiler"/>. Tests pass a fake to see which thread it runs on.</param>
    /// <param name="onParse">Called on the parsing thread each time a symbol map is parsed, just before the tree is built. A seam for tests to see how often and where the parse runs; null in the app.</param>
    public ResultsStore(IEventPublisher? events = null, IAssemblyDecompiler? decompiler = null, Action? onParse = null)
    {
        _onParse = onParse;
        _events = events;
        _decompiler = decompiler ?? new AssemblyPreviewDecompiler();
    }

    /// <summary>Raised once after any change, on the thread that made it, outside the store's lock.</summary>
    public event Action? Changed;

    /// <summary>The state, statistics and preview selection with the revision they reflect.</summary>
    public ResultsSnapshot GetSnapshot()
    {
        lock (_gate)
            return SnapshotLocked();
    }

    /// <summary>A copy of the symbol map, original key to obfuscated name, for export.</summary>
    public Dictionary<string, string> GetSymbolMap()
    {
        lock (_gate)
            return new Dictionary<string, string>(_symbolMap);
    }

    /// <summary>The report to export, or null when none was set.</summary>
    public ObfuscationReport? GetReport()
    {
        lock (_gate)
            return _report;
    }

    /// <summary>The preview as last loaded.</summary>
    public PreviewResult GetPreview()
    {
        lock (_gate)
            return new PreviewResult(_selectedPreviewPath, _previewText, _previewError);
    }

    /// <summary>
    /// Marks a run as started: the previous results are dropped and the state becomes <see cref="ResultsState.Running"/>, so
    /// the page shows progress rather than stale numbers.
    /// </summary>
    public void BeginRun() => Reset(ResultsState.Running);

    /// <summary>Drops every result and returns to <see cref="ResultsState.NotRun"/>.</summary>
    public void Clear() => Reset(ResultsState.NotRun);

    private void Reset(ResultsState state)
    {
        lock (_gate)
        {
            _generation++;
            _previewVersion++;
            _symbolMap = [];
            _index = SymbolIndex.Empty;
            _groups = [];
            _elapsedMs = 0;
            _report = null;
            _previewPaths = [];
            _selectedPreviewPath = null;
            _previewText = string.Empty;
            _previewError = null;
            _state = state;
            _revision++;
        }

        Publish();
    }

    /// <summary>
    /// Loads the results of a run: copies the map, parses its keys into the tree off the calling thread, then switches to
    /// <see cref="ResultsState.Finished"/>. Nothing is stored when the store was cleared or restarted meanwhile.
    /// </summary>
    public async Task LoadResultsAsync(IReadOnlyDictionary<string, string> symbolMap, ObfuscationStatistics? statistics, TimeSpan elapsedTime)
    {
        // Copied here so the caller may reuse its dictionary at once; the copy is cheap next to the parse.
        var map = new Dictionary<string, string>(symbolMap);
        int generation;
        lock (_gate)
            generation = ++_generation;

        var (index, groups) = await Task.Run(() =>
        {
            _onParse?.Invoke();
            return (SymbolIndex.Build(map), BuildGroups(statistics));
        }).ConfigureAwait(false);

        lock (_gate)
        {
            if (generation != _generation)
                return;

            _symbolMap = map;
            _index = index;
            _groups = groups;
            _elapsedMs = (long)elapsedTime.TotalMilliseconds;
            _state = ResultsState.Finished;
            _revision++;
        }

        Publish();
    }

    /// <summary>Keeps the report so it can be exported.</summary>
    public void SetReport(ObfuscationReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        lock (_gate)
        {
            _report = report;
            _revision++;
        }

        Publish();
    }

    /// <summary>Records the assemblies the user can decompile. Decompiles nothing until one is chosen.</summary>
    public void SetPreviewPaths(IEnumerable<string> paths)
    {
        var distinct = paths
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        lock (_gate)
        {
            _previewVersion++;
            _previewPaths = distinct;
            _selectedPreviewPath = null;
            _previewText = string.Empty;
            _previewError = null;
            _revision++;
        }

        Publish();
    }

    /// <summary>Whether <paramref name="assemblyPath"/> is one of the assemblies set with <see cref="SetPreviewPaths"/>. <c>preview.get</c> decompiles nothing else.</summary>
    public bool IsKnownPreviewPath(string? assemblyPath)
    {
        if (string.IsNullOrWhiteSpace(assemblyPath))
            return false;

        lock (_gate)
            return _previewPaths.Contains(assemblyPath, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Decompiles an output assembly (not a packed launcher) for the Preview tab, off the calling thread. Failures never
    /// throw: they come back, and are kept, as <see cref="PreviewResult.Error"/>. The text is cut by
    /// <see cref="Obfy.Core.Utilities.AssemblyPreview.Decompile"/>.
    /// </summary>
    public async Task<PreviewResult> LoadPreviewAsync(string? assemblyPath)
    {
        int version;
        lock (_gate)
            version = ++_previewVersion;

        var result = await Task.Run(() => ResolvePreview(assemblyPath)).ConfigureAwait(false);

        lock (_gate)
        {
            if (version != _previewVersion)
                return result;

            _selectedPreviewPath = assemblyPath;
            _previewText = result.Text;
            _previewError = result.Error;
            _revision++;
        }

        Publish();
        return result;
    }

    private PreviewResult ResolvePreview(string? assemblyPath)
    {
        if (string.IsNullOrWhiteSpace(assemblyPath))
            return new PreviewResult(assemblyPath, string.Empty, "Preview unavailable: no output path was recorded.");

        if (Directory.Exists(assemblyPath))
            return new PreviewResult(assemblyPath, string.Empty, "Preview is only available for assemblies, not source output: " + assemblyPath);

        if (!File.Exists(assemblyPath))
            return new PreviewResult(assemblyPath, string.Empty, "Preview unavailable: output file not found:" + Environment.NewLine + assemblyPath);

        try
        {
            return new PreviewResult(assemblyPath, _decompiler.Decompile(assemblyPath), null);
        }
        catch (Exception ex)
        {
            return new PreviewResult(assemblyPath, string.Empty, "Preview failed: " + ex.Message);
        }
    }

    /// <summary>
    /// One window of the symbol tree. Pure over the parsed model, so it is cheap to call again for every keystroke or scroll.
    /// </summary>
    /// <param name="text">Search text; null or blank shows everything. A type shows when it or a member matches, and only the matching members show under it.</param>
    /// <param name="parentId">The node whose children to list; null for the roots.</param>
    /// <param name="offset">Index of the first node wanted within the level; below zero counts as zero.</param>
    /// <param name="limit">Most nodes wanted; zero or less means 200, and more than 1000 is cut to 1000.</param>
    public SymbolsPage Query(string? text, string? parentId, int offset, int limit)
    {
        SymbolIndex index;
        long revision;
        lock (_gate)
        {
            index = _index;
            revision = _revision;
        }

        var query = string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        offset = Math.Max(0, offset);
        limit = limit <= 0 ? DefaultPageSize : Math.Min(limit, MaxPageSize);

        var level = index.Level(parentId, query) ?? [];
        var items = level
            .Skip(offset)
            .Take(limit)
            .Select(node =>
            {
                var childCount = SymbolIndex.VisibleChildCount(node, query);
                var container = node.IsContainer;
                return new SymbolNode(
                    node.Id,
                    node.Original,
                    container ? null : node.Obfuscated,
                    node.Kind,
                    container,
                    childCount,
                    Expanded: query is not null && childCount > 0);
            })
            .ToList();

        return new SymbolsPage(
            revision,
            parentId,
            level.Count,
            offset,
            items,
            index.TotalSymbols,
            HasNoSymbols: index.TotalSymbols == 0,
            HasNoSearchMatches: query is not null && parentId is null && level.Count == 0);
    }

    /// <summary><see cref="Query"/> on a pool thread, so a search over tens of thousands of entries never holds the caller.</summary>
    public Task<SymbolsPage> QueryAsync(string? text, string? parentId, int offset, int limit) =>
        Task.Run(() => Query(text, parentId, offset, limit));

    private static IReadOnlyList<StatisticGroup> BuildGroups(ObfuscationStatistics? statistics)
    {
        if (statistics is null)
            return [];

        var groups = new List<StatisticGroup>();
        AddGroup(groups, "Renamed",
        [
            Chip("Types", statistics.TypesRenamed),
            Chip("Methods", statistics.MethodsRenamed),
            Chip("Fields", statistics.FieldsRenamed),
            Chip("Properties", statistics.PropertiesRenamed),
            Chip("Parameters", statistics.ParametersRenamed),
            Chip("Events", statistics.EventsRenamed),
            Chip("Namespaces", statistics.NamespacesRenamed)
        ]);
        AddGroup(groups, "Encrypted",
        [
            Chip("Strings", statistics.StringsEncrypted),
            Chip("Constants", statistics.ConstantsEncrypted),
            Chip("Resources", statistics.ResourcesEncrypted)
        ]);
        AddGroup(groups, "Protection",
        [
            Chip("Control flow", statistics.MethodsControlFlowObfuscated),
            Chip("Protections", statistics.ProtectionsApplied),
            Chip("Methods encrypted", statistics.MethodsEncrypted),
            Chip("Assemblies embedded", statistics.AssembliesEmbedded)
        ]);
        AddGroup(groups, "Metadata",
        [
            Chip("Metadata removed", statistics.MetadataItemsRemoved)
        ]);

        if (statistics.TotalTransformations != 0)
            groups.Add(new StatisticGroup("Total", [new StatisticChip("Total", statistics.TotalTransformations)]));

        return groups;
    }

    private static void AddGroup(List<StatisticGroup> groups, string name, IEnumerable<StatisticChip?> chips)
    {
        var visible = chips.OfType<StatisticChip>().ToList();
        if (visible.Count > 0)
            groups.Add(new StatisticGroup(name, visible));
    }

    private static StatisticChip? Chip(string label, int value) => value == 0 ? null : new StatisticChip(label, value);

    private ResultsSnapshot SnapshotLocked() => new(
        _revision,
        _state,
        _groups,
        _elapsedMs,
        _index.TotalSymbols,
        HasReport: _report is not null,
        HasPreview: _previewError is null && _previewText.Length > 0,
        PreviewPaths: _previewPaths.ToList(),
        _selectedPreviewPath,
        _previewError);

    private void Publish()
    {
        // The snapshot is taken inside the lock that orders the pushes: one taken before waiting for it could be pushed
        // after a newer one.
        lock (_publishGate)
            _events?.Publish(AppEvents.ResultsChanged, GetSnapshot());

        Changed?.Invoke();
    }
}
