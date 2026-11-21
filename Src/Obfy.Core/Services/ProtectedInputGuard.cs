using dnlib.DotNet;
using Obfy.Core.Utilities;

namespace Obfy.Core.Services;

/// <summary>
/// Spots an assembly that already carries Obfy's injected runtime helpers (<c>Obfy.Runtime.*</c>).
/// Every injector adds a hard-coded-name helper type unconditionally, so a second run would duplicate
/// those types and, for resource strings, encrypt twice, which corrupts values at runtime. The guard stops
/// the run before anything is written.
/// Helper types that symbol renaming already renamed away are not recognisable and do not collide with the
/// next run's hard-coded names. The repo's own self-obfuscation inputs (obfy, Obfy.Core, Settings.Core,
/// Logging.Core) define no <c>Obfy.Runtime</c> types: Obfy.VmRuntime travels as an embedded resource.
/// </summary>
internal static class ProtectedInputGuard
{
    public static bool ContainsInjectedHelpers(ModuleDef module) =>
        module.GetTypes().Any(ObfuscatorHelpers.IsRuntimeHelper);

    /// <summary>The user-facing refusal. It names the file, never the full path.</summary>
    public static string Refusal(string inputPath) =>
        $"'{Path.GetFileName(inputPath)}' is already protected: it contains Obfy runtime helper types " +
        "(Obfy.Runtime.*). Running Obfy on its own output would inject them twice and can corrupt the result. " +
        "Protect the original build instead.";
}
