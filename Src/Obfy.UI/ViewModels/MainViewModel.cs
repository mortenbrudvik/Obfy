using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Obfy.Core.Models;
using Obfy.Core.Models.Solution;
using Obfy.Core.Services;
using Obfy.Core.Services.Reporting;
using Obfy.UI.Models;
using Obfy.UI.Services;
using Wpf.Ui;
using Wpf.Ui.Controls;
using Wpf.Ui.Extensions;

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
    private readonly IContentDialogService _contentDialogService;
    private readonly IUserNotificationService _notifications;
    private readonly NotifyCollectionChangedEventHandler _filesChanged;
    private readonly PropertyChangedEventHandler _settingsChanged;
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
    [NotifyCanExecuteChangedFor(nameof(ObfuscateCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private bool _isObfuscating;

    [ObservableProperty]
    private double _overallProgress;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    private bool _showResultsPanel;

    public MainViewModel(
        IObfuscationService obfuscationService,
        IFileDialogService fileDialogService,
        ISettingsService settingsService,
        IReportService reportService,
        IContentDialogService contentDialogService,
        IUserNotificationService notifications,
        SettingsViewModel settings,
        FilesViewModel files,
        OutputViewModel output,
        ResultsViewModel results)
    {
        _obfuscationService = obfuscationService;
        _fileDialogService = fileDialogService;
        _settingsService = settingsService;
        _reportService = reportService;
        _contentDialogService = contentDialogService;
        _notifications = notifications;
        Settings = settings;
        Files = files;
        Output = output;
        Results = results;
        _filesChanged = (_, _) => ObfuscateCommand.NotifyCanExecuteChanged();
        Files.Files.CollectionChanged += _filesChanged;
        _settingsChanged = (_, e) =>
        {
            if (e.PropertyName is nameof(SettingsViewModel.WatermarkEnabled)
                or nameof(SettingsViewModel.WatermarkId)
                or null)
                ObfuscateCommand.NotifyCanExecuteChanged();
        };
        Settings.PropertyChanged += _settingsChanged;
    }

    /// <summary>
    /// Initializes the ViewModel asynchronously.
    /// </summary>
    public async Task InitializeAsync()
    {
        await _settingsService.LoadPreferencesAsync();
        Files.ApplyPreferences();
        Output.Info("Obfy UI initialized. Add files and configure settings to begin.");
    }

    private bool CanObfuscate() =>
        !IsObfuscating && Files.HasIncludedFiles &&
        (!Settings.WatermarkEnabled || !string.IsNullOrWhiteSpace(Settings.WatermarkId));

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
        var included = files.Where(static f => f.IsIncluded).ToList();
        var skipped = files.Where(static f => f.IsSkipped).ToList();
        var outputDir = string.IsNullOrEmpty(Files.OutputDirectory) ? null : Files.OutputDirectory;

        var allSymbols = new Dictionary<string, string>();
        var totalStats = new ObfuscationStatistics();
        var successfulResults = new List<ObfuscationResult>();
        var stopwatch = Stopwatch.StartNew();

        Output.Clear();
        Output.Info("Starting obfuscation...");
        Output.Info($"Level: {settings.Level}");
        Output.Info($"Files: {included.Count}");
        foreach (var skippedFile in skipped)
            LogSkip(skippedFile);

        try
        {
            if (ShouldMerge(settings, included))
            {
                await MergeAndObfuscateAsync(included, settings, allSymbols, totalStats, successfulResults, cancellationToken);
            }
            else if (ShouldUseClosedSet(files))
            {
                await ObfuscateClosedSetAsync(
                    included, settings, outputDir, allSymbols, totalStats, successfulResults, cancellationToken);
            }
            else
            {
                await ObfuscateEachAsync(included, settings, outputDir, allSymbols, totalStats, successfulResults, cancellationToken);
            }

            stopwatch.Stop();

            Results.LoadResults(allSymbols, totalStats, stopwatch.Elapsed);

            if (successfulResults.Count > 0)
            {
                var combined = CombineResults(successfulResults, totalStats, allSymbols, stopwatch.Elapsed);
                Results.SetReport(_reportService.BuildReport(combined, settings));
                var previewPath = successfulResults
                    .Select(r => r.OutputPath)
                    .LastOrDefault(p => p is not null &&
                                        File.Exists(p) &&
                                        Path.GetExtension(p) is ".dll" or ".exe");
                await LoadPreviewAsync(previewPath, cancellationToken);
            }

            if (Files.GenerateSymbolMap && allSymbols.Count > 0)
            {
                await WriteSymbolMapAsync(allSymbols);
            }

            ShowResultsPanel = true;

            var failed = files.Count(f => f.Status == FileStatus.Error);
            if (failed > 0)
            {
                var message = $"Obfuscation finished with errors: {failed} file(s) failed.";
                Output.Error(message);
                StatusMessage = "Completed with errors";
                _notifications.Show("Completed with errors", message, NotificationSeverity.Error);
            }
            else
            {
                var message = $"Obfuscation completed: {totalStats.TotalTransformations} total transformations in {stopwatch.Elapsed:mm\\:ss\\.fff}";
                Output.Success(message);
                StatusMessage = "Obfuscation complete";
                _notifications.Show("Obfuscation complete", message, NotificationSeverity.Success);
            }
        }
        catch (OperationCanceledException)
        {
            Output.Warning("Obfuscation cancelled by user");
            StatusMessage = "Cancelled";
            ResetProcessingFiles(files, error: null);
            _notifications.Show("Cancelled", "Obfuscation cancelled by user", NotificationSeverity.Warning);
        }
        catch (Exception ex)
        {
            Output.Error($"Obfuscation failed: {ex.Message}");
            StatusMessage = "Error occurred";
            ResetProcessingFiles(files, ex.Message);
            _notifications.Show("Obfuscation failed", ex.Message, NotificationSeverity.Error);
        }
        finally
        {
            IsObfuscating = false;
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
            ObfuscateCommand.NotifyCanExecuteChanged();
        }
    }

    private async Task ObfuscateEachAsync(
        List<AssemblyFile> files,
        ObfySettings settings,
        string? outputDir,
        Dictionary<string, string> allSymbols,
        ObfuscationStatistics totalStats,
        List<ObfuscationResult> successfulResults,
        CancellationToken cancellationToken)
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

            var result = await Task.Run(
                () => _obfuscationService.ObfuscateAsync(
                    file.FilePath,
                    outputPath,
                    settings,
                    cancellationToken),
                cancellationToken);

            file.Progress = 100;
            ApplyResult(file, result, allSymbols, totalStats, successfulResults);
            OverallProgress = (i + 1) * 100.0 / files.Count;
        }
    }

    private async Task MergeAndObfuscateAsync(
        List<AssemblyFile> files,
        ObfySettings settings,
        Dictionary<string, string> allSymbols,
        ObfuscationStatistics totalStats,
        List<ObfuscationResult> successfulResults,
        CancellationToken cancellationToken)
    {
        foreach (var file in files)
        {
            file.Status = FileStatus.Processing;
            file.Progress = 0;
        }

        StatusMessage = "Merging and obfuscating assemblies...";
        Output.Info($"Merging {files.Count} assemblies...");

        var outputPath = Files.ResolveMergeOutputPath();
        var result = await Task.Run(
            () => _obfuscationService.MergeAndObfuscateAsync(
                files.Select(f => f.FilePath),
                outputPath,
                settings,
                cancellationToken),
            cancellationToken);

        OverallProgress = 100;
        foreach (var file in files)
            file.Progress = 100;

        if (result.Success)
        {
            foreach (var file in files)
            {
                file.Status = FileStatus.Success;
                file.OutputPath = result.OutputPath;
            }

            successfulResults.Add(result);
            if (result.Statistics != null)
                totalStats.Merge(result.Statistics);
            foreach (var (key, value) in result.SymbolMap)
                allSymbols[key] = value;

            Output.Success($"Merged and obfuscated {files.Count} assemblies: {result.Statistics?.TotalTransformations ?? 0} transformations");
            if (result.SkippedItems.Count > 0)
                Output.Warning($"{result.SkippedItems.Count} item(s) were skipped and left unobfuscated.");
            foreach (var warning in result.Warnings)
                Output.Warning(warning);
        }
        else
        {
            foreach (var file in files)
            {
                file.Status = FileStatus.Error;
                file.ErrorMessage = result.ErrorMessage;
            }

            Output.Error($"Merge failed: {result.ErrorMessage}");
        }
    }

    private void ApplyResult(
        AssemblyFile file,
        ObfuscationResult result,
        Dictionary<string, string> allSymbols,
        ObfuscationStatistics totalStats,
        List<ObfuscationResult> successfulResults)
    {
        if (result.Success)
        {
            file.Status = FileStatus.Success;
            file.OutputPath = result.OutputPath;
            successfulResults.Add(result);
            if (result.Statistics != null)
                totalStats.Merge(result.Statistics);

            foreach (var (key, value) in result.SymbolMap)
                allSymbols[key] = value;

            Output.Success($"Completed {file.FileName}: {result.Statistics?.TotalTransformations ?? 0} transformations");

            if (result.SkippedItems.Count > 0)
                Output.Warning($"{result.SkippedItems.Count} item(s) in {file.FileName} were skipped and left unobfuscated.");

            foreach (var warning in result.Warnings)
                Output.Warning(warning);
        }
        else
        {
            file.Status = FileStatus.Error;
            file.ErrorMessage = result.ErrorMessage;
            Output.Error($"Failed {file.FileName}: {result.ErrorMessage}");
        }
    }

    internal static bool ShouldUseClosedSet(IReadOnlyList<AssemblyFile> files)
    {
        var included = files.Where(static f => f.IsIncluded);
        return included.Count(static a => a.IsAssembly) >= 2
               || included.Any(static a => a.FromSession);
    }

    private static bool ShouldMerge(ObfySettings settings, List<AssemblyFile> files)
        => settings.AssemblyMerge.Enabled
           && files.Count >= 2
           && files.All(f => f.IsAssembly);

    private void LogSkip(AssemblyFile skippedFile)
    {
        var name = Path.GetFileNameWithoutExtension(skippedFile.FileName);
        Output.Info(string.IsNullOrWhiteSpace(skippedFile.SkipReason)
            ? $"Skipping {name}"
            : $"Skipping {name}: {skippedFile.SkipReason}");
    }

    private async Task ObfuscateClosedSetAsync(
        List<AssemblyFile> included,
        ObfySettings settings,
        string? outputDir,
        Dictionary<string, string> allSymbols,
        ObfuscationStatistics totalStats,
        List<ObfuscationResult> successfulResults,
        CancellationToken cancellationToken)
    {
        var assemblies = included.Where(static a => a.IsAssembly).ToList();
        if (assemblies.Count == 0)
        {
            await ObfuscateEachAsync(included, settings, outputDir, allSymbols, totalStats, successfulResults, cancellationToken);
            return;
        }

        foreach (var leftover in included.Where(static f => !f.IsAssembly))
        {
            leftover.Status = FileStatus.Skipped;
            leftover.SkipReason = "Source files are not part of a closed-set session";
            Output.Warning($"Skipping {leftover.FileName}: {leftover.SkipReason}");
        }

        foreach (var file in assemblies)
        {
            file.Status = FileStatus.Processing;
            file.Progress = 0;
        }

        StatusMessage = "Protecting closed set...";
        Output.Info($"Protecting {assemblies.Count} assemblies as a closed set...");

        var resolvedOutputDir = outputDir;
        if (string.IsNullOrEmpty(resolvedOutputDir))
        {
            var inputDir = Path.GetDirectoryName(assemblies[0].FilePath);
            resolvedOutputDir = Path.Combine(
                string.IsNullOrEmpty(inputDir) ? "." : inputDir,
                "obfy-out");
            Output.Info($"Output directory: {resolvedOutputDir}");
        }

        var inputs = assemblies.Select(static a => new ClosedSetInput
        {
            AssemblyPath = a.FilePath,
            Hints = a.Hints ?? new ProjectSettingsHints()
        }).ToList();

        var forcePreservePublic = settings.SymbolRenaming.PreservePublicApi;
        var result = await Task.Run(
            () => _obfuscationService.ObfuscateClosedSetAsync(
                inputs,
                resolvedOutputDir,
                settings,
                forcePreservePublic,
                cancellationToken),
            cancellationToken);

        OverallProgress = 100;
        ApplyClosedSetResult(included, result, allSymbols, totalStats, successfulResults);
    }

    private void ApplyClosedSetResult(
        List<AssemblyFile> included,
        ClosedSetResult result,
        Dictionary<string, string> allSymbols,
        ObfuscationStatistics totalStats,
        List<ObfuscationResult> successfulResults)
    {
        if (result.Success)
        {
            var byInput = result.ModuleResults
                .Where(static m => !string.IsNullOrEmpty(m.InputPath))
                .GroupBy(static m => m.InputPath!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(static g => g.Key, static g => g.Last(), StringComparer.OrdinalIgnoreCase);

            foreach (var file in included)
            {
                var module = FindModuleResult(file, byInput, result.ModuleResults);
                if (module != null)
                {
                    file.Progress = 100;
                    if (module.Success)
                    {
                        file.Status = FileStatus.Success;
                        file.OutputPath = module.OutputPath;
                    }
                    else
                    {
                        file.Status = FileStatus.Error;
                        file.ErrorMessage = module.ErrorMessage;
                    }

                    continue;
                }

                if (FindLoadFailure(file, result.LoadFailures) is { } loadFailure)
                {
                    file.Progress = 100;
                    file.Status = FileStatus.Error;
                    file.ErrorMessage = $"Failed to load {file.FileName}: {loadFailure.Message}";
                    continue;
                }

                if (file.Status == FileStatus.Processing)
                    file.Status = FileStatus.Pending;
            }

            foreach (var module in result.ModuleResults.Where(static m => m.Success))
            {
                successfulResults.Add(module);
                if (module.Statistics != null)
                    totalStats.Merge(module.Statistics);

                if (module.SkippedItems.Count > 0)
                    Output.Warning($"{module.SkippedItems.Count} item(s) in {Path.GetFileName(module.InputPath)} were skipped and left unobfuscated.");

                foreach (var warning in module.Warnings)
                    Output.Warning(warning);
            }

            foreach (var (key, value) in result.SymbolMap)
                allSymbols[key] = value;

            foreach (var module in result.ModuleResults)
            {
                foreach (var (key, value) in module.SymbolMap)
                    allSymbols.TryAdd(key, value);
            }

            Output.Success($"Closed-set obfuscation completed: {totalStats.TotalTransformations} transformations");
            foreach (var failure in result.LoadFailures)
                Output.Warning($"Failed to load {failure.Path}: {failure.Message}");
        }
        else
        {
            var message = result.ErrorMessage ?? "Closed-set obfuscation failed.";
            foreach (var file in included)
            {
                file.Progress = 100;
                file.Status = FileStatus.Error;
                file.ErrorMessage = message;
            }

            Output.Error(message);
            foreach (var failure in result.LoadFailures)
                Output.Warning($"Failed to load {failure.Path}: {failure.Message}");
            foreach (var module in result.ModuleResults.Where(static m => !m.Success))
                Output.Error($"Failed {Path.GetFileName(module.InputPath)}: {module.ErrorMessage}");
        }
    }

    private static ObfuscationResult? FindModuleResult(
        AssemblyFile file,
        Dictionary<string, ObfuscationResult> byInput,
        IReadOnlyList<ObfuscationResult> modules)
    {
        if (byInput.TryGetValue(file.FilePath, out var module))
            return module;

        return modules.FirstOrDefault(m =>
            (!string.IsNullOrEmpty(m.InputPath)
                && string.Equals(Path.GetFileName(m.InputPath), file.FileName, StringComparison.OrdinalIgnoreCase))
            || (!string.IsNullOrEmpty(m.OutputPath)
                && string.Equals(Path.GetFileName(m.OutputPath), file.FileName, StringComparison.OrdinalIgnoreCase)));
    }

    private static ClosedSetLoadFailure? FindLoadFailure(
        AssemblyFile file,
        IReadOnlyList<ClosedSetLoadFailure> loadFailures)
    {
        foreach (var failure in loadFailures)
        {
            if (string.Equals(failure.Path, file.FilePath, StringComparison.OrdinalIgnoreCase))
                return failure;
            if (string.Equals(Path.GetFileName(failure.Path), file.FileName, StringComparison.OrdinalIgnoreCase))
                return failure;
        }

        return null;
    }

    private static void ResetProcessingFiles(List<AssemblyFile> files, string? error)
    {
        foreach (var file in files.Where(f => f.Status == FileStatus.Processing))
        {
            if (error is null)
            {
                file.Status = FileStatus.Pending;
                file.ErrorMessage = null;
            }
            else
            {
                file.Status = FileStatus.Error;
                file.ErrorMessage = error;
            }

            file.Progress = 0;
        }
    }

    private async Task LoadPreviewAsync(string? previewPath, CancellationToken cancellationToken)
    {
        if (previewPath is null)
        {
            Results.LoadPreview(null);
            return;
        }

        try
        {
            var text = await Task.Run(
                () => Obfy.Core.Utilities.AssemblyPreview.Decompile(previewPath),
                cancellationToken);
            Results.PreviewError = null;
            Results.PreviewText = text;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Results.PreviewText = string.Empty;
            Results.PreviewError = "Preview failed: " + ex.Message;
            Output.Warning($"Preview failed: {ex.Message}");
        }
    }

    private static ObfuscationResult CombineResults(
        List<ObfuscationResult> successful,
        ObfuscationStatistics totalStats,
        Dictionary<string, string> allSymbols,
        TimeSpan elapsed)
    {
        return ObfuscationResult.Successful(
            totalStats,
            inputPath: string.Join(", ", successful.Select(r => r.InputPath).Where(p => !string.IsNullOrEmpty(p))),
            outputPath: successful.Last().OutputPath,
            elapsedTime: elapsed,
            skippedItems: successful.SelectMany(r => r.SkippedItems).ToList(),
            symbolMap: allSymbols,
            warnings: successful.SelectMany(r => r.Warnings).ToList(),
            packedLauncherPath: successful.LastOrDefault(r => r.PackedLauncherPath is not null)?.PackedLauncherPath);
    }

    private async Task WriteSymbolMapAsync(Dictionary<string, string> allSymbols)
    {
        var mapPath = Files.ResolveSymbolMapPath();
        try
        {
            await _obfuscationService.WriteSymbolMapAsync(allSymbols, mapPath);
            Output.Info($"Symbol map written to {mapPath}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Output.Warning($"Failed to write symbol map: {ex.Message}");
        }
    }

    private bool CanCancel() => IsObfuscating;

    [RelayCommand(CanExecute = nameof(CanCancel))]
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
            return;

        try
        {
            var settings = Settings.ToObfySettings();
            await _settingsService.SaveSettingsAsync(settings, filePath);
            Output.Info($"Configuration saved to {filePath}");
            _notifications.Show("Configuration saved", filePath, NotificationSeverity.Success);
        }
        catch (Exception ex)
        {
            Output.Error($"Failed to save configuration: {ex.Message}");
            _notifications.Show("Save failed", ex.Message, NotificationSeverity.Error);
        }
    }

    [RelayCommand]
    private async Task LoadConfigurationAsync()
    {
        var filePath = _fileDialogService.ShowOpenConfigDialog();
        if (string.IsNullOrEmpty(filePath))
            return;

        try
        {
            var settings = await _settingsService.LoadSettingsAsync(filePath);
            if (settings != null)
            {
                Settings.FromObfySettings(settings);
                Output.Info($"Configuration loaded from {filePath}");
                _notifications.Show("Configuration loaded", filePath, NotificationSeverity.Success);
            }
            else
            {
                Output.Error($"Failed to load configuration from {filePath}");
                _notifications.Show("Load failed", $"Could not read {filePath}", NotificationSeverity.Error);
            }
        }
        catch (Exception ex)
        {
            Output.Error($"Failed to load configuration: {ex.Message}");
            _notifications.Show("Load failed", ex.Message, NotificationSeverity.Error);
        }
    }

    [RelayCommand]
    private async Task ShowAboutAsync()
    {
        var versionText = GetInformationalVersion();

        await _contentDialogService.ShowSimpleDialogAsync(new SimpleContentDialogCreateOptions
        {
            Title = "About Obfy",
            Content = $"Version {versionText}\n\n.NET Obfuscation Tool that protects C# assemblies and source code.",
            CloseButtonText = "Close"
        });
    }

    [RelayCommand]
    private void OpenOutputFolder()
    {
        var path = Files.Files
            .Select(f => f.OutputPath)
            .LastOrDefault(p => !string.IsNullOrEmpty(p));

        if (string.IsNullOrEmpty(path))
            return;

        var directory = File.Exists(path) ? Path.GetDirectoryName(path) : path;
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            return;

        Process.Start(new ProcessStartInfo
        {
            FileName = directory,
            UseShellExecute = true
        });
    }

    public static string GetInformationalVersion()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            var plus = informational.IndexOf('+');
            return plus >= 0 ? informational[..plus] : informational;
        }

        var version = assembly.GetName().Version;
        return version is null ? "unknown" : $"{version.Major}.{version.Minor}.{version.Build}";
    }

    public void Dispose()
    {
        Files.Files.CollectionChanged -= _filesChanged;
        Settings.PropertyChanged -= _settingsChanged;
        var cts = Interlocked.Exchange(ref _cancellationTokenSource, null);
        if (cts is null)
            return;
        try
        {
            cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Already disposed by a concurrent cancel/complete.
        }
        finally
        {
            cts.Dispose();
        }
    }
}
