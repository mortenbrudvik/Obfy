using Obfy.Core.Models;
using Obfy.Core.Models.Solution;

namespace Obfy.Core.Services.Solution;

/// <summary>
/// Loads included assemblies, renames the closed set once, runs the remaining pipeline
/// per module, and commits output all-or-nothing. Pipeline/save/commit is all-or-nothing;
/// modules that fail to load are omitted and the remaining set may still commit.
/// </summary>
public interface IClosedSetProcessor
{
    /// <summary>
    /// Protects <paramref name="inputs"/> as one closed application.
    /// </summary>
    /// <param name="inputs">Assemblies to load. Load failures are recorded and omitted.</param>
    /// <param name="outputDirectory">Directory that receives committed output on success.</param>
    /// <param name="baseSettings">Session settings cloned per module before hint overlay.</param>
    /// <param name="forcePreservePublic">When true, keep public names on every module (escape hatch).
    /// When false, in-set libraries referenced by a remaining entry point are renamed; libraries-only
    /// and unreferenced extras stay library-mode.</param>
    /// <param name="cancellationToken">Cancels the run and discards temp output.</param>
    Task<ClosedSetResult> ExecuteAsync(
        IReadOnlyList<ClosedSetInput> inputs,
        string outputDirectory,
        ObfySettings baseSettings,
        bool forcePreservePublic = false,
        CancellationToken cancellationToken = default);
}
