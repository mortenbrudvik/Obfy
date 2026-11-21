using dnlib.DotNet;

namespace Obfy.Tests;

/// <summary>
/// A target framework a protected app can run on, as far as these tests can see it on this machine: the reference
/// assemblies an SDK build compiles against, and the assemblies the runtime resolves references through.
/// </summary>
internal sealed class TargetFramework
{
    private TargetFramework(string name, string frameworkName, string[] referenceFiles, string[] runtimeDirectories)
    {
        Name = name;
        FrameworkName = frameworkName;
        ReferenceFiles = referenceFiles;
        RuntimeDirectories = runtimeDirectories;
    }

    /// <summary>The short target framework moniker, e.g. <c>net6.0</c>.</summary>
    public string Name { get; }

    /// <summary>The value of the <c>TargetFrameworkAttribute</c> an SDK build gives an app for this target.</summary>
    public string FrameworkName { get; }

    /// <summary>The reference assemblies an input for this target is compiled against.</summary>
    public string[] ReferenceFiles { get; }

    /// <summary>Directories searched, in order, for the assemblies a reference to this target resolves through.</summary>
    public string[] RuntimeDirectories { get; }

    public override string ToString() => Name;

    /// <summary>
    /// The targets Obfy documents as supported (docs/Competitive-Analysis.md, "Platform and framework support"):
    /// .NET Core 3.x, .NET 6 to 10 (and 5, between them), .NET Framework 4.x (4.6.1 is the oldest a netstandard 2.0
    /// library can run on, 4.8 the newest) and .NET Standard. A netstandard library is checked on the runtimes that host it
    /// (<c>netstandard2.0@host</c>).
    /// </summary>
    public static readonly string[] Documented =
    {
        "netcoreapp3.1", "net5.0", "net6.0", "net7.0", "net8.0", "net9.0", "net10.0", "net461", "net48",
        "netstandard2.0@netcoreapp3.1", "netstandard2.0@net6.0", "netstandard2.0@net10.0", "netstandard2.0@net48"
    };

    /// <summary>The target, or null when this machine does not have what is needed to check it.</summary>
    public static TargetFramework? Find(string name)
    {
        var dotnetRoot = Path.GetFullPath(Path.Combine(
            System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", ".."));

        switch (name)
        {
            case "net48":
            case "net461":
            {
                var version = name == "net48" ? "4.8" : "4.6.1";
                var root = FrameworkReferenceDirectory(version);
                if (root is null)
                    return null;
                var files = new[] { "mscorlib.dll", "System.dll", "System.Core.dll" }.Select(f => Path.Combine(root, f)).ToArray();
                return new TargetFramework(
                    name, ".NETFramework,Version=v" + version, files, new[] { root, Path.Combine(root, "Facades") });
            }
        }

        if (name.StartsWith("netstandard2.0@", StringComparison.Ordinal))
        {
            var host = Find(name["netstandard2.0@".Length..]);
            var contract = Path.Combine(NuGetPackages(), "netstandard.library", "2.0.3", "build", "netstandard2.0", "ref");
            return host is null || !Directory.Exists(contract)
                ? null
                : new TargetFramework(
                    name, ".NETStandard,Version=v2.0", new[] { Path.Combine(contract, "netstandard.dll") }, host.RuntimeDirectories);
        }

        var moniker = System.Text.RegularExpressions.Regex.Match(name, @"^(?:netcoreapp(\d+\.\d+)|net(\d+)\.0)$");
        if (!moniker.Success)
            throw new ArgumentException("Unknown target framework " + name, nameof(name));
        var versionPrefix = (moniker.Groups[1].Success ? moniker.Groups[1].Value : moniker.Groups[2].Value) + ".";

        var shared = Highest(Path.Combine(dotnetRoot, "shared", "Microsoft.NETCore.App"), versionPrefix);
        string? reference;
        if (name == "netcoreapp3.1")
        {
            reference = Path.Combine(NuGetPackages(), "microsoft.netcore.app.ref", "3.1.0", "ref", "netcoreapp3.1");
        }
        else
        {
            var pack = Highest(Path.Combine(dotnetRoot, "packs", "Microsoft.NETCore.App.Ref"), versionPrefix);
            reference = pack is null ? null : Path.Combine(pack, "ref", name);
        }

        if (shared is null || reference is null || !Directory.Exists(reference))
            return null;
        return new TargetFramework(
            name, ".NETCoreApp,Version=v" + versionPrefix.TrimEnd('.'), Directory.GetFiles(reference, "*.dll"), new[] { shared });
    }

    /// <summary>
    /// The reference assemblies of a .NET Framework version: the targeting pack when it ships assemblies (the older
    /// ones are IntelliSense files only), else the Microsoft.NETFramework.ReferenceAssemblies package the SDK restores.
    /// </summary>
    private static string? FrameworkReferenceDirectory(string version)
    {
        var pack = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "Reference Assemblies", "Microsoft", "Framework", ".NETFramework", "v" + version);
        if (File.Exists(Path.Combine(pack, "mscorlib.dll")))
            return pack;

        var package = Path.Combine(
            NuGetPackages(), "microsoft.netframework.referenceassemblies.net" + version.Replace(".", ""));
        if (!Directory.Exists(package))
            return null;
        return Directory.GetDirectories(package)
            .Select(d => Path.Combine(d, "build", ".NETFramework", "v" + version))
            .FirstOrDefault(d => File.Exists(Path.Combine(d, "mscorlib.dll")));
    }

    private static string NuGetPackages() =>
        Environment.GetEnvironmentVariable("NUGET_PACKAGES") is { Length: > 0 } custom
            ? custom
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");

    private static string? Highest(string parent, string versionPrefix)
    {
        if (!Directory.Exists(parent))
            return null;
        return Directory.GetDirectories(parent)
            .Where(d => Path.GetFileName(d).StartsWith(versionPrefix, StringComparison.Ordinal))
            .Select(d => (Dir: d, Version: Version.TryParse(Path.GetFileName(d).Split('-')[0], out var v) ? v : null))
            .Where(t => t.Version is not null)
            .OrderByDescending(t => t.Version)
            .Select(t => t.Dir)
            .FirstOrDefault();
    }
}

/// <summary>
/// What a target's runtime resolves a reference through: the assemblies of one framework, read as metadata
/// (nothing is loaded). A type reference resolves when the assembly it is scoped to defines the type or forwards it
/// to an assembly that does; a member reference resolves when that type, or one of its bases, has a member of the
/// same name and signature.
/// </summary>
internal sealed class FacadeSet : IDisposable
{
    private readonly string[] _directories;
    private readonly Dictionary<string, ModuleDefMD?> _modules = new(StringComparer.OrdinalIgnoreCase);

