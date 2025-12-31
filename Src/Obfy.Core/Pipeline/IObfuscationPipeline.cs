using Obfy.Core.Models;
using Obfy.Core.Obfuscators;

namespace Obfy.Core.Pipeline;

/// <summary>
/// Orchestrates the execution of multiple obfuscators in sequence.
/// </summary>
public interface IObfuscationPipeline
{
    /// <summary>
    /// Gets the list of registered obfuscators, ordered by priority.
    /// </summary>
    IReadOnlyList<IObfuscator> Obfuscators { get; }

    /// <summary>
    /// Executes all enabled obfuscators against the target.
    /// </summary>
    /// <param name="context">The pipeline context containing the target.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>The aggregated result of all obfuscation operations.</returns>
    Task<ObfuscationResult> ExecuteAsync(PipelineContext context, CancellationToken cancellationToken = default);
}
