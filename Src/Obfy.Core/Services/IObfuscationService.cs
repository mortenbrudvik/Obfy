using Obfy.Core.Models;

namespace Obfy.Core.Services;

/// <summary>
/// Main service for orchestrating obfuscation operations.
/// </summary>
public interface IObfuscationService
{
    /// <summary>
    /// Obfuscates a file (assembly or source code).
    /// </summary>
    /// <param name="inputPath">Path to the input file.</param>
    /// <param name="outputPath">Path to write the obfuscated output. If null, a default is generated.</param>
    /// <param name="settings">Obfuscation settings to apply.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The result of the obfuscation operation.</returns>
    Task<ObfuscationResult> ObfuscateAsync(
        string inputPath,
        string? outputPath,
        ObfySettings settings,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Obfuscates multiple files.
    /// </summary>
    /// <param name="inputPaths">Paths to the input files.</param>
    /// <param name="outputDirectory">Directory to write obfuscated outputs.</param>
    /// <param name="settings">Obfuscation settings to apply.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The results of the obfuscation operations.</returns>
    Task<IReadOnlyList<ObfuscationResult>> ObfuscateBatchAsync(
        IEnumerable<string> inputPaths,
        string outputDirectory,
        ObfySettings settings,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes the symbol map to a file.
    /// </summary>
    /// <param name="symbolMap">The symbol map to write.</param>
    /// <param name="outputPath">Path to write the symbol map.</param>
    Task WriteSymbolMapAsync(Dictionary<string, string> symbolMap, string outputPath);

    /// <summary>
    /// Merges multiple assemblies into one and then obfuscates the result.
    /// </summary>
    /// <param name="inputPaths">Paths to assemblies to merge. The first assembly is treated as primary.</param>
    /// <param name="outputPath">Path to write the merged and obfuscated output.</param>
    /// <param name="settings">Obfuscation settings to apply.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The result of the merge and obfuscation operation.</returns>
    Task<ObfuscationResult> MergeAndObfuscateAsync(
        IEnumerable<string> inputPaths,
        string outputPath,
        ObfySettings settings,
        CancellationToken cancellationToken = default);
}
