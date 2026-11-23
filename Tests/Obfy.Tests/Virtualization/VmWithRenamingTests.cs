using System.Reflection;
using System.Runtime.Loader;
using Autofac;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Obfy.Core.DependencyInjection;
using Obfy.Core.Models;
using Obfy.Core.Models.Solution;
using Obfy.Core.Services;
using Shouldly;

namespace Obfy.Tests.Virtualization;

/// <summary>
/// Virtualization together with symbol renaming. The interpreter is copied into the module before the
/// renamer runs and is renamed with everything else (<c>PreservePublicApi</c> off), and the members the
/// bytecode names are renamed after the bytecode was encoded. Whatever the VM or its member tables refer
/// to must therefore survive that rename: by definition token or handle, never by a name the renamer
/// then changes. The existing full-pipeline tests keep public names, which hides the whole class of bug
/// (the interpreter's own public fields are then left alone).
/// </summary>
public class VmWithRenamingTests
{
    /// <summary>
    /// <c>Driver</c> keeps its names (excluded from renaming only, it is still virtualized) so the test
    /// can find it. <c>Counter</c> and its nested
    /// <c>Tag</c> are renamed, so every member the virtualized code touches changes name after encoding.
    /// </summary>
    private const string Source = """
        public class Counter
        {
            public int Value;
            public static int Total;

            public Counter() { }
            public Counter(int seed) { Value = seed; }

            public int Read() => Value;
            public void Bump(int by) { Value = Value + by; }
            public static void Add(int n) { Total = Total + n; }
            public static int Double(int n) => n * 2;

            public class Tag
            {
                public int Weight;
                public static int Scale = 3;
                public int Scaled() => Weight * Scale;
            }
        }

        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = true)]
        public static class Driver
        {
            public static int InstanceField(int n)
            {
                var c = new Counter();
                c.Value = n;
                c.Value = c.Value + 1;
                return c.Value;
            }

            public static int StaticField(int n)
            {
                Counter.Total = n;
                Counter.Total = Counter.Total + 5;
                return Counter.Total;
            }

            public static int InstanceMethod(int n)
            {
                var c = new Counter();
                c.Bump(n);
                c.Bump(n);
                return c.Read();
            }

            public static int StaticMethod(int n)
            {
                Counter.Total = 0;
                Counter.Add(n);
                Counter.Add(2);
                return Counter.Double(Counter.Total);
            }

            public static int Construct(int seed) => new Counter(seed).Read() + 1;

            public static int NestedType(int n)
            {
                var tag = new Counter.Tag();
                tag.Weight = n;
                Counter.Tag.Scale = Counter.Tag.Scale + 1;
                return tag.Scaled();
            }

            public static int TypeTable(int n)
            {
                object o = new Counter(n);
                var c = (Counter)o;
                var isCounter = o is Counter;
                var array = new Counter[2];
                array[0] = c;
                return array[0].Read() + (isCounter ? 1 : 0) + array.Length;
            }

            public static Counter Echo(Counter counter) => counter;

            public static int ThroughEcho(int n) => Echo(new Counter(n)).Read();

            public static string Text(int n) => "n=" + n.ToString();
        }
        """;

    private static readonly string[] DriverMethods =
    {
        "InstanceField", "StaticField", "InstanceMethod", "StaticMethod", "Construct", "NestedType",
        "TypeTable", "Echo", "ThroughEcho", "Text"
    };

    public static TheoryData<string> Combinations => new()
    {
        "renaming",
        "reference-proxy",
        "control-flow",
        "metadata-removal",
        "string-encryption",
        "sequential-names"
    };

    [Theory]
    [MemberData(nameof(Combinations))]
    public async Task VirtualizedCode_RunsAfterSymbolRenaming(string combination)
    {
        var dir = NewTempDir();
        try
        {
            var input = Compile(Source, dir, "VmRenameLib");
            var output = Path.Combine(dir, "VmRenameLib.obf.dll");
            var result = await CreateService().ObfuscateAsync(input, output, SettingsFor(combination));
            result.Success.ShouldBeTrue(result.ErrorMessage);
            result.Statistics.ProtectionsApplied.ShouldBeGreaterThan(0, "nothing was virtualized, so this run proves nothing");

            using (var loaded = ModuleDefMD.Load(File.ReadAllBytes(output)))
            {
                foreach (var name in DriverMethods.Where(n => n != "Text"))
                    IsVmStub(Driver(loaded).FindMethod(name)!).ShouldBeTrue($"Driver.{name} should be a VM stub");

                loaded.GetTypes().ShouldNotContain(t => t.Name == "Counter", "Counter must have been renamed");
                loaded.GetTypes().SelectMany(t => t.Fields).ShouldNotContain(
                    f => f.Name == "Value" || f.Name == "Total" || f.Name == "Weight" || f.Name == "Scale",
                    "the fields the bytecode names must have been renamed");
            }

            var native = Probe(input);
            native["InstanceField"].ShouldBe("Int32:5");
            native["StaticMethod"].ShouldBe("Int32:12");
            native["NestedType"].ShouldBe("Int32:16");
            Probe(output).ShouldBe(native);

            using var protectedModule = ModuleDefMD.Load(File.ReadAllBytes(output));
            StaleMemberReferenceScanner.Find(protectedModule).ShouldBeEmpty(
                "a MemberRef to a def of this module is bound by name; the rename left these behind");
        }
        finally
        {
            TryDelete(dir);
        }
    }

