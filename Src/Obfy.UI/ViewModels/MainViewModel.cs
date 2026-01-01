using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Obfy.Core.Models;
using Obfy.Core.Services;
using Obfy.Core.Services.Reporting;
using Obfy.UI.Models;
using Obfy.UI.Services;

namespace Obfy.UI.ViewModels;

/// <summary>
/// Main ViewModel that orchestrates the obfuscation process.
/// </summary>
public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly IObfuscationService _obfuscationService;
    private readonly IFileDialogService _fileDialogService;
    private readonly ISettingsService _settingsService;
    private readonly IReportService _reportService;
    private CancellationTokenSource? _cancellationTokenSource;

    /// <summary>
    /// Gets the settings ViewModel.
    /// </summary>
    public SettingsViewModel Settings { get; }

    /// <summary>
    /// Gets the files ViewModel.
    /// </summary>
    public FilesViewModel Files { get; }

    /// <summary>
    /// Gets the output ViewModel.
    /// </summary>
    public OutputViewModel Output { get; }

    /// <summary>
    /// Gets the results ViewModel.
    /// </summary>
    public ResultsViewModel Results { get; }

    [ObservableProperty]
    private bool _isObfuscating;

    [ObservableProperty]
    private double _overallProgress;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    private bool _showResultsPanel;

    [ObservableProperty]
    private int _selectedNavigationIndex;

    public MainViewModel(
        IObfuscationService obfuscationService,
        IFileDialogService fileDialogService,
        ISettingsService settingsService,
        IReportService reportService,
        SettingsViewModel settings,
        FilesViewModel files,
        OutputViewModel output,
        ResultsViewModel results)
    {
        _obfuscationService = obfuscationService;
        _fileDialogService = fileDialogService;
        _settingsService = settingsService;
        _reportService = reportService;
        Settings = settings;
        Files = files;
        Output = output;
        Results = results;
    }

    /// <summary>
    /// Initializes the ViewModel asynchronously.
    /// </summary>
    public async Task InitializeAsync()
    {
        await _settingsService.LoadPreferencesAsync();
        Output.Info("Obfy UI initialized. Add files and configure settings to begin.");
    }

    private bool CanObfuscate() => !IsObfuscating && Files.HasFiles;

    [RelayCommand(CanExecute = nameof(CanObfuscate))]
    private async Task ObfuscateAsync()
    {
        IsObfuscating = true;
        ShowResultsPanel = false;
        OverallProgress = 0;
        Results.Clear();
        Files.ResetFileStatus();

        _cancellationTokenSource = new CancellationTokenSource();
        var cancellationToken = _cancellationTokenSource.Token;

        var settings = Settings.ToObfySettings();
        var files = Files.Files.ToList();
        var outputDir = string.IsNullOrEmpty(Files.OutputDirectory) ? null : Files.OutputDirectory;

        var allSymbols = new Dictionary<string, string>();
        var totalStats = new ObfuscationStatistics();
        var stopwatch = Stopwatch.StartNew();
        ObfuscationResult? lastSuccessfulResult = null;

        Output.Clear();
        Output.Info("Starting obfuscation...");
        Output.Info($"Level: {settings.Level}");
        Output.Info($"Files: {files.Count}");

        try
        {
            for (int i = 0; i < files.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var file = files[i];
                file.Status = FileStatus.Processing;
                file.Progress = 0;
                StatusMessage = $"Processing {file.FileName}...";

                Output.Info($"Processing {file.FileName}...");

                var outputPath = outputDir != null
                    ? Path.Combine(outputDir, file.FileName)
                    : null;

                var result = await _obfuscationService.ObfuscateAsync(
                    file.FilePath,
                    outputPath,
                    settings,
                    cancellationToken);

                file.Progress = 100;

                if (result.Success)
                {
                    file.Status = FileStatus.Success;
                    lastSuccessfulResult = result;
                    if (result.Statistics != null)
                    {
                        totalStats = MergeStatistics(totalStats, result.Statistics);
                    }
                    // Collect symbols for the results panel
                    foreach (var (key, value) in result.SymbolMap)
                    {
                        allSymbols[key] = value;
                    }
                    Output.Success($"Completed {file.FileName}: {result.Statistics?.TotalTransformations ?? 0} transformations");
                }
                else
                {
                    file.Status = FileStatus.Error;
                    file.ErrorMessage = result.ErrorMessage;
                    Output.Error($"Failed {file.FileName}: {result.ErrorMessage}");
                }

                OverallProgress = (i + 1) * 100.0 / files.Count;
            }

            stopwatch.Stop();

            // Load results
            Results.LoadResults(allSymbols, totalStats, stopwatch.Elapsed);

            // Build and set report for export
            if (lastSuccessfulResult != null)
            {
                var report = _reportService.BuildReport(lastSuccessfulResult, settings);
                Results.SetReport(report);
            }

            ShowResultsPanel = true;

            Output.Success($"Obfuscation completed: {totalStats.TotalTransformations} total transformations in {stopwatch.Elapsed:mm\\:ss\\.fff}");
            StatusMessage = "Obfuscation complete";
        }
        catch (OperationCanceledException)
        {
            Output.Warning("Obfuscation cancelled by user");
            StatusMessage = "Cancelled";

            // Mark remaining files as pending
            foreach (var file in files.Where(f => f.Status == FileStatus.Processing))
            {
                file.Status = FileStatus.Pending;
                file.Progress = 0;
            }
        }
        catch (Exception ex)
        {
            Output.Error($"Obfuscation failed: {ex.Message}");
            StatusMessage = "Error occurred";
        }
        finally
        {
            IsObfuscating = false;
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
            ObfuscateCommand.NotifyCanExecuteChanged();
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        _cancellationTokenSource?.Cancel();
        StatusMessage = "Cancelling...";
    }

    [RelayCommand]
    private async Task SaveConfigurationAsync()
    {
        var filePath = _fileDialogService.ShowSaveConfigDialog();
        if (string.IsNullOrEmpty(filePath))
        {
            return;
        }

        var settings = Settings.ToObfySettings();
        await _settingsService.SaveSettingsAsync(settings, filePath);
        Output.Info($"Configuration saved to {filePath}");
    }

    [RelayCommand]
    private async Task LoadConfigurationAsync()
    {
        var filePath = _fileDialogService.ShowOpenConfigDialog();
        if (string.IsNullOrEmpty(filePath))
        {
            return;
        }

        var settings = await _settingsService.LoadSettingsAsync(filePath);
        if (settings != null)
        {
            Settings.FromObfySettings(settings);
            Output.Info($"Configuration loaded from {filePath}");
        }
        else
        {
            Output.Error($"Failed to load configuration from {filePath}");
        }
    }

    [RelayCommand]
    private void ShowAbout()
    {
        // About dialog would be shown here
        Output.Info("Obfy v1.0.0 - .NET Obfuscation Tool");
    }

    private static ObfuscationStatistics MergeStatistics(ObfuscationStatistics a, ObfuscationStatistics b)
    {
        return new ObfuscationStatistics
        {
            StringsEncrypted = a.StringsEncrypted + b.StringsEncrypted,
            TypesRenamed = a.TypesRenamed + b.TypesRenamed,
            MethodsRenamed = a.MethodsRenamed + b.MethodsRenamed,
            FieldsRenamed = a.FieldsRenamed + b.FieldsRenamed,
            PropertiesRenamed = a.PropertiesRenamed + b.PropertiesRenamed,
            ParametersRenamed = a.ParametersRenamed + b.ParametersRenamed,
            MethodsControlFlowObfuscated = a.MethodsControlFlowObfuscated + b.MethodsControlFlowObfuscated,
            ProtectionsApplied = a.ProtectionsApplied + b.ProtectionsApplied,
            MetadataItemsRemoved = a.MetadataItemsRemoved + b.MetadataItemsRemoved,
            ResourcesEncrypted = a.ResourcesEncrypted + b.ResourcesEncrypted
        };
    }

    public void Dispose()
    {
        _cancellationTokenSource?.Dispose();
    }
}
