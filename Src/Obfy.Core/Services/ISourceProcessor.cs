using Obfy.Core.Models;
using Obfy.Core.Pipeline;

namespace Obfy.Core.Services;

/// <summary>
/// Processes C# source code for obfuscation.
/// </summary>
public interface ISourceProcessor
{
    /// <summary>
    /// Loads source code for obfuscation.
    /// </summary>
    /// <param name="path">Path to the source file or directory.</param>
    /// <param name="settings">Obfuscation settings.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A pipeline context containing the parsed source code.</returns>
    Task<PipelineContext> LoadAsync(string path, ObfySettings settings, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves obfuscated source code.
    /// </summary>
    /// <param name="context">The pipeline context containing the obfuscated source.</param>
    /// <param name="outputPath">Path to write the output.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SaveAsync(PipelineContext context, string outputPath, CancellationToken cancellationToken = default);
}
