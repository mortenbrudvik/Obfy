using Obfy.Core.Models;

namespace Obfy.Core.Services;

/// <summary>
/// Service for merging multiple .NET assemblies into a single assembly.
/// </summary>
public interface IAssemblyMerger
{
    /// <summary>
    /// Merges multiple assemblies into a single output assembly.
    /// </summary>
    /// <param name="inputPaths">Paths to assemblies to merge. The first assembly is treated as the primary (entry point, version, etc.).</param>
    /// <param name="outputPath">Path to write the merged assembly.</param>
    /// <param name="settings">Assembly merge settings.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The path to the merged assembly.</returns>
    Task<AssemblyMergeResult> MergeAsync(
        IEnumerable<string> inputPaths,
        string outputPath,
        AssemblyMergeSettings settings,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of an assembly merge operation.
/// </summary>
public class AssemblyMergeResult
{
    /// <summary>
    /// Whether the merge was successful.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Path to the merged output assembly.
    /// </summary>
    public string OutputPath { get; init; } = string.Empty;

    /// <summary>
    /// Number of assemblies that were merged.
    /// </summary>
    public int MergedAssemblyCount { get; init; }

    /// <summary>
    /// Names of the merged assemblies.
    /// </summary>
    public IReadOnlyList<string> MergedAssemblies { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Error message if the merge failed.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Time taken to perform the merge.
    /// </summary>
    public TimeSpan Duration { get; init; }
}
