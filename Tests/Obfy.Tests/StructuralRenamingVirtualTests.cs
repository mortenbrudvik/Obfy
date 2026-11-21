using System.Reflection;
using System.Runtime.Loader;
using Autofac;
using dnlib.DotNet;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Obfy.Core.DependencyInjection;
using Obfy.Core.Models;
using Obfy.Core.Services;
using Shouldly;

namespace Obfy.Tests;

/// <summary>
/// Symbol renaming must never rename a virtual method (or a property/event accessor) that
/// overrides, implements, or can be overridden: the base keeps its name, so a renamed override
/// silently becomes a new vtable slot and dispatch through the base type reaches the wrong code.
/// Each test compiles real C#, obfuscates with symbol renaming only, loads the result in a
/// collectible load context, and dispatches through the base type or interface.
/// </summary>
public class StructuralRenamingVirtualTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"obfy-rename-virt-{Guid.NewGuid():N}");

    public StructuralRenamingVirtualTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_dir))
                Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    [Fact]
    public async Task StructToStringOverride_StillDispatchesThroughObject()
    {
        const string source = """
            public struct Pt { public override string ToString() => "pt-override"; }
            public static class Probe
            {
                public static string Run() { object o = new Pt(); return o.ToString(); }
            }
            """;

        var output = await Obfuscate(source, "StructToString");

        AssertTypeRenamed(output, "Pt");
        Invoke(output, "Run").ShouldBe("pt-override");
        AllMethodNames(output).ShouldContain("ToString");
    }

    [Fact]
    public async Task SealedClassToStringOverride_StillDispatchesThroughObject()
    {
        const string source = """
            public sealed class Widget { public override string ToString() => "widget-override"; }
            public static class Probe
            {
                public static string Run() { object o = new Widget(); return o.ToString(); }
            }
            """;

        var output = await Obfuscate(source, "SealedToString");

        AssertTypeRenamed(output, "Widget");
        Invoke(output, "Run").ShouldBe("widget-override");
    }

    [Fact]
    public async Task SealedClassOverridingInModuleBase_StillDispatchesThroughBase()
    {
        const string source = """
            public abstract class Shape
            {
                public abstract string Name();
                public virtual string Describe() => "shape";
            }
            public sealed class Circle : Shape
            {
                public override string Name() => "circle";
                public override string Describe() => "round";
            }
            public static class Probe
            {
                public static string Run() { Shape s = new Circle(); return s.Name() + "/" + s.Describe(); }
            }
            """;

        var output = await Obfuscate(source, "SealedOverrideBase");

        AssertTypeRenamed(output, "Circle");
        Invoke(output, "Run").ShouldBe("circle/round");
    }

    [Fact]
    public async Task SealedClassPropertyOverrides_StillDispatchThroughBase()
    {
        const string source = """
            public abstract class Base
            {
                public abstract string Label { get; }
                public virtual int Count { get; set; }
            }
            public sealed class Impl : Base
            {
                private int _count;
                public override string Label => "impl-label";
                public override int Count { get => _count + 40; set => _count = value; }
            }
            public static class Probe
            {
                public static string Run()
                {
                    Base b = new Impl();
                    b.Count = 2;
                    return b.Label + "/" + b.Count;
                }
            }
            """;

        var output = await Obfuscate(source, "SealedPropertyOverride");

        AssertTypeRenamed(output, "Impl");
        Invoke(output, "Run").ShouldBe("impl-label/42");
    }

    [Fact]
    public async Task SealedClassEventOverride_StillDispatchesThroughBase()
    {
        const string source = """
            using System;
            public abstract class Base { public abstract event EventHandler Changed; }
            public sealed class Impl : Base
            {
                public static int Adds;
                public override event EventHandler Changed { add { Adds++; } remove { } }
            }
            public static class Probe
            {
                public static string Run()
                {
                    Base b = new Impl();
                    b.Changed += (s, e) => { };
                    return Impl.Adds.ToString();
                }
            }
            """;

        var output = await Obfuscate(source, "SealedEventOverride");

        AssertTypeRenamed(output, "Impl");
        Invoke(output, "Run").ShouldBe("1");
    }

    [Fact]
    public async Task InternalAndPrivateProtectedVirtualOverrides_StayConsistentWithBase()
    {
        const string source = """
            public abstract class Base
            {
                internal abstract string Tag();
                internal virtual string Tag2() => "base2";
                private protected virtual string Tag3() => "base3";
                public string CallTag3() => Tag3();
            }
            public class Derived : Base
            {
                internal override string Tag() => "d1";
                internal override string Tag2() => "d2";
                private protected override string Tag3() => "d3";
            }
            public static class Probe
            {
                public static string Run()
                {
                    Base b = new Derived();
                    return b.Tag() + "/" + b.Tag2() + "/" + b.CallTag3();
                }
            }
            """;

        var output = await Obfuscate(source, "InternalVirtual");

        AssertTypeRenamed(output, "Derived");
        Invoke(output, "Run").ShouldBe("d1/d2/d3");
    }

    [Fact]
    public async Task ProtectedAndInternalOverridesInSealedClass_WithPreservePublicApi_StillDispatch()
    {
        const string source = """
            public abstract class Base
            {
                protected abstract string Core();
                internal abstract string Hidden();
                public string Run() => Core() + "/" + Hidden();
            }
            public sealed class Impl : Base
            {
                protected override string Core() => "core";
                internal override string Hidden() => "hidden";
            }
            public static class Probe
            {
                public static string Run() => new Impl().Run();
            }
            """;

        var output = await Obfuscate(source, "PreservePublicVirtual", preservePublicApi: true);

        Invoke(output, "Run").ShouldBe("core/hidden");
    }

    [Fact]
    public async Task SealedClassImplementingInheritedInterface_StillDispatches()
    {
        const string source = """
            public interface IBase { string M(); }
            public interface IDerived : IBase { }
            public sealed class Impl : IDerived { public string M() => "m"; }
            public static class Probe
            {
                public static string Run() { IBase b = new Impl(); return b.M(); }
            }
            """;

        var output = await Obfuscate(source, "InheritedInterface");

        AssertTypeRenamed(output, "Impl");
        Invoke(output, "Run").ShouldBe("m");
    }

    [Fact]
    public async Task SealedClassAndStructImplementingGenericInterfaces_StillDispatch()
    {
        const string source = """
            using System;
            public sealed class Key : IEquatable<Key>
            {
                public bool Equals(Key other) => other != null;
            }
            public struct Num : IComparable<Num>
            {
                public int V;
                public int CompareTo(Num other) => V.CompareTo(other.V);
            }
            public static class Probe
            {
                public static string Run()
                {
                    IEquatable<Key> k = new Key();
                    IComparable<Num> n = new Num { V = 1 };
                    return k.Equals(new Key()) + "/" + n.CompareTo(new Num { V = 3 });
                }
            }
            """;

        var output = await Obfuscate(source, "GenericInterfaces");

        AssertTypeRenamed(output, "Key");
        Invoke(output, "Run").ShouldBe("True/-1");
    }

    [Fact]
    public async Task ClassImplementingInterfaceThroughInheritedBaseMethod_StillDispatches()
    {
        const string source = """
            internal interface IFoo { string M(); }
            internal class BaseImpl { public string M() => "base-m"; }
            internal sealed class Derived : BaseImpl, IFoo { }
            public static class Probe
            {
                public static string Run() { IFoo f = new Derived(); return f.M(); }
            }
            """;

        var output = await Obfuscate(source, "BaseMethodImplementsInterface");

        AssertTypeRenamed(output, "Derived");
        Invoke(output, "Run").ShouldBe("base-m");
    }

    [Fact]
    public async Task DelegateTypeAndFinalizer_KeepRuntimeRequiredNames()
    {
        const string source = """
            using System;
            public delegate int Binary(int a, int b);
            public sealed class Holder
            {
                public static int Finalized;
                ~Holder() { Finalized++; }
            }
            public static class Probe
            {
                public static string Run()
                {
                    Binary add = (a, b) => a + b;
                    return add(2, 3).ToString();
                }
            }
            """;

        var output = await Obfuscate(source, "DelegateFinalizer");

        AssertTypeRenamed(output, "Holder");
        Invoke(output, "Run").ShouldBe("5");
        var names = AllMethodNames(output);
        names.ShouldContain("Finalize");
        names.ShouldContain("Invoke");
    }

    [Fact]
    public async Task ClosedSet_StructAndSealedClassOverrides_StillDispatchThroughObject()
    {
        var lib = Compile("ClosedLib", """
            namespace ClosedLib;
            public struct Pt { public override string ToString() => "pt-override"; }
            public sealed class Widget { public override string ToString() => "widget-override"; }
            public abstract class Shape { public abstract string Name(); }
            public sealed class Circle : Shape { public override string Name() => "circle"; }
            """, OutputKind.DynamicallyLinkedLibrary);
        var app = Compile("ClosedApp", """
            using ClosedLib;
            public static class Program
            {
                public static void Main() { }
                public static string Probe()
                {
                    object p = new Pt();
                    object w = new Widget();
                    Shape s = new Circle();
                    return p.ToString() + "/" + w.ToString() + "/" + s.Name();
                }
            }
            """, OutputKind.ConsoleApplication, lib);
        var outDir = Path.Combine(_dir, "closed-out");

        using var container = BuildContainer();
        var service = container.Resolve<IObfuscationService>();
        var results = await service.ObfuscateBatchAsync([lib, app], outDir, RenameOnlySettings(preservePublicApi: false));

        results.ShouldAllBe(r => r.Success, string.Join("; ", results.Select(r => r.ErrorMessage)));
        var outLib = Path.Combine(outDir, "ClosedLib.dll");
        var outApp = Path.Combine(outDir, "ClosedApp.exe");
        using (var module = ModuleDefMD.Load(outLib))
        {
            module.GetTypes().ShouldNotContain(t => t.Name == "Pt");
            module.GetTypes().ShouldNotContain(t => t.Name == "Circle");
        }

        var alc = new AssemblyLoadContext($"closed-{Guid.NewGuid():N}", isCollectible: true);
        try
        {
            alc.LoadFromAssemblyPath(outLib);
            var asm = alc.LoadFromAssemblyPath(outApp);
            var program = asm.GetTypes().Single(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Any(m => m.ReturnType == typeof(string) && m.GetParameters().Length == 0));
            var probe = program.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Single(m => m.ReturnType == typeof(string) && m.GetParameters().Length == 0);
            probe.Invoke(null, null).ShouldBe("pt-override/widget-override/circle");
        }
        finally
        {
            alc.Unload();
        }
    }

    private async Task<string> Obfuscate(string source, string name, bool preservePublicApi = false)
    {
        var input = Compile(name, source, OutputKind.DynamicallyLinkedLibrary);
        var output = Path.Combine(_dir, name + ".obf.dll");
        var settings = RenameOnlySettings(preservePublicApi);
        settings.Exclusions.Types.Add("Probe");

        using var container = BuildContainer();
        var service = container.Resolve<IObfuscationService>();
        var result = await service.ObfuscateAsync(input, output, settings);
        result.Success.ShouldBeTrue(result.ErrorMessage);
        return output;
    }

    private static ObfySettings RenameOnlySettings(bool preservePublicApi) => new()
    {
        Level = ObfuscationLevel.Custom,
        StringEncryption = { Enabled = false },
        ControlFlow = { Enabled = false },
        ConstantEncryption = { Enabled = false },
        ResourceEncryption = { Enabled = false },
        SymbolRenaming =
        {
            Enabled = true,
            Mode = NamingMode.Sequential,
            PreservePublicApi = preservePublicApi,
            RenameTypes = true,
            RenameMethods = true,
            RenameProperties = true,
            RenameEvents = true,
            RenameParameters = true
        },
        Metadata = { RemoveDebugInfo = false, RemoveAttributes = false, StripDocumentation = false },
        Protection =
        {
            AntiDebug = false,
            AntiTamper = { Enabled = false },
            AntiDecompiler = { Enabled = false },
            AntiDump = false,
            ReferenceProxy = false,
            MethodEncryption = false
        }
    };

    private static IContainer BuildContainer()
    {
        var builder = new ContainerBuilder();
        builder.RegisterGeneric(typeof(NullLogger<>)).As(typeof(ILogger<>)).SingleInstance();
        builder.RegisterModule<ObfuscationModule>();
        return builder.Build();
    }

    private string Compile(string assemblyName, string source, OutputKind kind, string? referencePath = null)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(p => p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p));
        if (referencePath != null)
            references = references.Append(MetadataReference.CreateFromFile(referencePath));

        var compilation = CSharpCompilation.Create(
            assemblyName,
            [tree],
            references,
            new CSharpCompilationOptions(kind));

        var extension = kind == OutputKind.ConsoleApplication ? ".exe" : ".dll";
        var path = Path.Combine(_dir, assemblyName + extension);
        var emit = compilation.Emit(path);
        emit.Success.ShouldBeTrue(string.Join("\n", emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        return path;
    }

    private static string? Invoke(string assemblyPath, string methodName)
    {
        var alc = new AssemblyLoadContext($"rt-{Guid.NewGuid():N}", isCollectible: true);
        try
        {
            var asm = alc.LoadFromAssemblyPath(assemblyPath);
            var type = asm.GetType("Probe");
            type.ShouldNotBeNull();
            var method = type!.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static);
            method.ShouldNotBeNull();
            return (string?)method!.Invoke(null, null);
        }
        finally
        {
            alc.Unload();
        }
    }

    private static void AssertTypeRenamed(string assemblyPath, string originalName)
    {
        using var module = ModuleDefMD.Load(assemblyPath);
        module.GetTypes().ShouldNotContain(t => t.Name == originalName, $"{originalName} should have been renamed");
    }

    private static List<string> AllMethodNames(string assemblyPath)
    {
        using var module = ModuleDefMD.Load(assemblyPath);
        return module.GetTypes().SelectMany(t => t.Methods).Select(m => m.Name.String).ToList();
    }
}