    [Fact]
    public async Task ClosedSet_VirtualizedMethodCallsAcrossRenamedAssemblies()
    {
        const string library = """
            public class Shared
            {
                public int Value;
                public static int Total;
                public int Twice() => Value * 2;
                public static int Add(int a, int b) => a + b;
            }
            """;
        const string app = """
            public static class Program { public static int Main() => 0; }

            [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = true)]
            public static class Driver
            {
                public static int Cross(int n)
                {
                    var s = new Shared();
                    s.Value = n;
                    Shared.Total = Shared.Add(s.Twice(), 1);
                    return Shared.Total + s.Value;
                }
            }
            """;
        var dir = NewTempDir();
        try
        {
            var libPath = Compile(library, dir, "SharedLib");
            var appPath = Compile(app, dir, "VmApp", OutputKind.ConsoleApplication, libPath);
            var outDir = Path.Combine(dir, "out");
            var settings = SettingsFor("renaming");

            var result = await CreateService().ObfuscateClosedSetAsync(
                new[] { ClosedSetInput.From(appPath), ClosedSetInput.From(libPath) }, outDir, settings);
            result.Success.ShouldBeTrue(result.ErrorMessage);

            using (var loadedApp = ModuleDefMD.Load(File.ReadAllBytes(Path.Combine(outDir, "VmApp.dll"))))
            using (var loadedLib = ModuleDefMD.Load(File.ReadAllBytes(Path.Combine(outDir, "SharedLib.dll"))))
            {
                IsVmStub(Driver(loadedApp).FindMethod("Cross")!).ShouldBeTrue("Driver.Cross should be a VM stub");
                loadedLib.GetTypes().ShouldNotContain(t => t.Name == "Shared", "the library must have been renamed too");
            }

            var expected = RunCross(Path.Combine(dir, "VmApp.dll"), 7);
            expected.ShouldBe("Int32:22");
            RunCross(Path.Combine(outDir, "VmApp.dll"), 7).ShouldBe(expected);
        }
        finally
        {
            TryDelete(dir);
        }
    }

    [Fact]
    public async Task MergeAndObfuscate_VirtualizedMethodCallsAcrossRenamedMergedAssemblies()
    {
        const string library = """
            public class Shared
            {
                public int Value;
                public static int Total;
                public int Twice() => Value * 2;
                public static int Add(int a, int b) => a + b;
            }
            """;
        const string app = """
            public static class Program { public static int Main() => 0; }

            [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = true)]
            public static class Driver
            {
                public static int Cross(int n)
                {
                    var s = new Shared();
                    s.Value = n;
                    Shared.Total = Shared.Add(s.Twice(), 1);
                    return Shared.Total + s.Value;
                }
            }
            """;
        var dir = NewTempDir();
        try
        {
            var libPath = Compile(library, dir, "SharedLib");
            var appPath = Compile(app, dir, "VmApp", OutputKind.ConsoleApplication, libPath);
            var outPath = Path.Combine(dir, "MergedVm.dll");
            var settings = SettingsFor("renaming");

            var result = await CreateService().MergeAndObfuscateAsync([appPath, libPath], outPath, settings);
            result.Success.ShouldBeTrue(result.ErrorMessage);
            result.Statistics.ProtectionsApplied.ShouldBeGreaterThan(0);

            using (var loaded = ModuleDefMD.Load(File.ReadAllBytes(outPath)))
            {
                IsVmStub(Driver(loaded).FindMethod("Cross")!).ShouldBeTrue("Driver.Cross should be a VM stub");
                loaded.GetTypes().ShouldNotContain(t => t.Name == "Shared", "the merged library type must have been renamed");
                StaleMemberReferenceScanner.Find(loaded).ShouldBeEmpty();
            }

            var expected = RunCross(appPath, 7);
            expected.ShouldBe("Int32:22");
            RunCross(outPath, 7).ShouldBe(expected);
        }
        finally
        {
            TryDelete(dir);
        }
    }

