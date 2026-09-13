using System.Collections.ObjectModel;
using System.IO;
using System.Xml;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Obfy.Core.Services.Solution;
using Obfy.UI.Models;
using Obfy.UI.Services;

namespace Obfy.UI.ViewModels;

/// <summary>
/// ViewModel for the files panel.
/// </summary>
public partial class FilesViewModel : ObservableObject
{
    private readonly IFileDialogService _fileDialogService;
    private readonly ISettingsService _settingsService;
    private readonly ISolutionAnalyzer? _solutionAnalyzer;
    private readonly ILogger<FilesViewModel>? _logger;
    private readonly IUserNotificationService? _notifications;
    private bool _suppressPreferenceSave;

    /// <summary>
    /// Input rows (included outputs and skipped session projects).
    /// </summary>
    public ObservableCollection<AssemblyFile> Files { get; } = new();

    [ObservableProperty]
    private string _outputDirectory = string.Empty;

    [ObservableProperty]
    private bool _generateSymbolMap = false;

    [ObservableProperty]
    private string _symbolMapPath = string.Empty;

    /// <summary>
    /// Gets whether there are files in the list.
    /// </summary>
    public bool HasFiles => Files.Count > 0;

    /// <summary>
    /// Gets whether there are no files in the list.
    /// </summary>
    public bool HasNoFiles => Files.Count == 0;

    /// <summary>
    /// Gets whether any listed file is included for obfuscation.
    /// </summary>
    public bool HasIncludedFiles =>
        Files.Any(f => f.IsIncluded && (f.IsAssembly || f.IsSourceFile));

    public FilesViewModel(
        IFileDialogService fileDialogService,
        ISettingsService settingsService,
        ISolutionAnalyzer? solutionAnalyzer = null,
        ILogger<FilesViewModel>? logger = null,
        IUserNotificationService? notifications = null)
    {
        _fileDialogService = fileDialogService;
        _settingsService = settingsService;
        _solutionAnalyzer = solutionAnalyzer;
        _logger = logger;
        _notifications = notifications;

        _suppressPreferenceSave = true;
        try
        {
            OutputDirectory = _settingsService.LastOutputDirectory ?? string.Empty;
            GenerateSymbolMap = _settingsService.GenerateSymbolMap;
        }
        finally
        {
            _suppressPreferenceSave = false;
        }

        Files.CollectionChanged += (s, e) =>
        {
            OnPropertyChanged(nameof(HasFiles));
            OnPropertyChanged(nameof(HasNoFiles));
            OnPropertyChanged(nameof(HasIncludedFiles));
        };
    }

    /// <summary>
    /// Copies persisted preference values from <see cref="ISettingsService"/> onto this ViewModel.
    /// Call after <see cref="ISettingsService.LoadPreferencesAsync"/> so the ctor snapshot is replaced
    /// with values loaded from disk.
    /// </summary>
    public void ApplyPreferences()
    {
        _suppressPreferenceSave = true;
        try
        {
            OutputDirectory = _settingsService.LastOutputDirectory ?? string.Empty;
            GenerateSymbolMap = _settingsService.GenerateSymbolMap;
        }
        finally
        {
            _suppressPreferenceSave = false;
        }
    }

    /// <summary>
    /// Writes the current UI preference values back to the settings service and persists them.
    /// </summary>
    public Task PersistPreferencesAsync()
    {
        _settingsService.LastOutputDirectory = string.IsNullOrWhiteSpace(OutputDirectory)
            ? null
            : OutputDirectory;
        _settingsService.GenerateSymbolMap = GenerateSymbolMap;
        return _settingsService.SavePreferencesAsync();
    }

    [RelayCommand]
    private void AddFiles()
    {
        var files = _fileDialogService.ShowOpenAssemblyDialog();
        AddFilesInternal(files);
    }

    [RelayCommand]
    private void AddSourceFiles()
    {
        var files = _fileDialogService.ShowOpenSourceDialog();
        AddFilesInternal(files);
    }

    [RelayCommand]
    private void RemoveFile(AssemblyFile? file)
    {
        if (file != null)
        {
            Files.Remove(file);
        }
    }

    [RelayCommand]
    private void ClearFiles()
    {
        Files.Clear();
    }

    [RelayCommand]
    private async Task BrowseOutputDirectoryAsync()
    {
        var folder = _fileDialogService.ShowFolderBrowserDialog();
        if (!string.IsNullOrEmpty(folder))
        {
            OutputDirectory = folder;
            _settingsService.LastOutputDirectory = folder;
            try
            {
                await _settingsService.SavePreferencesAsync();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger?.LogWarning(ex, "Failed to save UI preferences");
            }
        }
    }

