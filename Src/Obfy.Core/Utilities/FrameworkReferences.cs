using System.Runtime.CompilerServices;
using dnlib.DotNet;

namespace Obfy.Core.Utilities;

/// <summary>
/// The one place that decides how injected code names a framework type, and what the target framework offers.
/// </summary>
/// <remarks>
/// A type reference resolves at run time through the assembly it is scoped to, and which assembly carries a type
/// depends on the target. An SDK-built modern .NET app's core library is the <c>System.Runtime</c> facade, which
/// forwards only the types of its own contract; where the rest lives changed with each release (for example
/// <c>System.IO.File</c>, <c>RuntimeInformation</c> and the hashing types sit in <c>System.IO.FileSystem</c>,
/// <c>System.Runtime.InteropServices.RuntimeInformation</c> and <c>System.Security.Cryptography.Algorithms</c> on
/// older releases, and <c>Marshal</c> and <c>DllNotFoundException</c> in <c>System.Runtime.InteropServices</c>
/// throughout). A reference to a type the scoped assembly does not carry fails with a
/// <see cref="TypeLoadException"/> or <see cref="FileNotFoundException"/> when the JIT compiles the method that uses
/// it, which for injected code is at startup. <see cref="Type"/> scopes a reference by the module's core library and
/// its version, using the tables below; mscorlib (.NET Framework) and netstandard carry everything except
/// <c>Aes</c> (<c>System.Core</c> on .NET Framework), so they keep the plain core-library scope.
/// Injected code must create every framework type reference here, and must not use a member the target lacks (see the
/// <c>Supports*</c> checks). The tables are checked against the reference packs and shared frameworks by
/// <c>FrameworkReferencesTests</c>.
/// </remarks>
internal static class FrameworkReferences
{
    private const string SystemRuntime = "System.Runtime";
    private const string PrivateCoreLib = "System.Private.CoreLib";
    private const string Mscorlib = "mscorlib";
    private const string AesFullName = "System.Security.Cryptography.Aes";

    /// <summary>
    /// Where a type lives for core libraries up to <paramref name="UpToMajor"/> (the major version of the core
    /// library reference: 4 is netcoreapp 3.x and older, 5 is .NET 5, 6 is .NET 6, and so on). A null
    /// <paramref name="Version"/> means the version of the core library reference: from .NET 5 on every framework
    /// assembly carries the release's version. Before that the assemblies have their own contract versions, and a
    /// reference may not name a higher version than the runtime has.
    /// </summary>
    private sealed record Home(int UpToMajor, string Assembly, Version? Version = null);

    private static readonly Version NetCore3Extensions = new(4, 2, 2, 0);
    private static readonly Version NetCore3Debug = new(4, 1, 2, 0);
    private static readonly Version NetCore3FileSystem = new(4, 1, 2, 0);
    private static readonly Version NetCore3ResourceManager = new(4, 1, 2, 0);
    private static readonly Version NetCore3RuntimeInformation = new(4, 0, 4, 0);
    private static readonly Version NetCore3Algorithms = new(4, 3, 2, 0);
    private static readonly Version NetCore3Primitives = new(4, 1, 2, 0);

    private static readonly Home[] Extensions = { new(4, "System.Runtime.Extensions", NetCore3Extensions) };
    private static readonly Home[] RuntimeInformationHomes =
    {
        new(4, "System.Runtime.InteropServices.RuntimeInformation", NetCore3RuntimeInformation),
        new(6, "System.Runtime.InteropServices.RuntimeInformation")
    };

    /// <summary>
    /// Types the <c>System.Runtime</c> facade does not carry on some releases. A type not listed is carried by
    /// <c>System.Runtime</c> on every release (or, when it is in no facade, is not used by injected code).
    /// </summary>
    private static readonly Dictionary<string, Home[]> FacadeHomes = new(StringComparer.Ordinal)
    {
        ["System.Environment"] = Extensions,
        ["System.BitConverter"] = Extensions,
        ["System.AppDomain"] = Extensions,
        ["System.Convert"] = Extensions,
        ["System.ResolveEventHandler"] = Extensions,
        ["System.IO.MemoryStream"] = Extensions,
        ["System.Diagnostics.Debugger"] = new Home[] { new(4, "System.Diagnostics.Debug", NetCore3Debug) },
        ["System.IO.File"] = new Home[] { new(4, "System.IO.FileSystem", NetCore3FileSystem), new(5, "System.IO.FileSystem") },
        ["System.Resources.ResourceManager"] = new Home[] { new(4, "System.Resources.ResourceManager", NetCore3ResourceManager) },
        ["System.Runtime.InteropServices.RuntimeInformation"] = RuntimeInformationHomes,
        ["System.Runtime.InteropServices.Architecture"] = RuntimeInformationHomes,
        ["System.DllNotFoundException"] = new Home[] { new(int.MaxValue, "System.Runtime.InteropServices") },
        ["System.Runtime.InteropServices.Marshal"] = new Home[] { new(int.MaxValue, "System.Runtime.InteropServices") },
    };

    /// <summary>
    /// The hashing and AES types: outside the core library on every modern release, including when the core
    /// library is <c>System.Private.CoreLib</c> itself. Merged into <c>System.Security.Cryptography</c> in .NET 7.
    /// </summary>
    private static readonly Dictionary<string, Home[]> CryptographyHomes = new(StringComparer.Ordinal)
    {
        ["System.Security.Cryptography.SHA256"] = AlgorithmsThenCryptography(),
        [AesFullName] = AlgorithmsThenCryptography(),
        ["System.Security.Cryptography.ICryptoTransform"] = new Home[]
        {
            new(4, "System.Security.Cryptography.Primitives", NetCore3Primitives),
            new(6, "System.Security.Cryptography.Primitives"),
            new(int.MaxValue, "System.Security.Cryptography")
        }
    };

