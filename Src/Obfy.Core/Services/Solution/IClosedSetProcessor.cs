using Obfy.Core.Models;
using Obfy.Core.Models.Solution;

namespace Obfy.Core.Services.Solution;

/// <summary>
/// Loads included assemblies on one dnlib context, renames the closed set once,
/// runs the remaining pipeline per module, and commits output all-or-nothing.
/// </summary>
public interface IClosedSetProcessor
{
    /// <summary>
    /// Protects <paramref name="inputs"/> as one closed application.
    /// </summary>
    /// <param name="inputs">Assemblies to load. Load failures are recorded and omitted.</param>
    /// <param name="outputDirectory">Directory that receives committed output on success.</param>
    /// <param name="baseSettings">Session settings cloned per module before hint overlay.</param>
    /// <param name="forcePreservePublic">Forces library-mode public names on in-set libraries.</param>
    /// <param name="cancellationToken">Cancels the run and discards temp output.</param>
    Task<ClosedSetResult> ExecuteAsync(
        IReadOnlyList<ClosedSetInput> inputs,
        string outputDirectory,
        ObfySettings baseSettings,
        bool forcePreservePublic = false,
        CancellationToken cancellationToken = default);
}
