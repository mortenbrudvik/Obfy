using Obfy.Core.Models;

namespace Obfy.Core.Utilities;

/// <summary>
/// Shared post-save packing for <c>ObfuscationService</c> and closed-set commits.
/// </summary>
internal static class PackingApplicator
{
    internal const string DllExtensionWarning =
        "Packing a .dll writes a native PE with a .dll extension; leftover apphost EXEs will not load it. Prefer packing the entry-point .exe, or rename the packed file to .exe.";

    internal static string Pack(
        string assemblyPath,
        ObfySettings settings,
        string inputPath,
        ICollection<string> warnings)
    {
        if (settings.Packing.IsPortable)
        {
            var packed = ManagedLauncherPacker.Pack(assemblyPath);
            warnings.Add("Packed launcher: " + packed);
            return packed;
        }

        var native = NativePacker.Pack(assemblyPath, settings, inputPath);
        warnings.Add("Packed native host: " + native);
        if (string.Equals(Path.GetExtension(assemblyPath), ".dll", StringComparison.OrdinalIgnoreCase))
            warnings.Add(DllExtensionWarning);
        return native;
    }

    internal static IEnumerable<string> SidecarPaths(string assemblyPath, PackingSettings packing)
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
