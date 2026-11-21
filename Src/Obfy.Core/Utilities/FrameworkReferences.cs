using dnlib.DotNet;

namespace Obfy.Core.Utilities;

/// <summary>
/// Helpers for referencing framework types that do not live in the core library facade.
/// </summary>
/// <remarks>
/// On modern .NET an SDK-built app's core library is the <c>System.Runtime</c> facade, which forwards only the
/// types of its own contract. <c>System.Security.Cryptography.SHA256</c> and <c>Aes</c>, <c>DllNotFoundException</c>
/// and <c>Marshal</c> are not among them (they live in <c>System.Security.Cryptography</c> and
/// <c>System.Runtime.InteropServices</c>). Referencing them through <see cref="ICorLibTypes.AssemblyRef"/> produces a
/// <see cref="TypeLoadException"/> at runtime. These helpers resolve a reference to the assembly that actually
/// contains the type, reusing one the module already has or synthesizing one from the core library's
/// identity (framework assemblies share a version/public-key-token and the runtime unifies framework
/// versions). <see cref="Type"/> is the entry point for a framework type by name; a type referenced without
/// these helpers must be one the <c>System.Runtime</c> contract carries.
/// </remarks>
internal static class FrameworkReferences
{
    private const string SystemRuntime = "System.Runtime";

    /// <summary>
    /// Types the modern <c>System.Runtime</c> facade neither defines nor forwards, and the reference assembly
    /// that does. Taken from the Microsoft.NETCore.App reference packs (the contracts an SDK build compiles
    /// against) and checked against the shared frameworks for net8.0, net9.0 and net10.0. A reference to one of
    /// these scoped to <c>System.Runtime</c> fails to load with a <see cref="TypeLoadException"/>.
    /// </summary>
    private static readonly Dictionary<string, string> OutsideSystemRuntime = new(StringComparer.Ordinal)
    {
        ["System.DllNotFoundException"] = "System.Runtime.InteropServices",
        ["System.Runtime.InteropServices.Marshal"] = "System.Runtime.InteropServices",
    };

    /// <summary>
    /// A reference to the framework type <paramref name="ns"/>.<paramref name="name"/> that resolves at run
    /// time on the framework the module targets. Scoped to the core library, except on an SDK-built modern
    /// .NET app (core library <c>System.Runtime</c>) for a type the facade does not carry, which is scoped to
    /// the assembly that does. On mscorlib, netstandard and System.Private.CoreLib the core library defines
    /// or forwards everything.
    /// </summary>
    public static TypeRefUser Type(ModuleDef module, string ns, string name)
    {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(ns);
        ArgumentException.ThrowIfNullOrEmpty(name);

        var fullName = ns.Length == 0 ? name : ns + "." + name;
        var scope = CorlibName(module) == SystemRuntime && OutsideSystemRuntime.TryGetValue(fullName, out var assembly)
            ? Get(module, assembly)
            : module.CorLibTypes.AssemblyRef;
        return new TypeRefUser(module, ns, name, scope);
    }

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
