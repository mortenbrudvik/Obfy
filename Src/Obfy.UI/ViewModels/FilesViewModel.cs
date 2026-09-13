using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
    private bool _suppressPreferenceSave;

    /// <summary>
    /// Gets the collection of files to obfuscate.
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

    public FilesViewModel(IFileDialogService fileDialogService, ISettingsService settingsService)
    {
        _fileDialogService = fileDialogService;
        _settingsService = settingsService;

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
                System.Diagnostics.Trace.TraceWarning(
                    "Could not save output directory preference: {0}", ex.Message);
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
                || ext.Equals(".cs", StringComparison.OrdinalIgnoreCase);
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

                if (!Files.Any(f => f.FilePath.Equals(path, StringComparison.OrdinalIgnoreCase)))
                    Files.Add(AssemblyFile.FromPath(path));
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
            {
                System.Diagnostics.Trace.TraceWarning("Skipped '{0}': {1}", path, ex.Message);
            }
        }
    }

    /// <summary>
    /// Resets the status of all files to pending.
    /// </summary>
    public void ResetFileStatus()
    {
        foreach (var file in Files)
        {
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
