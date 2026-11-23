using Obfy.Core.Models;

namespace Obfy.Core.Utilities;

/// <summary>
/// Shared post-save packing for <c>ObfuscationService</c> and closed-set commits. Only <see cref="SidecarPaths"/> is public,
/// so a caller can name the files packing writes beside an output (the CLI's <c>--map</c> and <c>--report</c> guard does).
/// </summary>
public static class PackingApplicator
{
    internal const string DllExtensionWarning =
        "Packing a .dll writes a native PE with a .dll extension; leftover apphost EXEs will not load it. Prefer packing the entry-point .exe, or rename the packed file to .exe.";

    internal static string Pack(
        string assemblyPath,
        ObfySettings settings,
        string inputPath,
        ICollection<string> warnings)
    {
        string packed;
        if (settings.Packing.IsPortable)
        {
            var signingKey = AssemblySigner.TryLoadKey(settings.Signing);
            packed = ManagedLauncherPacker.Pack(assemblyPath, inputPath, signingKey);
        }
        else
        {
            packed = NativePacker.Pack(assemblyPath, settings, inputPath);
        }

        AddPackedWarnings(assemblyPath, settings.Packing, packed, warnings);
        return packed;
    }

    /// <summary>
    /// What a run that is already packed on disk (an incremental-cache hit) reports for it: the packed path, and adds
    /// to <paramref name="warnings"/> exactly the lines <see cref="Pack"/> adds for it. Packs nothing.
    /// </summary>
    internal static string DescribePacked(string assemblyPath, PackingSettings packing, ICollection<string> warnings)
    {
        var packed = packing.IsPortable ? ManagedLauncherPacker.LauncherPathFor(assemblyPath) : assemblyPath;
        AddPackedWarnings(assemblyPath, packing, packed, warnings);
        return packed;
    }

    private static void AddPackedWarnings(
        string assemblyPath, PackingSettings packing, string packed, ICollection<string> warnings)
    {
        if (packing.IsPortable)
        {
            warnings.Add("Packed launcher: " + packed);
            return;
        }

        warnings.Add("Packed native host: " + packed);
        if (string.Equals(Path.GetExtension(assemblyPath), ".dll", StringComparison.OrdinalIgnoreCase))
            warnings.Add(DllExtensionWarning);
    }

    /// <summary>
    /// The files packing writes beside <paramref name="assemblyPath"/>: the runtime configuration of the native host, or the
    /// managed launcher and its runtime configuration. Nothing when packing is off. Computes names only; touches no file.
    /// </summary>
    public static IEnumerable<string> SidecarPaths(string assemblyPath, PackingSettings packing)
    {
        if (!packing.Enabled)
            yield break;

        if (packing.IsPortable)
        {
            yield return ManagedLauncherPacker.LauncherPathFor(assemblyPath);
            yield return ManagedLauncherPacker.RuntimeConfigPathFor(assemblyPath);
        }
        else
        {
            yield return NativePacker.RuntimeConfigPathFor(assemblyPath);
        }
    }
}
