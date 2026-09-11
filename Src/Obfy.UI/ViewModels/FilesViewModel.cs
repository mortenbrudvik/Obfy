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
            await _settingsService.SavePreferencesAsync();
        }
    }

    /// <summary>
    /// Handles file drop from drag-and-drop.
    /// </summary>
    public void HandleFileDrop(string[] filePaths)
    {
        AddFilesInternal(filePaths);
    }

    private void AddFilesInternal(string[] filePaths)
    {
        foreach (var path in filePaths)
        {
            if (File.Exists(path))
            {
                var ext = Path.GetExtension(path).ToLowerInvariant();
                if (ext == ".dll" || ext == ".exe" || ext == ".cs")
                {
                    // Avoid duplicates
                    if (!Files.Any(f => f.FilePath.Equals(path, StringComparison.OrdinalIgnoreCase)))
                    {
                        Files.Add(AssemblyFile.FromPath(path));
                    }
                }
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
        {
            return;
        }

        _settingsService.GenerateSymbolMap = value;
        _ = _settingsService.SavePreferencesAsync();
    }
}
