using Obfy.Core.Models;
using Obfy.Core.Pipeline;

namespace Obfy.Core.Utilities;

/// <summary>
/// Disables PE-mutating protections on NativeAOT, Unity IL2CPP, and Blazor WASM:
/// method IL encryption, anti-dump, and dependency embedding.
/// Anti-debug stays enabled but omits kernel32 P/Invoke (see <c>AntiDebugObfuscator</c>).
/// <see cref="Apply"/> mutates the working clone of <see cref="ObfySettings"/> (callers are
/// cloned first by <c>ObfuscationService</c>) and records report warnings.
/// </summary>
public static class RuntimeProfileGating
{
    public static bool BlocksPeProtections(RuntimeProfile profile) =>
        profile is RuntimeProfile.NativeAot or RuntimeProfile.UnityIl2Cpp or RuntimeProfile.BlazorWasm;

    public static string Describe(RuntimeProfile profile) => profile switch
    {
        RuntimeProfile.NativeAot => "NativeAOT",
        RuntimeProfile.UnityIl2Cpp => "Unity IL2CPP",
        RuntimeProfile.BlazorWasm => "Blazor WebAssembly",
        _ => profile.ToString()
    };

    public static void Apply(ObfySettings settings, PipelineContext context)
    {
        if (!BlocksPeProtections(settings.RuntimeProfile))
            return;

        var label = Describe(settings.RuntimeProfile);

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
