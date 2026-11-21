using Autofac;
using dnlib.DotNet;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Obfy.Core.DependencyInjection;
using Obfy.Core.Models;
using Obfy.Core.Services;
using Obfy.Core.Utilities;
using Obfy.Core.Virtualization;
using Shouldly;

namespace Obfy.Tests;

/// <summary>
/// Injected framework type references must resolve at run time against the framework the input targets. An
/// SDK-built app's core library is the <c>System.Runtime</c> facade, which forwards only the types of its own
/// contract: <c>DllNotFoundException</c> and <c>Marshal</c> live in <c>System.Runtime.InteropServices</c>, so a
/// reference to them scoped to the core library loads as a <see cref="TypeLoadException"/>.
/// <c>EndToEndObfuscationTests</c> cannot see that: it compiles against the runtime's implementation assemblies,
/// whose core library is <c>System.Private.CoreLib</c>.
/// </summary>
public class FrameworkReferencesTests
{
    private const string PublicKeyToken = "b03f5f7f11d50a3a";

    private static ModuleDefUser ModuleWithCorlib(string corlibName, Version? version = null)
    {
        var corlib = new AssemblyRefUser(
            corlibName, version ?? new Version(10, 0, 0, 0), new PublicKeyToken(PublicKeyToken), UTF8String.Empty);
        return new ModuleDefUser("Subject", Guid.NewGuid(), corlib);
    }

    [Theory]
    [InlineData("System.Runtime", "System", "DllNotFoundException", "System.Runtime.InteropServices")]
    [InlineData("System.Runtime", "System.Runtime.InteropServices", "Marshal", "System.Runtime.InteropServices")]
    [InlineData("System.Runtime", "System", "EntryPointNotFoundException", "System.Runtime")]
    [InlineData("System.Runtime", "System.IO", "File", "System.Runtime")]
    [InlineData("System.Runtime", "System.Runtime.InteropServices", "RuntimeInformation", "System.Runtime")]
    [InlineData("mscorlib", "System", "DllNotFoundException", "mscorlib")]
    [InlineData("mscorlib", "System.Runtime.InteropServices", "Marshal", "mscorlib")]
    [InlineData("netstandard", "System", "DllNotFoundException", "netstandard")]
    [InlineData("netstandard", "System.Runtime.InteropServices", "Marshal", "netstandard")]
    [InlineData("System.Private.CoreLib", "System", "DllNotFoundException", "System.Private.CoreLib")]
    [InlineData("System.Private.CoreLib", "System.Runtime.InteropServices", "Marshal", "System.Private.CoreLib")]
    public void Type_ScopesTheReferenceToTheAssemblyThatCarriesTheType(
        string corlib, string ns, string name, string expectedScope)
    {
        var module = ModuleWithCorlib(corlib);

        var type = FrameworkReferences.Type(module, ns, name);

        type.Namespace.String.ShouldBe(ns);
        type.Name.String.ShouldBe(name);
        type.Module.ShouldBe(module);
        var scope = type.ResolutionScope.ShouldBeAssignableTo<AssemblyRef>()!;
        scope.Name.String.ShouldBe(expectedScope);
    }

    [Fact]
    public void Type_SynthesizedScope_TakesTheCoreLibraryVersionAndPublicKeyToken()
    {
        var module = ModuleWithCorlib("System.Runtime", new Version(8, 0, 0, 0));

        var scope = (AssemblyRef)FrameworkReferences.Type(module, "System", "DllNotFoundException").ResolutionScope;

        scope.Version.ShouldBe(new Version(8, 0, 0, 0));
        scope.PublicKeyOrToken.Token.ToString().ShouldBe(PublicKeyToken);
    }

    [Fact]
    public void Type_ReusesAReferenceTheModuleAlreadyHas()
    {
        // This test assembly is SDK-built and calls RuntimeEnvironment, so it references System.Runtime.InteropServices.
        using var module = ModuleDefMD.Load(typeof(FrameworkReferencesTests).Assembly.Location);
        var existing = module.GetAssemblyRefs().SingleOrDefault(a => a.Name == "System.Runtime.InteropServices");
        existing.ShouldNotBeNull();

        var scope = (AssemblyRef)FrameworkReferences.Type(module, "System", "DllNotFoundException").ResolutionScope;

        scope.ShouldBeSameAs(existing);
    }

    /// <summary>
    /// The routing table is data about the real frameworks, so check it against the one this test runs on:
    /// the assembly a reference is routed to must carry the type, and the facade must not (that is why it is routed).
    /// net8.0 and net9.0 were checked against their shared frameworks and reference packs when the table was written.
    /// </summary>
    [Theory]
    [InlineData("System", "DllNotFoundException")]
    [InlineData("System.Runtime.InteropServices", "Marshal")]
    public void Type_RoutedAssembly_CarriesTheTypeOnTheRunningFramework(string ns, string name)
    {
        var module = ModuleWithCorlib("System.Runtime", new Version(Environment.Version.Major, 0, 0, 0));
        var scope = (AssemblyRef)FrameworkReferences.Type(module, ns, name).ResolutionScope;
        scope.Name.String.ShouldNotBe("System.Runtime");

        RuntimeFacades.Carries(scope.Name.String, ns + "." + name)
            .ShouldBeTrue($"{ns}.{name} should resolve through {scope.Name}");
        RuntimeFacades.Carries("System.Runtime", ns + "." + name)
            .ShouldBeFalse($"System.Runtime now carries {ns}.{name}; the routing entry is no longer needed");
    }

