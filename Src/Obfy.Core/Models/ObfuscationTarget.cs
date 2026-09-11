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
    /// Creates a target from a file path, auto-detecting the target type.
    /// </summary>
    public static ObfuscationTarget FromFile(string inputPath, string? outputPath = null)
    {
        TargetType targetType;
        if (Directory.Exists(inputPath))
        {
            targetType = TargetType.SourceCode;
        }
        else
        {
            var extension = Path.GetExtension(inputPath).ToLowerInvariant();
            targetType = extension switch
            {
                ".dll" or ".exe" => TargetType.Assembly,
                ".cs" => TargetType.SourceCode,
                _ => throw new ArgumentException($"Unsupported file type: {extension}", nameof(inputPath))
            };
        }

        return new ObfuscationTarget
        {
            InputPath = inputPath,
            OutputPath = outputPath,
            TargetType = targetType
        };
    }
}
