using Obfy.Core.Models;
using Obfy.Core.Models.Solution;

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

    /// <summary>
    /// Protects <paramref name="inputs"/> as one closed application.
    /// </summary>
    /// <param name="inputs">Assemblies to load. Load failures are recorded and omitted.</param>
    /// <param name="outputDirectory">Directory that receives committed output on success.</param>
    /// <param name="settings">Session settings cloned per module before hint overlay.</param>
    /// <param name="forcePreservePublic">Forces library-mode public names on in-set libraries.</param>
    /// <param name="cancellationToken">Cancels the run and discards temp output.</param>
    /// <returns>The result of the closed-set protection run.</returns>
    Task<ClosedSetResult> ObfuscateClosedSetAsync(
        IReadOnlyList<ClosedSetInput> inputs,
        string outputDirectory,
        ObfySettings settings,
        bool forcePreservePublic = false,
        CancellationToken cancellationToken = default);
}