    public FacadeSet(IEnumerable<string> directories) => _directories = directories.ToArray();

    public void Dispose()
    {
        foreach (var module in _modules.Values)
            module?.Dispose();
        _modules.Clear();
    }

    public bool HasAssembly(string assemblyName) => Load(assemblyName) is not null;

    /// <summary>The version of the assembly the target has under that name, or null.</summary>
    public Version? AssemblyVersion(string assemblyName) => Load(assemblyName)?.Assembly?.Version;

    /// <summary>The type the scoped assembly carries (defines or forwards, to any depth), or null.</summary>
    public TypeDef? ResolveType(string assemblyName, string fullName)
    {
        for (var hops = 0; hops < 8; hops++)
        {
            var module = Load(assemblyName);
            if (module is null)
                return null;

            var slash = fullName.IndexOf('/');
            var outer = slash < 0 ? fullName : fullName[..slash];
            var type = module.Find(outer, isReflectionName: false);
            if (type is not null)
            {
                if (slash < 0)
                    return type;
                foreach (var part in fullName[(slash + 1)..].Split('/'))
                {
                    type = type.NestedTypes.FirstOrDefault(n => n.Name == part);
                    if (type is null)
                        return null;
                }

                return type;
            }

            var forward = module.ExportedTypes.FirstOrDefault(e => e.FullName == outer);
            if (forward?.Implementation is not AssemblyRef target)
                return null;
            assemblyName = target.Name.String;
        }

        return null;
    }

