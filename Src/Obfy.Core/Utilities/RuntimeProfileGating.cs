using Obfy.Core.Models;
using Obfy.Core.Pipeline;

namespace Obfy.Core.Utilities;

/// <summary>
/// Disables PE/kernel32 protections (method IL encryption, anti-dump) and AssemblyResolve
/// embedding on NativeAOT, Unity IL2CPP, and Blazor WASM.
/// Anti-debug stays enabled but omits kernel32 P/Invoke (see <c>AntiDebugObfuscator</c>).
/// <see cref="Apply"/> mutates the working clone of <see cref="ObfySettings"/> (callers are
/// cloned first by <c>ObfuscationService</c>) and records report warnings.
/// </summary>
public static class RuntimeProfileGating
{
    public static bool AllowsPeMutation(RuntimeProfile profile) =>
        profile is RuntimeProfile.Default;

    public static bool AllowsKernel32PInvoke(RuntimeProfile profile) =>
        profile is RuntimeProfile.Default;

    public static bool BlocksPeProtections(RuntimeProfile profile) => !AllowsPeMutation(profile);

    public static bool BlocksAssemblyResolve(RuntimeProfile profile) => !AllowsPeMutation(profile);

    public static string Describe(RuntimeProfile profile) => profile switch
    {
        RuntimeProfile.NativeAot => "NativeAOT",
        RuntimeProfile.UnityIl2Cpp => "Unity IL2CPP",
        RuntimeProfile.BlazorWasm => "Blazor WebAssembly",
        _ => profile.ToString()
    };

    public static void Apply(ObfySettings settings, PipelineContext context)
    {
        if (settings.Protection.ProxyExternalCalls && !settings.Protection.ReferenceProxy)
        {
            context.Warnings.Add(
                "protection.proxyExternalCalls is ignored unless protection.referenceProxy is true.");
        }

        var label = Describe(settings.RuntimeProfile);

        if (!AllowsPeMutation(settings.RuntimeProfile))
        {
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
                    $"Anti-dump disabled for {label}: PE wipe and MiniDumpWriteDump patch via kernel32/dbghelp are not safe on this runtime.");
            }
        }

        if (BlocksAssemblyResolve(settings.RuntimeProfile) && settings.DependencyEmbedding.Enabled)
        {
            settings.DependencyEmbedding.Enabled = false;
            context.Warnings.Add(
                $"Dependency embedding disabled for {label}: AssemblyResolve is not available on this runtime.");
        }

        if (!AllowsPeMutation(settings.RuntimeProfile) && settings.Packing.Enabled)
        {
            settings.Packing.Enabled = false;
            context.Warnings.Add(
                $"Native packing disabled for {label}: it emits a framework-dependent host that is not used on this runtime.");
        }
    }
}
