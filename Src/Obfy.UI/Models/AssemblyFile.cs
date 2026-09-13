using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Obfy.UI.Models;

/// <summary>
/// Represents the status of a file in the obfuscation process.
/// </summary>
public enum FileStatus
{
    Pending,
    Processing,
    Success,
    Error
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
    private FileStatus _status = FileStatus.Pending;

    [ObservableProperty]
    private double _progress;

    public override string ToString() => FileName;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string? _outputPath;

    /// <summary>
    /// Gets whether this file is currently being processed.
    /// </summary>
    public bool IsProcessing => Status == FileStatus.Processing;

    public bool IsSuccess => Status == FileStatus.Success;

    public bool IsError => Status == FileStatus.Error;

    public bool IsPending => Status == FileStatus.Pending;

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
}