    /// <summary>
    /// Problems with the framework references of a protected module: a type or member the target does not have
    /// where the reference points. References to <paramref name="ownAssemblies"/> (the app's own siblings) are skipped.
    /// </summary>
    public IReadOnlyList<string> Unresolved(ModuleDefMD module, IReadOnlySet<string> ownAssemblies)
    {
        var problems = new SortedSet<string>(StringComparer.Ordinal);

        // A reference binds to an assembly of the same name and public key token whose version is not lower.
        foreach (var reference in module.GetAssemblyRefs())
        {
            var simpleName = reference.Name.String;
            var target = ownAssemblies.Contains(simpleName) ? null : Load(simpleName);
            if (target?.Assembly is not { } assembly)
                continue;

            if (assembly.Version < reference.Version)
                problems.Add($"assembly {simpleName} is referenced as {reference.Version} but the target has {assembly.Version}");
            var referenced = reference.PublicKeyOrToken?.Token?.ToString();
            var available = assembly.PublicKeyToken?.ToString();
            if (referenced != available)
                problems.Add($"assembly {simpleName} is referenced with token {referenced} but the target's is {available}");
        }

        foreach (var typeRef in module.GetTypeRefs())
        {
            var (assembly, fullName) = Locate(typeRef);
            if (assembly is null || ownAssemblies.Contains(assembly))
                continue;

            if (ResolveType(assembly, fullName) is null)
                problems.Add($"type {fullName} is not carried by {assembly}{(HasAssembly(assembly) ? "" : " (assembly not in the target)")}");
        }

        foreach (var memberRef in module.GetMemberRefs())
        {
            var owner = memberRef.Class switch
            {
                TypeRef t => t,
                TypeSpec { TypeSig: GenericInstSig { GenericType.TypeDefOrRef: TypeRef t } } => t,
                _ => null
            };
            if (owner is null)
                continue;

            var (assembly, fullName) = Locate(owner);
            if (assembly is null || ownAssemblies.Contains(assembly))
                continue;

            var declaring = ResolveType(assembly, fullName);
            if (declaring is null)
                continue; // already reported as a type problem

            if (!HasMember(declaring, memberRef))
                problems.Add($"member {fullName}::{memberRef.Name} {Describe(memberRef)} is not on the target's {assembly}");
        }

        return problems.ToList();
    }

    private bool HasMember(TypeDef type, MemberRef reference)
    {
        for (var current = type; current is not null; current = BaseOf(current))
        {
            if (reference.IsMethodRef)
            {
                var wanted = Describe(reference.MethodSig);
                if (current.Methods.Any(m => m.Name == reference.Name && Describe(m.MethodSig) == wanted))
                    return true;
            }
            else if (current.Fields.Any(f => f.Name == reference.Name))
            {
                return true;
            }

            if (current.IsInterface)
                break;
        }

        return false;
    }

    private TypeDef? BaseOf(TypeDef type) => type.BaseType switch
    {
        TypeRef r => Locate(r) is ({ } assembly, var full) ? ResolveType(assembly, full) : null,
        TypeDef d => d,
        _ => null
    };

    private static (string? Assembly, string FullName) Locate(TypeRef typeRef)
    {
        var outermost = typeRef;
        while (outermost.ResolutionScope is TypeRef declaring)
            outermost = declaring;
        var assembly = (outermost.ResolutionScope as AssemblyRef)?.Name.String;
        return (assembly, typeRef.FullName);
    }

    private static string Describe(MemberRef reference) =>
        reference.IsMethodRef ? Describe(reference.MethodSig) : reference.FieldSig?.Type.FullName ?? "";

    private static string Describe(MethodSig? sig) =>
        sig is null
            ? ""
            : "(" + string.Join(", ", sig.Params.Select(Name)) + ") -> " + Name(sig.RetType);

    /// <summary>A signature type by name, with generic parameters by position, so a reference and a definition compare.</summary>
    private static string Name(TypeSig? sig) => sig switch
    {
        null => "",
        GenericMVar m => "!!" + m.Number,
        GenericVar v => "!" + v.Number,
        ModifierSig modifier => Name(modifier.Next),
        PinnedSig pinned => Name(pinned.Next),
        SZArraySig array => Name(array.Next) + "[]",
        ByRefSig byRef => Name(byRef.Next) + "&",
        PtrSig pointer => Name(pointer.Next) + "*",
        GenericInstSig generic => Name(generic.GenericType) + "<" + string.Join(",", generic.GenericArguments.Select(Name)) + ">",
        TypeDefOrRefSig named => named.TypeDefOrRef.FullName,
        _ => sig.FullName
    };

    private ModuleDefMD? Load(string assemblyName)
    {
        if (_modules.TryGetValue(assemblyName, out var cached))
            return cached;

        ModuleDefMD? module = null;
        foreach (var directory in _directories)
        {
            var path = Path.Combine(directory, assemblyName + ".dll");
            if (!File.Exists(path))
                continue;
            module = ModuleDefMD.Load(path);
            break;
        }

        _modules[assemblyName] = module;
        return module;
    }
}
