using Obfy.Core.Models;
using Obfy.Core.Pipeline;

namespace Obfy.Core.Obfuscators;

/// <summary>
/// Base interface for all obfuscation techniques.
/// Follows the Strategy pattern to allow pluggable obfuscation methods.
/// </summary>
public interface IObfuscator
{
    /// <summary>
    /// Gets the unique name of this obfuscator.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the execution priority (lower values execute first).
    /// Recommended ranges:
    /// - 10-20: Encoding and runtime helper injection
    /// - 30-40: Control flow and reference proxies
    /// - 50-60: Symbol renaming
    /// - 90-100: Metadata cleanup (last)
    /// </summary>
    int Priority { get; }

    /// <summary>
    /// Gets whether this obfuscator supports the given target type.
    /// </summary>
    /// <param name="targetType">The type of target (Assembly or SourceCode).</param>
    /// <returns>True if this obfuscator can process the target type.</returns>
    bool SupportsTargetType(TargetType targetType);

    /// <summary>
    /// Determines whether this obfuscator is enabled based on the settings.
    /// </summary>
    /// <param name="settings">The obfuscation settings.</param>
    /// <returns>True if this obfuscator should run.</returns>
    bool IsEnabled(ObfySettings settings);

    /// <summary>
    /// Applies the obfuscation technique to the pipeline context.
    /// </summary>
    /// <param name="context">The pipeline context containing the target and shared state.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>The result of the obfuscation operation.</returns>
    Task<ObfuscationResult> ObfuscateAsync(PipelineContext context, CancellationToken cancellationToken = default);
}