    private static readonly ConditionalWeakTable<ModuleDef, Dictionary<string, AssemblyRef>> Synthesized = new();

    /// <summary>The full names of the types the tables route, so tests can check every entry against a real framework.</summary>
    internal static IEnumerable<string> RoutedTypes => FacadeHomes.Keys.Concat(CryptographyHomes.Keys);

    private static Home[] AlgorithmsThenCryptography() => new Home[]
    {
        new(4, "System.Security.Cryptography.Algorithms", NetCore3Algorithms),
        new(6, "System.Security.Cryptography.Algorithms"),
        new(int.MaxValue, "System.Security.Cryptography")
    };

    /// <summary>
    /// A reference to the framework type <paramref name="ns"/>.<paramref name="name"/> that resolves at run time
    /// on the framework the module targets.
    /// </summary>
    public static TypeRefUser Type(ModuleDef module, string ns, string name)
    {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentException.ThrowIfNullOrEmpty(name);
        return new TypeRefUser(module, ns, name, Scope(module, ns, name));
    }

    /// <summary>The assembly a reference to <paramref name="ns"/>.<paramref name="name"/> must be scoped to.</summary>
    public static AssemblyRef Scope(ModuleDef module, string ns, string name)
    {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentException.ThrowIfNullOrEmpty(name);

        var corlib = module.CorLibTypes.AssemblyRef;
        var fullName = string.IsNullOrEmpty(ns) ? name : ns + "." + name;
        var major = corlib.Version.Major;

        Home? home = null;
        switch (CorlibName(module))
        {
            case Mscorlib:
                return fullName == AesFullName ? Get(module, "System.Core", null) : corlib;
            case SystemRuntime:
                home = Find(FacadeHomes, fullName, major) ?? Find(CryptographyHomes, fullName, major);
                break;
            case PrivateCoreLib:
                home = Find(CryptographyHomes, fullName, major);
                break;
        }

        return home is null ? corlib : Get(module, home.Assembly, home.Version);
    }

    /// <summary>
    /// <c>Environment.TickCount64</c> exists on .NET Core 3.0 and later, not on .NET Framework, netstandard 2.0 or
    /// earlier .NET Core.
    /// </summary>
    public static bool SupportsTickCount64(ModuleDef module)
    {
        ArgumentNullException.ThrowIfNull(module);
        var corlib = module.CorLibTypes.AssemblyRef;
        return CorlibName(module) switch
        {
            SystemRuntime => corlib.Version.Major >= 5 || corlib.Version >= NetCore3Extensions,
            PrivateCoreLib => corlib.Version.Major >= 5,
            _ => false
        };
    }

    /// <summary><c>Environment.ProcessPath</c> was added in .NET 6; it is not in .NET Framework, netstandard or older .NET.</summary>
    public static bool SupportsProcessPath(ModuleDef module)
    {
        ArgumentNullException.ThrowIfNull(module);
        return CorlibName(module) is SystemRuntime or PrivateCoreLib && module.CorLibTypes.AssemblyRef.Version.Major >= 6;
    }

    /// <summary>
    /// Whether <c>RuntimeInformation</c> is available to the module. .NET Framework has it from 4.7.1 (earlier
    /// versions get it only from a NuGet package the app does not reference); the module's
    /// <c>TargetFrameworkAttribute</c> says which. An unreadable or missing attribute counts as available.
    /// </summary>
    public static bool SupportsRuntimeInformation(ModuleDef module)
    {
        ArgumentNullException.ThrowIfNull(module);
        if (CorlibName(module) != Mscorlib)
            return true;

        var target = module.Assembly?.CustomAttributes
            .FirstOrDefault(a => a.TypeFullName == "System.Runtime.Versioning.TargetFrameworkAttribute")
            ?.ConstructorArguments.FirstOrDefault().Value;
        var text = (target as UTF8String)?.String ?? target as string;
        const string versionMarker = "Version=v";
        var start = text?.IndexOf(versionMarker, StringComparison.Ordinal) ?? -1;
        if (text is null || start < 0 || !text.StartsWith(".NETFramework", StringComparison.Ordinal))
            return true;

        var end = text.IndexOf(',', start);
        var versionText = end < 0 ? text[(start + versionMarker.Length)..] : text[(start + versionMarker.Length)..end];
        return !Version.TryParse(versionText, out var version) || version >= new Version(4, 7, 1);
    }

    private static Home? Find(Dictionary<string, Home[]> table, string fullName, int major)
    {
        if (!table.TryGetValue(fullName, out var homes))
            return null;
        foreach (var home in homes)
        {
            if (major <= home.UpToMajor)
                return home;
        }

        return null;
    }

    private static string CorlibName(ModuleDef module) =>
        UTF8String.ToSystemStringOrEmpty(module.CorLibTypes.AssemblyRef.Name);

    private static AssemblyRef Get(ModuleDef module, string simpleName, Version? version)
    {
        foreach (var existing in module.GetAssemblyRefs())
        {
            if (UTF8String.ToSystemStringOrEmpty(existing.Name) == simpleName)
                return existing;
        }

        var made = Synthesized.GetOrCreateValue(module);
        lock (made)
        {
            if (!made.TryGetValue(simpleName, out var assembly))
            {
                var corlib = module.CorLibTypes.AssemblyRef;
                made[simpleName] = assembly = new AssemblyRefUser(
                    simpleName, version ?? corlib.Version, corlib.PublicKeyOrToken, corlib.Culture);
            }

            return assembly;
        }
    }
}
