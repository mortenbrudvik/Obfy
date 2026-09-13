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
    private Dictionary<string, string> _symbolMap = new();
    private ObfuscationReport? _currentReport;

    /// <summary>
    /// Gets the root nodes of the symbol tree.
    /// </summary>
    public ObservableCollection<SymbolTreeNode> RootNodes { get; } = new();

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

    public bool HasPreview => PreviewError is null && PreviewText.Length > 0;

    public bool HasPreviewError => !string.IsNullOrEmpty(PreviewError);

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExportReportCommand))]
    private bool _hasReport;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CopySymbolCommand))]
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
        _symbolMap = symbolMap;
        Statistics = statistics;
        ElapsedTime = elapsedTime;
        ApplyFilter();
    }

    /// <summary>
    /// Clears all results.
    /// </summary>
    public void Clear()
    {
        RootNodes.Clear();
        Statistics = null;
        ElapsedTime = TimeSpan.Zero;
        _symbolMap.Clear();
        SearchText = string.Empty;
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

    private void BuildSymbolTree(IEnumerable<KeyValuePair<string, string>> symbols)
    {
        RootNodes.Clear();
        var typeGroups = new Dictionary<string, SymbolTreeNode>(StringComparer.Ordinal);

        foreach (var (original, obfuscated) in symbols)
        {
            var (typeName, memberName, symbolType) = ParseSymbol(original);

            if (string.IsNullOrEmpty(typeName))
            {
                if (typeGroups.TryGetValue(original, out var existing))
                {
                    existing.ObfuscatedName = obfuscated;
                }
                else
                {
                    var node = SymbolTreeNode.Create(original, obfuscated, SymbolType.Type);
                    typeGroups[original] = node;
                    RootNodes.Add(node);
                }

                continue;
            }

            if (!typeGroups.TryGetValue(typeName, out var typeNode))
            {
                typeNode = SymbolTreeNode.Create(typeName, typeName, SymbolType.Type);
                typeGroups[typeName] = typeNode;
                RootNodes.Add(typeNode);
            }

            typeNode.AddChild(SymbolTreeNode.Create(memberName, obfuscated, symbolType));
        }

        NotifyEmptyStates();
    }

    private void ApplyFilter()
    {
        if (string.IsNullOrWhiteSpace(SearchText))
        {
            BuildSymbolTree(_symbolMap);
            return;
        }

        var query = SearchText.Trim();
        var typeMatches = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (original, obfuscated) in _symbolMap)
        {
            var (typeName, _, _) = ParseSymbol(original);
            if (string.IsNullOrEmpty(typeName))
            {
                if (Matches(original, obfuscated, query))
                    typeMatches.Add(original);
            }
            else if (Matches(original, obfuscated, query) || Matches(typeName, typeName, query))
            {
                typeMatches.Add(typeName);
            }
        }

        var filtered = _symbolMap.Where(kv =>
        {
            var (typeName, _, _) = ParseSymbol(kv.Key);
            if (string.IsNullOrEmpty(typeName))
                return typeMatches.Contains(kv.Key);

            if (!typeMatches.Contains(typeName))
                return false;

            var typeSelfMatches = Matches(typeName, typeName, query);
            return typeSelfMatches || Matches(kv.Key, kv.Value, query);
        });

        BuildSymbolTree(filtered);
    }

    private static bool Matches(string original, string obfuscated, string query)
        => original.Contains(query, StringComparison.OrdinalIgnoreCase)
           || obfuscated.Contains(query, StringComparison.OrdinalIgnoreCase);

    private static (string typeName, string memberName, SymbolType symbolType) ParseSymbol(string symbol)
    {
        if (symbol.Contains("::"))
        {
            var parts = symbol.Split("::", 2);
            var memberName = parts[1];
            var symbolType = memberName.Contains('(') ? SymbolType.Method :
                            memberName.StartsWith("get_", StringComparison.Ordinal) || memberName.StartsWith("set_", StringComparison.Ordinal) ? SymbolType.Property :
                            SymbolType.Field;
            return (parts[0], memberName, symbolType);
        }

        return (string.Empty, symbol, SymbolType.Type);
    }

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

    [RelayCommand]
    private void CopySymbol(SymbolTreeNode? node)
    {
        var target = node ?? SelectedNode;
        if (target == null)
            return;

        try
        {
            _clipboard.SetText($"{target.OriginalName} -> {target.ObfuscatedName}");
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.ExternalException or InvalidOperationException)
        {
            _notifications.Show("Copy failed", ex.Message, NotificationSeverity.Error);
        }
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    private void NotifyEmptyStates()
    {
        OnPropertyChanged(nameof(HasNoSymbols));
        OnPropertyChanged(nameof(HasNoSearchMatches));
    }

}
