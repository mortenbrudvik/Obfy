using Obfy.Core.Models;
using Obfy.Core.Pipeline;

namespace Obfy.Core.Utilities;

/// <summary>
/// Disables Windows PE-mutating protections on NativeAOT / Unity IL2CPP targets.
/// </summary>
public static class RuntimeProfileGating
{
    public static bool BlocksPeProtections(RuntimeProfile profile) =>
        profile is RuntimeProfile.NativeAot or RuntimeProfile.UnityIl2Cpp;

    public static void Apply(ObfySettings settings, PipelineContext context)
    {
        if (!BlocksPeProtections(settings.RuntimeProfile))
            return;

        var label = settings.RuntimeProfile == RuntimeProfile.NativeAot
            ? "NativeAOT"
            : "Unity IL2CPP";

        if (settings.Protection.MethodEncryption)
        {
            settings.Protection.MethodEncryption = false;
            context.Warnings.Add(
                $"Method encryption disabled for {label}: it uses kernel32 VirtualProtect and is not safe on this runtime.");
        }

        if (settings.Protection.AntiDump)
        {
            settings.Protection.AntiDump = false;
            context.Warnings.Add(
                $"Anti-dump disabled for {label}: it wipes PE headers via kernel32 and is not safe on this runtime.");
        }
    }
}