    /// <summary>
    /// The virtualization runtime is a netstandard2.0 assembly whose core-library references are retargeted to the
    /// module's core library. That is only right while every type it names is carried by the <c>System.Runtime</c>
    /// facade; a type outside it needs the routing in <see cref="FrameworkReferences"/> (see the table there).
    /// </summary>
    [Fact]
    public void EmbeddedVmRuntime_NamesOnlyTypesTheSystemRuntimeFacadeCarries()
    {
        using var stream = typeof(VmImporter).Assembly.GetManifestResourceStream(VmImporter.EmbeddedName);
        stream.ShouldNotBeNull();
        using var buffer = new MemoryStream();
        stream!.CopyTo(buffer);
        using var vm = ModuleDefMD.Load(buffer.ToArray());

        var missing = vm.GetTypeRefs()
            .Where(t => t.ResolutionScope is AssemblyRef { Name.String: "netstandard" or "System.Runtime" or "mscorlib" })
            .Select(t => t.FullName)
            .Where(fullName => !RuntimeFacades.Carries("System.Runtime", fullName))
            .Distinct()
            .OrderBy(fullName => fullName, StringComparer.Ordinal)
            .ToList();

        missing.ShouldBeEmpty("The VM runtime names types the System.Runtime facade does not carry: " + string.Join(", ", missing));
    }

    /// <summary>
    /// Protects a real SDK-built assembly (Settings.Core: core library System.Runtime) with every technique that
    /// injects code, then checks each type reference in the result that points at a shared-framework assembly
    /// against that assembly. This covers every injection site at once, including ones added later.
    /// </summary>
    [Fact]
    public async Task SdkBuiltAssembly_AfterProtection_ReferencesOnlyFrameworkTypesThatResolve()
    {
        var input = typeof(Settings.Core.SettingsServiceBase<>).Assembly.Location;
        var directory = Path.Combine(Path.GetTempPath(), "obfy-fwref-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            using (var original = ModuleDefMD.Load(File.ReadAllBytes(input)))
            {
                original.CorLibTypes.AssemblyRef.Name.String.ShouldBe(
                    "System.Runtime", "the input must be built the way an SDK app is");
            }

            var settings = new ObfySettings { Level = ObfuscationLevel.Aggressive };
            settings.Watermark.Enabled = true;
            settings.Watermark.Id = "framework-references";
            settings.Virtualization.Enabled = true;

            var output = Path.Combine(directory, "Settings.Core.dll");
            var builder = new ContainerBuilder();
            builder.RegisterGeneric(typeof(NullLogger<>)).As(typeof(ILogger<>)).SingleInstance();
            builder.RegisterModule<ObfuscationModule>();
            await using (var container = builder.Build())
            {
                var result = await container.Resolve<IObfuscationService>().ObfuscateAsync(input, output, settings);
                result.Success.ShouldBeTrue(result.ErrorMessage);
            }

            using var protectedModule = ModuleDefMD.Load(File.ReadAllBytes(output));
            var unresolved = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var typeRef in protectedModule.GetTypeRefs())
            {
                var outermost = typeRef;
                while (outermost.ResolutionScope is TypeRef declaring)
                    outermost = declaring;
                if (outermost.ResolutionScope is not AssemblyRef assembly)
                    continue;
                if (!RuntimeFacades.Exists(assembly.Name.String))
                    continue;

                if (!RuntimeFacades.Carries(assembly.Name.String, outermost.FullName))
                    unresolved.Add($"{outermost.FullName} (scoped to {assembly.Name})");
            }

            unresolved.ShouldBeEmpty(
                "These framework types do not resolve through the assembly they are scoped to, so the protected app " +
                "fails with a TypeLoadException: " + string.Join("; ", unresolved));
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch { /* ignore */ }
        }
    }

    /// <summary>The shared framework this test process runs on, read as metadata (nothing is loaded).</summary>
    private static class RuntimeFacades
    {
        private static readonly string Directory = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory();
        private static readonly Dictionary<string, HashSet<string>> Cache = new(StringComparer.OrdinalIgnoreCase);

        public static bool Exists(string assemblyName) =>
            File.Exists(Path.Combine(Directory, assemblyName + ".dll"));

        /// <summary>True when the assembly defines the type, or forwards it to another assembly.</summary>
        public static bool Carries(string assemblyName, string fullName)
        {
            lock (Cache)
            {
                if (!Cache.TryGetValue(assemblyName, out var names))
                {
                    using var module = ModuleDefMD.Load(Path.Combine(Directory, assemblyName + ".dll"));
                    names = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var type in module.Types)
                        names.Add(type.FullName);
                    foreach (var exported in module.ExportedTypes)
                        names.Add(exported.FullName);
                    Cache[assemblyName] = names;
                }

                return names.Contains(fullName);
            }
        }
    }
}
