using System.Reflection;
using System.Runtime.Loader;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.Logging.Abstractions;
using Obfy.Core.Models;
using Obfy.Core.Obfuscators.Assembly;
using Obfy.Core.Pipeline;
using Shouldly;

namespace Obfy.Tests;

/// <summary>
/// Each call shape runs through the reference-proxy pass on its own, so a failure names the shape.
/// Shapes the pass is supposed to rewrite must contain a <c>calli</c>. Generic calls and calls with
/// more than 16 arguments stay direct and only have to keep returning the original value.
/// </summary>
public class ReferenceProxyRuntimeMatrixTests
{
    public static TheoryData<string, string, int> Cases()
    {
        var data = new TheoryData<string, string, int>();
        data.Add("""
            public static class Lib
            {
                public static int Get() => new Adder().Add(20, 22);
            }
            public class Adder
            {
                public int Add(int a, int b) => a + b;
            }
            """, "public-instance", 42);
        data.Add("""
            public static class Lib
            {
                public static int Get()
                {
                    IAdder adder = new Adder();
                    return adder.Add(20, 22);
                }
            }
            public interface IAdder { int Add(int a, int b); }
            public class Adder : IAdder
            {
                public int Add(int a, int b) => a + b;
            }
            """, "interface", 42);
        data.Add("""
            public static class Lib
            {
                public static int Get()
                {
                    var n = 20;
                    Bump(ref n);
                    return n;
                }
                static void Bump(ref int n) => n += 22;
            }
            """, "byref", 42);
        data.Add("""
            public static class Lib
            {
                public static int Get() => Make().N;
                static Pair Make() => new Pair(42);
            }
            public struct Pair
            {
                public int N;
                public Pair(int n) { N = n; }
            }
            """, "struct-return", 42);
        data.Add("""
            public static class Lib
            {
                public static int Get() => new Derived().Value();
            }
            public class Base
            {
                public virtual int Compute() => 40;
            }
            public class Derived : Base
            {
                public override int Compute() => 1;
                public int Value() => base.Compute() + 2;
            }
            """, "base-call", 42);
        data.Add("""
            public static class Lib
            {
                public static int Get()
                {
                    IAdder adder = new Adder();
                    return adder.Add(20, 22);
                }
            }
            public interface IAdder { int Add(int a, int b); }
            public class Adder : IAdder
            {
                int IAdder.Add(int a, int b) => a + b;
            }
            """, "explicit-interface", 42);
        data.Add("""
            public static class Lib
            {
                public static int Get()
                {
                    Base b = new Derived();
                    return b.Compute();
                }
            }
            public class Base
            {
                public virtual int Compute() => 1;
            }
            public class Derived : Base
            {
                public override int Compute() => 42;
            }
            """, "virtual-override", 42);
        data.Add("""
            public static class Lib
            {
                public static int Get() => new Box<int>().Pick();
            }
            public class Box<T>
            {
                public int Pick() => From(20);
                public int From(int n) => n + 22;
            }
            """, "generic-instance", 42);
        data.Add("""
            public static class Lib
            {
                public static int Get() => Box<int>.Pick();
            }
            public class Box<T>
            {
                public static int Pick() => From(20);
                public static int From(int n) => n + 22;
            }
            """, "generic-static", 42);
        data.Add("""
            public static class Lib
            {
                public static int Get() => new Box<int>().Pick();
            }
            public class Box<T>
            {
                public int Pick() => From(20);
                internal int From(int n) => n + 22;
            }
            """, "generic-internal", 42);
        data.Add("""
            public static class Lib
            {
                public static int Get() => new Box<int>().Pick();
            }
            public class Box<T>
            {
                public int Pick() => Secret(20);
                private int Secret(int n) => n + 22;
            }
            """, "generic-private", 42);
        data.Add("""
            public static class Lib
            {
                public static int Get()
                {
                    var n = 20;
                    return Ref(ref n);
                }
                static ref int Ref(ref int n)
                {
                    n += 22;
                    return ref n;
                }
            }
            """, "ref-return", 42);
        data.Add("""
            public static class Lib
            {
                public static int Get() => new Adder().Add(20, 22);
            }
            public struct Adder
            {
                public int Add(int a, int b) => a + b;
            }
            """, "struct-instance", 42);
        data.Add("""
            public static class Lib
            {
                public static int Get()
                {
                    IAdder adder = new Adder();
                    return adder.Add(20, 22);
                }
            }
            public interface IAdder { int Add(int a, int b); }
            public struct Adder : IAdder
            {
                public int Add(int a, int b) => a + b;
            }
            """, "struct-interface", 42);
        data.Add("""
            public static class Lib
            {
                public static int Get() => new Factory().Make().A;
            }
            public class Factory
            {
                public Big Make() => new Big(42);
            }
            public struct Big
            {
                public int A, B, C, D;
                public Big(int n) { A = n; B = 1; C = 2; D = 3; }
            }
            """, "large-struct-return", 42);
        data.Add("""
            public static class Lib
            {
                public static int Get() => new Adder(20).Bump(22).N;
            }
            public struct Adder
            {
                public int N;
                public Adder(int n) { N = n; }
                public Adder Bump(int n) { N += n; return this; }
            }
            """, "struct-method-returns-struct", 42);
        data.Add("""
            public static class Lib
            {
                public static int Get() => new Box().N;
            }
            public class Box
            {
                public int N;
                public Box() { N = Compute(); }
                public int Compute() => 42;
            }
            """, "ctor-calls-method", 42);
        data.Add("""
            public static class Lib
            {
                public static int Get() => new Box().N;
            }
            public class Box
            {
                public int N => 42;
            }
            """, "property", 42);
        data.Add("""
            public static class Lib
            {
                public static int Get() => Sum(1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 26);
                static int Sum(int a, int b, int c, int d, int e, int f, int g, int h, int i, int j, int k, int l, int m, int n, int o, int p, int q)
                    => a + b + c + d + e + f + g + h + i + j + k + l + m + n + o + p + q;
            }
            """, "skipped-many-args", 42);
        return data;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task ProxiedCall_ReturnsTheOriginalValue(string source, string name, int expected)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"obfy-proxy-matrix-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var input = Compile(source, dir, "ProxyMatrix");
            using var module = ModuleDefMD.Load(File.ReadAllBytes(input));
            var context = PipelineContext.ForAssembly(module, new ObfySettings
            {
                Level = ObfuscationLevel.Custom,
                Protection = { ReferenceProxy = true }
            });
            var result = await new ReferenceProxyObfuscator(NullLogger<ReferenceProxyObfuscator>.Instance)
                .ObfuscateAsync(context);
            result.Success.ShouldBeTrue(result.ErrorMessage);

            var calli = module.GetTypes().SelectMany(t => t.Methods).Count(m =>
                m.HasBody && m.Body.Instructions.Any(i => i.OpCode == OpCodes.Calli));
            if (!name.StartsWith("generic", StringComparison.Ordinal) && name != "skipped-many-args")
                calli.ShouldBeGreaterThan(0, name);

            var output = Path.Combine(dir, "ProxyMatrix.obf.dll");
            module.Write(output);
            Invoke(output).ShouldBe(expected, name);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* temp */ }
        }
    }

    private static string Compile(string source, string dir, string assemblyName)
    {
        var compilation = CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText(source)],
            ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
                .Split(Path.PathSeparator)
                .Where(p => p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var path = Path.Combine(dir, assemblyName + ".dll");
        var emit = compilation.Emit(path);
        emit.Success.ShouldBeTrue(string.Join("\n", emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        return path;
    }

    private static int Invoke(string assemblyPath)
    {
        var alc = new AssemblyLoadContext($"proxy-{Guid.NewGuid():N}", isCollectible: true);
        try
        {
            var type = alc.LoadFromAssemblyPath(assemblyPath).GetType("Lib");
            var method = type!.GetMethod("Get", BindingFlags.Public | BindingFlags.Static);
            return (int)method!.Invoke(null, null)!;
        }
        finally
        {
            alc.Unload();
        }
    }
}
