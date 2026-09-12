using dnlib.DotNet;

namespace Obfy.Core.Utilities;

/// <summary>
/// Helpers for referencing framework types that do not live in the core library facade.
/// </summary>
/// <remarks>
/// On modern .NET, types such as <c>System.Security.Cryptography.SHA256</c> and <c>Aes</c> are not in
/// the assembly that <see cref="ICorLibTypes.AssemblyRef"/> points to (e.g. <c>System.Runtime</c>), and
/// are not forwarded from it. Referencing them through that facade produces a
/// <see cref="TypeLoadException"/> at runtime. This resolves a reference to the assembly that actually
/// contains the type, reusing one the module already has or synthesizing one from the core library's
/// identity (framework assemblies share a version/public-key-token and the runtime unifies framework
/// versions).
/// </remarks>
internal static class FrameworkReferences
{
    /// <summary>
    /// Assembly that hosts hashing types (<c>SHA256</c>, <c>ICryptoTransform</c>, ...).
    /// On .NET Framework / netstandard these live in corlib; on modern .NET they live in
    /// <c>System.Security.Cryptography</c>.
    /// </summary>
    public static AssemblyRef Cryptography(ModuleDef module)
    {
        ArgumentNullException.ThrowIfNull(module);
        return IsClassicCorlib(module)
            ? module.CorLibTypes.AssemblyRef
            : Get(module, "System.Security.Cryptography");
    }

    /// <summary>
    /// Assembly that hosts <c>System.Security.Cryptography.Aes</c>.
    /// On .NET Framework this is <c>System.Core</c>; on netstandard it is corlib; on modern .NET it
    /// is <c>System.Security.Cryptography</c>.
    /// </summary>
    public static AssemblyRef Aes(ModuleDef module)
    {
        ArgumentNullException.ThrowIfNull(module);
        var corlibName = CorlibName(module);
        if (corlibName == "mscorlib")
            return Get(module, "System.Core");
        if (corlibName == "netstandard")
            return module.CorLibTypes.AssemblyRef;
        return Get(module, "System.Security.Cryptography");
    }

    /// <summary>
    /// <c>Environment.TickCount64</c> exists on modern .NET, not on .NET Framework / netstandard 2.0.
    /// </summary>
    public static bool SupportsTickCount64(ModuleDef module)
    {
        ArgumentNullException.ThrowIfNull(module);
        return !IsClassicCorlib(module);
    }

    private static bool IsClassicCorlib(ModuleDef module)
    {
        var name = CorlibName(module);
        return name is "mscorlib" or "netstandard";
    }

    private static string CorlibName(ModuleDef module) =>
        UTF8String.ToSystemStringOrEmpty(module.CorLibTypes.AssemblyRef.Name);

    private static AssemblyRef Get(ModuleDef module, string simpleName)
    {
        ArgumentException.ThrowIfNullOrEmpty(simpleName);

        foreach (var existing in module.GetAssemblyRefs())
        {
            if (UTF8String.ToSystemStringOrEmpty(existing.Name) == simpleName)
                return existing;
        }

        var corlib = module.CorLibTypes.AssemblyRef;
        return new AssemblyRefUser(simpleName, corlib.Version, corlib.PublicKeyOrToken, corlib.Culture);
    }
}
