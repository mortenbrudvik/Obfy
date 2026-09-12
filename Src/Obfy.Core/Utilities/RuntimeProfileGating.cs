using Obfy.Core.Models;
using Obfy.Core.Pipeline;

namespace Obfy.Core.Utilities;

/// <summary>
/// Disables method IL encryption and anti-dump on NativeAOT / Unity IL2CPP targets.
/// <see cref="Apply"/> mutates the working clone of <see cref="ObfySettings"/> (callers are
/// cloned first by <c>ObfuscationService</c>) and records report warnings.
/// </summary>
public static class RuntimeProfileGating
{
    public static bool BlocksPeProtections(RuntimeProfile profile) =>
        profile is RuntimeProfile.NativeAot or RuntimeProfile.UnityIl2Cpp or RuntimeProfile.BlazorWasm;

    public static void Apply(ObfySettings settings, PipelineContext context)
    {
        if (!BlocksPeProtections(settings.RuntimeProfile))
            return;

        var label = settings.RuntimeProfile switch
        {
            RuntimeProfile.NativeAot => "NativeAOT",
            RuntimeProfile.UnityIl2Cpp => "Unity IL2CPP",
            RuntimeProfile.BlazorWasm => "Blazor WebAssembly",
            _ => settings.RuntimeProfile.ToString()
        };

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

        if (settings.DependencyEmbedding.Enabled)
        {
            settings.DependencyEmbedding.Enabled = false;
            context.Warnings.Add(
                $"Dependency embedding disabled for {label}: AssemblyResolve is not available on this runtime.");
        }
    }
}
