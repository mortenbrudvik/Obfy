using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Obfy.Core.Models;
using Obfy.Core.Services.Reporting;
using Obfy.UI.Models;
using Obfy.UI.Services;

namespace Obfy.UI.ViewModels;

/// <summary>
/// ViewModel for the results panel showing obfuscation statistics and symbol mappings.
/// </summary>
public partial class ResultsViewModel : ObservableObject
{
    private readonly IFileDialogService _fileDialogService;
    private readonly IReportService _reportService;
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
    [NotifyCanExecuteChangedFor(nameof(ExportReportCommand))]
    private bool _hasReport;

    public ResultsViewModel(IFileDialogService fileDialogService, IReportService reportService)
    {
        _fileDialogService = fileDialogService;
        _reportService = reportService;
    }

    /// <summary>
    /// Loads the results from the obfuscation process.
    /// </summary>
    public void LoadResults(Dictionary<string, string> symbolMap, ObfuscationStatistics? statistics, TimeSpan elapsedTime)
    {
        _symbolMap = symbolMap;
        Statistics = statistics;
        ElapsedTime = elapsedTime;

        BuildSymbolTree();
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
        _currentReport = null;
        HasReport = false;
    }

    /// <summary>
    /// Sets the obfuscation report for export.
    /// </summary>
    public void SetReport(ObfuscationReport report)
    {
        _currentReport = report;
        HasReport = true;
    }

    private void BuildSymbolTree()
    {
        RootNodes.Clear();

        // Group symbols by namespace/type
        var typeGroups = new Dictionary<string, SymbolTreeNode>();

        foreach (var (original, obfuscated) in _symbolMap)
        {
            // Parse the symbol name to determine its type
            var (typeName, memberName, symbolType) = ParseSymbol(original);

            if (string.IsNullOrEmpty(typeName))
            {
                // Top-level type
                var typeNode = SymbolTreeNode.Create(original, obfuscated, SymbolType.Type);
                RootNodes.Add(typeNode);
            }
            else
            {
                // Member of a type
                if (!typeGroups.TryGetValue(typeName, out var typeNode))
                {
                    typeNode = SymbolTreeNode.Create(typeName, typeName, SymbolType.Type);
                    typeGroups[typeName] = typeNode;
                    RootNodes.Add(typeNode);
                }

                var memberNode = SymbolTreeNode.Create(memberName, obfuscated, symbolType);
                typeNode.AddChild(memberNode);
            }
        }
    }

    private static (string typeName, string memberName, SymbolType symbolType) ParseSymbol(string symbol)
    {
        // Simple heuristic: if contains "::" it's a member
        if (symbol.Contains("::"))
        {
            var parts = symbol.Split("::", 2);
            var memberName = parts[1];
            var symbolType = memberName.Contains("(") ? SymbolType.Method :
                            memberName.StartsWith("get_") || memberName.StartsWith("set_") ? SymbolType.Property :
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
        {
            return;
        }

        var json = JsonSerializer.Serialize(_symbolMap, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(filePath, json);
    }

    [RelayCommand(CanExecute = nameof(HasReport))]
    private async Task ExportReportAsync()
    {
        if (_currentReport == null)
        {
            return;
        }

        var filePath = _fileDialogService.ShowSaveReportDialog();
        if (string.IsNullOrEmpty(filePath))
        {
            return;
        }

        var format = Path.GetExtension(filePath).ToLowerInvariant() == ".json"
            ? ReportFormat.Json
            : ReportFormat.Html;

        await _reportService.GenerateReportAsync(_currentReport, filePath, format);
    }

    [RelayCommand]
    private void CopySymbol(SymbolTreeNode? node)
    {
        if (node != null)
        {
            Clipboard.SetText($"{node.OriginalName} -> {node.ObfuscatedName}");
        }
    }

    partial void OnSearchTextChanged(string value)
    {
        // Filter the tree based on search text
        // For now, just rebuild - could optimize with filtering
        if (string.IsNullOrWhiteSpace(value))
        {
            BuildSymbolTree();
        }
        else
        {
            // Simple filter: show only matching symbols
            var filtered = _symbolMap
                .Where(kv => kv.Key.Contains(value, StringComparison.OrdinalIgnoreCase) ||
                            kv.Value.Contains(value, StringComparison.OrdinalIgnoreCase))
                .ToDictionary(kv => kv.Key, kv => kv.Value);

            RootNodes.Clear();
            foreach (var (original, obfuscated) in filtered)
            {
                var node = SymbolTreeNode.Create(original, obfuscated, SymbolType.Type);
                RootNodes.Add(node);
            }
        }
    }
}
