using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using Obfy.Core.Models.Solution;

namespace Obfy.UI.Models;

/// <summary>
/// Represents the status of a file in the obfuscation process.
/// </summary>
public enum FileStatus
{
    Pending,
    Processing,
    Success,
    Error,
    Skipped
}

/// <summary>
/// Represents an assembly file to be obfuscated.
/// </summary>
public partial class AssemblyFile : ObservableObject
{
    [ObservableProperty]
    private string _filePath = string.Empty;

    [ObservableProperty]
    private string _fileName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FormattedSize))]
    private long _fileSize;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsProcessing))]
    [NotifyPropertyChangedFor(nameof(IsSuccess))]
    [NotifyPropertyChangedFor(nameof(IsError))]
    [NotifyPropertyChangedFor(nameof(IsPending))]
    [NotifyPropertyChangedFor(nameof(IsSkipped))]
    [NotifyPropertyChangedFor(nameof(IsIncluded))]
    private FileStatus _status = FileStatus.Pending;

    [ObservableProperty]
    private double _progress;

    public override string ToString() => FileName;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string? _outputPath;

    [ObservableProperty]
    private string? _skipReason;

    [ObservableProperty]
    private ProjectSettingsHints? _hints;

    /// <summary>
    /// Gets whether this file is currently being processed.
    /// </summary>
    public bool IsProcessing => Status == FileStatus.Processing;

    public bool IsSuccess => Status == FileStatus.Success;

    public bool IsError => Status == FileStatus.Error;

    public bool IsPending => Status == FileStatus.Pending;

    public bool IsSkipped => Status == FileStatus.Skipped;

    public bool IsIncluded => Status != FileStatus.Skipped;

    public string FormattedSize => FileSize switch
    {
        < 1024 => $"{FileSize} B",
        < 1024 * 1024 => $"{FileSize / 1024.0:0.#} KB",
        _ => $"{FileSize / (1024.0 * 1024.0):0.#} MB"
    };

    /// <summary>
    /// Gets whether this is a valid .NET assembly file.
    /// </summary>
    public bool IsAssembly => Path.GetExtension(FilePath).Equals(".dll", StringComparison.OrdinalIgnoreCase) ||
                              Path.GetExtension(FilePath).Equals(".exe", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Gets whether this is a C# source file.
    /// </summary>
    public bool IsSourceFile => Path.GetExtension(FilePath).Equals(".cs", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Creates an AssemblyFile from a file path.
    /// </summary>
    public static AssemblyFile FromPath(string path)
    {
        var fileInfo = new FileInfo(path);
        return new AssemblyFile
        {
            FilePath = path,
            FileName = fileInfo.Name,
            FileSize = fileInfo.Exists ? fileInfo.Length : 0
        };
    }

    /// <summary>
    /// Creates an AssemblyFile from a protection-session project entry.
    /// </summary>
    public static AssemblyFile FromSessionEntry(ProjectProtectionEntry entry)
    {
        var path = entry.OutputPath ?? entry.ProjectPath;
        var fileInfo = new FileInfo(path);
        return new AssemblyFile
        {
            FilePath = path,
            FileName = fileInfo.Name,
            FileSize = fileInfo.Exists ? fileInfo.Length : 0,
            Status = entry.IsIncluded ? FileStatus.Pending : FileStatus.Skipped,
            SkipReason = entry.IsIncluded ? null : entry.SkipMessage ?? entry.SkipReason.ToString(),
            Hints = entry.Hints
        };
    }
}
