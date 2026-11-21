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
    /// Obfuscates a file and says what the caller will read from the result, so an incremental cache hit is taken
    /// only when it can supply that (see <see cref="ObfuscationRunOptions"/>). An implementation that has no
    /// incremental cache ignores <paramref name="options"/>.
    /// </summary>
    /// <param name="inputPath">Path to the input file.</param>
    /// <param name="outputPath">Path to write the obfuscated output. If null, a default is generated.</param>
    /// <param name="settings">Obfuscation settings to apply.</param>
    /// <param name="options">What the caller reads from the result.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The result of the obfuscation operation.</returns>
    Task<ObfuscationResult> ObfuscateAsync(
        string inputPath,
        string? outputPath,
        ObfySettings settings,
        ObfuscationRunOptions options,
        CancellationToken cancellationToken = default) =>
        ObfuscateAsync(inputPath, outputPath, settings, cancellationToken);

    /// <summary>
    /// Obfuscates multiple files. Two or more assemblies share one closed set so a
    /// name used across them stays consistent. Two or more C# files share one compilation.
    /// A single file uses the per-file path.
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
    /// Obfuscates several C# files as one compilation so shared types keep one name.
    /// </summary>
    Task<ObfuscationResult> ObfuscateSourcesAsync(
        IReadOnlyList<string> inputPaths,
        string? outputDirectory,
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
    /// Merges multiple assemblies into one and obfuscates the result, saying what the caller will read from the
    /// result (see <see cref="ObfuscationRunOptions"/>). An implementation that has no incremental cache ignores
    /// <paramref name="options"/>.
    /// </summary>
    Task<ObfuscationResult> MergeAndObfuscateAsync(
        IEnumerable<string> inputPaths,
        string outputPath,
        ObfySettings settings,
        ObfuscationRunOptions options,
        CancellationToken cancellationToken = default) =>
        MergeAndObfuscateAsync(inputPaths, outputPath, settings, cancellationToken);

    /// <summary>
    /// Protects <paramref name="inputs"/> as one closed application.
    /// </summary>
    /// <param name="inputs">Assemblies to load. Load failures are recorded and omitted.</param>
    /// <param name="outputDirectory">Directory that receives committed output on success.</param>
    /// <param name="settings">Session settings cloned per module before hint overlay.</param>
    /// <param name="forcePreservePublic">When true, keep public names on every module (escape hatch).
    /// When false, in-set libraries referenced by a remaining entry point are renamed; libraries-only
    /// and unreferenced extras stay library-mode.</param>
    /// <param name="cancellationToken">Cancels the run and discards temp output.</param>
    /// <returns>The result of the closed-set protection run.</returns>
    Task<ClosedSetResult> ObfuscateClosedSetAsync(
        IReadOnlyList<ClosedSetInput> inputs,
        string outputDirectory,
        ObfySettings settings,
        bool forcePreservePublic = false,
        CancellationToken cancellationToken = default);
}
