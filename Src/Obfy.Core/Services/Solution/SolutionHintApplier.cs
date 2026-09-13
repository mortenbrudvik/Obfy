using Obfy.Core.Models;
using Obfy.Core.Models.Solution;
using Obfy.Core.Utilities;

namespace Obfy.Core.Services.Solution;

/// <summary>
/// Overlays per-project <see cref="ProjectSettingsHints"/> onto a settings clone.
/// </summary>
public static class SolutionHintApplier
{
    /// <summary>
    /// Clones <paramref name="source"/> and overlays hints. Prefer this over <see cref="Apply"/> so
    /// callers cannot mutate the original settings bag.
    /// </summary>
    public static ObfySettings Overlay(ObfySettings source, ProjectSettingsHints hints, bool forcePreservePublic)
    {
        ArgumentNullException.ThrowIfNull(source);
        var clone = source.Clone();
        Apply(clone, hints, forcePreservePublic);
        return clone;
    }

    /// <summary>
    /// Applies XAML / runtime / exclusion / public-API hints in place. Does not overwrite a non-Default
    /// <see cref="ObfySettings.RuntimeProfile"/>. <paramref name="forcePreservePublic"/> forces
    /// <see cref="SymbolRenamingSettings.PreservePublicApi"/> to true. When false, public API is
    /// assigned from <see cref="ProjectSettingsHints.PreservePublicApi"/> (closed-set processors
    /// may overwrite that again from AssemblyRef).
    /// </summary>
    public static void Apply(ObfySettings settings, ProjectSettingsHints hints, bool forcePreservePublic)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(hints);

        if (hints.PreserveXaml)
            settings.SymbolRenaming.PreserveXaml = true;

        if (settings.RuntimeProfile == RuntimeProfile.Default)
            settings.RuntimeProfile = hints.RuntimeProfile;

        if (hints.AddUnityExcludes)
            AddUnique(settings.Exclusions.Namespaces, PlatformExclusions.UnityNamespaces);

        if (hints.AddAspNetMvcExcludes)
            AddUnique(settings.Exclusions.Attributes, PlatformExclusions.AspNetMvcAttributes);

        settings.SymbolRenaming.PreservePublicApi = forcePreservePublic || hints.PreservePublicApi;
    }

    private static void AddUnique(List<string> target, IReadOnlyList<string> items)
    {
        foreach (var item in items)
        {
            if (!target.Contains(item, StringComparer.Ordinal))
                target.Add(item);
        }
    }
}
