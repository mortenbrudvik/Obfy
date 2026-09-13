using System.Threading;
using System.Threading.Tasks;

namespace Obfy.VisualStudio.Services;

/// <summary>
/// Wrapper for obfuscation service that invokes the CLI
/// </summary>
public interface IObfuscationServiceWrapper
{
    /// <summary>
    /// Obfuscate an assembly with the given settings
    /// </summary>
    Task<ObfuscationResult> ObfuscateAsync(
        string assemblyPath,
        string? outputPath,
        ObfySettings settings,
        CancellationToken cancellationToken = default,
        string? configPath = null);
}
