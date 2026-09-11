using dnlib.DotNet;

namespace Obfy.Core.Utilities;

/// <summary>
/// Helpers for referencing framework types that do not live in the core library facade.
/// </summary>
/// <remarks>
/// On modern .NET, types such as <c>System.Security.Cryptography.SHA256</c> and <c>Aes</c> are not in
/// the assembly that <see cref="ICorLibTypes.AssemblyRef"/> points to (e.g. <c>System.Runtime</c>), and
/// are not forwarded from it. Referencing them through that facade produces a <see cref="System"/>
/// <c>TypeLoadException</c> at runtime. This resolves a reference to the assembly that actually contains
/// the type, reusing one the module already has or synthesizing one from the core library's identity
/// (framework assemblies share a version/public-key-token and the runtime unifies framework versions).
/// </remarks>
internal static class FrameworkReferences
{
    /// <summary>
    /// Gets an assembly reference for the framework assembly with the given simple name.
    /// </summary>
    public static AssemblyRef Get(ModuleDef module, string simpleName)
    {
        foreach (var existing in module.GetAssemblyRefs())
        {
            if (UTF8String.ToSystemStringOrEmpty(existing.Name) == simpleName)
                return existing;
        }

        var corlib = module.CorLibTypes.AssemblyRef;
        return new AssemblyRefUser(simpleName, corlib.Version, corlib.PublicKeyOrToken, corlib.Culture);
    }

    /// <summary>The assembly that contains the runtime cryptography types (SHA256, Aes, ...).</summary>
    public static AssemblyRef Cryptography(ModuleDef module) => Get(module, "System.Security.Cryptography");
}
