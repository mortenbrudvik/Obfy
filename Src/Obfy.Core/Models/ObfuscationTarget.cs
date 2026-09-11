namespace Obfy.Core.Models;

/// <summary>
/// Represents a target to be obfuscated.
/// </summary>
public class ObfuscationTarget
{
    /// <summary>
    /// Gets or sets the input file path.
    /// </summary>
    public required string InputPath { get; init; }

    /// <summary>
    /// Gets or sets the output file path. If null, overwrites the input.
    /// </summary>
    public string? OutputPath { get; init; }

    /// <summary>
    /// Gets or sets the type of target.
    /// </summary>
    public TargetType TargetType { get; init; }

    /// <summary>
    /// Gets the effective output path (OutputPath if specified, otherwise InputPath).
    /// </summary>
    public string EffectiveOutputPath => OutputPath ?? InputPath;

    /// <summary>
    /// Creates a target from a path, choosing the target type. The caller supplies whether the path is
    /// a directory so this model performs no filesystem IO (extension inspection is a pure string op).
    /// </summary>
    public static ObfuscationTarget ForFile(string inputPath, string? outputPath, bool isDirectory)
    {
        var targetType = isDirectory ? TargetType.SourceCode : DetectFromExtension(inputPath);

        return new ObfuscationTarget
        {
            InputPath = inputPath,
            OutputPath = outputPath,
            TargetType = targetType
        };
    }

    private static TargetType DetectFromExtension(string inputPath)
    {
        var extension = Path.GetExtension(inputPath).ToLowerInvariant();
        return extension switch
        {
            ".dll" or ".exe" => TargetType.Assembly,
            ".cs" => TargetType.SourceCode,
            _ => throw new ArgumentException($"Unsupported file type: {extension}", nameof(inputPath))
        };
    }
}
