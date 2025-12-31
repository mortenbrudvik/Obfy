using Obfy.Core.Models;
using Obfy.Core.Pipeline;

namespace Obfy.Core.Services;

/// <summary>
/// Processes .NET assemblies for obfuscation.
/// </summary>
public interface IAssemblyProcessor
{
    /// <summary>
    /// Loads an assembly for obfuscation.
    /// </summary>
    /// <param name="path">Path to the assembly file.</param>
    /// <param name="settings">Obfuscation settings.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A pipeline context containing the loaded assembly.</returns>
    Task<PipelineContext> LoadAsync(string path, ObfySettings settings, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves an obfuscated assembly.
    /// </summary>
    /// <param name="context">The pipeline context containing the obfuscated assembly.</param>
    /// <param name="outputPath">Path to write the output.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SaveAsync(PipelineContext context, string outputPath, CancellationToken cancellationToken = default);
}
