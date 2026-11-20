using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Obfy.Core.Models;
using Obfy.Core.Services.Reporting;
using Obfy.UI.Models;
using Obfy.UI.Services;

namespace Obfy.UI.ViewModels;

/// <summary>
/// ViewModel for the results panel showing obfuscation statistics, symbol mappings,
/// and a C# decompile preview of the output assembly.
/// </summary>
public partial class ResultsViewModel : ObservableObject
{
    private readonly IFileDialogService _fileDialogService;
    private readonly IReportService _reportService;
    private readonly IClipboardService _clipboard;
    private readonly IUserNotificationService _notifications;
    private readonly Dictionary<string, string> _symbolMap = new();
    private List<ParsedSymbol> _parsed = [];
    private ObfuscationReport? _currentReport;
    private int _searchVersion;
    private bool _suppressPreview;

    /// <summary>
    /// Gets the root nodes of the symbol tree.
    /// </summary>
    public ObservableCollection<SymbolTreeNode> RootNodes { get; } = new();

    public ObservableCollection<StatisticGroup> StatisticGroups { get; } = new();

    public ObservableCollection<string> PreviewPaths { get; } = new();

    [ObservableProperty]
    private ObfuscationStatistics? _statistics;

    [ObservableProperty]
    private TimeSpan _elapsedTime;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPreview))]
    [NotifyPropertyChangedFor(nameof(HasPreviewError))]
    private string _previewText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPreview))]
    [NotifyPropertyChangedFor(nameof(HasPreviewError))]
    private string? _previewError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewFileLabel))]
    private string? _selectedPreviewPath;

    public bool HasPreview => PreviewError is null && PreviewText.Length > 0;

    public bool HasPreviewError => !string.IsNullOrEmpty(PreviewError);

    public string PreviewFileLabel => string.IsNullOrEmpty(SelectedPreviewPath)
        ? "Choose an output assembly"
        : SelectedPreviewPath;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExportReportCommand))]
    private bool _hasReport;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CopySymbolCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopyOriginalNameCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopyObfuscatedNameCommand))]
    private SymbolTreeNode? _selectedNode;

    public bool HasNoSymbols => RootNodes.Count == 0 && string.IsNullOrWhiteSpace(SearchText);

    public bool HasNoSearchMatches => RootNodes.Count == 0 && !string.IsNullOrWhiteSpace(SearchText);

    public ResultsViewModel(
        IFileDialogService fileDialogService,
        IReportService reportService,
        IClipboardService clipboard,
        IUserNotificationService notifications)
    {
        _fileDialogService = fileDialogService;
        _reportService = reportService;
        _clipboard = clipboard;
        _notifications = notifications;
    }

    /// <summary>
    /// Loads the results from the obfuscation process.
    /// </summary>
    public void LoadResults(Dictionary<string, string> symbolMap, ObfuscationStatistics? statistics, TimeSpan elapsedTime)
    {
        _symbolMap.Clear();
        foreach (var (key, value) in symbolMap)
            _symbolMap[key] = value;

        Statistics = statistics;
        ElapsedTime = elapsedTime;
        RebuildParsed();
        RebuildStatistics();
        ApplyFilter();
    }

    /// <summary>
    /// Clears all results.
    /// </summary>
    public void Clear()
    {
        RootNodes.Clear();
        StatisticGroups.Clear();
        Statistics = null;
        ElapsedTime = TimeSpan.Zero;
        _symbolMap.Clear();
        _parsed = [];
        SearchText = string.Empty;
        _suppressPreview = true;
        PreviewPaths.Clear();
        SelectedPreviewPath = null;
        _suppressPreview = false;
        PreviewText = string.Empty;
        PreviewError = null;
        _currentReport = null;
        HasReport = false;
        SelectedNode = null;
        NotifyEmptyStates();
    }

    /// <summary>
    /// Sets the obfuscation report for export.
    /// </summary>
    public void SetReport(ObfuscationReport report)
    {
        _currentReport = report;
        HasReport = true;
    }

    /// <summary>
    /// Records assemblies the user can decompile. Does not decompile until one is selected.
    /// </summary>
    public void SetPreviewPaths(IEnumerable<string> paths)
    {
        _suppressPreview = true;
        PreviewPaths.Clear();
        foreach (var path in paths.Where(static path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase))
            PreviewPaths.Add(path);

        SelectedPreviewPath = null;
        PreviewText = string.Empty;
        PreviewError = null;
        _suppressPreview = false;
        OnPropertyChanged(nameof(PreviewFileLabel));
    }

    /// <summary>
    /// Decompiles the obfuscated assembly (not the packed launcher) for the Preview tab.
    /// Failures never throw; they set <see cref="PreviewError"/>. Truncated by
    /// <see cref="Obfy.Core.Utilities.AssemblyPreview.Decompile"/>.
    /// </summary>
    public void LoadPreview(string? assemblyPath)
    {
        PreviewText = string.Empty;
        PreviewError = null;

        if (string.IsNullOrWhiteSpace(assemblyPath))
        {
            PreviewError = "Preview unavailable: no output path was recorded.";
            return;
        }

        if (Directory.Exists(assemblyPath))
        {
            PreviewError = "Preview is only available for assemblies, not source output: " + assemblyPath;
            return;
        }

        if (!File.Exists(assemblyPath))
        {
            PreviewError = "Preview unavailable: output file not found:" + Environment.NewLine + assemblyPath;
            return;
        }

        try
        {
            PreviewText = Obfy.Core.Utilities.AssemblyPreview.Decompile(assemblyPath);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            PreviewError = "Preview failed: " + ex.Message;
        }
    }

    partial void OnSelectedPreviewPathChanged(string? value)
    {
        OnPropertyChanged(nameof(PreviewFileLabel));
        if (_suppressPreview)
            return;

        if (string.IsNullOrWhiteSpace(value))
        {
            PreviewText = string.Empty;
            PreviewError = null;
            return;
        }

        LoadPreview(value);
    }

    private void RebuildParsed()
    {
        _parsed = SymbolMapKey.ParseAll(_symbolMap.Keys)
            .Select(entry => new ParsedSymbol(entry, _symbolMap[entry.Key]))
            .ToList();
    }

    private void RebuildStatistics()
    {
        StatisticGroups.Clear();
        if (Statistics is null)
            return;

        AddGroup("Renamed",
        [
            Chip("Types", Statistics.TypesRenamed),
            Chip("Methods", Statistics.MethodsRenamed),
            Chip("Fields", Statistics.FieldsRenamed),
            Chip("Properties", Statistics.PropertiesRenamed),
            Chip("Parameters", Statistics.ParametersRenamed),
            Chip("Events", Statistics.EventsRenamed),
            Chip("Namespaces", Statistics.NamespacesRenamed)
        ]);
        AddGroup("Encrypted",
        [
            Chip("Strings", Statistics.StringsEncrypted),
            Chip("Constants", Statistics.ConstantsEncrypted),
            Chip("Resources", Statistics.ResourcesEncrypted)
        ]);
        AddGroup("Protection",
        [
            Chip("Control flow", Statistics.MethodsControlFlowObfuscated),
            Chip("Protections", Statistics.ProtectionsApplied),
            Chip("Methods encrypted", Statistics.MethodsEncrypted),
            Chip("Assemblies embedded", Statistics.AssembliesEmbedded)
        ]);
        AddGroup("Metadata",
        [
            Chip("Metadata removed", Statistics.MetadataItemsRemoved)
        ]);

        if (Statistics.TotalTransformations != 0)
        {
            StatisticGroups.Add(new StatisticGroup("Total", [new StatisticChip("Total", Statistics.TotalTransformations)]));
        }
    }

    private void AddGroup(string name, IEnumerable<StatisticChip?> chips)
    {
        var visible = chips.Where(static chip => chip is not null).Cast<StatisticChip>().ToList();
        if (visible.Count > 0)
            StatisticGroups.Add(new StatisticGroup(name, visible));
    }

    private static StatisticChip? Chip(string label, int value)
        => value == 0 ? null : new StatisticChip(label, value);

    private void BuildSymbolTree(IEnumerable<ParsedSymbol> symbols, bool expandMatches)
    {
        RootNodes.Clear();
        var typeNodes = new Dictionary<string, SymbolTreeNode>(StringComparer.Ordinal);

        foreach (var symbol in symbols)
        {
            var entry = symbol.Entry;
            if (entry.TypeFullName is null)
            {
                if (entry.Kind == SymbolMapKey.TypeKind && typeNodes.TryGetValue(entry.DisplayName, out var existing))
                {
                    existing.ObfuscatedName = symbol.Obfuscated;
                    existing.IsExpanded = expandMatches || existing.IsExpanded;
                    continue;
                }

                var node = SymbolTreeNode.Create(entry.DisplayName, symbol.Obfuscated, MapKind(entry.Kind));
                node.IsExpanded = expandMatches;
                if (entry.Kind == SymbolMapKey.TypeKind)
                    typeNodes[entry.DisplayName] = node;

                RootNodes.Add(node);
                continue;
            }

            if (!typeNodes.TryGetValue(entry.TypeFullName, out var parent))
            {
                parent = SymbolTreeNode.Create(entry.TypeFullName, entry.TypeFullName, SymbolType.Type);
                parent.IsExpanded = expandMatches;
                typeNodes[entry.TypeFullName] = parent;
                RootNodes.Add(parent);
            }
            else if (expandMatches)
            {
                parent.IsExpanded = true;
            }

            var child = SymbolTreeNode.Create(entry.DisplayName, symbol.Obfuscated, MapKind(entry.Kind));
            parent.AddChild(child);
        }

        NotifyEmptyStates();
    }

    private void ApplyFilter()
    {
        if (string.IsNullOrWhiteSpace(SearchText))
        {
            BuildSymbolTree(_parsed, expandMatches: false);
            return;
        }

        var query = SearchText.Trim();
        var matchingTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var symbol in _parsed)
        {
            if (!Matches(symbol, query))
                continue;

            if (symbol.Entry.TypeFullName is { } parent)
                matchingTypes.Add(parent);
            else if (symbol.Entry.Kind == SymbolMapKey.TypeKind)
                matchingTypes.Add(symbol.Entry.DisplayName);
        }

        var filtered = _parsed.Where(symbol =>
        {
            if (symbol.Entry.Kind == SymbolMapKey.TypeKind && symbol.Entry.TypeFullName is null)
            {
                return matchingTypes.Contains(symbol.Entry.DisplayName)
                       && (Matches(symbol, query) || MemberMatches(symbol.Entry.DisplayName, query));
            }

            if (symbol.Entry.TypeFullName is { } parent)
            {
                if (!matchingTypes.Contains(parent))
                    return false;

                return TypeSelfMatches(parent, query) || Matches(symbol, query);
            }

            return Matches(symbol, query);
        });

        BuildSymbolTree(filtered, expandMatches: true);
    }

    private bool MemberMatches(string typeName, string query)
        => _parsed.Any(symbol =>
            symbol.Entry.TypeFullName is not null
            && symbol.Entry.TypeFullName.Equals(typeName, StringComparison.Ordinal)
            && Matches(symbol, query));

    private bool TypeSelfMatches(string typeName, string query)
        => _parsed.Any(symbol =>
            symbol.Entry.Kind == SymbolMapKey.TypeKind
            && symbol.Entry.DisplayName.Equals(typeName, StringComparison.Ordinal)
            && Matches(symbol, query))
           || typeName.Contains(query, StringComparison.OrdinalIgnoreCase);

    private static bool Matches(ParsedSymbol symbol, string query)
        => symbol.Entry.Key.Contains(query, StringComparison.OrdinalIgnoreCase)
           || symbol.Entry.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)
           || symbol.Obfuscated.Contains(query, StringComparison.OrdinalIgnoreCase);

    private static SymbolType MapKind(string kind) => kind switch
    {
        SymbolMapKey.NamespaceKind => SymbolType.Namespace,
        SymbolMapKey.MethodKind => SymbolType.Method,
        SymbolMapKey.FieldKind => SymbolType.Field,
        SymbolMapKey.PropertyKind => SymbolType.Property,
        SymbolMapKey.EventKind => SymbolType.Event,
        SymbolMapKey.ParameterKind => SymbolType.Parameter,
        _ => SymbolType.Type
    };

    [RelayCommand]
    private async Task ExportSymbolMapAsync()
    {
        var filePath = _fileDialogService.ShowSaveSymbolMapDialog();
        if (string.IsNullOrEmpty(filePath))
            return;

        try
        {
            var json = JsonSerializer.Serialize(_symbolMap, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(filePath, json);
            _notifications.Show("Symbol map exported", Path.GetFileName(filePath), NotificationSeverity.Success);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _notifications.Show("Export failed", ex.Message, NotificationSeverity.Error);
        }
    }

    [RelayCommand(CanExecute = nameof(HasReport))]
    private async Task ExportReportAsync()
    {
        if (_currentReport == null)
            return;

        var filePath = _fileDialogService.ShowSaveReportDialog();
        if (string.IsNullOrEmpty(filePath))
            return;

        var format = Path.GetExtension(filePath).Equals(".json", StringComparison.OrdinalIgnoreCase)
            ? ReportFormat.Json
            : ReportFormat.Html;

        try
        {
            await _reportService.GenerateReportAsync(_currentReport, filePath, format);
            _notifications.Show("Report exported", Path.GetFileName(filePath), NotificationSeverity.Success);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _notifications.Show("Export failed", ex.Message, NotificationSeverity.Error);
        }
    }

    private bool CanCopySymbol(SymbolTreeNode? node) => node != null || SelectedNode != null;

    [RelayCommand(CanExecute = nameof(CanCopySymbol))]
    private void CopySymbol(SymbolTreeNode? node)
    {
        var target = node ?? SelectedNode;
        if (target == null)
            return;

        CopyText($"{target.OriginalName} -> {target.ObfuscatedName}", "Copied mapping");
    }

    [RelayCommand(CanExecute = nameof(CanCopySymbol))]
    private void CopyOriginalName(SymbolTreeNode? node)
    {
        var target = node ?? SelectedNode;
        if (target == null)
            return;

        CopyText(target.OriginalName, "Copied original name");
    }

    [RelayCommand(CanExecute = nameof(CanCopySymbol))]
    private void CopyObfuscatedName(SymbolTreeNode? node)
    {
        var target = node ?? SelectedNode;
        if (target == null)
            return;

        CopyText(target.ObfuscatedName, "Copied obfuscated name");
    }

    private void CopyText(string text, string title)
    {
        try
        {
            _clipboard.SetText(text);
            _notifications.Show(title, text, NotificationSeverity.Success);
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.ExternalException or InvalidOperationException)
        {
            _notifications.Show("Copy failed", ex.Message, NotificationSeverity.Error);
        }
    }

    partial void OnSearchTextChanged(string value) => ScheduleFilter();

    private void ScheduleFilter()
    {
        var version = ++_searchVersion;
        if (System.Windows.Application.Current is null)
        {
            ApplyFilter();
            return;
        }

        _ = FilterAfterDelayAsync(version);
    }

    private async Task FilterAfterDelayAsync(int version)
    {
        await Task.Delay(200);
        if (version != _searchVersion)
            return;

        ApplyFilter();
    }

    private void NotifyEmptyStates()
    {
        OnPropertyChanged(nameof(HasNoSymbols));
        OnPropertyChanged(nameof(HasNoSearchMatches));
    }

    private readonly record struct ParsedSymbol(SymbolMapKey.Entry Entry, string Obfuscated);
}