    /// <summary>
    /// Handles file drop from drag-and-drop.
    /// </summary>
    public void HandleFileDrop(string[] filePaths)
    {
        AddFilesInternal(filePaths);
    }

    /// <summary>
    /// Applies parsed process-start arguments (files and <c>-o</c> output directory).
    /// </summary>
    public void ApplyStartup(StartupCommandLine parsed)
    {
        if (parsed.Files.Count > 0)
            HandleFileDrop(parsed.Files.ToArray());

        if (!string.IsNullOrWhiteSpace(parsed.OutputDirectory))
            OutputDirectory = parsed.OutputDirectory;
    }

    public static bool CanAcceptDrop(IEnumerable<string>? paths)
        => paths != null && paths.Any(IsSupportedInputPath);

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
        catch (ArgumentException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private void AddFilesInternal(string[] filePaths)
    {
        foreach (var path in filePaths)
        {
            try
            {
                if (!IsSupportedInputPath(path))
                    continue;

                if (IsSolutionOrProjectExtension(Path.GetExtension(path)))
                {
                    AddSessionEntries(path);
                    continue;
                }

                if (!Files.Any(f => f.FilePath.Equals(path, StringComparison.OrdinalIgnoreCase)))
                    Files.Add(AssemblyFile.FromPath(path));
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or XmlException or InvalidOperationException)
            {
                _logger?.LogWarning(ex, "Skipped adding file {Path}", path);
                _notifications?.Show(
                    "Could not open file",
                    $"{Path.GetFileName(path)}: {ex.Message}",
                    NotificationSeverity.Error);
            }
        }
    }

    private void AddSessionEntries(string path)
    {
        if (_solutionAnalyzer is null)
        {
            _logger?.LogError("Solution analyzer is not configured");
            _notifications?.Show(
                "Could not open solution",
                "Solution analyzer is not configured.",
                NotificationSeverity.Error);
            return;
        }

        try
        {
            var session = _solutionAnalyzer.Analyze(path);
            foreach (var entry in session.Entries)
            {
                var file = AssemblyFile.FromSessionEntry(entry);
                if (!Files.Any(f => f.FilePath.Equals(file.FilePath, StringComparison.OrdinalIgnoreCase)))
                    Files.Add(file);
            }

            if (!session.Entries.Any(static e => e.IsIncluded))
            {
                _notifications?.Show(
                    "Nothing to protect",
                    "No built outputs found. Build Release and drop the solution again.",
                    NotificationSeverity.Warning);
            }
        }
        catch (Exception ex) when (ex is XmlException or InvalidOperationException or ArgumentException
            or IOException or UnauthorizedAccessException)
        {
            _logger?.LogWarning(ex, "Failed to expand {Path}", path);
            _notifications?.Show(
                "Could not open solution",
                $"{Path.GetFileName(path)}: {ex.Message}",
                NotificationSeverity.Error);
        }
    }

    private static bool IsSolutionOrProjectExtension(string ext)
        => ext.Equals(".sln", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".slnx", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".csproj", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".vbproj", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".fsproj", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Resets non-skipped files to pending (skipped session rows are left unchanged).
    /// </summary>
    public void ResetFileStatus()
    {
        foreach (var file in Files)
        {
            if (file.Status == FileStatus.Skipped)
                continue;

            file.Status = FileStatus.Pending;
            file.Progress = 0;
            file.ErrorMessage = null;
        }
    }

    partial void OnGenerateSymbolMapChanged(bool value)
    {
        if (_suppressPreferenceSave)
            return;

        _settingsService.GenerateSymbolMap = value;
    }

    public string ResolveSymbolMapPath()
    {
        if (!string.IsNullOrWhiteSpace(SymbolMapPath))
            return SymbolMapPath;

        var directory = !string.IsNullOrWhiteSpace(OutputDirectory)
            ? OutputDirectory
            : Files.Count > 0
                ? Path.GetDirectoryName(Files[0].FilePath)
                : null;

        return Path.Combine(directory ?? ".", "symbolmap.json");
    }

    public string ResolveMergeOutputPath()
    {
        var primary = Files.First(f => f.IsAssembly);
        var directory = !string.IsNullOrWhiteSpace(OutputDirectory)
            ? OutputDirectory
            : Path.GetDirectoryName(primary.FilePath) ?? ".";
        Directory.CreateDirectory(directory);
        return Path.Combine(
            directory,
            $"{Path.GetFileNameWithoutExtension(primary.FileName)}.obfuscated{Path.GetExtension(primary.FileName)}");
    }
}