    private static ObfySettings SettingsFor(string combination)
    {
        var settings = new ObfySettings
        {
            Level = ObfuscationLevel.Custom,
            StringEncryption = { Enabled = combination == "string-encryption", MinStringLength = 1 },
            SymbolRenaming =
            {
                Enabled = true,
                PreservePublicApi = false,
                Mode = combination == "sequential-names" ? NamingMode.Sequential : NamingMode.Unreadable
            },
            Virtualization = { Enabled = true },
            Metadata = { RemoveDebugInfo = combination == "metadata-removal", RemoveAttributes = combination == "metadata-removal" },
            ControlFlow = { Enabled = combination == "control-flow" }
        };
        settings.Protection.ReferenceProxy = combination == "reference-proxy";
        return settings;
    }

    private static IObfuscationService CreateService()
    {
        var builder = new ContainerBuilder();
        builder.RegisterGeneric(typeof(NullLogger<>)).As(typeof(ILogger<>)).SingleInstance();
        builder.RegisterModule<ObfuscationModule>();
        return builder.Build().Resolve<IObfuscationService>();
    }

    private static string Compile(
        string source,
        string dir,
        string assemblyName,
        OutputKind kind = OutputKind.DynamicallyLinkedLibrary,
        string? referencePath = null)
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(p => p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .ToList();
        if (referencePath is not null)
            references.Add(MetadataReference.CreateFromFile(referencePath));

        var compilation = CSharpCompilation.Create(
            assemblyName,
            new[] { CSharpSyntaxTree.ParseText(source) },
            references,
            new CSharpCompilationOptions(kind, optimizationLevel: OptimizationLevel.Release));
        var path = Path.Combine(dir, assemblyName + ".dll");
        var emit = compilation.Emit(path);
        emit.Success.ShouldBeTrue(string.Join("\n", emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        return path;
    }

    private static TypeDef Driver(ModuleDef module) => module.Types.Single(t => t.Name == "Driver");

    /// <summary>
    /// A stub the pass wrote: a static call to an <c>object (int, object[])</c> method of this module
    /// (the interpreter's <c>Run</c>, whatever it is called by now).
    /// </summary>
    private static bool IsVmStub(MethodDef method) =>
        method.HasBody
        && method.Body.Instructions.Any(i => i.OpCode == OpCodes.Newarr)
        && method.Body.Instructions.Any(i =>
            i.OpCode == OpCodes.Call
            && i.Operand is MethodDef run
            && run.IsStatic
            && run.MethodSig.Params.Count == 2
            && run.MethodSig.Params[0].ElementType == ElementType.I4
            && run.MethodSig.Params[1].ElementType == ElementType.SZArray
            && run.MethodSig.RetType.ElementType == ElementType.Object);

    private static Dictionary<string, string> Probe(string assemblyPath)
    {
        var results = new Dictionary<string, string>();
        WithAssembly(assemblyPath, assembly =>
        {
            var driver = assembly.GetType("Driver")!;
            // Echo takes a Counter; ThroughEcho reaches it.
            foreach (var name in DriverMethods.Where(n => n != "Echo"))
            {
                var method = driver.GetMethod(name)!;
                results[name] = Outcome(() => method.Invoke(null, new object[] { name == "Text" ? 12 : 4 }));
            }
        });
        return results;
    }

    private static string RunCross(string appPath, int n)
    {
        var outcome = string.Empty;
        WithAssembly(appPath, assembly =>
        {
            var method = assembly.GetType("Driver")!.GetMethod("Cross")!;
            outcome = Outcome(() => method.Invoke(null, new object[] { n }));
        });
        return outcome;
    }

    private static void WithAssembly(string assemblyPath, Action<Assembly> action)
    {
        var context = new IsolatedLoadContext(assemblyPath);
        try
        {
            action(context.LoadFromAssemblyPath(assemblyPath));
        }
        finally
        {
            context.Unload();
        }
    }

    private static string Outcome(Func<object?> action)
    {
        try
        {
            var value = action();
            return value is null ? "null" : value.GetType().Name + ":" + value;
        }
        catch (TargetInvocationException ex)
        {
            return ex.InnerException!.GetType().Name + ": " + ex.InnerException.Message;
        }
    }

    /// <summary>Resolves siblings of the assembly from its own folder, so each copy sees its own library.</summary>
    private sealed class IsolatedLoadContext : AssemblyLoadContext
    {
        private readonly string _directory;

        public IsolatedLoadContext(string assemblyPath)
            : base("vm-rename-" + Guid.NewGuid().ToString("N"), isCollectible: true) =>
            _directory = Path.GetDirectoryName(assemblyPath)!;

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            var candidate = Path.Combine(_directory, assemblyName.Name + ".dll");
            return File.Exists(candidate) ? LoadFromAssemblyPath(candidate) : null;
        }
    }

    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "obfy-vm-rename-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void TryDelete(string dir)
    {
        try { Directory.Delete(dir, true); } catch { /* ignore */ }
    }
}
